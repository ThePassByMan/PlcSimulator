using System;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus;

namespace PlcSimulator;

// Each area shares one lock across local and network requests, including two-word values.
internal class Bank<T> : IPointSource<T>
{
    protected readonly T[] cells = new T[65536];
    public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
    {
        var result = new T[numberOfPoints];
        lock (cells) Array.Copy(cells, startAddress, result, 0, numberOfPoints);
        return result;
    }
    public void WritePoints(ushort startAddress, T[] points)
    {
        lock (cells) Array.Copy(points, 0, cells, startAddress, points.Length);
    }
}

internal sealed class WordBank : Bank<ushort>
{
    public void WriteBit(ushort address, int bit, bool value)
    {
        var mask = 1 << bit;
        lock (cells) cells[address] = (ushort)(value ? cells[address] | mask : cells[address] & ~mask);
    }
}

public sealed class Simulation : ISlaveDataStore, IPointMemory
{
    public IPointSource<bool> CoilDiscretes { get; } = new Bank<bool>();
    public IPointSource<bool> CoilInputs { get; } = new Bank<bool>();
    private readonly WordBank holding = new(), inputs = new();
    public IPointSource<ushort> HoldingRegisters => holding;
    public IPointSource<ushort> InputRegisters => inputs;
    private IPointSource<bool> Bits(Area a) => a == Area.Coil ? CoilDiscretes : CoilInputs;
    private WordBank Words(Area a) => a == Area.HR ? holding : inputs;

    public void Write(PointSpec p)
    {
        var n = p.Encode();
        if (p.IsBit && p.Type == DataKind.Bool) { Bits(p.Area).WritePoints(p.Address, [n != 0]); return; }
        if (p.RegisterBit is int bit) { Words(p.Area).WriteBit(p.Address, bit, n != 0); return; }
        var data = new ushort[p.IsBit ? p.Width / 16 : p.Width];
        for (int i = 0; i < data.Length; i++) data[i] = (ushort)(n >> (16 * (data.Length - 1 - i)));
        if (p.Order == WordOrder.CDAB && data.Length > 1)
            for (int i = 0; i < data.Length / 2; i++) (data[i], data[data.Length / 2 + i]) = (data[data.Length / 2 + i], data[i]);
        if (p.IsBit)
        {
            // Within a coil group, the lowest address is the least significant bit.
            var bits = new bool[p.Width];
            for (int i = 0; i < bits.Length; i++) bits[i] = (data[i / 16] & (1 << (i % 16))) != 0;
            Bits(p.Area).WritePoints(p.Address, bits);
        }
        else Words(p.Area).WritePoints(p.Address, data);
    }

    public string Read(PointSpec p)
    {
        ushort[] w;
        if (p.IsBit)
        {
            var bits = Bits(p.Area).ReadPoints(p.Address, (ushort)p.Width);
            if (p.Type == DataKind.Bool) return bits[0] ? "true" : "false";
            w = new ushort[bits.Length / 16];
            for (int i = 0; i < bits.Length; i++)
                if (bits[i]) w[i / 16] |= (ushort)(1 << (i % 16));
        }
        else w = Words(p.Area).ReadPoints(p.Address, (ushort)p.Width);
        if (p.RegisterBit is int bit) return (w[0] & (1 << bit)) != 0 ? "true" : "false";
        var c = CultureInfo.InvariantCulture;
        if (!p.Wide) return p.Type == DataKind.Int16 ? ((short)w[0]).ToString(c) : w[0].ToString(c);
        if (p.Order == WordOrder.CDAB && w.Length > 1)
            for (int i = 0; i < w.Length / 2; i++) (w[i], w[w.Length / 2 + i]) = (w[w.Length / 2 + i], w[i]);
        ulong n = 0;
        foreach (var word in w) n = (n << 16) | word;
        return p.Type switch
        {
            DataKind.Int32 => ((int)n).ToString(c),
            DataKind.Float32 => BitConverter.UInt32BitsToSingle((uint)n).ToString("R", c),
            DataKind.Int64 => ((long)n).ToString(c),
            DataKind.Float64 => BitConverter.Int64BitsToDouble((long)n).ToString("R", c),
            _ => n.ToString(c)
        };
    }
}

