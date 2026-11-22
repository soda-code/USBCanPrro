using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;
using CanUSB.Core;
using CanUSB.UI.Controls;

namespace CanUSB.UI
{
    public class MainForm : Form
    {
        private readonly SlcanDriver _driver = new SlcanDriver();
        private readonly ConcurrentQueue<CanFrame> _rxQueue = new ConcurrentQueue<CanFrame>();
        private readonly List<CanFrame> _displayBuffer = new List<CanFrame>(10000);
        private System.Windows.Forms.Timer? _uiRefreshTimer;
        private System.Windows.Forms.Timer? _portRescanTimer;
        private readonly ToolTip _statusTip = new ToolTip();

        // USB 热插拔
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        // 外壳
        private Panel pnlHeader = null!;
        private Panel pnlSidebar = null!;
        private Panel pnlMain = null!;
        private Panel pnlContent = null!;
        private Panel pnlStatus = null!;
        private Label lblClock = null!;
        private Label lblTopState = null!;

        // 连接配置（顶栏同行）
        private FieldBox boxPorts = null!;
        private FieldBox boxCanBaud = null!;
        private FieldBox boxFdBaud = null!;
        private ModernButton btnRefresh = null!;
        private ComboBox cmbPorts = null!;
        private ComboBox cmbCanBaud = null!;
        private ComboBox cmbFdDataBaud = null!;
        private CheckBox chkCanFd = null!;
        private ModernButton btnConnect = null!;

        // 导航
        private NavItem navTrace = null!;
        private NavItem navCurve = null!;
        private NavItem navSend = null!;
        private NavItem navStats = null!;
        private NavItem navAdapter = null!;
        private NavItem navDiag = null!;

        // 子界面
        private Panel pageTrace = null!;
        private Panel pageCurve = null!;
        private Panel pageSend = null!;
        private Panel pageStats = null!;
        private Panel pageAdapter = null!;
        private Panel pageDiag = null!;

        // 诊断页
        private RoundedCardPanel cardDiagCmd = null!;
        private ModernTextBox txtDiagCmd = null!;
        private ModernButton btnDiagSend = null!;
        private ModernButton btnDiagClear = null!;
        private TextBox txtDiagLog = null!;
        private Label lblDiagState = null!;

        // 接收曲线
        private RoundedCardPanel cardCurveCtrl = null!;
        private LineChartPanel chartCurve = null!;
        private ModernTextBox txtCurveId = null!;
        private ComboBox cmbCurveSource = null!;
        private FieldBox boxCurveSource = null!;
        private ModernButton btnCurvePause = null!;
        private ModernButton btnCurveClear = null!;
        private Label lblCurveInfo = null!;
        private readonly object _curveLock = new();
        private readonly List<(DateTime Time, double Value)> _curvePoints = new(300);
        private uint _curveTargetId = 0x123;
        private int _curveChannel = 0;
        private bool _curvePaused;
        private DateTime _lastCurveTime = DateTime.MinValue;
        private const int CurveCapacity = 240;

        // 报文追踪页
        private RoundedCardPanel cardTrace = null!;
        private DataGridView gridTrace = null!;
        private Panel barTraceFilter = null!;
        private ModernTextBox txtIdFilter = null!;
        private Label lblFilterInfo = null!;
        private Label lblFrameRate = null!;
        private Label lblTotalFrames = null!;
        private ModernButton btnClear = null!;

        // 报文过滤：null 表示全部
        private readonly List<CanFrame> _viewFrames = new(8192);
        private HashSet<uint>? _filterIds;

        // 发送页
        private RoundedCardPanel cardTx = null!;
        private ModernTextBox txtTxId = null!;
        private ByteGridInput byteInput = null!;
        private ComboBox cmbFrameType = null!;
        private CheckBox chkBrs = null!;
        private ModernButton btnSend = null!;

        // 批量发送
        private RoundedCardPanel cardBulk = null!;
        private FrameGridInput bulkGrid = null!;
        private FieldBox boxBulkGap = null!;
        private ComboBox cmbBulkGap = null!;
        private ModernButton btnBulkSend = null!;

        // 统计页
        private Label lblKpiFps = null!;
        private Label lblKpiTotal = null!;
        private Label lblKpiLink = null!;
        private Label lblKpiErr = null!;

        // 状态栏
        private Label lblLastUpdate = null!;
        private Label lblStatusMsg = null!;
        private Label lblSourceTag = null!;
        private System.Windows.Forms.Timer? _msgTimer;

        private int _framesReceivedLastSec = 0;
        private DateTime _lastFpsTime = DateTime.Now;
        private DateTime _lastStatsTime = DateTime.MinValue;

        // 报文统计（按方向 + CAN ID 聚合）
        private sealed class FrameStat
        {
            public bool IsTx;
            public uint Id;
            public CanFrameType FrameType;
            public CanProtocolType Protocol;
            public int Count;
            public DateTime FirstSeen;
            public DateTime LastSeen;
            public byte Dlc;
            public string DataHex = string.Empty;
        }

        private readonly Dictionary<(bool Tx, uint Id), FrameStat> _stats = new();
        private readonly object _statsLock = new();
        private List<FrameStat> _statsSnapshot = new();
        private RoundedCardPanel cardStats = null!;
        private Panel pnlStatsHeader = null!;
        private DataGridView gridStats = null!;
        private Label lblStatRx = null!;
        private Label lblStatTx = null!;
        private long _rxTotal;
        private long _txTotal;

        public MainForm()
        {
            InitializeLayout();
            SetupTraceGrid();
            RegisterEvents();
            ShowPage(pageTrace, navTrace);
        }

        // ============================ 布局 ============================

        private void InitializeLayout()
        {
            Text = "CanUSB - CAN / CAN-FD 总线分析仪";
            Size = new Size(1280, 820);
            MinimumSize = new Size(1180, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            DoubleBuffered = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // 图标资源缺失时保持系统默认
            }

            BuildHeader();
            BuildSidebar();
            BuildMainArea();

            // Dock 生效顺序：最后添加者最先停靠
            Controls.Add(pnlMain);
            Controls.Add(pnlSidebar);
            Controls.Add(pnlHeader);

            pnlHeader.Resize += (s, e) => LayoutHeader();
            pageTrace.Resize += (s, e) => LayoutTracePage();
            pageCurve.Resize += (s, e) => LayoutCurvePage();
            pageSend.Resize += (s, e) => LayoutSendPage();
            pageStats.Resize += (s, e) => LayoutStatsHeader();
            pageDiag.Resize += (s, e) => LayoutDiagPage();
            pnlStatus.Resize += (s, e) => LayoutStatusBar();
            Load += (s, e) =>
            {
                LayoutHeader();
                LayoutTracePage();
                UpdateTraceCounters();
                LayoutCurvePage();
                LayoutSendPage();
                LayoutDiagPage();
                LayoutStatusBar();
            };
        }

