using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    /// <summary>
    /// 去掉原生直角的圆角下拉框：自绘白底、文本与箭头，配合 <see cref="FieldBox"/> 提供圆角边框。
    /// </summary>
    public class ModernComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;

        public Color ArrowColor { get; set; } = Color.FromArgb(161, 161, 170);        // zinc-400
        public Color FillColor { get; set; } = Color.FromArgb(247, 248, 250);         // zinc-50 静息底色
        public Color FocusFillColor { get; set; } = Color.White;                      // 展开/聚焦时纯白
        public Color DisabledFillColor { get; set; } = Color.FromArgb(243, 244, 246);
        public Color DisabledTextColor { get; set; } = Color.FromArgb(161, 161, 170);
        public Color SelectedItemColor { get; set; } = Color.FromArgb(239, 246, 255); // blue-50
        public Color SelectedTextColor { get; set; } = Color.FromArgb(29, 78, 216);   // blue-700
        public int TextIndent { get; set; } = 2;

        public ModernComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = FillColor;
            ItemHeight = 22;
        }

        protected override void OnDropDown(EventArgs e)
        {
            base.OnDropDown(e);
            Invalidate();
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT)
            {
                using var g = Graphics.FromHwnd(Handle);
                PaintSurface(g);
            }
        }

        private void PaintSurface(Graphics g)
        {
            Color fill = !Enabled
                ? DisabledFillColor
                : (DroppedDown ? FocusFillColor : FillColor);

            using (var brush = new SolidBrush(fill))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            var textRect = new Rectangle(TextIndent, 0, Math.Max(0, Width - TextIndent - 22), Height);
            TextRenderer.DrawText(g, Text, Font, textRect,
                Enabled ? ForeColor : DisabledTextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width - 10;
            int cy = Height / 2;
            using var pen = new Pen(Enabled ? ArrowColor : DisabledTextColor, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, new[]
            {
                new Point(cx - 4, cy - 2),
                new Point(cx, cy + 2),
                new Point(cx + 4, cy - 2)
            });
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var itemRect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 1, Math.Max(0, e.Bounds.Width - 4), Math.Max(0, e.Bounds.Height - 2));

            if (itemRect.Width > 0 && itemRect.Height > 0)
            {
                using var path = RoundedRect(itemRect, 6);
                using var brush = new SolidBrush(selected ? SelectedItemColor : Color.White);
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(g, Items[e.Index]?.ToString() ?? string.Empty, Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 12), e.Bounds.Height),
                selected ? SelectedTextColor : Color.FromArgb(63, 63, 70),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2)) * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
