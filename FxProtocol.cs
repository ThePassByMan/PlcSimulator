using System;
using System.Globalization;
using System.Text;

namespace PlcSimulator;

internal static class FxProtocol
{
    private static int Hex(string text) => int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    internal static byte[] Reply(NativeMemory memory, byte[] frame, Action<int> rejected)
    {
        string text = Encoding.ASCII.GetString(frame);
        if (frame.Length < 8 || frame[0] != 2 || frame[^3] != 3)
            throw new FormatException("Invalid FX serial frame");
        int checksum = 3;
        for (int i = 1; i < frame.Length - 3; i++) checksum += frame[i];
        if (Hex(text[^2..]) != (checksum & 255)) throw new FormatException("FX checksum mismatch");
        string body = text[1..^3];
        try
        {
            if (body.Length == 5 && body[0] is '7' or '8' && body[^1] == '8')
            {
                int number = Hex(body.Substring(1, 3));
                if (number % 16 != 0) throw new FormatException();
                memory.Transfer("M", number / 16, 1, [(byte)(body[0] == '7' ? 1 : 0)]);
                return [6, 3];
            }
            int address = Hex(body.Substring(2, 5));
            int count = Hex(body.Substring(7, 2));
            if (count < 1 || count > 64)
                throw new FormatException();
            string bank;
            int bit;
            if (address >= 0x8800 && address < 0xA000)
            {
                bank = "M";
                bit = (address - 0x8800) * 8;
            }
            else if (address >= 0x4000 && (address - 0x4000) % 2 == 0)
            {
                bank = "D";
                bit = (address - 0x4000) * 8;
            }
            else throw new FormatException();
            if (body.StartsWith("E0", StringComparison.Ordinal) && body.Length == 9)
            {
                var data = memory.Transfer(bank, bit, count * 8);
                return Frame(Convert.ToHexString(data));
            }
            if (body.StartsWith("E1", StringComparison.Ordinal) && body.Length == 9 + count * 2)
            {
                memory.Transfer(bank, bit, count * 8, Convert.FromHexString(body[9..]));
                return [6, 3];
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { rejected(0xC051); }
        return [0x15, 3];
    }
    private static byte[] Frame(string data)
    {
        var bytes = Encoding.ASCII.GetBytes(data);
        var result = new byte[bytes.Length + 4];
        result[0] = 2;
        bytes.CopyTo(result, 1);
        result[^3] = 3;
        int sum = 3;
        foreach (var value in bytes) sum += value;
        Encoding.ASCII.GetBytes((sum & 255).ToString("X2")).CopyTo(result, result.Length - 2);
        return result;
    }
}