        private void BuildHeader()
        {
            pnlHeader = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.White };
            pnlHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(228, 228, 231));
                e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
            };

            var logo = new RoundedCardPanel
            {
                Left = 16,
                Top = 18,
                Width = 28,
                Height = 28,
                BorderRadius = 8,
                BackColor = Color.FromArgb(37, 99, 235),
                BorderColor = Color.FromArgb(37, 99, 235),
                Padding = new Padding(0)
            };
            logo.Controls.Add(new Label
            {
                Text = "C",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent
            });

            var lblAppTitle = new Label
            {
                Text = "CanUSB",
                Left = 54,
                Top = 10,
                AutoSize = true,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 24, 27)
            };
            var lblAppSub = new Label
            {
                Text = "CAN / CAN-FD 总线分析仪",
                Left = 56,
                Top = 36,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };

            lblTopState = new Label
            {
                Text = "● 未连接",
                AutoSize = true,
                Padding = new Padding(10, 3, 10, 3),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(113, 113, 122),
                BackColor = Color.FromArgb(244, 244, 245)
            };

            lblClock = new Label
            {
                Text = "--:--:--",
                Width = 120,
                Height = 20,
                Top = 22,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Consolas", 10f),
                ForeColor = Color.FromArgb(82, 82, 91)
            };

            // ---- 连接配置：与连接状态同一行，右对齐排布 ----
            cmbPorts = new ModernComboBox { Font = new Font("Segoe UI", 9f) };
            boxPorts = WrapField(cmbPorts, 0, 17, 120, 30);

            btnRefresh = new ModernButton
            {
                Text = "⟳",
                Width = 32,
                Height = 30,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(244, 244, 245),
                HoverColor = Color.FromArgb(228, 228, 231),
                PressedColor = Color.FromArgb(212, 212, 216),
                BorderOutlineColor = Color.FromArgb(196, 200, 208),
                ForeColor = Color.FromArgb(63, 63, 70)
            };
            btnRefresh.Click += (s, e) => RefreshSerialPorts();

            cmbCanBaud = new ModernComboBox { Font = new Font("Segoe UI", 9f) };
            cmbCanBaud.Items.AddRange(new object[] { "1000 kbps (S8)", "500 kbps (S6)", "250 kbps (S5)", "125 kbps (S4)", "50 kbps (S2)" });
            cmbCanBaud.SelectedIndex = 1;
            boxCanBaud = WrapField(cmbCanBaud, 0, 17, 140, 30);

            chkCanFd = new CheckBox
            {
                Text = "CAN FD",
                AutoSize = true,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(39, 39, 42)
            };

            cmbFdDataBaud = new ModernComboBox { Font = new Font("Segoe UI", 9f), Enabled = false };
            cmbFdDataBaud.Items.AddRange(new object[] { "5 Mbps", "4 Mbps", "2 Mbps", "1 Mbps" });
            cmbFdDataBaud.SelectedIndex = 2;
            boxFdBaud = WrapField(cmbFdDataBaud, 0, 17, 116, 30);

            chkCanFd.CheckedChanged += (s, e) =>
            {
                cmbFdDataBaud.Enabled = chkCanFd.Checked;
                boxFdBaud.FillColor = chkCanFd.Checked ? Color.White : Color.FromArgb(250, 250, 250);
                boxFdBaud.Invalidate();
                cmbFdDataBaud.Invalidate();
            };

            btnConnect = new ModernButton
            {
                Text = "⚡ 连接设备",
                Width = 104,
                Height = 32,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(37, 99, 235),
                HoverColor = Color.FromArgb(29, 78, 216),
                PressedColor = Color.FromArgb(30, 64, 175)
            };

            RefreshSerialPorts();

            _statusTip.SetToolTip(cmbPorts, "适配器串口");
            _statusTip.SetToolTip(cmbCanBaud, "标称波特率（连接时下发 S 指令）");
            _statusTip.SetToolTip(cmbFdDataBaud, "CAN FD 数据段波特率（连接时下发 Y 指令）");

            pnlHeader.Controls.AddRange(new Control[]
            {
                logo, lblAppTitle, lblAppSub,
                boxPorts, btnRefresh, boxCanBaud, chkCanFd, boxFdBaud, btnConnect,
                lblTopState, lblClock
            });
        }

        private void BuildSidebar()
        {
            pnlSidebar = new Panel { Dock = DockStyle.Left, Width = 276, BackColor = Color.White };
            pnlSidebar.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(228, 228, 231));
                e.Graphics.DrawLine(pen, pnlSidebar.Width - 1, 0, pnlSidebar.Width - 1, pnlSidebar.Height);
            };

            // ---------- 01 功能导航 ----------
            var cardNav = new RoundedCardPanel
            {
                Left = 12,
                Top = 12,
                Width = 252,
                Height = 278,
                BorderRadius = 12,
                BackColor = Color.FromArgb(250, 250, 251),
                BorderColor = Color.FromArgb(228, 228, 231),
                Padding = new Padding(0)
            };

            var lblNavTitle = new Label
            {
                Text = "功能导航",
                Left = 12,
                Top = 10,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(161, 161, 170)
            };

            navTrace = MakeNavItem("▤", "报文追踪", 32);
            navCurve = MakeNavItem("∿", "接收曲线", 72);
            navSend = MakeNavItem("➤", "发送帧", 112);
            navStats = MakeNavItem("◈", "统计", 152);
            navDiag = MakeNavItem("◎", "诊断", 192);
            navAdapter = MakeNavItem("⚙", "适配器信息", 232);

            cardNav.Controls.AddRange(new Control[] { lblNavTitle, navTrace, navCurve, navSend, navStats, navDiag, navAdapter });

            // ---------- 02 支持的适配器 ----------
            var cardSupport = new RoundedCardPanel
            {
                Left = 12,
                Top = 302,
                Width = 252,
                Height = 132,
                BorderRadius = 12,
                BackColor = Color.White,
                Padding = new Padding(0)
            };
            var lblSupportTitle = new Label
            {
                Text = "支持的适配器",
                Left = 14,
                Top = 12,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(82, 82, 91)
            };
            var lblSupport = new Label
            {
                Text = "CANable / CANable 2\nUSB2CANFDV1 (slcan)\ncandleLight / gs_usb",
                Left = 14,
                Top = 38,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };
            cardSupport.Controls.AddRange(new Control[] { lblSupportTitle, lblSupport });

            // ---------- 03 底栏 ----------
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            footer.Controls.Add(new Label
            {
                Text = "V1.0.0  ·  SLCAN / CAN-FD",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                Font = new Font("Consolas", 8f),
                ForeColor = Color.FromArgb(161, 161, 170)
            });

            pnlSidebar.Controls.Add(cardNav);
            pnlSidebar.Controls.Add(cardSupport);
            pnlSidebar.Controls.Add(footer);
        }

        private NavItem MakeNavItem(string glyph, string title, int top)
        {
            var nav = new NavItem { Glyph = glyph, Title = title, Left = 8, Top = top, Width = 236, Height = 38 };
            return nav;
        }

        private void BuildMainArea()
        {
            pnlMain = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };

            BuildStatusBar();

            pnlContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 0) };

            pageTrace = BuildTracePage();
            pageCurve = BuildCurvePage();
            pageSend = BuildSendPage();
            pageStats = BuildStatsPage();
            pageDiag = BuildDiagPage();
            pageAdapter = BuildAdapterPage();

            pnlContent.Controls.AddRange(new Control[] { pageTrace, pageCurve, pageSend, pageStats, pageDiag, pageAdapter });

            pnlMain.Controls.Add(pnlContent);
            pnlMain.Controls.Add(pnlStatus);
        }


        private Panel BuildTracePage()
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false };

            // ---------- ID 过滤条 ----------
            barTraceFilter = new Panel { Dock = DockStyle.Top, Height = 46 };

            var lblFilter = new Label
            {
                Text = "ID 过滤",
                AutoSize = true,
                Left = 2,
                Top = 15,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };

            txtIdFilter = new ModernTextBox
            {
                Left = 76,
                Top = 8,
                Width = 220,
                Height = 30,
                Font = new Font("Consolas", 9.5f),
                Padding = new Padding(10, 6, 10, 6)
            };
            txtIdFilter.TextChanged += (s, e) => ApplyIdFilter();

            _statusTip.SetToolTip(txtIdFilter, "按 CAN ID 过滤，多个 ID 用逗号分隔（如 123,0x456）；留空显示全部");

            lblFilterInfo = new Label
            {
                Text = "全部报文 · 0 帧",
                AutoSize = true,
                Top = 15,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(161, 161, 170)
            };

            btnClear = new ModernButton
            {
                Text = "清空报文",
                Width = 96,
                Height = 30,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(247, 248, 250),
                HoverColor = Color.FromArgb(237, 239, 243),
                PressedColor = Color.FromArgb(228, 231, 236),
                BorderOutlineColor = Color.FromArgb(196, 200, 208),
                ForeColor = Color.FromArgb(63, 63, 70),
                Enabled = false
            };

            barTraceFilter.Controls.AddRange(new Control[] { lblFilter, txtIdFilter, lblFilterInfo, btnClear });

            cardTrace = new RoundedCardPanel
            {
                Dock = DockStyle.Fill,
                BorderRadius = 12,
                BackColor = Color.White,
                Padding = new Padding(1)
            };

            page.Controls.Add(cardTrace);
            page.Controls.Add(barTraceFilter);
            return page;
        }

        /// <summary>解析过滤输入（逗号/空格分隔的 Hex ID），返回 null 表示不过滤。</summary>
        private static HashSet<uint>? ParseIdFilter(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var set = new HashSet<uint>();
            foreach (string part in text.Split(new[] { ',', '，', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = part.Trim();
                if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    token = token.Substring(2);
                }

                if (uint.TryParse(token, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out uint id))
                {
                    set.Add(id);
                }
            }

            return set.Count > 0 ? set : null;
        }

        private bool MatchesFilter(CanFrame frame)
        {
            return _filterIds == null || _filterIds.Contains(frame.Id);
        }

        private void ApplyIdFilter()
        {
            _filterIds = ParseIdFilter(txtIdFilter.Text);

            _viewFrames.Clear();
            foreach (var frame in _displayBuffer)
            {
                if (MatchesFilter(frame)) _viewFrames.Add(frame);
            }

            gridTrace.RowCount = _viewFrames.Count;
            if (gridTrace.RowCount > 0)
            {
                gridTrace.FirstDisplayedScrollingRowIndex = gridTrace.RowCount - 1;
            }
            gridTrace.Invalidate();

            UpdateTraceCounters();
        }

        private void UpdateTraceCounters()
        {
            lblTotalFrames.Text = _filterIds == null
                ? $"帧数: {_displayBuffer.Count:N0}"
                : $"帧数: {_viewFrames.Count:N0} / {_displayBuffer.Count:N0}";

            lblFilterInfo.Text = _filterIds == null
                ? $"全部报文 · {_viewFrames.Count:N0} 帧"
                : $"过滤 {_filterIds.Count} 个 ID · {_viewFrames.Count:N0} 帧";

            if (btnClear != null) btnClear.Enabled = _displayBuffer.Count > 0;

            LayoutTracePage();
            LayoutStatusLabels();
        }

        private void ClearTrace()
        {
            _displayBuffer.Clear();
            _viewFrames.Clear();

            gridTrace.RowCount = 0;
            gridTrace.Invalidate();
            lblLastUpdate.Text = "最后更新 --:--:--.---";

            lock (_statsLock)
            {
                _stats.Clear();
                _rxTotal = 0;
                _txTotal = 0;
            }

            RefreshStatsTable();
            UpdateTraceCounters();
        }

        private Panel BuildCurvePage()
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false };

            // ---------- 控制条 ----------
            cardCurveCtrl = new RoundedCardPanel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BorderRadius = 12,
                BackColor = Color.FromArgb(241, 245, 249),   // slate-100
                Padding = new Padding(0)
            };

            var lblCurveId = new Label
            {
                Text = "CAN ID",
                Left = 16,
                Top = 12,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };
            txtCurveId = new ModernTextBox
            {
                Left = 16,
                Top = 32,
                Width = 96,
                Height = 30,
                Text = "123",
                Font = new Font("Consolas", 9.5f),
                Padding = new Padding(10, 6, 10, 6)
            };
            txtCurveId.TextChanged += (s, e) =>
            {
                string text = txtCurveId.Text.Trim();
                if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out uint id))
                {
                    lock (_curveLock)
                    {
                        _curveTargetId = id;
                        _curvePoints.Clear();
                    }
                }
            };

            var lblCurveSrc = new Label
            {
                Text = "数据源",
                Left = 124,
                Top = 12,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };
            cmbCurveSource = new ModernComboBox { Font = new Font("Segoe UI", 9f) };
            cmbCurveSource.Items.AddRange(new object[]
            {
                "字节 0", "字节 1", "字节 2", "字节 3",
                "字节 4", "字节 5", "字节 6", "字节 7",
                "全部字节和", "DLC"
            });
            cmbCurveSource.SelectedIndex = 0;
            cmbCurveSource.SelectedIndexChanged += (s, e) =>
            {
                int index = cmbCurveSource.SelectedIndex;
                lock (_curveLock)
                {
                    _curveChannel = index < 8 ? index : (index == 8 ? -1 : -2);
                    _curvePoints.Clear();
                }
            };
            boxCurveSource = WrapField(cmbCurveSource, 124, 32, 150, 30);

            btnCurvePause = new ModernButton
            {
                Text = "暂停",
                Left = 288,
                Top = 34,
                Width = 84,
                Height = 30,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(247, 248, 250),
                HoverColor = Color.FromArgb(237, 239, 243),
                PressedColor = Color.FromArgb(228, 231, 236),
                BorderOutlineColor = Color.FromArgb(196, 200, 208),
                ForeColor = Color.FromArgb(63, 63, 70)
            };
            btnCurvePause.Click += (s, e) =>
            {
                _curvePaused = !_curvePaused;
                btnCurvePause.Text = _curvePaused ? "继续" : "暂停";
                btnCurvePause.ForeColor = _curvePaused
                    ? Color.FromArgb(180, 83, 9)
                    : Color.FromArgb(63, 63, 70);
            };

            btnCurveClear = new ModernButton
            {
                Text = "清空曲线",
                Left = 380,
                Top = 34,
                Width = 96,
                Height = 30,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(247, 248, 250),
                HoverColor = Color.FromArgb(237, 239, 243),
                PressedColor = Color.FromArgb(228, 231, 236),
                BorderOutlineColor = Color.FromArgb(196, 200, 208),
                ForeColor = Color.FromArgb(63, 63, 70)
            };
            btnCurveClear.Click += (s, e) =>
            {
                lock (_curveLock) _curvePoints.Clear();
                RefreshCurve();
            };

            lblCurveInfo = new Label
            {
                Text = "采样 0 点",
                AutoSize = true,
                Top = 42,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(161, 161, 170)
            };

            cardCurveCtrl.Controls.AddRange(new Control[]
            {
                lblCurveId, txtCurveId, lblCurveSrc, boxCurveSource,
                btnCurvePause, btnCurveClear, lblCurveInfo
            });

            // ---------- 图表 ----------
            var cardChart = new RoundedCardPanel
            {
                Dock = DockStyle.Fill,
                BorderRadius = 12,
                BackColor = Color.White,
                Padding = new Padding(10)
            };
            chartCurve = new LineChartPanel
            {
                Dock = DockStyle.Fill,
                EmptyText = "等待目标 ID 的报文…  连接设备后在左侧选择 CAN ID 与数据源"
            };
            cardChart.Controls.Add(chartCurve);

            page.Controls.Add(cardChart);
            page.Controls.Add(cardCurveCtrl);
            return page;
        }

        private void LayoutCurvePage()
        {
            if (cardCurveCtrl == null) return;
            lblCurveInfo.Left = Math.Max(500, cardCurveCtrl.Width - 16 - lblCurveInfo.Width);
        }

        /// <summary>接收帧到达时采样曲线数据（驱动线程调用）。</summary>
        private void TryAppendCurvePoint(CanFrame frame)
        {
            lock (_curveLock)
            {
                if (_curvePaused || frame.Id != _curveTargetId) return;
                if (frame.Data == null || frame.Data.Length == 0) return;

                double value;
                if (_curveChannel >= 0)
                {
                    if (_curveChannel >= frame.Data.Length) return;
                    value = frame.Data[_curveChannel];
                }
                else if (_curveChannel == -1)
                {
                    double sum = 0;
                    foreach (byte b in frame.Data) sum += b;
                    value = sum;
                }
                else
                {
                    value = frame.Dlc;
                }

                _curvePoints.Add((frame.Timestamp, value));
                if (_curvePoints.Count > CurveCapacity)
                {
                    _curvePoints.RemoveRange(0, _curvePoints.Count - CurveCapacity);
                }
            }
        }

        private void RefreshCurve()
        {
            if (chartCurve == null) return;

            (DateTime Time, double Value)[] snapshot;
            uint target;

            lock (_curveLock)
            {
                snapshot = _curvePoints.ToArray();
                target = _curveTargetId;
            }

            chartCurve.SetPoints(snapshot);
            lblCurveInfo.Text = $"采样 {snapshot.Length} 点 · 目标 0x{target:X3}" + (_curvePaused ? " · 已暂停" : string.Empty);
            LayoutCurvePage();
        }

        private Panel BuildSendPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false };

            cardTx = new RoundedCardPanel { Dock = DockStyle.Top, Height = 232, BorderRadius = 12, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(0) };

            var lblCardTitle = new Label
            {
                Text = "发送帧",
                Left = 16,
                Top = 14,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(82, 82, 91)
            };

            var lblId = new Label { Text = "CAN ID (Hex)", Left = 16, Top = 42, AutoSize = true, Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(113, 113, 122) };
            txtTxId = new ModernTextBox { Left = 16, Top = 66, Width = 140, Height = 34, Text = "123", Padding = new Padding(10, 6, 10, 6) };

            var lblType = new Label { Text = "帧类型", Left = 172, Top = 42, AutoSize = true, Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(113, 113, 122) };
            cmbFrameType = new ModernComboBox { Font = new Font("Segoe UI", 9f) };
            cmbFrameType.Items.AddRange(new object[] { "自动识别", "标准帧 (11 bit)", "扩展帧 (29 bit)" });
            cmbFrameType.SelectedIndex = 0;
            var boxFrameType = WrapField(cmbFrameType, 172, 66, 160, 34);

            chkBrs = new CheckBox { Text = "BRS (FD 加速)", Left = 348, Top = 74, AutoSize = true, Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(39, 39, 42) };

            var lblData = new Label { Text = "数据帧 (Hex，按字节 0-7 填写)", Left = 16, Top = 112, AutoSize = true, Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(113, 113, 122) };
            byteInput = new ByteGridInput { Left = 16, Top = 132, Width = 560 };

            btnSend = new ModernButton
            {
                Text = "发送帧",
                Width = 120,
                Height = 34,
                Top = 140,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(16, 185, 129),
                HoverColor = Color.FromArgb(5, 150, 105),
                PressedColor = Color.FromArgb(4, 120, 87)
            };

            var lblHint = new Label
            {
                Text = "提示：Enter / → 跳到下一个字节；末尾留空的字节不会发送。",
                Left = 16,
                Top = 200,
                AutoSize = true,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(161, 161, 170)
            };

            cardTx.Controls.AddRange(new Control[]
            {
                lblCardTitle, lblId, txtTxId, lblType, boxFrameType, chkBrs,
                lblData, byteInput, btnSend, lblHint
            });

            // ---- 批量发送 ----
            cardBulk = new RoundedCardPanel { Dock = DockStyle.Top, Height = 266, BorderRadius = 12, BackColor = Color.White, Padding = new Padding(0) };

            var lblBulkTitle = new Label
            {
                Text = "批量发送",
                Left = 16,
                Top = 12,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(82, 82, 91)
            };
            var lblBulkHint = new Label
            {
                Text = "每行一条报文：CAN ID + 字节 0-7，空行自动跳过；Enter / → 依次跳到下一格",
                Left = 16,
                Top = 34,
                AutoSize = true,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(161, 161, 170)
            };

            bulkGrid = new FrameGridInput(5) { Left = 16, Top = 58, Width = 600 };

            var lblGap = new Label
            {
                Text = "行间隔",
                Left = 16,
                Top = 232,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };
            cmbBulkGap = new ModernComboBox { Font = new Font("Segoe UI", 9f) };
            cmbBulkGap.Items.AddRange(new object[] { "0 ms", "5 ms", "10 ms", "20 ms", "50 ms", "100 ms" });
            cmbBulkGap.SelectedIndex = 2;
            boxBulkGap = WrapField(cmbBulkGap, 64, 224, 110, 30);

            btnBulkSend = new ModernButton
            {
                Text = "批量发送",
                Width = 120,
                Height = 32,
                Top = 224,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(16, 185, 129),
                HoverColor = Color.FromArgb(5, 150, 105),
                PressedColor = Color.FromArgb(4, 120, 87)
            };

            cardBulk.Controls.AddRange(new Control[]
            {
                lblBulkTitle, lblBulkHint, bulkGrid, lblGap, boxBulkGap, btnBulkSend
            });

            page.Controls.Add(cardBulk);
            page.Controls.Add(cardTx);
            return page;
        }

        private Panel BuildStatsPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false };

            // ---------- KPI 行 ----------
            var kpiRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 120,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            for (int i = 0; i < 4; i++)
                kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            kpiRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            kpiRow.Controls.Add(MakeKpiCard("实时帧率", "0 帧/秒", out lblKpiFps), 0, 0);
            kpiRow.Controls.Add(MakeKpiCard("收发总数", "0", out lblKpiTotal), 1, 0);
            kpiRow.Controls.Add(MakeKpiCard("链路状态", "未连接", out lblKpiLink), 2, 0);
            kpiRow.Controls.Add(MakeKpiCard("错误寄存器", "—", out lblKpiErr), 3, 0);

            // ---------- 报文统计表 ----------
            cardStats = new RoundedCardPanel { Dock = DockStyle.Fill, BorderRadius = 12, BackColor = Color.White, Padding = new Padding(1) };

            pnlStatsHeader = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.White };
            pnlStatsHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(228, 228, 231));
                e.Graphics.DrawLine(pen, 0, pnlStatsHeader.Height - 1, pnlStatsHeader.Width, pnlStatsHeader.Height - 1);
            };

            var lblStatsTitle = new Label
            {
                Text = "报文统计",
                Dock = DockStyle.Left,
                Width = 96,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(82, 82, 91)
            };

            lblStatRx = new Label
            {
                Text = "RX 0",
                AutoSize = true,
                Padding = new Padding(8, 3, 8, 3),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(4, 120, 87),
                BackColor = Color.FromArgb(236, 253, 245)
            };
            lblStatTx = new Label
            {
                Text = "TX 0",
                AutoSize = true,
                Padding = new Padding(8, 3, 8, 3),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 83, 9),
                BackColor = Color.FromArgb(255, 251, 235)
            };

            pnlStatsHeader.Controls.AddRange(new Control[] { lblStatsTitle, lblStatRx, lblStatTx });

            gridStats = new DataGridView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Color.White,
                VirtualMode = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(228, 228, 231),
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ScrollBars = ScrollBars.Vertical
            };

            gridStats.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 244, 245);
            gridStats.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(63, 63, 70);
            gridStats.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            gridStats.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
            gridStats.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(244, 244, 245);
            gridStats.ColumnHeadersHeight = 34;

            gridStats.DefaultCellStyle.Font = new Font("Consolas", 9f);
            gridStats.DefaultCellStyle.ForeColor = Color.FromArgb(39, 39, 42);
            gridStats.DefaultCellStyle.BackColor = Color.White;
            gridStats.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
            gridStats.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
            gridStats.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 64, 175);
            gridStats.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 251);
            gridStats.RowTemplate.Height = 28;

            gridStats.Columns.Add("colDir", "方向");
            gridStats.Columns["colDir"].Width = 80;
            gridStats.Columns.Add("colId", "CAN ID");
            gridStats.Columns["colId"].Width = 120;
            gridStats.Columns.Add("colFmt", "格式");
            gridStats.Columns["colFmt"].Width = 90;
            gridStats.Columns.Add("colCnt", "帧数");
            gridStats.Columns["colCnt"].Width = 90;
            gridStats.Columns.Add("colPct", "占比");
            gridStats.Columns["colPct"].Width = 90;
            gridStats.Columns.Add("colPeriod", "平均周期");
            gridStats.Columns["colPeriod"].Width = 110;
            gridStats.Columns.Add("colDlc", "DLC");
            gridStats.Columns["colDlc"].Width = 70;
            gridStats.Columns.Add("colData", "最近数据 (Hex)");
            gridStats.Columns["colData"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            gridStats.CellValueNeeded += StatsCellValueNeeded;
            gridStats.CellFormatting += StatsCellFormatting;

            cardStats.Controls.Add(gridStats);
            cardStats.Controls.Add(pnlStatsHeader);

            page.Controls.Add(cardStats);
            page.Controls.Add(kpiRow);
            return page;
        }

        private void StatsCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _statsSnapshot.Count) return;

            var stat = _statsSnapshot[e.RowIndex];
            long total = _rxTotal + _txTotal;

            e.Value = e.ColumnIndex switch
            {
                0 => stat.IsTx ? "TX" : "RX",
                1 => stat.FrameType == CanFrameType.Standard ? $"0x{stat.Id:X3}" : $"0x{stat.Id:X8}",
                2 => stat.Protocol == CanProtocolType.CANFD ? "CAN FD" : (stat.FrameType == CanFrameType.Standard ? "STD" : "EXT"),
                3 => stat.Count.ToString("N0"),
                4 => total > 0 ? $"{stat.Count * 100.0 / total:F1}%" : "—",
                5 => stat.Count > 1
                    ? $"{FormatInterval(TimeSpan.FromMilliseconds((stat.LastSeen - stat.FirstSeen).TotalMilliseconds / (stat.Count - 1)))}"
                    : "—",
                6 => stat.Dlc.ToString(),
                7 => stat.DataHex,
                _ => string.Empty
            };
        }

        private void StatsCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex != 0 || e.RowIndex < 0 || e.RowIndex >= _statsSnapshot.Count) return;

            e.CellStyle!.ForeColor = _statsSnapshot[e.RowIndex].IsTx
                ? Color.FromArgb(180, 83, 9)
                : Color.FromArgb(4, 120, 87);
            e.CellStyle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        }

        private RoundedCardPanel MakeKpiCard(string title, string initialValue, out Label valueLabel)
        {
            // 每个指标一张浅色底卡片，便于快速区分
            Color cardBg = title switch
            {
                "实时帧率" => Color.FromArgb(239, 246, 255),    // blue-50
                "收发总数" => Color.FromArgb(236, 253, 245),    // emerald-50
                "链路状态" => Color.FromArgb(240, 249, 255),    // sky-50
                "错误寄存器" => Color.FromArgb(254, 242, 242),  // red-50
                _ => Color.White
            };

            var card = new RoundedCardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6),
                BorderRadius = 12,
                BackColor = cardBg,
                Padding = new Padding(0)
            };

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(18, 16, 0, 0),
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };

            valueLabel = new Label
            {
                Text = initialValue,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 12, 16),
                Font = new Font("Consolas", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 24, 27)
            };

            card.Controls.Add(valueLabel);
            card.Controls.Add(lblTitle);
            return card;
        }

        private Panel BuildDiagPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false };

            // ---------- 原始命令 ----------
            cardDiagCmd = new RoundedCardPanel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BorderRadius = 12,
                BackColor = Color.FromArgb(241, 245, 249),   // slate-100
                Padding = new Padding(0)
            };

            var lblCmdTitle = new Label
            {
                Text = "原始 SLCAN 命令",
                Left = 16,
                Top = 12,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(82, 82, 91)
            };
            var lblCmdHint = new Label
            {
                Text = "直接向适配器写入命令并查看回显，如 V 版本 / E 错误寄存器 / F 状态标志 / Z0 开通道 / C 关通道",
                Left = 118,
                Top = 14,
                AutoSize = true,
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(161, 161, 170)
            };

            txtDiagCmd = new ModernTextBox
            {
                Left = 16,
                Top = 44,
                Width = 240,
                Height = 30,
                Font = new Font("Consolas", 9.5f),
                Padding = new Padding(10, 6, 10, 6)
            };
            txtDiagCmd.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    SendDiagnostic(txtDiagCmd.Text);
                }
            };
            _statusTip.SetToolTip(txtDiagCmd, "输入 SLCAN 命令（不含回车），按 Enter 或点「发送命令」执行");

            btnDiagSend = new ModernButton
            {
                Text = "发送命令",
                Left = 264,
                Top = 44,
                Width = 100,
                Height = 30,
                BorderRadius = 8,
                NormalColor = Color.FromArgb(37, 99, 235),
                HoverColor = Color.FromArgb(29, 78, 216),
                PressedColor = Color.FromArgb(30, 64, 175)
            };

            var quickButtons = new List<(string Text, string Command, int Left)>
            {
                ("读取版本 V", "V", 380),
                ("错误寄存器 E", "E", 480),
                ("状态标志 F", "F", 590)
            };

            cardDiagCmd.Controls.AddRange(new Control[] { lblCmdTitle, lblCmdHint, txtDiagCmd, btnDiagSend });

            foreach (var (text, command, left) in quickButtons)
            {
                var btn = new ModernButton
                {
                    Text = text,
                    Left = left,
                    Top = 44,
                    Width = 96,
                    Height = 30,
                    BorderRadius = 8,
                    Font = new Font("Segoe UI", 8.5f),
                    NormalColor = Color.FromArgb(247, 248, 250),
                    HoverColor = Color.FromArgb(237, 239, 243),
                    PressedColor = Color.FromArgb(228, 231, 236),
                    BorderOutlineColor = Color.FromArgb(196, 200, 208),
                    ForeColor = Color.FromArgb(63, 63, 70)
                };
                string cmd = command;
                btn.Click += (s, e) => SendDiagnostic(cmd);
                cardDiagCmd.Controls.Add(btn);
            }

            // ---------- 通信日志 ----------
            var cardLog = new RoundedCardPanel
            {
                Dock = DockStyle.Fill,
                BorderRadius = 12,
                BackColor = Color.White,
                Padding = new Padding(1)
            };

            var pnlLogHeader = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.White };
            pnlLogHeader.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(228, 228, 231));
                e.Graphics.DrawLine(pen, 0, pnlLogHeader.Height - 1, pnlLogHeader.Width, pnlLogHeader.Height - 1);
            };

            var lblLogTitle = new Label
            {
                Text = "通信日志",
                Dock = DockStyle.Left,
                Width = 90,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(82, 82, 91)
            };

            lblDiagState = new Label
            {
                Text = "未连接",
                AutoSize = true,
                Padding = new Padding(8, 3, 8, 3),
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(113, 113, 122),
                BackColor = Color.FromArgb(244, 244, 245)
            };

            btnDiagClear = new ModernButton
            {
                Text = "清空日志",
                Width = 84,
                Height = 26,
                BorderRadius = 6,
                Font = new Font("Segoe UI", 8f),
                NormalColor = Color.FromArgb(247, 248, 250),
                HoverColor = Color.FromArgb(237, 239, 243),
                PressedColor = Color.FromArgb(228, 231, 236),
                BorderOutlineColor = Color.FromArgb(196, 200, 208),
                ForeColor = Color.FromArgb(63, 63, 70)
            };
            btnDiagClear.Click += (s, e) => txtDiagLog.Clear();

            pnlLogHeader.Controls.AddRange(new Control[] { lblLogTitle, lblDiagState, btnDiagClear });

            txtDiagLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f),
                ForeColor = Color.FromArgb(63, 63, 70),
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Padding = new Padding(10, 8, 10, 8)
            };

            cardLog.Controls.Add(txtDiagLog);
            cardLog.Controls.Add(pnlLogHeader);

            page.Controls.Add(cardLog);
            page.Controls.Add(cardDiagCmd);
            return page;
        }

        private void LayoutDiagPage()
        {
            if (btnDiagClear?.Parent is not { } header || lblDiagState == null) return;

            btnDiagClear.Left = header.Width - 12 - btnDiagClear.Width;
            btnDiagClear.Top = (header.Height - btnDiagClear.Height) / 2;

            lblDiagState.Left = btnDiagClear.Left - 8 - lblDiagState.Width;
            lblDiagState.Top = (header.Height - lblDiagState.Height) / 2;
            ApplyPillRegion(lblDiagState, lblDiagState.Height / 2);
        }

        /// <summary>向适配器发送一条诊断命令并记录。</summary>
        private void SendDiagnostic(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            string cmd = command.Trim();

            if (!_driver.IsOpen)
            {
                AppendDiagLog("!!", "设备未连接，请先在顶栏连接适配器");
                return;
            }

            AppendDiagLog(">>", cmd);

            if (!_driver.SendRawCommand(cmd))
            {
                AppendDiagLog("!!", "命令写入失败，请检查适配器状态");
            }
        }

        private void AppendDiagLog(string tag, string text)
        {
            if (txtDiagLog == null) return;

            if (txtDiagLog.TextLength > 200000) txtDiagLog.Clear();

            txtDiagLog.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {tag}  {text}{Environment.NewLine}");
        }

        private Panel BuildAdapterPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false };

            var cardList = new RoundedCardPanel { Dock = DockStyle.Top, Height = 214, BorderRadius = 12, BackColor = Color.White, Padding = new Padding(0) };
            var lblListTitle = new Label { Text = "支持的适配器", Left = 18, Top = 16, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(82, 82, 91) };
            var lblList = new Label
            {
                Text = "CANable 1.0 / CANable 2.0   ·   VID 16D0:117E · slcan\r\n" +
                       "USB2CANFDV1   ·   扩展 slcan，支持 CAN FD (b/B)\r\n" +
                       "candleLight / gs_usb   ·   需刷写 SLCAN 固件后使用\r\n" +
                       "WeAct Studio USB2CAN   ·   VID 0483:5740 · 序列号 AAA 开头",
                Left = 18,
                Top = 50,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(63, 63, 70)
            };
            cardList.Controls.AddRange(new Control[] { lblListTitle, lblList });

            var cardTips = new RoundedCardPanel { Dock = DockStyle.Fill, BorderRadius = 12, BackColor = Color.FromArgb(250, 250, 251), BorderColor = Color.FromArgb(228, 228, 231), Padding = new Padding(0) };
            var lblTipsTitle = new Label { Text = "连接排查", Left = 18, Top = 16, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(82, 82, 91) };
            var lblTips = new Label
            {
                Text = "1. 设备管理器中确认存在「USB 串行设备 (COMx)」，带黄色感叹号说明缺 VCP 驱动。\r\n" +
                       "2. 若显示为 candleLight / WinUSB device，说明固件不是 SLCAN，需重新刷写。\r\n" +
                       "3. COM 口为独占模式，请关闭串口助手 / 原版 CANgaroo 后再连接。\r\n" +
                       "4. 握手时上位机会发送 V 命令，正常返回固件版本号即链路畅通。",
                Left = 18,
                Top = 48,
                AutoSize = true,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(82, 82, 91)
            };
            cardTips.Controls.AddRange(new Control[] { lblTipsTitle, lblTips });

            page.Controls.Add(cardTips);
            page.Controls.Add(cardList);
            return page;
        }

        private void BuildStatusBar()
        {
            pnlStatus = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.White };
            pnlStatus.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(228, 228, 231));
                e.Graphics.DrawLine(pen, 0, 0, pnlStatus.Width, 0);
            };

            lblFrameRate = new Label
            {
                Text = "0 帧/秒",
                AutoSize = true,
                Left = 12,
                Top = 4,
                Font = new Font("Consolas", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 24, 27)
            };
            lblTotalFrames = new Label
            {
                Text = "帧数: 0",
                AutoSize = true,
                Left = 96,
                Top = 7,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };
            lblLastUpdate = new Label
            {
                Text = "最后更新 --:--:--.---",
                AutoSize = true,
                Left = 186,
                Top = 7,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(113, 113, 122)
            };
            lblSourceTag = new Label
            {
                Text = "SLCAN · 1 Mbaud",
                AutoSize = true,
                Top = 5,
                Padding = new Padding(8, 2, 8, 2),
                Font = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(180, 83, 9),
                BackColor = Color.FromArgb(255, 251, 235)
            };
            lblStatusMsg = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                Left = 420,
                Top = 7,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(185, 28, 28)
            };

            pnlStatus.Controls.AddRange(new Control[] { lblFrameRate, lblTotalFrames, lblLastUpdate, lblStatusMsg, lblSourceTag });
        }

        // ============================ 布局对齐 ============================

        private static FieldBox WrapField(Control inner, int left, int top, int width, int height)
        {
            var box = new FieldBox
            {
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                FillColor = Color.FromArgb(247, 248, 250)
            };

            // 内嵌控件四周留出 2px，避免它盖住容器自身的描边（尤其是底边）。
            // ComboBox 的高度由字体决定、设置 Height 无效，所以按它的实际高度反推容器高度。
            const int padding = 2;
            int innerHeight = Math.Max(18, inner.PreferredSize.Height);

            inner.Left = padding;
            inner.Top = padding;
            inner.Width = width - padding * 2;
            inner.Height = innerHeight;

            box.Height = Math.Max(height, innerHeight + padding * 2);
            box.Controls.Add(inner);
            return box;
        }

        private void LayoutHeader()
        {
            int right = pnlHeader.Width - 16;

            lblClock.Left = right - lblClock.Width;
            lblClock.Top = (pnlHeader.Height - lblClock.Height) / 2;
            right = lblClock.Left - 12;

            lblTopState.Left = right - lblTopState.Width;
            lblTopState.Top = (pnlHeader.Height - lblTopState.Height) / 2;
            ApplyPillRegion(lblTopState, lblTopState.Height / 2);
            right = lblTopState.Left - 12;

            btnConnect.Left = right - btnConnect.Width;
            btnConnect.Top = (pnlHeader.Height - btnConnect.Height) / 2;
            right = btnConnect.Left - 12;

            boxFdBaud.Left = right - boxFdBaud.Width;
            right = boxFdBaud.Left - 8;

            chkCanFd.Left = right - chkCanFd.Width;
            chkCanFd.Top = (pnlHeader.Height - chkCanFd.Height) / 2;
            right = chkCanFd.Left - 10;

            boxCanBaud.Left = right - boxCanBaud.Width;
            right = boxCanBaud.Left - 8;

            btnRefresh.Left = right - btnRefresh.Width;
            btnRefresh.Top = (pnlHeader.Height - btnRefresh.Height) / 2;
            right = btnRefresh.Left - 6;

            boxPorts.Left = right - boxPorts.Width;

            int fieldTop = (pnlHeader.Height - boxPorts.Height) / 2;
            boxPorts.Top = fieldTop;
            boxCanBaud.Top = fieldTop;
            boxFdBaud.Top = fieldTop;
        }

        private void LayoutTracePage()
        {
            if (barTraceFilter == null || btnClear == null) return;

            btnClear.Left = barTraceFilter.Width - btnClear.Width;
            btnClear.Top = (barTraceFilter.Height - btnClear.Height) / 2;

            lblFilterInfo.Left = btnClear.Left - 14 - lblFilterInfo.Width;
            lblFilterInfo.Top = (barTraceFilter.Height - lblFilterInfo.Height) / 2;
        }

        private void LayoutSendPage()
        {
            btnSend.Left = Math.Max(300, cardTx.Width - 16 - btnSend.Width);

            // 字节单元格宽度上限 72px，窗口再宽也不无限拉伸
            byteInput.Width = Math.Min(8 * 72, Math.Max(360, btnSend.Left - 16 - byteInput.Left));

            bulkGrid.Width = Math.Max(420, cardBulk.Width - 32);
            btnBulkSend.Left = Math.Max(200, cardBulk.Width - 16 - btnBulkSend.Width);
        }

        private void LayoutStatsHeader()
        {
            if (pnlStatsHeader == null || lblStatTx == null) return;

            int right = pnlStatsHeader.Width - 12;

            lblStatTx.Left = right - lblStatTx.Width;
            lblStatTx.Top = (pnlStatsHeader.Height - lblStatTx.Height) / 2;
            ApplyPillRegion(lblStatTx, lblStatTx.Height / 2);
            right = lblStatTx.Left - 8;

            lblStatRx.Left = right - lblStatRx.Width;
            lblStatRx.Top = (pnlStatsHeader.Height - lblStatRx.Height) / 2;
            ApplyPillRegion(lblStatRx, lblStatRx.Height / 2);
        }

        private void LayoutStatusBar()
        {
            lblSourceTag.Left = pnlStatus.Width - 12 - lblSourceTag.Width;
            ApplyPillRegion(lblSourceTag, lblSourceTag.Height / 2);

            LayoutStatusLabels();
        }

        /// <summary>状态栏左侧文字按实际宽度依次排列，避免计数变长后互相覆盖。</summary>
        private void LayoutStatusLabels()
        {
            if (lblFrameRate == null || lblTotalFrames == null || lblLastUpdate == null) return;

            lblTotalFrames.Left = lblFrameRate.Right + 14;
            lblTotalFrames.Top = (pnlStatus.Height - lblTotalFrames.Height) / 2;

            lblLastUpdate.Left = lblTotalFrames.Right + 14;
            lblLastUpdate.Top = (pnlStatus.Height - lblLastUpdate.Height) / 2;

            lblStatusMsg.Left = lblLastUpdate.Right + 24;
            lblStatusMsg.Top = (pnlStatus.Height - lblStatusMsg.Height) / 2;
        }

        private static void ApplyPillRegion(Control control, int radius)
        {
            if (control.Width <= 2 || control.Height <= 2) return;

            using var path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius, Math.Min(control.Width, control.Height) / 2)) * 2;
            var rect = new Rectangle(0, 0, control.Width, control.Height);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            var old = control.Region;
            control.Region = new Region(path);
            old?.Dispose();
        }

        private void ShowPage(Panel page, NavItem nav)
        {
            pageTrace.Visible = false;
            pageCurve.Visible = false;
            pageSend.Visible = false;
            pageStats.Visible = false;
            pageDiag.Visible = false;
            pageAdapter.Visible = false;

            page.Visible = true;
            page.BringToFront();

            navTrace.Active = navTrace == nav;
            navCurve.Active = navCurve == nav;
            navSend.Active = navSend == nav;
            navStats.Active = navStats == nav;
            navDiag.Active = navDiag == nav;
            navAdapter.Active = navAdapter == nav;
        }

        // ============================ 报文表格 ============================

        private void SetupTraceGrid()
        {
            gridTrace = new DataGridView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Color.White,
                VirtualMode = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(228, 228, 231),
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ScrollBars = ScrollBars.Vertical
            };

            gridTrace.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 244, 245);
            gridTrace.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(63, 63, 70);
            gridTrace.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            gridTrace.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
            gridTrace.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(244, 244, 245);
            gridTrace.ColumnHeadersHeight = 34;

            gridTrace.DefaultCellStyle.Font = new Font("Consolas", 9f);
            gridTrace.DefaultCellStyle.ForeColor = Color.FromArgb(39, 39, 42);
            gridTrace.DefaultCellStyle.BackColor = Color.White;
            gridTrace.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
            gridTrace.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 255);
            gridTrace.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 64, 175);
            gridTrace.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 251);
            gridTrace.RowTemplate.Height = 28;

            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, gridTrace, new object[] { true });

            gridTrace.Columns.Add("colTime", "时间戳");
            gridTrace.Columns["colTime"].Width = 140;

            gridTrace.Columns.Add("colGap", "间隔");
            gridTrace.Columns["colGap"].Width = 100;

            gridTrace.Columns.Add("colId", "CAN ID");
            gridTrace.Columns["colId"].Width = 120;

            gridTrace.Columns.Add("colType", "格式");
            gridTrace.Columns["colType"].Width = 100;

            gridTrace.Columns.Add("colDlc", "DLC");
            gridTrace.Columns["colDlc"].Width = 70;

            gridTrace.Columns.Add("colData", "数据 (Hex)");
            gridTrace.Columns["colData"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            gridTrace.CellValueNeeded += (s, e) =>
            {
                if (e.RowIndex >= _viewFrames.Count) return;
                var frame = _viewFrames[e.RowIndex];

                e.Value = e.ColumnIndex switch
                {
                    0 => frame.Timestamp.ToString("HH:mm:ss.fff"),
                    1 => e.RowIndex == 0
                        ? "—"
                        : $"+{FormatInterval(frame.Timestamp - _viewFrames[e.RowIndex - 1].Timestamp)}",
                    2 => frame.FrameType == CanFrameType.Standard ? $"0x{frame.Id:X3}" : $"0x{frame.Id:X8}",
                    3 => frame.Protocol == CanProtocolType.CANFD ? "CAN FD" : (frame.FrameType == CanFrameType.Standard ? "STD" : "EXT"),
                    4 => frame.Dlc.ToString(),
                    5 => frame.DataHexString,
                    _ => string.Empty
                };
            };

            cardTrace.Controls.Add(gridTrace);
        }

        // ============================ 事件 ============================

        private void RegisterEvents()
        {
            navTrace.Click += (s, e) => ShowPage(pageTrace, navTrace);
            navCurve.Click += (s, e) => ShowPage(pageCurve, navCurve);
            navSend.Click += (s, e) => ShowPage(pageSend, navSend);
            navStats.Click += (s, e) => ShowPage(pageStats, navStats);
            navDiag.Click += (s, e) => ShowPage(pageDiag, navDiag);
            navAdapter.Click += (s, e) => ShowPage(pageAdapter, navAdapter);

            _driver.FrameReceived += frame =>
            {
                _rxQueue.Enqueue(frame);
                System.Threading.Interlocked.Increment(ref _framesReceivedLastSec);
                RecordStat(frame, false);
                TryAppendCurvePoint(frame);
            };

            _driver.LogMessage += msg =>
            {
                if (IsDisposed || Disposing) return;
                if (InvokeRequired)
                {
                    try { BeginInvoke(new Action(() => HandleDriverLog(msg))); } catch { }
                    return;
                }
                HandleDriverLog(msg);
            };

            btnConnect.Click += (s, e) =>
            {
                if (!_driver.IsOpen)
                {
                    if (cmbPorts.SelectedItem == null)
                    {
                        MessageBox.Show("请选择一个串口号！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    string port = cmbPorts.SelectedItem.ToString()!;
                    string baudCode = cmbCanBaud.SelectedIndex switch
                    {
                        0 => "S8",
                        1 => "S6",
                        2 => "S5",
                        3 => "S4",
                        4 => "S2",
                        _ => "S6"
                    };

                    string? fdCode = chkCanFd.Checked
                        ? cmbFdDataBaud.SelectedIndex switch
                        {
                            0 => "Y5",
                            1 => "Y4",
                            2 => "Y2",
                            3 => "Y1",
                            _ => "Y2"
                        }
                        : null;

                    if (_driver.Connect(port, 1000000, baudCode, fdCode))
                    {
                        SetConnectionUi(true, port);
                    }
                }
                else
                {
                    _driver.Disconnect();
                    SetConnectionUi(false, null);
                }
            };

            btnClear.Click += (s, e) => ClearTrace();

            btnSend.Click += (s, e) => SendCurrentFrame();
            btnBulkSend.Click += async (s, e) => await SendBulkFramesAsync();

            _uiRefreshTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _uiRefreshTimer.Tick += (s, e) =>
            {
                bool hasNewData = false;
                while (_rxQueue.TryDequeue(out var frame))
                {
                    _displayBuffer.Add(frame);
                    if (MatchesFilter(frame)) _viewFrames.Add(frame);
                    hasNewData = true;
                }

                if (hasNewData)
                {
                    gridTrace.RowCount = _viewFrames.Count;
                    if (gridTrace.RowCount > 0)
                    {
                        gridTrace.FirstDisplayedScrollingRowIndex = gridTrace.RowCount - 1;
                    }
                    UpdateTraceCounters();
                }

                var now = DateTime.Now;
                lblClock.Text = now.ToString("HH:mm:ss");
                if (hasNewData)
                {
                    lblLastUpdate.Text = $"最后更新 {now:HH:mm:ss.fff}";
                }

                if ((now - _lastFpsTime).TotalSeconds >= 1.0)
                {
                    lblFrameRate.Text = $"{_framesReceivedLastSec} 帧/秒";
                    lblKpiFps.Text = $"{_framesReceivedLastSec} 帧/秒";
                    _framesReceivedLastSec = 0;
                    _lastFpsTime = now;
                    LayoutStatusLabels();
                }

                if ((now - _lastStatsTime).TotalMilliseconds >= 500)
                {
                    RefreshStatsTable();
                    _lastStatsTime = now;
                }

                if (pageCurve.Visible && (now - _lastCurveTime).TotalMilliseconds >= 60)
                {
                    RefreshCurve();
                    _lastCurveTime = now;
                }
            };
            _uiRefreshTimer.Start();
        }

        private void SetConnectionUi(bool connected, string? port)
        {
            if (connected)
            {
                string fw = _driver.FirmwareVersion ?? string.Empty;

                btnConnect.Text = "断开连接";
                btnConnect.NormalColor = Color.FromArgb(239, 68, 68);
                btnConnect.HoverColor = Color.FromArgb(220, 38, 38);
                btnConnect.PressedColor = Color.FromArgb(185, 28, 28);

                lblTopState.Text = $"● 已连接 {port}";
                lblTopState.ForeColor = Color.FromArgb(4, 120, 87);
                lblTopState.BackColor = Color.FromArgb(236, 253, 245);

                lblKpiLink.Text = "已连接";
                lblKpiLink.ForeColor = Color.FromArgb(16, 185, 129);
                lblKpiErr.Text = FormatErrorRegister(_driver.ErrorRegisterText);

                _statusTip.SetToolTip(lblTopState,
                    $"端口: {port}\r\n波特率: 1 Mbaud\r\n固件: {(string.IsNullOrEmpty(fw) ? "未识别" : fw)}\r\n自检: {_driver.ErrorRegisterText ?? "无回应"}");
            }
            else
            {
                btnConnect.Text = "⚡ 连接设备";
                btnConnect.NormalColor = Color.FromArgb(37, 99, 235);
                btnConnect.HoverColor = Color.FromArgb(29, 78, 216);
                btnConnect.PressedColor = Color.FromArgb(30, 64, 175);

                lblTopState.Text = "● 未连接";
                lblTopState.ForeColor = Color.FromArgb(113, 113, 122);
                lblTopState.BackColor = Color.FromArgb(244, 244, 245);

                lblKpiLink.Text = "未连接";
                lblKpiLink.ForeColor = Color.FromArgb(161, 161, 170);
                lblKpiErr.Text = "—";
            }

            UpdateDiagState(connected, port);
            LayoutHeader();
        }

        /// <summary>同步诊断页右上角的连接状态胶囊。</summary>
        private void UpdateDiagState(bool connected, string? port)
        {
            if (lblDiagState == null) return;

            if (connected)
            {
                lblDiagState.Text = $"● 已连接 {port}";
                lblDiagState.ForeColor = Color.FromArgb(4, 120, 87);
                lblDiagState.BackColor = Color.FromArgb(236, 253, 245);
            }
            else
            {
                lblDiagState.Text = "● 未连接";
                lblDiagState.ForeColor = Color.FromArgb(113, 113, 122);
                lblDiagState.BackColor = Color.FromArgb(244, 244, 245);
            }

            LayoutDiagPage();
        }

        /// <summary>在状态栏显示一条临时提示，4 秒后自动消失。</summary>
        private void ShowTransientMessage(string message, bool isError)
        {
            if (lblStatusMsg == null) return;

            lblStatusMsg.Text = message;
            lblStatusMsg.ForeColor = isError ? Color.FromArgb(185, 28, 28) : Color.FromArgb(4, 120, 87);

            if (_msgTimer == null)
            {
                _msgTimer = new System.Windows.Forms.Timer { Interval = 4000 };
                _msgTimer.Tick += (s, e) =>
                {
                    _msgTimer!.Stop();
                    lblStatusMsg.Text = string.Empty;
                };
            }

            _msgTimer.Stop();
            _msgTimer.Start();
        }

        private void SendCurrentFrame()
        {
            if (!_driver.IsOpen)
            {
                MessageBox.Show("请先连接硬件适配器！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                uint id = Convert.ToUInt32(txtTxId.Text.Trim(), 16);
                byte[] data = byteInput.GetBytes();

                CanFrameType frameType = cmbFrameType.SelectedIndex switch
                {
                    1 => CanFrameType.Standard,
                    2 => CanFrameType.Extended,
                    _ => id > 0x7FF ? CanFrameType.Extended : CanFrameType.Standard
                };

                var frame = new CanFrame
                {
                    Id = id,
                    FrameType = frameType,
                    Protocol = (chkCanFd.Checked || data.Length > 8) ? CanProtocolType.CANFD : CanProtocolType.CAN20,
                    BRS = chkBrs.Checked,
                    Dlc = (byte)data.Length,
                    Data = data
                };

                bool ok = _driver.SendFrame(frame);
                if (ok)
                {
                    RecordStat(frame, true);
                    ShowTransientMessage($"已发送 0x{frame.Id:X3}  [{frame.Dlc}]", false);
                }
                else
                {
                    ShowTransientMessage("发送失败：串口写入异常", true);
                    MessageBox.Show(
                        "发送失败：串口未成功写入。\r\n\r\n可能原因：\r\n  · 适配器已拔出或端口被占用\r\n  · USB 线接触不良\r\n\r\n请重新连接后再试。",
                        "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                ShowTransientMessage($"发送失败：{ex.Message}", true);
                MessageBox.Show($"发送格式错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>按行解析并依次发送多条帧，每条之间按所选间隔延时。</summary>
        private async System.Threading.Tasks.Task SendBulkFramesAsync()
        {
            if (!_driver.IsOpen)
            {
                MessageBox.Show("请先连接硬件适配器！", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<(uint Id, byte[] Data)> rows;
            try
            {
                rows = bulkGrid.GetFrames();
            }
            catch (FormatException ex)
            {
                MessageBox.Show(ex.Message, "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (rows.Count == 0)
            {
                MessageBox.Show("没有可发送的帧，请至少填写一行的 CAN ID 与数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var frames = new List<CanFrame>();
            foreach (var (id, data) in rows)
            {
                frames.Add(new CanFrame
                {
                    Id = id,
                    FrameType = cmbFrameType.SelectedIndex switch
                    {
                        1 => CanFrameType.Standard,
                        2 => CanFrameType.Extended,
                        _ => id > 0x7FF ? CanFrameType.Extended : CanFrameType.Standard
                    },
                    Protocol = (chkCanFd.Checked || data.Length > 8) ? CanProtocolType.CANFD : CanProtocolType.CAN20,
                    BRS = chkBrs.Checked,
                    Dlc = (byte)data.Length,
                    Data = data
                });
            }

            int gap = cmbBulkGap.SelectedIndex switch
            {
                0 => 0,
                1 => 5,
                2 => 10,
                3 => 20,
                4 => 50,
                5 => 100,
                _ => 10
            };

            int failed = 0;
            btnBulkSend.Enabled = false;
            try
            {
                foreach (var frame in frames)
                {
                    bool ok = _driver.SendFrame(frame);
                    if (ok) RecordStat(frame, true);
                    else failed++;

                    if (gap > 0) await System.Threading.Tasks.Task.Delay(gap);
                }
            }
            finally
            {
                btnBulkSend.Enabled = true;
            }

            if (failed > 0)
            {
                ShowTransientMessage($"批量发送：{failed}/{frames.Count} 条失败", true);
                MessageBox.Show(
                    $"批量发送完成，{frames.Count} 条中有 {failed} 条发送失败。\r\n请检查适配器连接状态或重新插拔后重试。",
                    "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                ShowTransientMessage($"批量发送完成，共 {frames.Count} 条", false);
            }
        }

        /// <summary>记录一帧收发报文，按方向 + CAN ID 聚合。</summary>
        private void RecordStat(CanFrame frame, bool isTx)
        {
            lock (_statsLock)
            {
                var key = (isTx, frame.Id);

                if (!_stats.TryGetValue(key, out var stat))
                {
                    stat = new FrameStat
                    {
                        IsTx = isTx,
                        Id = frame.Id,
                        FrameType = frame.FrameType,
                        Protocol = frame.Protocol,
                        FirstSeen = frame.Timestamp
                    };
                    _stats[key] = stat;
                }

                stat.Count++;
                stat.LastSeen = frame.Timestamp;
                stat.Dlc = frame.Dlc;
                stat.DataHex = frame.DataHexString;
                stat.FrameType = frame.FrameType;
                stat.Protocol = frame.Protocol;

                if (isTx) _txTotal++;
                else _rxTotal++;
            }
        }

        private void RefreshStatsTable()
        {
            List<FrameStat> snapshot;
            long rx, tx;

            lock (_statsLock)
            {
                snapshot = _stats.Values
                    .OrderByDescending(s => s.Count)
                    .ThenBy(s => s.IsTx)
                    .ThenBy(s => s.Id)
                    .ToList();
                rx = _rxTotal;
                tx = _txTotal;
            }

            _statsSnapshot = snapshot;
            gridStats.RowCount = snapshot.Count;
            gridStats.Invalidate();

            lblStatRx.Text = $"RX {rx:N0}";
            lblStatTx.Text = $"TX {tx:N0}";

            LayoutStatsHeader();
            lblKpiTotal.Text = (rx + tx).ToString("N0");
        }

        /// <summary>把相邻帧的时间差格式化为 0.8 ms / 125.4 ms 之类的短文本。</summary>
        private static string FormatInterval(TimeSpan delta)
        {
            double ms = delta.TotalMilliseconds;
            if (ms < 0) ms = 0;
            return ms < 1000 ? $"{ms:F1} ms" : $"{ms / 1000:F2} s";
        }

        /// <summary>从 "CANable Error Register: 0" 这类回包中只取数值部分。</summary>
        private static string FormatErrorRegister(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "—";

            int end = text.Length - 1;
            while (end >= 0 && !char.IsDigit(text[end])) end--;
            if (end < 0) return text;

            int start = end;
            while (start > 0 && char.IsDigit(text[start - 1])) start--;
            return text.Substring(start, end - start + 1);
        }

        private void HandleDriverLog(string msg)
        {
            // 驱动层的每条消息（含适配器回显）都记入诊断日志
            AppendDiagLog("<<", msg);

            if (msg.StartsWith("[TX Error]", StringComparison.Ordinal))
            {
                // 驱动层写入异常：只在状态栏提示，避免批量发送时弹窗刷屏
                ShowTransientMessage(msg.Replace("[TX Error]", "发送失败：").Trim(), true);
            }
            else if (msg.StartsWith("[Error]", StringComparison.Ordinal))
            {
                lblTopState.Text = "● 连接失败";
                lblTopState.ForeColor = Color.FromArgb(185, 28, 28);
                lblTopState.BackColor = Color.FromArgb(254, 242, 242);
                LayoutHeader();
                MessageBox.Show(msg, "设备连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else if (msg.StartsWith("[Warn]", StringComparison.Ordinal))
            {
                lblTopState.Text = "● 警告：固件无回应";
                lblTopState.ForeColor = Color.FromArgb(180, 83, 9);
                lblTopState.BackColor = Color.FromArgb(255, 251, 235);
                LayoutHeader();
            }
        }

        private void RefreshSerialPorts()
        {
            string? current = cmbPorts.SelectedItem as string;

            cmbPorts.BeginUpdate();
            try
            {
                cmbPorts.Items.Clear();
                string[] ports = SerialPort.GetPortNames();
                Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
                cmbPorts.Items.AddRange(ports);
            }
            finally
            {
                cmbPorts.EndUpdate();
            }

            if (!string.IsNullOrEmpty(current) && cmbPorts.Items.Contains(current))
                cmbPorts.SelectedItem = current;
            else if (cmbPorts.Items.Count > 0)
                cmbPorts.SelectedIndex = 0;
        }

        /// <summary>设备插入后 COM 口注册略有延迟，稍等再扫描。</summary>
        private void SchedulePortRescan()
        {
            if (_portRescanTimer == null)
            {
                _portRescanTimer = new System.Windows.Forms.Timer { Interval = 700 };
                _portRescanTimer.Tick += (s, e) =>
                {
                    _portRescanTimer!.Stop();
                    RefreshSerialPorts();
                };
            }

            _portRescanTimer.Stop();
            _portRescanTimer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_DEVICECHANGE)
            {
                int eventType = m.WParam.ToInt32();
                if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE)
                {
                    SchedulePortRescan();
                }
            }
        }

        // ============================ 窗口圆角（DWM） ============================

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int DWMWCP_ROUND = 2;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowChrome();
        }

        /// <summary>Windows 11 下启用 DWM 圆角与浅色标题栏；旧系统调用失败时保持默认外观。</summary>
        private void ApplyWindowChrome()
        {
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

                int caption = 0x00FFFFFF;   // COLORREF 白
                DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

                int text = 0x00272727;      // COLORREF 深灰
                DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch
            {
                // 不支持的系统保持默认直角窗口
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _uiRefreshTimer?.Stop();
            _portRescanTimer?.Stop();
            _portRescanTimer?.Dispose();
            _msgTimer?.Stop();
            _msgTimer?.Dispose();
            _driver.Dispose();
            base.OnFormClosing(e);
        }
    }
}
