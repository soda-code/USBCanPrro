using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    public class RoundedCardPanel : Panel
    {
        public int BorderRadius { get; set; } = 12;
        public Color BorderColor { get; set; } = Color.FromArgb(226, 232, 240); // zinc-200
        public int BorderWidth { get; set; } = 1;

        public RoundedCardPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(14);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = GetRoundedRectangle(rect, BorderRadius);

            if (BorderWidth > 0)
            {
                using var pen = new Pen(BorderColor, BorderWidth);
                e.Graphics.DrawPath(pen, path);
            }
        }

        /// <summary>Region 只在尺寸变化时设置，避免在绘制过程中改变窗口区域影响命中测试。</summary>
        private void UpdateRegion()
        {
            if (Width <= 2 || Height <= 2) return;

            // Region 用完整尺寸，绘制才内缩 1px，避免卡片边框被裁细
            using var path = GetRoundedRectangle(new Rectangle(0, 0, Width, Height), BorderRadius);
            var region = new Region(path);
            var old = Region;
            Region = region;
            old?.Dispose();
        }

        private static GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
        {
            int diameter = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2)) * 2;

            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