public sealed class Listener : IServer
{
    private readonly IModbusTcpSlaveNetwork network;
    private readonly CancellationTokenSource stop = new();
    public Task Listening { get; }
    public int Clients => network.Masters.Count;

    private sealed class EventLogger(Action<string,string> log) : IModbusLogger
    {
        public bool ShouldLog(LoggingLevel level) => level >= LoggingLevel.Information;
        public void Log(LoggingLevel level, string message)
        {
            if (level >= LoggingLevel.Warning) log("Failed", message);
            else if (ShouldLog(level)) log("ServerEvent", message);
        }
    }
    public Listener(Simulation memory, IPAddress ip, int port, byte unit) : this(memory, ip, port, unit, null) { }

    public Listener(Simulation memory, IPAddress ip, int port, byte unit, Action<string,string>? log)
    {
        var tcp = new TcpListener(ip, port);
        var factory = log == null ? new ModbusFactory() : new ModbusFactory(null, true, new EventLogger(log));
        network = factory.CreateSlaveNetwork(tcp);
        try
        {
            network.AddSlave(factory.CreateSlave(unit, memory));
            tcp.Start();
            Listening = network.ListenAsync(stop.Token);
            if (Listening.IsFaulted) Listening.GetAwaiter().GetResult();
        }
        catch { network.Dispose(); stop.Dispose(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            stop.Cancel();
            await Listening.ConfigureAwait(false);
        }
        catch (Exception ex) when (stop.IsCancellationRequested &&
            ex is SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally { network.Dispose(); stop.Dispose(); }
    }
}

// Serialize cancellation and writes so removing or editing an item cannot leave a queued write behind.
public sealed class WatchRow : INotifyPropertyChanged, IDisposable
{
    private readonly object gate = new();
    private readonly IPointMemory memory;
    public event Action<string,string>? TaskMessage;
    private Timer? timer;
    private long generation;
    private DateTime? due;
    private bool cyclic;
    public PointSpec Spec { get; }
    public string Current { get; private set; } = "";
    public string Timing { get; private set; } = "—";
    public bool Active { get { lock (gate) return timer != null; } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public WatchRow(PointSpec spec, IPointMemory memory)
    {
        spec.Validate();
        Spec = spec;
        this.memory = memory;
        Refresh();
    }
    public void Write() => memory.Write(Spec);
    public void StartCycle() => Start(null);
    public void Schedule(DateTime when) => Start(when);
    private void Start(DateTime? when)
    {
        lock (gate)
        {
            var delay = when.HasValue ? when.Value - DateTime.Now : TimeSpan.FromMilliseconds(Spec.IntervalMs);
            if (delay <= TimeSpan.Zero) throw new InputError("TimeError");
            StopLocked();
            due = when;
            cyclic = !when.HasValue;
            var token = generation;
            timer = new Timer(_ =>
            {
                lock (gate)
                {
                    if (token != generation) return;
                    if (due.HasValue && due.Value > DateTime.Now)
                    {
                        timer!.Change(NextWake(due.Value - DateTime.Now), Timeout.InfiniteTimeSpan);
                        return;
                    }
                    try
                    {
                        Write();
                        if (!cyclic)
                        {
                            StopLocked();
                            TaskMessage?.Invoke("Written", $"{Spec.AddressLabel} ← {Spec.WriteValue}");
                        }
                    }
                    catch (Exception ex)
                    {
                        StopLocked();
                        TaskMessage?.Invoke("Failed", $"{Spec.AddressLabel} · {ex.Message}");
                    }
                }
            }, null, cyclic ? delay : NextWake(delay), cyclic ? delay : Timeout.InfiniteTimeSpan);
        }
        Refresh();
    }
    private static TimeSpan NextWake(TimeSpan delay) =>
        TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 1, 60000));
    private void StopLocked()
    {
        generation++;
        timer?.Dispose();
        timer = null;
        due = null;
        cyclic = false;
    }
    public void Stop() { lock (gate) StopLocked(); Refresh(); }
    public void Dispose() { lock (gate) StopLocked(); }
    public void Refresh()
    {
        var value = memory.Read(Spec);
        string timing;
        lock (gate) timing = cyclic ? $"↻ {Spec.IntervalMs} ms" : due?.ToString("MM-dd HH:mm:ss") ?? "—";
        if (timing == Timing && value == Current) return;
        Current = value;
        Timing = timing;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
