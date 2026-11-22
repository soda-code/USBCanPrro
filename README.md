# CanUSB - 现代化 C# WinForms CAN/CAN-FD 上位机

基于 **Schildkroet/CANgaroo** 设计理念开发的现代化 C# WinForms 工业上位机。
针对 **CANable**、**CANable 2.0**、**USB2CANFDV1** 以及各类基于 **slcan** / 增强型 CAN FD 固件的硬件适配器深度定制。

---

## 🌟 核心特性

1. **现代化全圆角 UI 设计**：
   - 遵从工业 HMI 与圆角侧边栏设计规范，消除传统 WinForms 生硬刺眼的直角灰阶感。
   - 自定义封装了抗锯齿的 `RoundedCardPanel`、`ModernButton` 与无边框输入框。
2. **高吞吐量虚拟化渲染（Virtual DataGridView）**：
   - 采用 `ConcurrentQueue` 异步环形队列与 30ms 批量缓冲技术，5000+ fps 高频报文接收不卡死、不假死。
   - 所有时间戳与 Hex 报文数据采用等宽字体（Consolas）与 `tabular-nums` 排版，防止数值宽度抖动。
3. **协议支持**：
   - 兼容标准 slcan 指令集（`t`/`T`/`r`/`R`）。
   - 扩展支持 CAN FD 报文解析（`b`/`B` 格式及变长 64 字节数据载荷）。
   - 支持自动计算并显示总线实时帧率（Frames/s）与总帧数统计。

---

## 🚀 编译与运行要求

- **开发环境**：Visual Studio 2022 或 VS Code + .NET SDK
- **目标框架**：.NET 8.0-windows (C# 12)
- **依赖库**：`System.IO.Ports` (8.0.0+)

### 快速命令行构建与启动：
```bash
# 还原依赖
dotnet restore

# 编译并运行
dotnet run --project CanUSB/CanUSB.csproj
```

---

## 📦 下载与发布

推送到 `v*` 标签时，GitHub Actions 会自动构建并创建 Release：

- 平台：**Windows x64**
- 形式：**自包含单文件**，目标机器无需安装 .NET 运行时
- 产物：`CanUSB-v1.0.0-win-x64.zip`，解压后直接双击 `CanUSB.exe` 运行

手动打发布包：

```bash
dotnet publish CanUSB/CanUSB.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish
```

发布新版本：

```bash
git tag v1.0.1
git push origin v1.0.1
```

---

## 📂 项目结构说明

- `Core/`
  - `CanFrame.cs`：CAN 2.0B / CAN FD 报文核心实体定义
  - `SlcanDriver.cs`：CANable/USB2CAN 串口 ASCII 协议驱动与解析引擎
- `UI/`
  - `MainForm.cs`：主界面、侧边栏配置面板、虚拟 Trace 报文表与发送台
  - `Controls/`：现代化圆角卡片、平滑过渡按键与输入框自定义控件