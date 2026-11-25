# PLC Simulator User Guide

[简体中文](产品使用说明.md)

PLC Simulator provides a virtual PLC endpoint for testing machine vision and industrial PC applications without physical PLC hardware. The client and simulator interface can read and write the same simulated memory. Use it to check connections, trigger signals, and result writes.

## 1. Start the application

1. On 64-bit Windows, extract the entire ZIP. For the package with the runtime installer, open `启动模拟器.cmd`. If the .NET 10 Desktop Runtime x64 is already installed, the simulator opens directly. Otherwise, press `Y` to open the bundled Microsoft installer or `N` to exit. Select **Install** and approve the Windows administrator prompt if it appears. The launcher checks again and starts the simulator when the runtime is available.
2. For the Lite package, install [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0) if needed, then open `PlcSimulator.exe`.
3. In **Protocol**, select the mode used by your client. Set **Listen IP** and **Port**. For testing on the same PC, use `127.0.0.1` at both ends. For LAN testing, use the simulator PC's LAN IPv4 address as both the listen address and the client's destination.
4. For Modbus TCP and Inovance modes, check the **Unit ID**. The default is `1`.
5. Click **Start server**, wait until the status shows **Running**, then connect your client. Click **Stop server** before changing the protocol or port.

Switch between **简体中文** and **English** in the upper-right corner.

## 2. Protocols and address examples

| Server mode | Default port | Address examples |
| --- | ---: | --- |
| Modbus TCP | 1502 | `M100` coil, `D100` holding register, `X0` discrete input, `IR0` input register |
| Siemens S7-1200 / S7-1500 | 102 | `M100.0`, `I0.0`, `Q0.0`, `DB1.DBW2` |
| Mitsubishi Q/L MC 3E Binary | 5000 | `D100`, `M100`, `X1A`, `W10` |
| Mitsubishi Q/L MC 3E ASCII | 5001 | `D100`, `M100`, `X1A`, `W10` |
| Mitsubishi FX3U Serial OverTcp | 5002 | `D100`, `M100` |
| Mitsubishi FX5U SLMP 3E Binary | 5003 | `D100`, `M100`, `X10` in the simulator |
| Mitsubishi FX5U SLMP 3E ASCII (X/Y OCT) | 5004 | `D100`, `M100`, `X10` |
| Omron CJ/CP FINS/TCP | 9600 | `D100`, `D100.3`, `W10`, `CIO0.1` |
| Inovance H3U/H5U/Easy TCP | 1503 | `D100`, `M100` |

Use the port shown in the simulator if it differs from the default. Match the frame type as well as the protocol. Mitsubishi MC Binary, MC ASCII, and FX serial tunneling use different frames.

FX5U uses octal X/Y notation in the simulator. `X10` means point 8. If a binary client expects a hexadecimal device number, enter `X8` for the same point. For ASCII (X/Y OCT), enter `X10`. Q/L MC uses hexadecimal X/Y notation, such as `X1A`.

## 3. Add and monitor watch items

Click **Add item**, or double-click a row to edit it.

| Field | Example | Purpose |
| --- | --- | --- |
| Name | Inspection trigger | Identifies the point in the watch list |
| Address | `M100` | Uses the selected protocol's address syntax |
| Data type | `Bool` | Determines how the value is read and written |
| Multiword order | `ABCD` or `CDAB` | Match the client's settings for 32-bit and 64-bit values |
| Write value | `true` | A Bool also accepts `false`, `1`, or `0` |
| Interval (ms) | `1000` | Writes once per second in a cycle. The minimum is 50 ms |

Supported types include Bool, signed and unsigned 16-, 32-, and 64-bit integers, Float32, and Float64. The address stays as entered when you select a different type. The simulator checks the address format and how much space the type occupies.

**Live value** shows the simulated memory. **Write value** is used when a write runs. To change the display refresh interval, set **Refresh (ms)** and click **Apply**. The default is 200 ms, and the range is 50–60,000 ms. The client controls its own polling interval.

## 4. Write values

| Action | How to use it |
| --- | --- |
| **Write once** | Writes the row's configured value immediately |
| **Start cycle** | Repeats the same value at the configured interval. The first write occurs after one interval |
| **Schedule** | Select the row and click **Schedule**. Enter a future local time in `yyyy-MM-dd HH:mm:ss` format to write once at that time |
| **Stop task** | Stops the row's active task. Saving an edit or deleting the row also cancels its task |

To simulate a `false → true → false` pulse manually, set and write each of the three values in sequence using **Write once**.

## 5. Example: test a trigger and result with Mitsubishi MC 3E

1. Select **Mitsubishi Q/L MC 3E Binary**, listen on `127.0.0.1:5000`, and start the server.
2. Select the matching MC 3E Binary driver in your client and connect to `127.0.0.1:5000`.
3. Add or edit `M100` as a `Bool`. Set the write value to `true`, click **Write once**, and check that the client reads `true`.
4. Add or edit `D100` as a `UInt16`. Write `123` from the client and check that the simulator displays `123` as the live value.
5. Set the simulator's write value to `456` and click **Write once**. Read `D100` from the client to confirm it returns `456`.

## 6. Troubleshooting

| Symptom | What to check |
| --- | --- |
| The server remains stopped | Open the **Event log** and check for a port already in use or a listen address that does not belong to this PC |
| The local client connects but another PC cannot | Check the LAN listen address, destination port, and Windows Firewall |
| TCP connects but reads fail | Check the protocol, Binary/ASCII selection, and frame type. Test a known address such as `D100` or `M100` |
| A Modbus client reads the wrong address | Check the unit ID, register area, and zero-based versus one-based addressing. Simulator `D100` means holding register offset 100 |
| A Bool differs between client and simulator | Check the bit address, such as `M100.0` for Siemens or `D100.3` for Omron |
| 16-bit values work but wider values do not | Check the data type, starting address, multiword order, and overlapping points |
| The live value stays the same after editing the write value | Run a one-time, cyclic, or scheduled write. Watch item configurations persist, while live memory and active tasks reset when the app restarts |

## 7. Scope

This version simulates protocol communication and point memory for connectivity and read/write testing. Validate PLC ladder logic, physical I/O, and scan-cycle behavior in the relevant PLC environment. S7 supports absolute addresses in standard, non-optimized DBs. FX3U serial tunneling covers a subset of D/M, Omron EM is currently unavailable, and Inovance coverage depends on the implemented point mappings.
