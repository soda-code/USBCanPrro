using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    public class ModernButton : Button
    {
        private int _borderRadius = 8;

        public int BorderRadius
        {
            get => _borderRadius;
            set
            {
                _borderRadius = value;
                UpdateRegion();
                Invalidate();
            }
        }

        public Color NormalColor { get; set; } = Color.FromArgb(59, 130, 246);  // blue-500
        public Color HoverColor { get; set; } = Color.FromArgb(37, 99, 235);    // blue-600
        public Color PressedColor { get; set; } = Color.FromArgb(29, 78, 216);  // blue-700
        public Color BorderOutlineColor { get; set; } = Color.Transparent;

        private bool _isHovered = false;
        private bool _isPressed = false;

        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            DoubleBuffered = true;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            Height = 36;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _isPressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs mevent) { _isPressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _isPressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var path = CreateRoundedPath();

            Color currentBg = NormalColor;
            if (!Enabled) currentBg = Color.FromArgb(244, 244, 245);
            else if (_isPressed) currentBg = PressedColor;
            else if (_isHovered) currentBg = HoverColor;

            using (var brush = new SolidBrush(currentBg))
            {
                pevent.Graphics.FillPath(brush, path);
            }

            Color outline = Enabled ? BorderOutlineColor : Color.FromArgb(228, 228, 231);
            if (outline != Color.Transparent)
            {
                using var pen = new Pen(outline, 1);
                pevent.Graphics.DrawPath(pen, path);
            }

            Color textColor = Enabled ? ForeColor : Color.FromArgb(161, 161, 170);
            TextRenderer.DrawText(pevent.Graphics, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private GraphicsPath CreateRoundedPath(int inset = 1)
        {
            var rect = new Rectangle(0, 0, Math.Max(1, Width - inset), Math.Max(1, Height - inset));
            int diameter = Math.Max(1, Math.Min(_borderRadius, Math.Min(rect.Width, rect.Height) / 2)) * 2;

            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// 只在尺寸/圆角变化时更新窗口区域。绝不能在 OnPaint 里改 Region：
        /// 鼠标按下会触发重绘，SetWindowRgn 会导致 MouseUp 丢失，Click 不触发。
        /// </summary>
        private void UpdateRegion()
        {
            if (Width <= 2 || Height <= 2) return;

            using var path = CreateRoundedPath(0);
            var region = new Region(path);
            var old = Region;
            Region = region;
            old?.Dispose();
        }
    }
}
