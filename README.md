# PLC 模拟服务器 | PLC Simulator

[简体中文](#简体中文) · [English](#english)

## 简体中文

用于在 Windows 上联调视觉检测和工业上位机。软件提供可读写的模拟 PLC 点位，支持 Modbus TCP、西门子 S7、三菱 MC/SLMP/FX、欧姆龙 FINS 和汇川兼容模式。各模式的地址示例和覆盖范围见[使用说明](产品使用说明.md)和[地址覆盖说明](ADDRESS_COVERAGE.md)。

### 下载

在仓库的 [Releases](../../releases) 页面选择 Windows x64 下载包。两个版本的模拟器功能相同。

| 下载文件 | 如何启动 |
| --- | --- |
| `PlcSimulator-win-x64-Lite.zip` | 安装 [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0) 后，解压并双击 `PlcSimulator.exe`。已有运行时可直接启动 |
| `PlcSimulator-win-x64-WithRuntime.zip` | 解压并双击 `启动模拟器.cmd`。已有运行时会直接启动。缺少时，按 Y 打开随包附带的微软安装程序，按 N 退出 |

### 快速联调

1. 在模拟器选择与上位机相同的服务端协议。本机联调时，监听 IP 和客户端地址都填 `127.0.0.1`。
2. 核对端口后点击**启动服务器**，再连接上位机。
3. 添加或编辑点位，设置地址和数据类型。点击**单次写入**后，在客户端读取同一地址，确认值一致。

点位支持实时监视、单次写入、循环写入和定时写入。界面可切换简体中文与英文。

### 从源码构建

在 Windows 上使用 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，在工程目录运行：

```powershell
dotnet build PlcSimulator.csproj -c Release
```

Modbus TCP 使用 NuGet 包 NModbus 3.0.83。其版权和 MIT 许可随发布包提供，也可查阅[NModbus 许可证](https://github.com/NModbus/NModbus/blob/master/LICENSE.txt)。

## English

PLC Simulator is a Windows desktop tool for testing machine vision and industrial PC applications. It provides readable and writable PLC points and supports Modbus TCP, Siemens S7, Mitsubishi MC/SLMP/FX, Omron FINS, and Inovance compatibility modes. See the [user guide](产品使用说明.md#english) for examples and the [address coverage guide](ADDRESS_COVERAGE.md#english) for supported areas and limits.

### Downloads

Both Windows x64 packages provide the same simulator features. Download either package from [Releases](../../releases).

| Package | How to start |
| --- | --- |
| `PlcSimulator-win-x64-Lite.zip` | Extract the package and open `PlcSimulator.exe`. Install [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0) first if needed |
| `PlcSimulator-win-x64-WithRuntime.zip` | Extract the package and open `启动模拟器.cmd`. It starts the simulator directly if the runtime is present. If prompted, press Y to open the bundled Microsoft installer or N to exit |

### Quick start

1. Select the same protocol as your client. When testing on one PC, use `127.0.0.1` for both the simulator's listen address and the client's destination.
2. Check the port, click **Start server**, and connect the client.
3. Add or edit a watch item. Set its address and data type, click **Write once**, and read the same address from the client to confirm the value.

The app supports live monitoring, one-time writes, cyclic writes, scheduled writes, and both Simplified Chinese and English.

### Build from source

On Windows, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and run the following command from the project directory.

```powershell
dotnet build PlcSimulator.csproj -c Release
```

Modbus TCP uses the NModbus 3.0.83 NuGet package. Release packages include its copyright and MIT license. [NModbus license](https://github.com/NModbus/NModbus/blob/master/LICENSE.txt)
