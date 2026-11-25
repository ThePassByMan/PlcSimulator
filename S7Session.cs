using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace PlcSimulator;

// ISO-on-TCP, classic S7 setup communication and Read Var / Write Var (S7ANY addressing).
internal sealed class S7Session(NativeMemory memory, Action<int> rejected)
{
    private bool connected;
    private int pduLimit;
    private static ushort U16(byte[] frame, int at) => BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(at));
    private static void Set16(byte[] frame, int at, int value) =>
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(at), checked((ushort)value));

    internal byte[] Reply(byte[] frame)
    {
        if (frame.Length >= 11 && frame[5] == 0xE0 && frame[4] + 5 == frame.Length)
        {
            var confirm = (byte[])frame.Clone();
            confirm[5] = 0xD0;
            Array.Copy(frame, 8, confirm, 6, 2);
            Set16(confirm, 8, 1);
            connected = true;
            pduLimit = 0;
            return confirm;
        }
        if (!connected || frame.Length < 18 || frame[4] != 2 || frame[5] != 0xF0 ||
            frame[7] != 0x32 || frame[8] != 1) throw new InvalidDataException("Expected S7 job");
        int paramSize = U16(frame, 13), dataSize = U16(frame, 15);
        if (paramSize < 1 || 17 + paramSize + dataSize != frame.Length) throw new InvalidDataException("Invalid S7 length");
        byte op = frame[17];
        if (op == 0xF0 && paramSize == 8)
        {
            pduLimit = Math.Min(960, (int)U16(frame, 23));
            if (pduLimit < 32) throw new InvalidDataException("Invalid S7 PDU size");
            var setup = new byte[] { 0xf0, 0, 0, 1, 0, 1, 0, 0 };
            Set16(setup, 6, pduLimit);
            return Ack(frame, setup, []);
        }
        if (pduLimit == 0 || op is not (4 or 5) || paramSize < 2) return Ack(frame, [], [], 0x8104);
        int count = frame[18];
        if (count < 1 || paramSize != 2 + 12 * count || frame.Length - 7 > pduLimit)
            return Ack(frame, [], [], 0x8500);
        bool write = op == 5;
        var result = new List<byte>();
        int cursor = 17 + paramSize;
        for (int i = 0; i < count; i++)
        {
            int item = 19 + i * 12;
            int transport = frame[item + 3], elements = U16(frame, item + 4);
            int bits = transport switch
            {
                1 => elements, 2 or 3 => elements * 8, 4 or 5 => elements * 16,
                6 or 7 or 8 => elements * 32, _ => 0
            };
            int size = (bits + 7) / 8;
            byte[]? value = null;
            if (write)
            {
                if (cursor + 4 > frame.Length) return Ack(frame, [], [], 0x8500);
                int wireLength = U16(frame, cursor + 2);
                int wireBytes = frame[cursor+1] is 3 or 4 or 5 ? (wireLength + 7) / 8 : wireLength;
                if (wireBytes != size || cursor + 4 + size > frame.Length) return Ack(frame, [], [], 0x8500);
                value = frame.AsSpan(cursor + 4, size).ToArray();
                cursor += 4 + size + (i + 1 < count ? size % 2 : 0);
            }
            try
            {
                int db = U16(frame, item + 6);
                string bank = frame[item + 8] switch
                {
                    0x81 => "I", 0x82 => "Q", 0x83 => "M", 0x84 => "DB" + db,
                    _ => throw new ArgumentException()
                };
                int address = (frame[item+9] << 16) | (frame[item+10] << 8) | frame[item+11];
                if (frame[item] != 0x12 || frame[item+1] != 10 || frame[item+2] != 0x10 ||
                    bits == 0 || (transport != 1 && address % 8 != 0) ||
                    size + (write ? 0 : result.Count + 18) > pduLimit) throw new ArgumentException();
                var bytes = memory.Transfer(bank, address, bits, value);
                result.Add(0xff);
                if (!write)
                {
                    byte responseType = transport switch { 1 => 3, 5 => 5, 7 => 6, 8 => 7, _ => 4 };
                    int length = responseType is 6 or 7 ? size : bits;
                    result.Add(responseType);
                    result.Add((byte)(length >> 8)); result.Add((byte)length);
                    result.AddRange(bytes);
                    if (i + 1 < count && size % 2 != 0) result.Add(0);
                }
            }
            catch (ArgumentException)
            {
                rejected(5);
                result.Add(5);
                if (!write) result.AddRange(new byte[3]);
            }
        }
        return Ack(frame, [op, (byte)count], result.ToArray());
    }
    private byte[] Ack(byte[] request, byte[] parameters, byte[] data, ushort error = 0)
    {
        if (error != 0) rejected(error);
        var answer = new byte[19 + parameters.Length + data.Length];
        answer[0] = 3;
        Set16(answer, 2, answer.Length);
        answer[4] = 2; answer[5] = 0xf0; answer[6] = 0x80;
        answer[7] = 0x32; answer[8] = 3;
        Array.Copy(request, 11, answer, 11, 2);
        Set16(answer, 13, parameters.Length);
        Set16(answer, 15, data.Length);
        Set16(answer, 17, error);
        parameters.CopyTo(answer, 19);
        data.CopyTo(answer, 19 + parameters.Length);
        return answer;
    }
}
