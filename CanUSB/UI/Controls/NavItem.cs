using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    /// <summary>
    /// 侧边栏导航项：圆角背景 + 激活态 2px 实心左线 + 悬停浅灰底。
    /// </summary>
    public class NavItem : Control
    {
        private bool _active;
        private bool _hovered;

        private readonly Font _fontNormal = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        private readonly Font _fontActive = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        private readonly Font _fontGlyph = new Font("Segoe UI Symbol", 10f, FontStyle.Regular);

        public bool Active
        {
            get => _active;
            set { _active = value; Invalidate(); }
        }

        public string Glyph { get; set; } = string.Empty;

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                Text = value;
                AccessibleName = value;
                Invalidate();
            }
        }

        public int BorderRadius { get; set; } = 8;

        public NavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
            Height = 38;
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var path = RoundedRect(rect, BorderRadius);

            Color bg = _active
                ? Color.FromArgb(239, 246, 255)                                  // blue-50
                : (_hovered ? Color.FromArgb(244, 244, 245) : BackColor);       // zinc-100

            using (var brush = new SolidBrush(bg))
            {
                g.FillPath(brush, path);
            }

            if (_active)
            {
                var bar = new Rectangle(0, 8, 3, Math.Max(4, Height - 16));
                using var barPath = RoundedRect(bar, 2);
                using var accent = new SolidBrush(Color.FromArgb(37, 99, 235));  // blue-600
                g.FillPath(accent, barPath);
            }

            Color fg = _active
                ? Color.FromArgb(29, 78, 216)                                    // blue-700
                : Color.FromArgb(63, 63, 70);                                    // zinc-700

            TextRenderer.DrawText(g, Glyph, _fontGlyph,
                new Rectangle(12, 0, 20, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, Title, _active ? _fontActive : _fontNormal,
                new Rectangle(36, 0, Math.Max(0, Width - 44), Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fontNormal.Dispose();
                _fontActive.Dispose();
                _fontGlyph.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
