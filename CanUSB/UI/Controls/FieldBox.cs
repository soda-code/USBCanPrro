using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    /// <summary>
    /// 圆角输入容器：为原生 ComboBox / TextBox 提供统一的圆角白底边框外观。
    /// </summary>
    public class FieldBox : Panel
    {
        public int BorderRadius { get; set; } = 8;
        public Color BorderColor { get; set; } = Color.FromArgb(107, 114, 128);   // gray-400，与输入框一致
        public Color FocusBorderColor { get; set; } = Color.FromArgb(59, 130, 246); // blue-500
        public Color FillColor { get; set; } = Color.White;
        public bool IsFocused { get; set; }

        public FieldBox()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(1);
        }

        /// <summary>内部下拉框获得/失去焦点时同步边框状态，与输入框表现一致。</summary>
        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);

            var child = e.Control;
            if (child == null) return;

            child.GotFocus += (s, a) => SetFocused(true);
            child.LostFocus += (s, a) => SetFocused(false);
            child.Resize += (s, a) => ApplyChildRegion(child);
            ApplyChildRegion(child);
        }

        /// <summary>子控件也裁成圆角，否则作为矩形会盖住容器四角的描边弧线。</summary>
        private void ApplyChildRegion(Control child)
        {
            if (child.Width <= 4 || child.Height <= 4) return;

            // 半径必须不小于容器，否则子控件圆角会外扩到容器弧线上、把弧线吃掉
            using var path = GetRoundedRectangle(new Rectangle(0, 0, child.Width, child.Height),
                BorderRadius);
            var region = new Region(path);
            var old = child.Region;
            child.Region = region;
            old?.Dispose();
        }

        private void SetFocused(bool focused)
        {
            if (IsFocused == focused) return;
            IsFocused = focused;
            Invalidate();
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

            var fill = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var fillPath = GetRoundedRectangle(fill, BorderRadius))
            using (var brush = new SolidBrush(FillColor))
            {
                e.Graphics.FillPath(brush, fillPath);
            }

            // 描边中心线内缩半个线宽，线条外沿正好贴住控件边界，不被窗口区域裁掉
            float penWidth = IsFocused ? 2f : 1.5f;
            float inset = penWidth / 2f;
            var stroke = new RectangleF(inset, inset,
                Math.Max(1, Width - 1 - inset * 2), Math.Max(1, Height - 1 - inset * 2));

            using var path = GetRoundedRectangle(stroke, BorderRadius);
            using var pen = new Pen(IsFocused ? FocusBorderColor : BorderColor, penWidth);

            // 描边关闭抗锯齿并对齐像素中心，线条才实、四角才清晰
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.DrawPath(pen, path);
        }

        private void UpdateRegion()
        {
            if (Width <= 2 || Height <= 2) return;

            using var path = GetRoundedRectangle(new Rectangle(0, 0, Width, Height), BorderRadius);
            var region = new Region(path);
            var old = Region;
            Region = region;
            old?.Dispose();

            foreach (Control child in Controls) ApplyChildRegion(child);
        }

        private static GraphicsPath GetRoundedRectangle(RectangleF rect, int radius)
        {
            float diameter = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2)) * 2;

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
