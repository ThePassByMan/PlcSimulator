using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PlcSimulator;

public enum PlcProtocol { Modbus, S7, MC, FINS, Inovance, MCAscii, FxSerial, Fx5Slmp, Fx5SlmpAscii }

public interface IPointMemory
{
    string Read(PointSpec point);
    void Write(PointSpec point);
}

internal readonly record struct NativeAddress(string Bank, int Bit, bool SwapBitBytes)
{
    private static readonly Regex S7Address = new(
        @"^(?:DB(?<db>[0-9]+)\.(?:DB[BWDX])?|(?<area>[MIQ])(?:[BWD])?)(?<n>[0-9]+)(?:\.(?<bit>[0-9]+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WordAddress = new(@"^(?<area>CIO|ZR|SB|SM|SW|SD|[CDMWHAXYBLR])(?<n>[0-9A-F]+)(?:\.(?<bit>[0-9]+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static NativeAddress Parse(PlcProtocol protocol, string text, DataKind type)
    {
        var s = text.Trim().ToUpperInvariant();
        var m = (protocol == PlcProtocol.S7 ? S7Address : WordAddress).Match(s);
        var error = protocol + "AddressError";
        if (!m.Success) throw new InputError(error);
        string bank = m.Groups["area"].Value;
        bool packed = false, word = false;
        if (protocol == PlcProtocol.S7)
        {
            if (m.Groups["db"].Success)
            {
                if (!ushort.TryParse(m.Groups["db"].Value, out var db)) throw new InputError(error);
                bank = "DB" + db;
            }
        }
        else if (protocol is PlcProtocol.MC or PlcProtocol.MCAscii or PlcProtocol.FxSerial or PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii)
        {
            packed = bank is "M" or "X" or "Y" or "B" or "L" or "SB" or "SM";
            word = bank is "D" or "W" or "SW" or "SD" or "R" or "ZR";
            if (!packed && !word) throw new InputError(error);
            if (protocol == PlcProtocol.FxSerial && bank is not ("M" or "D")) throw new InputError(error);
        }
        else
        {
            if (bank == "C") bank = "CIO";
            word = bank is "D" or "W" or "H" or "A" or "CIO";
            if (!word) throw new InputError(error);
        }
        bool hex = protocol is PlcProtocol.MC or PlcProtocol.MCAscii or PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii &&
            bank is ("W" or "SW" or "B" or "SB") || protocol is PlcProtocol.MC or PlcProtocol.MCAscii && bank is ("X" or "Y");
        var numberText = m.Groups["n"].Value;
        ushort offset;
        if (protocol is PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii && bank is "X" or "Y")
        {
            if (numberText.Length == 0 || numberText.IndexOfAny(['8', '9', 'A', 'B', 'C', 'D', 'E', 'F']) >= 0)
                throw new InputError(error);
            try { offset = checked((ushort)Convert.ToInt32(numberText, 8)); }
            catch (Exception ex) when (ex is FormatException or OverflowException) { throw new InputError(error); }
        }
        else if (!ushort.TryParse(numberText, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None,
            CultureInfo.InvariantCulture, out offset)) throw new InputError(error);
        if (protocol == PlcProtocol.FxSerial && bank == "M" && offset > 255) throw new InputError(error);
        if (protocol == PlcProtocol.FxSerial && bank == "D" && offset > 8511) throw new InputError("RangeError");
        if (protocol is PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii && bank == "ZR") throw new InputError(error);
        if (protocol is PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii &&
            (bank is "X" or "Y" && offset > 1023 || bank == "M" && offset > 32767 ||
             bank is "D" && offset > 7999 || bank is "L" or "R" && offset > 32767 ||
             bank is "SM" && offset > 9999 || bank is "SD" && offset > 11999)) throw new InputError("RangeError");
        if (protocol == PlcProtocol.FINS && offset > (bank switch
        {
            "D" => 32767, "CIO" => 6143, "W" or "H" => 511, "A" => 959, _ => -1
        })) throw new InputError("RangeError");
        bool hasBit = m.Groups["bit"].Success;
        int bit = 0;
        if (hasBit && (!int.TryParse(m.Groups["bit"].Value, out bit) || bit > (word ? 15 : 7) || packed || type != DataKind.Bool))
            throw new InputError(error);
        // SiemensS7Net treats a byte address without a bit suffix as bit 0 for Bool access.
        if (protocol != PlcProtocol.S7 && !packed && type == DataKind.Bool && !hasBit) throw new InputError(error);
        var location = new NativeAddress(bank, offset * (packed ? 1 : word ? 16 : 8) + bit,
            word && type == DataKind.Bool && protocol == PlcProtocol.FINS);
        int width = NativeMemory.Width(type);
        int limit = packed ? 65536 : word ? 65536 * 16 : 65536 * 8;
        if (protocol == PlcProtocol.FxSerial) limit = bank == "M" ? 256 : 8512 * 16;
        if (protocol is PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii)
        {
            int max = bank switch
            {
                "X" or "Y" => 1023, "M" or "L" or "SB" => 32767, "B" => 0x7FEF,
                "SM" => 9999, "D" => 7999, "W" or "SW" or "R" => 32767,
                "SD" => 11999, _ => -1
            };
            limit = (max + 1) * (packed ? 1 : 16);
        }
        else if (protocol == PlcProtocol.FINS)
        {
            int max = bank switch { "D" => 32767, "CIO" => 6143, "W" or "H" => 511, "A" => 959, _ => -1 };
            limit = (max + 1) * 16;
        }
        if (location.Bit + width > limit) throw new InputError("RangeError");
        return location;
    }
}

// Each bank stores bytes in wire order. A single lock covers an entire protocol or UI operation.
public sealed class NativeMemory(PlcProtocol protocol) : IPointMemory
{
    public PlcProtocol Protocol { get; } = protocol;
    private readonly ConcurrentDictionary<string, byte[]> banks = new();
    internal static int Width(DataKind kind) => kind switch
    {
        DataKind.Bool => 1, DataKind.Int16 or DataKind.UInt16 => 16,
        DataKind.Int64 or DataKind.UInt64 or DataKind.Float64 => 64, _ => 32
    };
    internal byte[] Transfer(string bank, int bit, int count, byte[]? source = null, bool swapBitBytes = false)
    {
        if (Protocol == PlcProtocol.FINS)
        {
            int max = bank switch { "D" => 32767, "CIO" => 6143, "W" or "H" => 511, "A" => 959, _ => -1 };
            if (max < 0 || (long)bit + count > ((long)max + 1) * 16)
                throw new ArgumentOutOfRangeException(nameof(bit));
        }
        if (Protocol is PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii)
        {
            int max = bank switch
            {
                "X" or "Y" => 1023, "M" or "L" or "SB" => 32767, "B" => 0x7FEF,
                "SM" => 9999, "D" => 7999, "W" or "SW" or "R" => 32767,
                "SD" => 11999, _ => -1
            };
            int scale = bank is "X" or "Y" or "M" or "L" or "B" or "SB" or "SM" ? 1 : 16;
            if (max < 0 || (long)bit + count > ((long)max + 1) * scale)
                throw new ArgumentOutOfRangeException(nameof(bit));
        }
        int bytes = Protocol == PlcProtocol.S7 ? 65536 :
            Protocol is PlcProtocol.MC or PlcProtocol.MCAscii or PlcProtocol.FxSerial or PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii && bank is ("M" or "X" or "Y" or "B" or "L" or "SB" or "SM") ? 8192 : 131072;
        if (count < 1 || bit < 0 || (long)bit + count > bytes * 8 ||
            (source != null && source.Length != (count + 7) / 8)) throw new ArgumentOutOfRangeException(nameof(bit));
        var cells = banks.GetOrAdd(bank, _ => new byte[bytes]);
        var data = source ?? new byte[(count + 7) / 8];
        lock (cells)
        {
            if (!swapBitBytes && (bit | count) % 8 == 0)
            {
                if (source == null) Array.Copy(cells, bit / 8, data, 0, data.Length);
                else Array.Copy(data, 0, cells, bit / 8, data.Length);
            }
            else for (int i = 0; i < count; i++)
            {
                int at = (bit + i) ^ (swapBitBytes ? 8 : 0);
                int mask = 1 << (at % 8);
                if (source == null)
                {
                    if ((cells[at / 8] & mask) != 0) data[i / 8] |= (byte)(1 << (i % 8));
                }
                else if ((data[i / 8] & (1 << (i % 8))) != 0) cells[at / 8] |= (byte)mask;
                else cells[at / 8] &= (byte)~mask;
            }
        }
        return data;
    }
    private void Transform(byte[] data, WordOrder order, bool writing)
    {
        if (Protocol is PlcProtocol.MC or PlcProtocol.MCAscii or PlcProtocol.FxSerial or PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii && data.Length == 8 && order == WordOrder.CDAB)
        {
            Array.Reverse(data);
            return;
        }
        if (Protocol == PlcProtocol.FINS && data.Length == 8 && order == WordOrder.CDAB)
        {
            var copy = (byte[])data.Clone();
            if (writing)
            {
                copy.AsSpan(4, 4).CopyTo(data);
                copy.AsSpan(2, 2).CopyTo(data.AsSpan(4));
                copy.AsSpan(0, 2).CopyTo(data.AsSpan(6));
            }
            else
            {
                copy.AsSpan(6, 2).CopyTo(data);
                copy.AsSpan(4, 2).CopyTo(data.AsSpan(2));
                copy.AsSpan(0, 4).CopyTo(data.AsSpan(4));
            }
            return;
        }
        // Both swaps are self-inverse and commute.
        if (data.Length >= 4 && order == WordOrder.CDAB)
        {
            int half = data.Length / 2;
            for (int i = 0; i < half; i++) (data[i], data[half + i]) = (data[half + i], data[i]);
        }
        if (Protocol is PlcProtocol.MC or PlcProtocol.MCAscii or PlcProtocol.FxSerial or PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii)
            for (int i = 0; i + 1 < data.Length; i += 2) (data[i], data[i+1]) = (data[i+1], data[i]);
    }
    public void Write(PointSpec p)
    {
        var loc = NativeAddress.Parse(Protocol, p.AddressLabel, p.Type);
        ulong n = p.Encode();
        int bits = Width(p.Type);
        var data = new byte[(bits + 7) / 8];
        if (bits == 1) data[0] = (byte)n;
        else if (bits == 16) BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)n);
        else if (bits == 32) BinaryPrimitives.WriteUInt32BigEndian(data, (uint)n);
        else BinaryPrimitives.WriteUInt64BigEndian(data, n);
        Transform(data, p.Order, true);
        Transfer(loc.Bank, loc.Bit, bits, data, loc.SwapBitBytes);
    }
    public string Read(PointSpec p)
    {
        var loc = NativeAddress.Parse(Protocol, p.AddressLabel, p.Type);
        var data = Transfer(loc.Bank, loc.Bit, Width(p.Type), null, loc.SwapBitBytes);
        Transform(data, p.Order, false);
        var c = CultureInfo.InvariantCulture;
        return p.Type switch
        {
            DataKind.Bool => data[0] == 0 ? "false" : "true",
            DataKind.Int16 => BinaryPrimitives.ReadInt16BigEndian(data).ToString(c),
            DataKind.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(data).ToString(c),
            DataKind.Int32 => BinaryPrimitives.ReadInt32BigEndian(data).ToString(c),
            DataKind.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(data).ToString(c),
            DataKind.Int64 => BinaryPrimitives.ReadInt64BigEndian(data).ToString(c),
            DataKind.UInt64 => BinaryPrimitives.ReadUInt64BigEndian(data).ToString(c),
            DataKind.Float64 => BinaryPrimitives.ReadDoubleBigEndian(data).ToString("R", c),
            _ => BinaryPrimitives.ReadSingleBigEndian(data).ToString("R", c)
        };
    }
}
