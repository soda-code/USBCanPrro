using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    /// <summary>
    /// 批量帧输入表格：每行一条报文，列为 CAN ID + 字节 0-7，外观与报文表格一致。
    /// </summary>
    public class FrameGridInput : UserControl
    {
        private const int HeaderHeight = 22;
        private const int RowHeight = 28;
        private const int BorderRadius = 8;

        private readonly int _rows;
        private readonly TextBox[] _ids;
        private readonly TextBox[,] _data;
        private readonly int _idWidth = 92;

        public Color BorderColor { get; set; } = Color.FromArgb(107, 114, 128);   // gray-400
        public Color HeaderColor { get; set; } = Color.FromArgb(244, 244, 245);
        public Color GridColor { get; set; } = Color.FromArgb(228, 228, 231);
        public Color HeaderTextColor { get; set; } = Color.FromArgb(113, 113, 122);

        public FrameGridInput(int rows = 5)
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            _rows = Math.Max(1, rows);

            _ids = new TextBox[_rows];
            _data = new TextBox[_rows, 8];

            for (int r = 0; r < _rows; r++)
            {
                _ids[r] = CreateCell(false);
                Controls.Add(_ids[r]);

                for (int c = 0; c < 8; c++)
                {
                    _data[r, c] = CreateCell(true);
                    Controls.Add(_data[r, c]);
                }
            }

            Height = HeaderHeight + _rows * RowHeight + 2;
            SetSample();
        }

        private TextBox CreateCell(bool hexByte)
        {
            var cell = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Consolas", 9.5f),
                ForeColor = Color.FromArgb(39, 39, 42),
                TextAlign = HorizontalAlignment.Center,
                MaxLength = hexByte ? 2 : 8,
                CharacterCasing = CharacterCasing.Upper
            };

            cell.KeyDown += Cell_KeyDown;
            cell.GotFocus += (s, e) => Invalidate();
            cell.LostFocus += (s, e) => Invalidate();
            return cell;
        }

        public void SetSample()
        {
            string[,] sample =
            {
                { "123", "11", "22", "33", "44", "", "", "", "" },
                { "123", "55", "66", "77", "88", "", "", "", "" },
                { "123", "99", "AA", "BB", "CC", "", "", "", "" }
            };

            for (int r = 0; r < _rows; r++)
            {
                bool hasRow = r < sample.GetLength(0);
                _ids[r].Text = hasRow ? sample[r, 0] : string.Empty;

                for (int c = 0; c < 8; c++)
                {
                    _data[r, c].Text = hasRow ? sample[r, c + 1] : string.Empty;
                }
            }
        }

        public void ClearRows()
        {
            foreach (var id in _ids) id.Clear();
            foreach (var cell in _data) cell.Clear();
        }

        /// <summary>按行读取，ID 与数据都为空的行会被跳过。</summary>
        public List<(uint Id, byte[] Data)> GetFrames()
        {
            var frames = new List<(uint, byte[])>();

            for (int r = 0; r < _rows; r++)
            {
                string idText = _ids[r].Text.Trim();

                int last = 7;
                while (last >= 0 && _data[r, last].Text.Trim().Length == 0) last--;

                if (idText.Length == 0 && last < 0) continue;

                if (idText.Length == 0)
                {
                    throw new FormatException($"第 {r + 1} 行缺少 CAN ID");
                }

                if (!uint.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id))
                {
                    throw new FormatException($"第 {r + 1} 行 CAN ID 不是合法 Hex：{idText}");
                }

                var data = new byte[last + 1];
                for (int c = 0; c <= last; c++)
                {
                    string text = _data[r, c].Text.Trim();
                    if (text.Length == 0)
                    {
                        data[c] = 0x00;
                        continue;
                    }

                    if (!byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data[c]))
                    {
                        throw new FormatException($"第 {r + 1} 行第 {c} 字节不是合法 Hex：{text}");
                    }
                }

                frames.Add((id, data));
            }

            return frames;
        }

        private void Cell_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox cell) return;

            if (e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Right && cell.SelectionStart >= cell.TextLength))
            {
                SelectNextControl(cell, true, true, true, true);
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Left && cell.SelectionStart == 0)
            {
                SelectNextControl(cell, false, true, true, true);
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
            if (_ids == null || _ids[0] == null) return;

            int cellWidth = Math.Max(1, (Width - _idWidth - 2) / 8);

            for (int r = 0; r < _rows; r++)
            {
                int y = HeaderHeight + r * RowHeight + 4;

                _ids[r].SetBounds(3, y, _idWidth - 6, RowHeight - 8);

                for (int c = 0; c < 8; c++)
                {
                    _data[r, c].SetBounds(_idWidth + c * cellWidth + 3, y, cellWidth - 6, RowHeight - 8);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, BorderRadius);

            using (var brush = new SolidBrush(HeaderColor))
            {
                var oldClip = e.Graphics.Clip;
                e.Graphics.SetClip(path, CombineMode.Intersect);
                e.Graphics.FillRectangle(brush, new Rectangle(rect.X + 1, rect.Y + 1, rect.Width - 1, HeaderHeight));
                e.Graphics.Clip = oldClip;
            }

            int cellWidth = Math.Max(1, (Width - _idWidth - 2) / 8);

            using (var pen = new Pen(GridColor))
            {
                e.Graphics.DrawLine(pen, _idWidth, rect.Y + 1, _idWidth, rect.Bottom - 1);

                for (int c = 1; c < 8; c++)
                {
                    int x = _idWidth + c * cellWidth;
                    e.Graphics.DrawLine(pen, x, rect.Y + 1, x, rect.Bottom - 1);
                }

                e.Graphics.DrawLine(pen, rect.X + 1, HeaderHeight, rect.Right - 1, HeaderHeight);

                for (int r = 1; r < _rows; r++)
                {
                    int y = HeaderHeight + r * RowHeight;
                    e.Graphics.DrawLine(pen, rect.X + 1, y, rect.Right - 1, y);
                }
            }

            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.DrawPath(pen, path);
            }

            using var font = new Font("Segoe UI", 8f);
            TextRenderer.DrawText(e.Graphics, "CAN ID", font,
                new Rectangle(0, 1, _idWidth, HeaderHeight - 1), HeaderTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            for (int c = 0; c < 8; c++)
            {
                var textRect = new Rectangle(_idWidth + c * cellWidth, 1, cellWidth, HeaderHeight - 1);
                TextRenderer.DrawText(e.Graphics, c.ToString(), font, textRect, HeaderTextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            for (int r = 0; r < _rows; r++)
            {
                int y = HeaderHeight + r * RowHeight + 2;

                if (_ids[r] != null && _ids[r].Focused)
                {
                    using var focusPen = new Pen(Color.FromArgb(59, 130, 246), 2);
                    e.Graphics.DrawRectangle(focusPen, 2, y, _idWidth - 4, RowHeight - 4);
                }

                for (int c = 0; c < 8; c++)
                {
                    if (_data[r, c] != null && _data[r, c].Focused)
                    {
                        using var focusPen = new Pen(Color.FromArgb(59, 130, 246), 2);
                        e.Graphics.DrawRectangle(focusPen,
                            _idWidth + c * cellWidth + 2, y, cellWidth - 4, RowHeight - 4);
                    }
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
