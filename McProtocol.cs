using System;
using System.Buffers.Binary;

namespace PlcSimulator;

// Binary QnA-compatible 3E frame. Batch read/write can use bit units or word units.
internal static class McProtocol
{
    internal static byte[] Reply(NativeMemory memory, byte[] frame, Action<int> rejected)
    {
        byte[] data = [];
        ushort status = 0;
        try
        {
            if (frame.Length < 21) throw new ArgumentException();
            int command = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(11));
            int unit = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(13));
            if (command is not (0x0401 or 0x1401) || unit > 1) status = 0xC059;
            else
            {
                string bank = frame[18] switch
                {
                    0xA8 => "D", 0xB4 => "W", 0x90 => "M", 0x9C => "X", 0x9D => "Y",
                    0x92 => "L", 0xA0 => "B", 0xA1 => "SB", 0x91 => "SM",
                    0xB5 => "SW", 0xA9 => "SD", 0xAF => "R", 0xB0 => "ZR",
                    _ => throw new ArgumentException()
                };
                bool bitBank = bank is "M" or "X" or "Y" or "L" or "B" or "SB" or "SM";
                int address = frame[15] | (frame[16] << 8) | (frame[17] << 16);
                int count = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(19));
                bool bitUnit = unit == 1, write = command == 0x1401;
                if (count == 0 || count > (bitUnit ? 7168 : 960) || (bitUnit && !bitBank)) throw new ArgumentException();
                int wireSize = bitUnit ? (count + 1) / 2 : count * 2;
                if (frame.Length != 21 + (write ? wireSize : 0)) throw new ArgumentException();
                int bit = address * (bitBank ? 1 : 16), bits = count * (bitUnit ? 1 : 16);
                if (write)
                {
                    byte[] packed;
                    if (bitUnit)
                    {
                        packed = new byte[(count + 7) / 8];
                        for (int i = 0; i < count; i++)
                            if ((frame[21 + i/2] & ((i % 2 == 0) ? 0x10 : 1)) != 0) packed[i/8] |= (byte)(1 << (i % 8));
                    }
                    else packed = frame[21..];
                    memory.Transfer(bank, bit, bits, packed);
                }
                else
                {
                    var packed = memory.Transfer(bank, bit, bits);
                    if (bitUnit)
                    {
                        data = new byte[wireSize];
                        for (int i = 0; i < count; i++)
                            if ((packed[i/8] & (1 << (i % 8))) != 0) data[i/2] |= (byte)((i % 2 == 0) ? 0x10 : 1);
                    }
                    else data = packed;
                }
            }
        }
        catch (ArgumentException) { status = 0xC051; }
        if (status != 0) rejected(status);
        var reply = new byte[11 + data.Length];
        reply[0] = 0xD0;
        frame.AsSpan(2, 5).CopyTo(reply.AsSpan(2));
        BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(7), (ushort)(2 + data.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(9), status);
        data.CopyTo(reply, 11);
        return reply;
    }
}
