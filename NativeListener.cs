using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace PlcSimulator;

public interface IServer : IAsyncDisposable
{
    Task Listening { get; }
    int Clients { get; }
}

internal sealed class NativeListener : IServer
{
    private readonly TcpListener tcp;
    private readonly NativeMemory memory;
    private readonly Action<string,string> log;
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<TcpClient,Task> sessions = [];
    public Task Listening { get; }
    public int Clients { get { lock (sessions) return sessions.Count; } }

    public NativeListener(NativeMemory memory, IPAddress ip, int port, Action<string,string> log)
    {
        this.memory = memory;
        this.log = log;
        tcp = new TcpListener(ip, port);
        tcp.Start();
        Listening = Task.Run(Accept);
    }
    private async Task Accept()
    {
        while (!stop.IsCancellationRequested)
        {
            var client = await tcp.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
            lock (sessions) sessions.Add(client, Task.Run(() => Serve(client)));
        }
    }
    private async Task Serve(TcpClient client)
    {
        string peer = client.Client.RemoteEndPoint?.ToString() ?? "";
        try
        {
            log("Connected", peer);
            var stream = client.GetStream();
            var protocol = memory.Protocol;
            int headerSize = protocol switch { PlcProtocol.S7 => 4, PlcProtocol.MC or PlcProtocol.Fx5Slmp => 9,
                PlcProtocol.MCAscii or PlcProtocol.Fx5SlmpAscii => 18, PlcProtocol.FxSerial => 1, _ => 8 };
            var header = new byte[headerSize];
            void Rejected(int code) => log("Failed", $"{protocol} · {peer} · 0x{code:X4}");
            var s7 = protocol == PlcProtocol.S7 ? new S7Session(memory, Rejected) : null;
            var fins = protocol == PlcProtocol.FINS ? new FinsSession(memory, Rejected) : null;
            while (!stop.IsCancellationRequested)
            {
                if (await stream.ReadAsync(header.AsMemory(0, 1), stop.Token).ConfigureAwait(false) == 0) break;
                if (protocol == PlcProtocol.FxSerial)
                {
                    if (header[0] != 2) throw new InvalidDataException("Invalid FX STX");
                    using var request = new MemoryStream();
                    request.WriteByte(2);
                    int next;
                    while ((next = stream.ReadByte()) != 3)
                    {
                        if (next < 0 || request.Length > 1024) throw new InvalidDataException("Invalid FX frame");
                        request.WriteByte((byte)next);
                    }
                    request.WriteByte(3);
                    var check = new byte[2];
                    await stream.ReadExactlyAsync(check, stop.Token).ConfigureAwait(false);
                    request.Write(check);
                    var fxResponse = FxProtocol.Reply(memory, request.ToArray(), Rejected);
                    await stream.WriteAsync(fxResponse, stop.Token).ConfigureAwait(false);
                    continue;
                }
                await stream.ReadExactlyAsync(header.AsMemory(1), stop.Token).ConfigureAwait(false);
                int size = protocol switch
                {
                    PlcProtocol.S7 when header[0] == 3 && header[1] == 0 => BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)) - 4,
                    PlcProtocol.MC or PlcProtocol.Fx5Slmp when header[0] == 0x50 && header[1] == 0 => BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7)),
                    PlcProtocol.MCAscii or PlcProtocol.Fx5SlmpAscii when header.AsSpan(0,4).SequenceEqual("5000"u8) =>
                        Convert.ToInt32(Encoding.ASCII.GetString(header, 14, 4), 16),
                    PlcProtocol.FINS when header.AsSpan(0,4).SequenceEqual("FINS"u8) => BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)),
                    _ => throw new InvalidDataException(
                        $"Invalid {protocol} header {Convert.ToHexString(header)}. Check the client protocol and binary/ASCII frame setting.")
                };
                if (size < 1 || size > 65535) throw new InvalidDataException("Invalid message length");
                byte[] frame = new byte[size + headerSize];
                header.CopyTo(frame, 0);
                await stream.ReadExactlyAsync(frame.AsMemory(headerSize), stop.Token).ConfigureAwait(false);
                var response = protocol switch
                {
                    PlcProtocol.S7 => s7!.Reply(frame),
                    PlcProtocol.MC or PlcProtocol.Fx5Slmp => McProtocol.Reply(memory, frame, Rejected),
                    PlcProtocol.MCAscii or PlcProtocol.Fx5SlmpAscii => McAsciiProtocol.Reply(memory, frame, Rejected),
                    _ => fins!.Reply(frame)
                };
                await stream.WriteAsync(response, stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or SocketException or OperationCanceledException or ObjectDisposedException or ArgumentException)
        {
            if (!stop.IsCancellationRequested) log("Failed", peer + " · " + ex.Message);
        }
        finally
        {
            client.Dispose();
            log("Disconnected", peer);
            lock (sessions) sessions.Remove(client);
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        tcp.Stop();
        try { await Listening.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally
        {
            Task[] pending;
            lock (sessions)
            {
                foreach (var client in sessions.Keys) client.Dispose();
                pending = sessions.Values.ToArray();
            }
            await Task.WhenAll(pending).ConfigureAwait(false);
            stop.Dispose();
        }
    }
}

