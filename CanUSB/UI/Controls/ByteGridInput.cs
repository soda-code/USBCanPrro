using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    /// <summary>
    /// 表格式字节输入：表头标注 0-7，每格一个 Hex 字节，外观与报文表格保持一致。
    /// </summary>
    public class ByteGridInput : UserControl
    {
        private const int ByteCount = 8;
        private const int HeaderHeight = 22;
        private const int RowHeight = 34;
        private const int BorderRadius = 8;

        private readonly TextBox[] _cells = new TextBox[ByteCount];

        public Color BorderColor { get; set; } = Color.FromArgb(107, 114, 128);      // gray-400
        public Color HeaderColor { get; set; } = Color.FromArgb(244, 244, 245);      // zinc-100
        public Color GridColor { get; set; } = Color.FromArgb(228, 228, 231);        // zinc-200
        public Color HeaderTextColor { get; set; } = Color.FromArgb(113, 113, 122);  // zinc-500

        public ByteGridInput()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Height = HeaderHeight + RowHeight + 2;
            MinimumSize = new Size(ByteCount * 42, HeaderHeight + RowHeight + 2);

            for (int i = 0; i < ByteCount; i++)
            {
                var cell = new TextBox
                {
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.White,
                    Font = new Font("Consolas", 10f),
                    ForeColor = Color.FromArgb(39, 39, 42),
                    TextAlign = HorizontalAlignment.Center,
                    MaxLength = 2,
                    CharacterCasing = CharacterCasing.Upper,
                    Tag = i
                };

                cell.KeyDown += Cell_KeyDown;
                cell.TextChanged += (s, e) => Invalidate();
                cell.GotFocus += (s, e) => Invalidate();
                cell.LostFocus += (s, e) => Invalidate();

                _cells[i] = cell;
                Controls.Add(cell);
            }

            SetBytes(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 });
        }

        /// <summary>按 0-7 顺序读取字节；中间留空的格子按 0x00 处理，末尾空格外全部忽略。</summary>
        public byte[] GetBytes()
        {
            int last = ByteCount - 1;
            while (last >= 0 && _cells[last].Text.Trim().Length == 0) last--;

            if (last < 0) return Array.Empty<byte>();

            var data = new byte[last + 1];
            for (int i = 0; i <= last; i++)
            {
                string text = _cells[i].Text.Trim();
                if (text.Length == 0)
                {
                    data[i] = 0x00;
                    continue;
                }

                if (!byte.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out data[i]))
                {
                    throw new FormatException($"第 {i} 字节不是合法的 Hex：{text}");
                }
            }

            return data;
        }

        public void SetBytes(byte[] data)
        {
            for (int i = 0; i < ByteCount; i++)
            {
                _cells[i].Text = data != null && i < data.Length ? data[i].ToString("X2") : string.Empty;
            }
        }

        public void ClearBytes()
        {
            foreach (var cell in _cells) cell.Clear();
        }

        private void Cell_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox cell) return;

            int index = (int)cell.Tag!;

            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Right && cell.SelectionStart >= cell.TextLength)
            {
                if (index < ByteCount - 1)
                {
                    _cells[index + 1].Focus();
                    _cells[index + 1].SelectAll();
                }
                else
                {
                    Parent?.SelectNextControl(this, true, true, true, true);
                }
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Left && cell.SelectionStart == 0)
            {
                if (index > 0)
                {
                    _cells[index - 1].Focus();
                    _cells[index - 1].SelectAll();
                }
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCells();
            UpdateRegion();
        }

        private void LayoutCells()
        {
            // 构造期间设置 Height 会触发 OnResize，此时单元格还没创建
            if (_cells[0] == null) return;

            int cellWidth = Math.Max(1, (Width - 2) / ByteCount);

            for (int i = 0; i < ByteCount; i++)
            {
                _cells[i].SetBounds(i * cellWidth + 3, HeaderHeight + 5, cellWidth - 6, RowHeight - 8);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, BorderRadius);

            using (var brush = new SolidBrush(HeaderColor))
            using (var headerPath = new GraphicsPath())
            {
                // 只填充表头区域（矩形裁到圆角外形以内）
                headerPath.AddRectangle(new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 1, HeaderHeight));
                var old = e.Graphics.Clip;
                e.Graphics.SetClip(path, CombineMode.Intersect);
                e.Graphics.FillPath(brush, headerPath);
                e.Graphics.Clip = old;
            }

            int cellWidth = Math.Max(1, (Width - 2) / ByteCount);

            using (var pen = new Pen(GridColor))
            {
                for (int i = 1; i < ByteCount; i++)
                {
                    int x = i * cellWidth;
                    e.Graphics.DrawLine(pen, x, rect.Y + 1, x, rect.Bottom - 1);
                }
                e.Graphics.DrawLine(pen, rect.X + 1, HeaderHeight, rect.Right - 1, HeaderHeight);
            }

            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.DrawPath(pen, path);
            }

            using var font = new Font("Segoe UI", 8f, FontStyle.Regular);
            for (int i = 0; i < ByteCount; i++)
            {
                var textRect = new Rectangle(i * cellWidth, 1, cellWidth, HeaderHeight - 1);
                TextRenderer.DrawText(e.Graphics, i.ToString(), font, textRect, HeaderTextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                if (_cells[i] != null && _cells[i].Focused)
                {
                    using var focusPen = new Pen(Color.FromArgb(59, 130, 246), 2);
                    e.Graphics.DrawRectangle(focusPen,
                        i * cellWidth + 2, HeaderHeight + 3, cellWidth - 4, RowHeight - 4);
                }
            }
        }

        private void UpdateRegion()
        {
            if (Width <= 2 || Height <= 2) return;

            using var path = RoundedRect(new Rectangle(0, 0, Width, Height), BorderRadius);
            var region = new Region(path);
            var old = Region;
            Region = region;
            old?.Dispose();
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = Math.Max(2, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2)) * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
