using System;
using System.IO.Ports;
using System.Text;

namespace CanUSB.Core
{
    public class SlcanDriver : IDisposable
    {
        private SerialPort? _port;
        public event Action<CanFrame>? FrameReceived;
        public event Action<string>? LogMessage;
        public bool IsOpen => _port != null && _port.IsOpen;

        /// <summary>连接时探测到的 SLCAN 固件版本号，未探测到则为 null。</summary>
        public string? FirmwareVersion { get; private set; }

        /// <summary>连接后自检读到的错误寄存器原文，未读到则为 null。</summary>
        public string? ErrorRegisterText { get; private set; }

        /// <summary>设备是否不回 TX 确认（无裸 CR / BEL 回包），与 CANgaroo 的 _noConfirm 语义一致。</summary>
        public bool NoConfirmMode { get; private set; }

        /// <summary>
        /// 连接 SLCAN 适配器。
        /// 初始化顺序对齐 Schildkroet/CANgaroo：C(关闭/探测) → V(版本) → S(标称波特率) → Y(FD 数据波特率) → M0(正常模式) → O(打开)。
        /// 波特率默认 1 Mbaud（CANgaroo 硬编码值，USB-CDC 虚拟串口下无实际影响）。
        /// </summary>
        public bool Connect(string portName, int baudRate = 1000000, string canBaudCode = "S6", string? canFdDataBaudCode = null)
        {
            try
            {
                if (IsOpen) Disconnect();

                _port = new SerialPort(portName, baudRate)
                {
                    NewLine = "\r",
                    ReadTimeout = 1000,
                    WriteTimeout = 1000,
                    // 某些 USB 虚拟串口(CDC-ACM)固件必须拉高 DTR/RTS 才会触发握手
                    DtrEnable = true,
                    RtsEnable = true
                };

                _port.Open();

                // 丢弃上电/复位时残留的杂乱字节，避免污染第一帧解析
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                System.Threading.Thread.Sleep(50);

                // 1) C\r 关闭通道，同时探测设备是否回显/确认（CANgaroo 的 _noConfirm 判定）
                _port.Write("C\r");
                System.Threading.Thread.Sleep(50);
                NoConfirmMode = _port.BytesToRead == 0;
                _port.DiscardInBuffer();

                // 2) V\r 读固件版本，区分 SLCAN 固件与 candleLight/WinUSB
                FirmwareVersion = ProbeFirmware();
                if (!string.IsNullOrEmpty(FirmwareVersion))
                {
                    LogMessage?.Invoke($"[System] 固件版本: {FirmwareVersion}");
                }
                else
                {
                    LogMessage?.Invoke("[Warn] 未收到固件版本回应，设备可能不是 SLCAN 固件(如 candleLight/WinUSB)或已假死，建议拔插复位。");
                }

                // 3) S 设定标称波特率（必须在 O 之前）
                _port.Write($"{canBaudCode}\r");
                System.Threading.Thread.Sleep(50);

                // 4) Y 设定 CAN FD 数据段波特率（可选，必须在 O 之前）
                if (!string.IsNullOrEmpty(canFdDataBaudCode))
                {
                    _port.Write($"{canFdDataBaudCode}\r");
                    System.Threading.Thread.Sleep(50);
                }

                // 5) 明确切入正常模式，避免残留的 Listen-Only/Silent 状态导致只收不发
                _port.Write("M0\r");
                System.Threading.Thread.Sleep(50);

                // 6) O 打开通道
                _port.Write("O\r");
                System.Threading.Thread.Sleep(100);

                // 通道打开后读错误寄存器：能读到数值说明链路真实存活
                ErrorRegisterText = QueryCommand("E");
                if (!string.IsNullOrEmpty(ErrorRegisterText))
                {
                    LogMessage?.Invoke($"[System] 链路自检: {ErrorRegisterText}");
                    if (TryParseTrailingNumber(ErrorRegisterText, out int err) && err != 0)
                    {
                        LogMessage?.Invoke($"[Warn] 错误寄存器非 0 (0x{err:X})，总线可能缺终端电阻、接线错误或波特率不匹配。");
                    }
                }

                _port.DataReceived += Port_DataReceived;

                LogMessage?.Invoke($"[System] 已连接到 {portName}，初始化指令发送完毕 (BaudCode: {canBaudCode})");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                LogMessage?.Invoke($"[Error] 端口 {portName} 被其他程序占用，请关闭串口助手/CANgaroo 等软件，或重新拔插 USB 后重试。");
                Disconnect();
                return false;
            }
            catch (System.IO.IOException ex)
            {
                LogMessage?.Invoke($"[Error] 端口 {portName} 已被移除或驱动异常: {ex.Message}");
                Disconnect();
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[Error] 连接失败: {ex.GetType().Name} - {ex.Message}");
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// 发送 "V\r" 读取 SLCAN 固件版本号；无回应返回 null。
        /// </summary>
        private string? ProbeFirmware() => QueryCommand("V");

        /// <summary>
        /// 发送单条查询命令并读取回应（不参与报文解析）。
        /// </summary>
        private string? QueryCommand(string cmd)
        {
            try
            {
                _port!.DiscardInBuffer();
                _port.Write($"{cmd}\r");
                System.Threading.Thread.Sleep(150);

                if (_port.BytesToRead == 0) return null;

                string resp = _port.ReadExisting().Trim('\r', '\n', ' ', '\0');
                _port.DiscardInBuffer();
                return string.IsNullOrEmpty(resp) ? null : resp;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>取字符串末尾的十进制数字（如 "CANable Error Register: 0" → 0）。</summary>
        private static bool TryParseTrailingNumber(string text, out int value)
        {
            value = 0;
            int end = text.Length - 1;
            while (end >= 0 && !char.IsDigit(text[end])) end--;
            if (end < 0) return false;

            int start = end;
            while (start > 0 && char.IsDigit(text[start - 1])) start--;
            return int.TryParse(text.Substring(start, end - start + 1), out value);
        }

        public void Disconnect()
        {
            try
            {
                if (_port != null && _port.IsOpen)
                {
                    _port.Write("C\r");
                    _port.DataReceived -= Port_DataReceived;
                    _port.Close();
                    LogMessage?.Invoke("[System] 设备已断开并重置总线。");
                }
            }
            catch { }
            finally
            {
                _port?.Dispose();
                _port = null;
                FirmwareVersion = null;
                ErrorRegisterText = null;
            }
        }

        public bool SendFrame(CanFrame frame)
        {
            if (!IsOpen || _port == null) return false;

            try
            {
                var sb = new StringBuilder();

                if (frame.Protocol == CanProtocolType.CANFD)
                {
                    // CANgaroo 约定：'d' = FD 无 BRS，'b' = FD 带 BRS，ID 长度由标准/扩展决定
                    sb.Append(frame.BRS ? 'b' : 'd');
                    sb.Append(frame.Id.ToString(frame.FrameType == CanFrameType.Standard ? "X3" : "X8"));
                    sb.Append(DlcToLenCode(frame.Dlc).ToString("X1"));
                    foreach (var b in frame.Data)
                    {
                        sb.Append(b.ToString("X2"));
                    }
                }
                else if (frame.FrameType == CanFrameType.Standard)
                {
                    sb.Append(frame.IsRemote ? 'r' : 't');
                    sb.Append(frame.Id.ToString("X3"));
                    sb.Append(frame.Dlc.ToString("X1"));
                }
                else
                {
                    sb.Append(frame.IsRemote ? 'R' : 'T');
                    sb.Append(frame.Id.ToString("X8"));
                    sb.Append(frame.Dlc.ToString("X1"));
                }

                if (!frame.IsRemote && frame.Protocol != CanProtocolType.CANFD)
                {
                    foreach (var b in frame.Data)
                    {
                        sb.Append(b.ToString("X2"));
                    }
                }
                sb.Append('\r');

                _port.Write(sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[TX Error] 发送失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>发送一条原始 SLCAN 命令（用于诊断），自动补 CR。</summary>
        public bool SendRawCommand(string command)
        {
            if (!IsOpen || _port == null || string.IsNullOrWhiteSpace(command)) return false;

            try
            {
                string line = command.Trim();
                if (!line.EndsWith("\r", StringComparison.Ordinal)) line += "\r";

                _port.Write(line);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[TX Error] 命令写入失败: {ex.Message}");
                return false;
            }
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_port == null || !_port.IsOpen) return;

            try
            {
                while (_port.BytesToRead > 0)
                {
                    string line = _port.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string trimmed = line.Trim();
                    var frame = ParseSlcanFrame(trimmed);
                    if (frame != null)
                    {
                        FrameReceived?.Invoke(frame);
                    }
                    else
                    {
                        // 非报文回显（如 V1013 / CR / 错误串）交给诊断日志
                        LogMessage?.Invoke(trimmed);
                    }
                }
            }
            catch { }
        }

        private CanFrame? ParseSlcanFrame(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            char type = cmd[0];

            try
            {
                var frame = new CanFrame();
                if (type == 't' || type == 'r')
                {
                    frame.FrameType = CanFrameType.Standard;
                    frame.IsRemote = (type == 'r');
                    frame.Id = Convert.ToUInt32(cmd.Substring(1, 3), 16);
                    frame.Dlc = byte.Parse(cmd.Substring(4, 1));
                    if (!frame.IsRemote && cmd.Length >= 5 + frame.Dlc * 2)
                    {
                        frame.Data = HexToBytes(cmd.Substring(5, frame.Dlc * 2));
                    }
                    return frame;
                }
                else if (type == 'T' || type == 'R')
                {
                    frame.FrameType = CanFrameType.Extended;
                    frame.IsRemote = (type == 'R');
                    frame.Id = Convert.ToUInt32(cmd.Substring(1, 8), 16);
                    frame.Dlc = byte.Parse(cmd.Substring(9, 1));
                    if (!frame.IsRemote && cmd.Length >= 10 + frame.Dlc * 2)
                    {
                        frame.Data = HexToBytes(cmd.Substring(10, frame.Dlc * 2));
                    }
                    return frame;
                }
                else if (type == 'b' || type == 'B')
                {
                    frame.Protocol = CanProtocolType.CANFD;
                    frame.FrameType = (type == 'b') ? CanFrameType.Standard : CanFrameType.Extended;
                    int idLen = (type == 'b') ? 3 : 8;
                    frame.Id = Convert.ToUInt32(cmd.Substring(1, idLen), 16);
                    frame.Dlc = LenCodeToDlc(Convert.ToByte(cmd.Substring(1 + idLen, 2), 16));
                    int dataStart = 3 + idLen;
                    if (cmd.Length >= dataStart + frame.Dlc * 2)
                    {
                        frame.Data = HexToBytes(cmd.Substring(dataStart, frame.Dlc * 2));
                    }
                    return frame;
                }
            }
            catch { }
            return null;
        }

        /// <summary>CAN FD 长度编码(0-F) → 实际数据字节数。0-8 同上，9=12, A=16, B=20, C=24, D=32, E=48, F=64。</summary>
        private static byte LenCodeToDlc(byte code) => code switch
        {
            9 => 12,
            10 => 16,
            11 => 20,
            12 => 24,
            13 => 32,
            14 => 48,
            15 => 64,
            _ => code > 8 ? (byte)8 : code
        };

        /// <summary>实际数据字节数 → CAN FD 长度编码(0-F)。</summary>
        private static byte DlcToLenCode(byte len)
        {
            if (len <= 8) return len;
            if (len <= 12) return 9;
            if (len <= 16) return 10;
            if (len <= 20) return 11;
            if (len <= 24) return 12;
            if (len <= 32) return 13;
            if (len <= 48) return 14;
            return 15;
        }

        private byte[] HexToBytes(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}