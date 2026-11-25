using System;
using System.Buffers.Binary;
using System.IO;

namespace PlcSimulator;

internal sealed class FinsSession(NativeMemory memory, Action<int> rejected)
{
    private bool initialized;
    internal byte[] Reply(byte[] frame)
    {
        if (frame.Length < 16) throw new InvalidDataException("Incomplete FINS/TCP header");
        int command = BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(8));
        if (command == 0 && frame.Length == 20)
        {
            var hello = Envelope(1, 8);
            uint node = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(16));
            BinaryPrimitives.WriteUInt32BigEndian(hello.AsSpan(16), node == 0 ? 1u : node);
            hello[23] = node == 2 ? (byte)1 : (byte)2;
            initialized = true;
            return hello;
        }
        if (!initialized || command != 2 || frame.Length < 28) throw new InvalidDataException("Expected FINS memory request");
        ushort status = 0;
        byte[] data = [];
        int op = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(26));
        if (op is not (0x0101 or 0x0102)) status = 0x0401;
        else try
        {
            if (frame.Length < 34) throw new ArgumentException();
            byte area = frame[28];
            string bank = area switch
            {
                0x82 or 0x02 => "D", 0xB0 or 0x30 => "CIO", 0xB1 or 0x31 => "W",
                0xB2 or 0x32 => "H", 0xB3 or 0x33 => "A", _ => throw new ArgumentException()
            };
            bool bitArea = area < 0x80, write = op == 0x0102;
            int address = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(29));
            int bitOffset = frame[31], count = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(32));
            if (count is < 1 or > 8192 || bitOffset > (bitArea ? 15 : 0) ||
                frame.Length != 34 + (write ? count * (bitArea ? 1 : 2) : 0)) throw new ArgumentException();
            int start = address * 16 + bitOffset;
            if (!bitArea) data = memory.Transfer(bank, start, count * 16, write ? frame[34..] : null);
            else if (write)
            {
                var bits = new byte[(count + 7)/8];
                for (int i = 0; i < count; i++)
                    if (frame[34+i] != 0) bits[i/8] |= (byte)(1 << (i%8));
                memory.Transfer(bank, start, count, bits, true);
            }
            else
            {
                var bits = memory.Transfer(bank, start, count, null, true);
                data = new byte[count];
                for (int i = 0; i < count; i++) data[i] = (byte)((bits[i/8] >> (i%8)) & 1);
            }
            if (write) data = [];
        }
        catch (ArgumentException) { status = 0x1103; }
        if (status != 0) rejected(status);
        var response = Envelope(2, 14 + data.Length);
        response[16] = (byte)(frame[16] | 0x40);
        response[18] = frame[18];
        Array.Copy(frame, 22, response, 19, 3);
        Array.Copy(frame, 19, response, 22, 3);
        Array.Copy(frame, 25, response, 25, 3);
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(28), status);
        data.CopyTo(response, 30);
        return response;
    }

    private static byte[] Envelope(int command, int size)
    {
        var bytes = new byte[size + 16];
        "FINS"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), size + 8);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8), command);
        return bytes;
    }
}
