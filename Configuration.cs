using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlcSimulator;

public enum Area { Coil, DI, HR, IR }
public enum DataKind { Bool, Int16, UInt16, Int32, UInt32, Float32, Int64, UInt64, Float64 }
public enum WordOrder { ABCD, CDAB }

public readonly record struct PlcAddress(Area Area, ushort Number, int? Bit = null)
{
    public override string ToString() => Area switch
    {
        Area.Coil => $"M{Number}", Area.HR => $"D{Number}",
        Area.DI => $"X{Number}", _ => $"IR{Number}"
    };
    public static bool TryParse(string text, DataKind type, out PlcAddress address)
    {
        address = default;
        var s = text.Trim().ToUpperInvariant();
        int i = 0;
        while (i < s.Length && char.IsAsciiLetter(s[i])) i++;
        var suffix = s.AsSpan(i);
        int dot = suffix.IndexOf('.');
        int? bit = null;
        if (dot >= 0)
        {
            if (!int.TryParse(suffix[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int b) ||
                b is < 0 or > 15) return false;
            bit = b;
            suffix = suffix[..dot];
        }
        Area? area = s[..i] switch
        {
            "" => type == DataKind.Bool && bit == null ? Area.Coil : Area.HR,
            "M" or "COIL" => Area.Coil, "D" or "HR" => Area.HR,
            "X" or "DI" => Area.DI, "IR" => Area.IR, _ => null
        };
        if (area == null || !ushort.TryParse(suffix, NumberStyles.None,
            CultureInfo.InvariantCulture, out var number)) return false;
        address = new PlcAddress(area.Value, number, bit);
        return true;
    }
}

public sealed record PointSpec(string Name, Area Area, ushort Address, DataKind Type,
    WordOrder Order, string WriteValue, int IntervalMs)
{
    public PlcProtocol Protocol { get; init; } = PlcProtocol.Modbus;
    public string? AddressText { get; init; }
    public int? RegisterBit { get; init; }
    [JsonIgnore] public bool Wide => Type is DataKind.Int32 or DataKind.UInt32 or DataKind.Float32 or DataKind.Int64 or DataKind.UInt64 or DataKind.Float64;
    [JsonIgnore] public bool IsBit => Area is Area.Coil or Area.DI;
    [JsonIgnore] public int Width => IsBit && Type != DataKind.Bool ? (Type is DataKind.Int64 or DataKind.UInt64 or DataKind.Float64 ? 64 : Wide ? 32 : 16) :
        Type is DataKind.Int64 or DataKind.UInt64 or DataKind.Float64 ? 4 : Wide ? 2 : 1;
    [JsonIgnore] public string AddressLabel => AddressText ?? new PlcAddress(Area, Address).ToString();
    [JsonIgnore] public string Mapping => RegisterBit.HasValue ? $"{Area}[{Address}].{RegisterBit}" :
        Width == 1 ? $"{Area}[{Address}]" : $"{Area}[{Address}…{Address + Width - 1}]" + (Wide ? $" · {Order}" : "");

    public static PointSpec FromText(string name, string text, DataKind type, WordOrder order, string value, int interval)
    {
        if (!PlcAddress.TryParse(text, type, out var addr)) throw new InputError("AddressError");
        return new PointSpec(string.IsNullOrWhiteSpace(name) ? text.Trim() : name.Trim(), addr.Area, addr.Number,
            type, order, value.Trim(), interval) { AddressText = text.Trim(), RegisterBit = addr.Bit };
    }
    public static PointSpec ForProtocol(PlcProtocol protocol, string name, string text, DataKind type, WordOrder order, string value, int interval)
    {
        if (protocol == PlcProtocol.Modbus) return FromText(name, text, type, order, value, interval);
        if (protocol == PlcProtocol.Inovance)
        {
            if (!PlcAddress.TryParse(text, type, out var address) || address.Bit.HasValue) throw new InputError("AddressError");
            var area = address.Area == Area.DI ? Area.Coil : address.Area;
            var index = address.Area == Area.DI ? address.Number + 63488 : address.Number;
            if (index > ushort.MaxValue) throw new InputError("RangeError");
            return new PointSpec(string.IsNullOrWhiteSpace(name) ? text.Trim() : name.Trim(), area, (ushort)index,
                type, order, value.Trim(), interval) { Protocol = protocol, AddressText = text.Trim() };
        }
        return new PointSpec(string.IsNullOrWhiteSpace(name) ? text.Trim() : name.Trim(), Area.HR, 0,
            type, order, value.Trim(), interval) { Protocol = protocol, AddressText = text.Trim() };
    }

    public void ValidateAddress()
    {
        if (Protocol == PlcProtocol.Inovance)
        {
            if (ForProtocol(Protocol, Name, AddressText ?? "", Type, Order, WriteValue, IntervalMs).Address != Address)
                throw new InputError("AddressError");
            if (IsBit && Type != DataKind.Bool) throw new InputError("TypeError");
            return;
        }
        if (Protocol != PlcProtocol.Modbus)
        {
            _ = NativeAddress.Parse(Protocol, AddressText ?? "", Type);
            return;
        }
        if (IsBit && RegisterBit.HasValue) throw new InputError("CoilSuffixError");
        if (!IsBit && Type == DataKind.Bool && !RegisterBit.HasValue) throw new InputError("BitRequired");
        if (RegisterBit.HasValue && (Type != DataKind.Bool || RegisterBit.Value is < 0 or > 15)) throw new InputError("BitTypeError");
        if (Address + Width > 65536) throw new InputError("RangeError");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InputError("NameError");
        if (!Enum.IsDefined(Protocol) || !Enum.IsDefined(Area) || !Enum.IsDefined(Type) || !Enum.IsDefined(Order)) throw new InputError("TypeError");
        if (Protocol == PlcProtocol.Modbus && AddressText != null && (!PlcAddress.TryParse(AddressText, Type, out var parsed) ||
            parsed != new PlcAddress(Area, Address, RegisterBit))) throw new InputError("AddressError");
        ValidateAddress();
        if (IntervalMs < 50) throw new InputError("IntervalError");
        _ = Encode();
    }

    public ulong Encode()
    {
        var text = WriteValue?.Trim();
        var c = CultureInfo.InvariantCulture;
        switch (Type)
        {
            case DataKind.Bool:
                if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return 1;
                if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return 0;
                break;
            case DataKind.UInt16 when ushort.TryParse(text, NumberStyles.Integer, c, out var u): return u;
            case DataKind.Int16 when short.TryParse(text, NumberStyles.Integer, c, out var s): return unchecked((ushort)s);
            case DataKind.Int32 when int.TryParse(text, NumberStyles.Integer, c, out var i): return unchecked((uint)i);
            case DataKind.UInt32 when uint.TryParse(text, NumberStyles.Integer, c, out var n): return n;
            case DataKind.Float32 when float.TryParse(text, NumberStyles.Float, c, out var f) && float.IsFinite(f):
                return BitConverter.SingleToUInt32Bits(f);
            case DataKind.Int64 when long.TryParse(text, NumberStyles.Integer, c, out var l): return unchecked((ulong)l);
            case DataKind.UInt64 when ulong.TryParse(text, NumberStyles.Integer, c, out var ul): return ul;
            case DataKind.Float64 when double.TryParse(text, NumberStyles.Float, c, out var d) && double.IsFinite(d):
                return unchecked((ulong)BitConverter.DoubleToInt64Bits(d));
        }
        throw new InputError("ValueError");
    }
}

public sealed class InputError(string key) : Exception(key);

public sealed class Configuration
{
    public PlcProtocol Protocol { get; set; }
    public Dictionary<PlcProtocol, int> NativePorts { get; set; } = [];
    public void Prepare(PlcProtocol mode)
    {
        if (mode == PlcProtocol.Modbus || NativePorts.ContainsKey(mode)) return;
        NativePorts[mode] = mode switch { PlcProtocol.S7 => 102, PlcProtocol.MC => 5000, PlcProtocol.FINS => 9600,
            PlcProtocol.MCAscii => 5001, PlcProtocol.FxSerial => 5002,
            PlcProtocol.Fx5Slmp => 5003, PlcProtocol.Fx5SlmpAscii => 5004, _ => 1503 };
        if (mode == PlcProtocol.Inovance)
        {
            Points.Add(PointSpec.ForProtocol(mode, "D100", "D100", DataKind.UInt16, WordOrder.ABCD, "80", 1000));
            Points.Add(PointSpec.ForProtocol(mode, "M100", "M100", DataKind.Bool, WordOrder.ABCD, "true", 500));
            return;
        }
        var word = mode == PlcProtocol.S7 ? "DB1.0" : "D100";
        var bit = mode switch { PlcProtocol.S7 => "M0.0", PlcProtocol.MC or PlcProtocol.MCAscii or PlcProtocol.FxSerial or PlcProtocol.Fx5Slmp or PlcProtocol.Fx5SlmpAscii => "M100", _ => "D101.0" };
        var order = mode == PlcProtocol.S7 ? WordOrder.ABCD : WordOrder.CDAB;
        Points.Add(PointSpec.ForProtocol(mode, word, word, DataKind.UInt16, order, "80", 1000));
        Points.Add(PointSpec.ForProtocol(mode, bit, bit, DataKind.Bool, order, "true", 500));
    }
    public string Language { get; set; } = "zh";
    public string ListenIp { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1502;
    public byte Unit { get; set; } = 1;
    public int SampleMs { get; set; } = 200;
    public List<PointSpec> Points { get; set; } = [];
    public static readonly string LegacyFilePath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ModbusPlcSimulator", "settings.json");
    public static readonly string FilePath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "PlcSimulator", "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true,
        Converters = { new JsonStringEnumConverter() } };

    public static IPAddress ParseEndpoint(string ip, int port, int unit)
    {
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
            port is < 1 or > 65535 || unit is < 1 or > 247) throw new InputError("EndpointError");
        return address;
    }

    public static Configuration Load(string path, string? legacyPath = null)
    {
        if (!File.Exists(path) && legacyPath != null && File.Exists(legacyPath)) path = legacyPath;
        if (!File.Exists(path)) return new Configuration { Points = [
            new("M100", Area.Coil, 100, DataKind.Bool, WordOrder.ABCD, "true", 500),
            new("D100", Area.HR, 100, DataKind.UInt16, WordOrder.ABCD, "80", 1000)] };
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), Options)
            ?? throw new InputError("ConfigError");
        ParseEndpoint(config.ListenIp, config.Port, config.Unit);
        if (!Enum.IsDefined(config.Protocol) || config.NativePorts == null) throw new InputError("ConfigError");
        if (config.SampleMs is < 50 or > 60000 || config.Points is null) throw new InputError("ConfigError");
        foreach (var point in config.Points) point.Validate();
        return config;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }
}
