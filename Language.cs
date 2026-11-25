using System.Collections.Generic;
using System.Windows;

namespace PlcSimulator;

internal static class UiText
{
    private static readonly Dictionary<string, (string Zh, string En)> Labels = new()
    {
        ["Title"] = ("PLC 模拟服务器", "PLC Simulator"),
        ["Server"] = ("Modbus TCP 服务端", "Modbus TCP server"),
        ["ListenIp"] = ("监听 IP", "Listen IP"), ["Port"] = ("端口", "Port"),
        ["Unit"] = ("站号", "Unit ID"), ["Start"] = ("启动服务器", "Start server"),
        ["Stop"] = ("停止服务器", "Stop server"), ["Idle"] = ("已停止", "Stopped"),
        ["Online"] = ("运行中", "Running"), ["Clients"] = ("客户端", "Clients"),
        ["Protocol"] = ("服务端协议", "Protocol"), ["Log"] = ("运行日志", "Event log"),
        ["Connected"] = ("客户端已连接", "Client connected"), ["Disconnected"] = ("客户端已断开", "Client disconnected"),
        ["Ready"] = ("模拟内存就绪", "Simulator ready"), ["ServerEvent"] = ("服务器事件", "Server event"),
        ["Switched"] = ("已切换协议，原点位任务已停止", "Protocol changed; previous item tasks stopped"),
        ["S7Hint"] = ("地址示例：DB1.0、DB1.DBW2、DB1.DBX4.0。Bool 省略位号时取第 0 位，如 M100 等同于 M100.0。", "Examples: DB1.0, DB1.DBW2, DB1.DBX4.0. For Bool, an omitted bit index means bit 0 (M100 = M100.0)."),
        ["MCHint"] = ("Q/L MC 3E：X/Y/M/L/B/SB/SM、D/W/SW/SD/R/ZR；X/Y/B/SB/W/SW 用十六进制。实际容量取决于 CPU 配置。", "Q/L MC 3E: X/Y/M/L/B/SB/SM, D/W/SW/SD/R/ZR; X/Y/B/SB/W/SW use hex. Capacity depends on CPU settings."),
        ["MCAsciiHint"] = ("Q/L MC 3E ASCII：X/Y/M/L/B/SB/SM、D/W/SW/SD/R/ZR；X/Y/B/SB/W/SW 用十六进制。", "Q/L MC 3E ASCII: X/Y/M/L/B/SB/SM, D/W/SW/SD/R/ZR; X/Y/B/SB/W/SW use hex."),
        ["Fx5SlmpHint"] = ("FX5U SLMP：X/Y 用八进制 0–1777（X10 为第 8 点）；M/L 0–32767，D 0–7999；另支持 B/SB/SM/W/SW/SD/R。", "FX5U SLMP: X/Y use octal 0–1777 (X10 is point 8); M/L 0–32767, D 0–7999; B/SB/SM/W/SW/SD/R also supported."),
        ["Fx5SlmpAsciiHint"] = ("FX5U SLMP ASCII (X/Y OCT)：X/Y 用八进制 0–1777；M/L 0–32767，D 0–7999；另支持 B/SB/SM/W/SW/SD/R。", "FX5U SLMP ASCII (X/Y OCT): X/Y use octal 0–1777; M/L 0–32767, D 0–7999; B/SB/SM/W/SW/SD/R also supported."),
        ["FxSerialHint"] = ("当前 FX3U 编程口透传实现支持 D0–D8511 字区及 M0–M255 位区。", "Current FX3U programming-port tunnel supports D0–D8511 words and M0–M255 bits."),
        ["FINSHint"] = ("CJ/CP FINS：D0–32767、CIO0–6143、W/H0–511、A0–959；位号写作 D100.3。部分 A 区在真实 CPU 上只读。", "CJ/CP FINS: D0–32767, CIO0–6143, W/H0–511, A0–959; use D100.3 for a bit. Some A words are read-only on real CPUs."),
        ["ModbusHint"] = ("地址示例：M100、D100、D100.3、X0、IR0。", "Address examples: M100, D100, D100.3, X0, IR0."),
        ["InovanceHint"] = ("汇川 H3U/H5U/Easy 地址示例：D100、M100、X0。AM 系列的地址规则不同。", "Inovance H3U/H5U/Easy examples: D100, M100, X0. AM uses different addresses."),
        ["S7AddressError"] = ("请检查 S7 地址和类型。字地址示例 DB1.0，位地址示例 DB1.0.3。支持 DB0–65535、M、I、Q；DB 使用标准访问。", "Check the S7 address/type. Word: DB1.0; bit: DB1.0.3. Areas: DB0–65535, M, I, Q; DB uses standard access."),
        ["MCAddressError"] = ("请检查 MC 地址和类型。支持 D、M、X、Y、L、B、SB、SM、W、SW、SD、R、ZR；X/Y/B/SB/W/SW 为十六进制。", "Check the MC address/type. D, M, X, Y, L, B, SB, SM, W, SW, SD, R and ZR are supported; X/Y/B/SB/W/SW use hex."),
        ["MCAsciiAddressError"] = ("请检查 MC ASCII 地址和类型。支持 D、M、X、Y、L、B、SB、SM、W、SW、SD、R、ZR。", "Check the MC ASCII address/type. Supported: D, M, X, Y, L, B, SB, SM, W, SW, SD, R and ZR."),
        ["Fx5SlmpAddressError"] = ("FX5U：X/Y 必须用八进制（0–7）；支持 D、M、X、Y、L、B、SB、SM、W、SW、SD、R。", "FX5U: X/Y must be octal (digits 0–7); supported: D, M, X, Y, L, B, SB, SM, W, SW, SD and R."),
        ["Fx5SlmpAsciiAddressError"] = ("FX5U ASCII (X/Y OCT)：X/Y 必须用八进制（0–7）。", "FX5U ASCII (X/Y OCT): X/Y must be octal (digits 0–7)."),
        ["FxSerialAddressError"] = ("当前 FX 模式支持 D0–D8511 字区及 M0–M255 位区，请检查地址和类型。", "Current FX mode supports D0–D8511 words and M0–M255 bits. Check the address and type."),
        ["FINSAddressError"] = ("请检查 FINS 地址和类型。支持 D、W、H、A、CIO。位地址请写作 D100.3。", "Check the FINS address/type. Areas: D, W, H, A, CIO. Example bit address: D100.3."),
        ["Points"] = ("监视点位", "Watch items"), ["Address"] = ("地址位", "Address"),
        ["Add"] = ("添加点位", "Add item"), ["Edit"] = ("编辑", "Edit"), ["Delete"] = ("删除", "Delete"),
        ["Sample"] = ("刷新周期 (ms)", "Refresh (ms)"), ["Apply"] = ("应用", "Apply"),
        ["Name"] = ("名称", "Name"), ["Type"] = ("数据类型", "Data type"),
        ["Mapping"] = ("Modbus 映射", "Modbus mapping"), ["Current"] = ("当前值", "Live value"),
        ["Value"] = ("待写值", "Write value"), ["Interval"] = ("循环间隔 (ms)", "Interval (ms)"),
        ["Timing"] = ("运行中的任务", "Active task"), ["Actions"] = ("写入操作", "Write actions"),
        ["Write"] = ("单次写入", "Write once"), ["Cycle"] = ("启动循环", "Start cycle"),
        ["Halt"] = ("停止任务", "Stop task"), ["Schedule"] = ("定时写入", "Schedule"),
        ["At"] = ("本地时间 (yyyy-MM-dd HH:mm:ss)", "Local time (yyyy-MM-dd HH:mm:ss)"),
        ["Order"] = ("多字节字序", "Multiword order"), ["Save"] = ("保存", "Save"),
        ["Cancel"] = ("取消", "Cancel"), ["Empty"] = ("选择或添加一个监视点位。", "Select or add a watch item."),
        ["Guide"] = ("M = 线圈　D = 保持寄存器　X = 离散输入　IR = 输入寄存器。地址编号从 0 开始，使用十进制。", "M = coil · D = holding register · X = discrete input · IR = input register. Numbers are decimal and start at 0."),
        ["EditNote"] = ("保存修改后，此点位的写入任务将停止。", "Saving changes stops this item’s active task."),
        ["TimeNote"] = ("到时写入该点位已保存的待写值。", "Writes the item’s saved value at the selected time."),
        ["Written"] = ("已写入", "Written"), ["Saved"] = ("已保存", "Saved"), ["Removed"] = ("已删除", "Deleted"),
        ["Scheduled"] = ("已设置定时写入", "Write scheduled"), ["CycleOn"] = ("循环写入已启动", "Cycle started"),
        ["Halted"] = ("任务已停止", "Task stopped"), ["SelectError"] = ("请先选择一个点位。", "Select a watch item first."),
        ["ActiveError"] = ("请先停止此点位正在运行的任务。", "Stop this item’s active task first."),
        ["NameError"] = ("请填写点位名称。", "Enter an item name."),
        ["AddressError"] = ("请输入有效地址位，例如 M100、D100、D100.3 或 100。编号范围为 0–65535。", "Enter an address such as M100, D100, D100.3 or 100. Numbers range from 0 to 65535."),
        ["PackedBits"] = ("每 16 位组成一个字，最低地址对应 bit 0", "16 bits per word, lowest address = bit 0"),
        ["RangeError"] = ("所选类型超出此协议或型号的地址区域。", "The selected type extends beyond this protocol or model's address area."),
        ["TypeError"] = ("数据类型与地址区域不匹配。", "The data type does not match the address area."),
        ["CoilSuffixError"] = ("线圈和离散输入地址已经指向单个位，请直接填写地址，如 M100。", "Coils and discrete inputs already address individual bits. Use an address such as M100."),
        ["BitRequired"] = ("读写寄存器中的 Bool 请指定位号，例如 D100.3（0–15）。", "For a register Bool, specify its bit, e.g. D100.3 (0–15)."),
        ["BitTypeError"] = ("带位号的寄存器地址请选 Bool。读取整字请直接填写寄存器地址。", "Select Bool for a register bit. Use the register address without a bit suffix for numeric values."),
        ["ValueError"] = ("待写值与数据类型不匹配。Bool 可填 true、false、1 或 0。", "The write value must match the selected type. For Bool use true, false, 1 or 0."),
        ["IntervalError"] = ("循环间隔须为至少 50 ms 的整数。", "Enter a whole interval of at least 50 ms."),
        ["SampleError"] = ("刷新周期范围为 50–60000 ms。", "Refresh interval must be 50–60000 ms."),
        ["TimeError"] = ("请输入将来的本地日期时间。", "Enter a future local date and time."),
        ["EndpointError"] = ("请输入本机 IPv4、1–65535 端口和 1–247 站号。", "Enter a local IPv4 address, port 1–65535 and unit ID 1–247."),
        ["ConfigError"] = ("配置文件无效。", "Invalid configuration file."),
        ["LoadError"] = ("配置加载失败", "Could not load settings"), ["Failed"] = ("操作失败", "Operation failed")
    };
    public static void Set(string code)
    {
        foreach (var pair in Labels) Application.Current.Resources[pair.Key] =
            code == "en" ? pair.Value.En : pair.Value.Zh;
    }
    public static string Get(string key) => (string)Application.Current.FindResource(key);
}

