# 地址区域与型号边界 | Address Coverage and Model Limits

[简体中文](#简体中文) · [English](#english)

## 简体中文

本程序模拟通信服务端，不运行真实 PLC 的扫描周期、系统软元件或 I/O 模块。地址位显示采用用户习惯的 PLC 写法；可用区域同时受 **CPU 型号、通信帧、PLC 工程中的设备分配** 影响。下表描述当前实现，不能作为各厂家所有型号的通用地址表。

| 界面模式 | 当前可用的地址区域 | 编号与边界 |
| --- | --- | --- |
| S7-1200 / S7-1500 | I、Q、M、DB | 字节编号 0–65535，DB 号 0–65535。DB 为标准、非优化绝对地址 |
| Q/L MC 3E Binary / ASCII | X、Y、M、L、B、SB、SM、D、W、SW、SD、R、ZR | X/Y/B/SB/W/SW 为十六进制，其余为十进制。模拟器每区最多 65536 位或字，具体 CPU 的设备分配尚未按型号限制 |
| FX5U SLMP 3E Binary / ASCII (X/Y OCT) | X、Y、M、L、B、SB、SM、D、W、SW、SD、R | 界面 X/Y 为八进制 0–1777，M/L/R 0–32767，B 0–7FEFh，SB/W/SW 0–7FFFh，SM 0–9999，D 0–7999，SD 0–11999。实际项目还受设备和标签内存分配限制 |
| FX3U Serial OverTcp | D、M | 目前 D0–D8511、M0–M255 |
| CJ/CP FINS/TCP | CIO、W、H、A、D | CIO 0–6143，W/H 0–511，A 0–959，D 0–32767。部分 A 区在真实 CPU 上只读，模拟器尚未限制其写入 |
| 汇川 H3U/H5U/Easy TCP | D、M、X 等当前映射 | 目前沿用通信适配映射，尚未按具体 CPU 建立完整软元件目录 |
| Modbus TCP | M/Coil、X/DI、D/HR、IR | 0–65535，按 Modbus 数据区解释 |

FX5U 界面以八进制显示 X/Y，`X10` 表示第 8 点。二进制帧使用数值 8。若客户端要求用十六进制填写设备编号，可输入 `X8`。本软件的 ASCII 模式采用 **X/Y OCT**，该点填写 `X10`。

真实 Q/L、FX5U 的设备容量可能由 CPU 参数配置改变；本模拟器不会伪造这些参数。西门子优化 DB 无法用本程序当前的 S7ANY 绝对地址接口访问。FX3U 经 Ethernet 模块使用的 MC 1E 帧也不是这里的 MC 3E 模式。

下一步的型号目录不能仅凭厂家名称推断：例如汇川 H3U 手册列出 M0–M7679、M8000–M8511 以及 D0–D8511，但当前汇川模式并未完整实现这些系统区；欧姆龙 CJ2 的 EM 扩展区随 CPU 容量变化，目前 FINS 服务端也未实现 EM。三菱 iQ-R 与 FX3U MC 1E 帧尚未联调。界面只把已实现且通过客户端验证的范围标为可用。

## English

The simulator implements PLC communication and point memory. Actual device availability depends on the CPU model, frame type, and the device allocation in the PLC project.

| Simulator mode | Supported areas | Numbering and limits |
| --- | --- | --- |
| S7-1200 / S7-1500 | I, Q, M, DB | Byte offsets 0–65535 and DB numbers 0–65535. Use absolute addresses in standard, non-optimized DBs |
| Q/L MC 3E Binary / ASCII | X, Y, M, L, B, SB, SM, D, W, SW, SD, R, ZR | X/Y/B/SB/W/SW are hexadecimal. The rest are decimal. The simulator allows up to 65,536 bits or words per area, without enforcing a specific CPU's allocation |
| FX5U SLMP 3E Binary / ASCII (X/Y OCT) | X, Y, M, L, B, SB, SM, D, W, SW, SD, R | X/Y use octal 0–1777 in the simulator. M/L/R 0–32767, B 0–7FEFh, SB/W/SW 0–7FFFh, SM 0–9999, D 0–7999, SD 0–11999. Actual projects depend on device and label allocation |
| FX3U Serial OverTcp | D, M | D0–D8511 and M0–M255 |
| CJ/CP FINS/TCP | CIO, W, H, A, D | CIO 0–6143, W/H 0–511, A 0–959, D 0–32767. Some real CPUs make parts of A read-only. The simulator currently permits writes there |
| Inovance H3U/H5U/Easy TCP | D, M, X and other implemented mappings | Coverage follows the current protocol mapping, with partial coverage of model-specific device areas |
| Modbus TCP | M/Coil, X/DI, D/HR, IR | 0–65535, interpreted according to the Modbus data area |

The FX5U interface displays X/Y in octal, so `X10` means point 8. A client that takes hexadecimal device numbers for binary frames can address the same point as `X8`. In ASCII mode, use the simulator's **X/Y OCT** format and enter `X10`.

On real Q/L and FX5U controllers, capacities can vary with CPU parameters. The current S7ANY interface supports absolute access to standard DBs. Omron CJ2 EM banks are unavailable in this FINS implementation. Mitsubishi iQ-R and the MC 1E frame used by FX3U Ethernet modules have yet to be validated.

## 参考手册 | Reference manuals

- [三菱 FX5 SLMP 帧及 X/Y 编号](https://dl.mitsubishielectric.com/dl/fa/document/manual/plcf/jy997d56001/jy997d56001k.pdf)
- [三菱 FX5、Q、L、FX3 设备范围](https://dl.mitsubishielectric.com/dl/fa/document/manual/plcf/sh082625eng/sh082625engf.pdf)
- [三菱 QnU MC 设备代码](https://dl.mitsubishielectric.com/dl/fa/document/manual/plc/sh080811eng/sh080811engy.pdf)
- [西门子 S7 绝对地址与 DB](https://docs.tia.siemens.cloud/r/simatic_s7_1200_manual_collection_enus_20/plc-concepts/data-storage-memory-areas-i/o-and-addressing/accessing-the-data-of-the-cpu)
- [欧姆龙 CJ2 软元件容量](https://www.ia.omron.com/data_pdf/cat/cj2h_cj2m_p059-e1_17_27_csm1004123.pdf?id=2712)
- [汇川 H3U/H3S 编程手册](https://portal-file.inovance.com/owfile/ProdDoc/SC/19010575-SC/A04/19010575-SC_A04%E3%80%8AH3U%20H3S%20Series%20Programmable%20Logic%20Controller%20Instructions%20and%20Programming%20Guide%E3%80%8B-EN.pdf)
