# IndustrialDeviceCommunication

面向工业设备和仪器连接的 .NET 通讯库，Apache-2.0，版权 fangxinghui。

代码覆盖 PLC、Modbus、CAN、TCP、MQTT、串口、USB VISA、数字量 IO 和模拟量采集。能不能连上某台设备，取决于本机是否安装了对应的厂商驱动或运行时。本仓库不附带这些专有二进制文件。

## 项目结构

```text
IndustrialDeviceCommunication-OSS/
├─ IndustrialDeviceCommunication.sln
├─ Directory.Build.props
├─ LICENSE                         # Apache-2.0
├─ NOTICE                          # 厂商 SDK 与可能的示例代码
├─ DeviceCommunication/            # 接口、配置、工厂和协议实现
└─ CoreShared/                     # 地址、数据转换、信号映射、日志事件
```

没有 `dll/` 目录。Advantech `Automation.BDaq.dll`、`Automation.BDaq4.dll` 和商业版 `HslCommunication.dll` 都不再随仓库或 NuGet 包分发。

### DeviceCommunication

- 控制器、通讯口、数字量 IO、模拟量 IO、CAN 接口
- Modbus RTU / TCP 和共享总线
- TCP、MQTT、串口
- Siemens S7、Omron FINS（默认不编译，见下文）、Beckhoff ADS
- ZLG CAN、研华 BDaq、NI-DAQmx、阿尔泰 ART-DAQ
- USB VISA（仅 `net9.0` 编译引用；运行时仅 Windows）
- Mock 控制器，便于没有硬件时做基本联调

### CoreShared

`ControllerAddress`、数据类型转换、模拟信号映射，以及日志和异常事件。这些类型出现在 `DeviceCommunication` 的公开接口上，两个项目一起使用。

## 目标框架

- `CoreShared`：`netstandard2.1`
- `DeviceCommunication`：`netstandard2.1`、`net9.0`

需要能编译 `net9.0` 的 .NET SDK。默认 `dotnet build` 已在 Linux 上用 SDK 10.0.402 编过 `netstandard2.1` 和 `net9.0`（0 error，原有 nullable 警告还在）。这次没有在 Windows 上再编一遍。

## 构建

Linux 和 Windows 使用同一组命令。默认构建不需要 HslCommunication、DAQNavi、NI、ART 或 ZLG 的安装包。Beckhoff ADS 和 IVI VISA 通过 NuGet 还原，不把它们的程序集打进本仓库的包里。

```bash
dotnet restore ./IndustrialDeviceCommunication.sln
dotnet build ./IndustrialDeviceCommunication.sln -c Release --no-restore
```

输出在各项目的 `bin/Release` 目录。

### HslCommunication（Siemens S7 与 Omron FINS）

`OperationResult` 是本库自己的结果类型，不再继承 `HslCommunication.OperateResult`。默认构建不引用 Hsl。

Siemens 和 Omron 的真实驱动仍调用 Hsl。需要它们时，自行安装有授权的 `HslCommunication.dll`，然后：

```bash
dotnet build ./IndustrialDeviceCommunication.sln -c Release \
  -p:IncludeHslDrivers=true \
  -p:HslCommunicationDll=/path/to/HslCommunication.dll
```

该 DLL 必须能被 `netstandard2.1` 和 `net9.0` 同时引用（通常是 netstandard2.0 构建）。引用的 `Private` 为 false，不会复制进输出，也不会打进 NuGet 包。未打开这个开关时，编译的是 `SiemensNet.Stub.cs`：类型还在，连接和读写会失败并说明如何打开开关。`OmronFinsTcp.cs` 在默认构建中不参与编译。原来的代码里这个类也没有 `[ControllerDriver]`，工厂不会创建它。

### 需要自行安装的厂商运行时

| 能力 | 本仓库中的代码 | 运行时 |
|---|---|---|
| 研华数字量 IO | 反射加载 `Automation.BDaq`，无编译期引用 | 安装 Advantech DAQNavi，把 `Automation.BDaq.dll` 放在程序目录，或把路径传给 `BDaqInterop.Load` |
| Siemens S7 / Omron FINS | 仅在 `IncludeHslDrivers=true` 时编译 | 自行提供 `HslCommunication.dll` |
| Beckhoff ADS | 引用 NuGet `Beckhoff.TwinCAT.Ads` 7.0.172 与 `Beckhoff.TwinCAT.Ads.TcpRouter` 7.0.172 | 由 NuGet 还原。不嵌入到 `DeviceCommunication.dll` |
| NI 采集 | P/Invoke `nicaiu.dll` | 安装 NI-DAQmx |
| 阿尔泰采集 | P/Invoke `Art_DAQ.dll` | 安装 ART-DAQ |
| ZLG CAN | P/Invoke，见 `ZLGAPI.cs` | 安装 ZLG CAN 库 |
| USB VISA | `net9.0` 引用 `IviFoundation.Visa`；非 Windows 在运行时直接报告不可用 | Windows 上安装 NI-VISA 或其他 VISA 运行时 |

NI、ART、ZLG 的 P/Invoke 在没有厂商库时仍能通过编译，缺的是运行时。

## 引用

```xml
<ItemGroup>
  <ProjectReference Include="..\IndustrialDeviceCommunication-OSS\DeviceCommunication\DeviceCommunication.csproj" />
</ItemGroup>
```

`DeviceCommunication` 会带上 `CoreShared`。需要直接使用 `ControllerAddress` 时可以再显式引用 `CoreShared`。

NuGet 元数据：`PackageLicenseExpression` 为 `Apache-2.0`，`RepositoryUrl` 为 `https://github.com/fangxinghui/IndustrialDeviceCommunication-OSS`。

## 可能来自厂商示例的文件

这些文件保留在树里，发布前需要确认版权。详见 `NOTICE`。

- `DeviceCommunication/Protocols/CanBus/ZLG/ZLGAPI.cs`（UTF-8 BOM，字节未改）
- `DeviceCommunication/Protocols/IOCard/NationalInstruments/NIDaqNative.cs`
- `DeviceCommunication/Protocols/IOCard/NationalInstruments/NIDaqInterop.cs`
- `DeviceCommunication/Protocols/IOCard/ArtTechnology/ArtDaqNative.cs`
- `DeviceCommunication/Protocols/IOCard/ArtTechnology/ArtDaqInterop.cs`
- `DeviceCommunication/Protocols/IOCard/BDaqInterop.cs`

## 源文件编码

- `DeviceCommunication/Protocols/ModbusSlaveController.cs` 已转为 UTF-8（无 BOM）。三处 `//` 注释原来是 GBK，转换后仍是：必须在锁内设置 `_released`、防止竞态、本线程释放总线。类注释原来是损坏的 UTF-8（「享」和句号的最后一个字节被换成了 `0x3F`），已还原为：`绑定到共享Modbus 总线上的单个从站视图。`
- `DeviceCommunication/Protocols/CanBus/ZLG/ZLGAPI.cs` 带 UTF-8 BOM（`EF BB BF`）。这个文件没有改过。

## 许可证

Apache License 2.0。版权 2026 fangxinghui。详见 `LICENSE` 和 `NOTICE`。

厂商 SDK、HslCommunication，以及可能抄自厂商示例的互操作文件，各自另有许可。Apache-2.0 不能代替那些许可。
