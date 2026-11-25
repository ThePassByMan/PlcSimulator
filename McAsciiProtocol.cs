using System;
using System.Globalization;
using System.Text;

namespace PlcSimulator;

internal static class McAsciiProtocol
{
    private static int Hex(string s) => int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    internal static byte[] Reply(NativeMemory memory, byte[] frame, Action<int> rejected)
    {
        string request = Encoding.ASCII.GetString(frame);
        int status = 0;
        string payload = "";
        try
        {
            if (request.Length < 42) throw new FormatException();
            int command = Hex(request.Substring(22, 4));
            int unit = Hex(request.Substring(26, 4));
            string device = request.Substring(30, 2);
            string bank = device switch
            {
                "D*" => "D", "W*" => "W", "M*" => "M", "X*" => "X", "Y*" => "Y",
                "L*" => "L", "B*" => "B", "SB" => "SB", "SM" => "SM",
                "SW" => "SW", "SD" => "SD", "R*" => "R", "ZR" => "ZR",
                _ => throw new FormatException()
            };
            bool bitBank = bank is "M" or "X" or "Y" or "L" or "B" or "SB" or "SM";
            string number = request.Substring(32, 6);
            int address = memory.Protocol == PlcProtocol.Fx5SlmpAscii && bank is "X" or "Y" ?
                Convert.ToInt32(number, 8) : int.Parse(number, bank is "X" or "Y" or "W" or "SW" or "B" or "SB" ?
                NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture);
            int count = Hex(request.Substring(38, 4));
            bool bitUnit = unit == 1, write = command == 0x1401;
            if (command is not (0x0401 or 0x1401) || count < 1 || count > (bitUnit ? 7168 : 960) ||
                (bitUnit && !bitBank) || address > 65535) throw new FormatException();
            int expected = 42 + (write ? count * (bitUnit ? 1 : 4) : 0);
            if (request.Length != expected) throw new FormatException();
            int start = address * (bitBank ? 1 : 16), bits = count * (bitUnit ? 1 : 16);
            if (write)
            {
                var data = new byte[(bits + 7) / 8];
                if (bitUnit)
                {
                    for (int i = 0; i < count; i++)
                        if (request[42+i] == '1') data[i/8] |= (byte)(1 << (i%8));
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        ushort word = (ushort)Hex(request.Substring(42 + i * 4, 4));
                        data[i*2] = (byte)word;
                        data[i*2+1] = (byte)(word >> 8);
                    }
                }
                memory.Transfer(bank, start, bits, data);
            }
            else
            {
                var data = memory.Transfer(bank, start, bits);
                if (bitUnit)
                {
                    for (int i = 0; i < count; i++) payload += (data[i/8] & (1 << (i%8))) != 0 ? "1" : "0";
                }
                else for (int i = 0; i < count; i++)
                    payload += ((data[i*2+1] << 8) | data[i*2]).ToString("X4");
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { status = 0xC051; rejected(status); }
        string response = "D000" + request.Substring(4, 10) + (4 + payload.Length).ToString("X4") +
            status.ToString("X4") + payload;
        return Encoding.ASCII.GetBytes(response);
    }
}
