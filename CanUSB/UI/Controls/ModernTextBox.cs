using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    public class ModernTextBox : UserControl
    {
        private TextBox _innerBox;
        private int _borderRadius = 8;
        private Color _fillColor = Color.FromArgb(247, 248, 250);   // zinc-50 静息底色
        private bool _isFocused = false;

        /// <summary>获得焦点时的填充色，默认纯白以突出当前编辑项。</summary>
        public Color FocusFillColor { get; set; } = Color.White;

        public int BorderRadius
        {
            get => _borderRadius;
            set { _borderRadius = value; UpdateRegion(); Invalidate(); }
        }

        public Color BorderColor { get; set; } = Color.FromArgb(107, 114, 128);   // gray-400，与下拉框一致
        public Color FocusBorderColor { get; set; } = Color.FromArgb(59, 130, 246);

        public Color FillColor
        {
            get => _fillColor;
            set
            {
                _fillColor = value;
                ApplyFill();
            }
        }

        private void ApplyFill()
        {
            if (_innerBox == null) return;

            _innerBox.BackColor = _isFocused ? FocusFillColor : _fillColor;
            Invalidate();
        }

        [AllowNull]
        public override string Text
        {
            get => _innerBox.Text;
            set => _innerBox.Text = value ?? string.Empty;
        }

        public bool ReadOnly
        {
            get => _innerBox.ReadOnly;
            set => _innerBox.ReadOnly = value;
        }

        public ModernTextBox()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(10, 8, 10, 8);
            Height = 36;

            _innerBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = _fillColor,
                Font = new Font("Consolas", 10f),
                Dock = DockStyle.Fill
            };

            _innerBox.GotFocus += (s, e) => { _isFocused = true; ApplyFill(); };
            _innerBox.LostFocus += (s, e) => { _isFocused = false; ApplyFill(); };

            // 内部输入框的文本变化转发给宿主控件，外部订阅 TextChanged 才能生效
            _innerBox.TextChanged += (s, e) => OnTextChanged(EventArgs.Empty);

            // 内部输入框尺寸变化时重设圆角裁剪，避免它盖住四角描边
            _innerBox.Resize += (s, e) => ApplyInnerRegion();

            this.Controls.Add(_innerBox);
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
            using (var fillPath = GetRoundedRectangle(fill, _borderRadius))
            using (var brush = new SolidBrush(_isFocused ? FocusFillColor : _fillColor))
            {
                e.Graphics.FillPath(brush, fillPath);
            }

            // 描边中心线内缩半个线宽，线条外沿正好贴住控件边界，不被窗口区域裁掉
            float penWidth = _isFocused ? 2f : 1.5f;
            float inset = penWidth / 2f;
            var stroke = new RectangleF(inset, inset,
                Math.Max(1, Width - 1 - inset * 2), Math.Max(1, Height - 1 - inset * 2));

            using var path = GetRoundedRectangle(stroke, _borderRadius);
            using var pen = new Pen(_isFocused ? FocusBorderColor : BorderColor, penWidth);

            // 描边关闭抗锯齿并对齐像素中心，线条才实、四角才清晰
            e.Graphics.SmoothingMode = SmoothingMode.None;
            e.Graphics.DrawPath(pen, path);
        }

        /// <summary>Region 只在尺寸变化时设置，避免绘制期间改变窗口区域导致子控件点击异常。</summary>
        private void UpdateRegion()
        {
            if (Width <= 2 || Height <= 2) return;

            using var path = GetRoundedRectangle(new Rectangle(0, 0, Width, Height), _borderRadius);
            var region = new Region(path);
            var old = Region;
            Region = region;
            old?.Dispose();

            ApplyInnerRegion();
        }

        /// <summary>内部输入框裁成圆角，否则作为矩形会盖住四角的描边弧线。</summary>
        private void ApplyInnerRegion()
        {
            if (_innerBox == null || _innerBox.Width <= 4 || _innerBox.Height <= 4) return;

            using var innerPath = GetRoundedRectangle(
                new Rectangle(0, 0, _innerBox.Width, _innerBox.Height),
                _borderRadius);
            var innerRegion = new Region(innerPath);
            var innerOld = _innerBox.Region;
            _innerBox.Region = innerRegion;
            innerOld?.Dispose();
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
