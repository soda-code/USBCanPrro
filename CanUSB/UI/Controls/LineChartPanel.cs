using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CanUSB.UI.Controls
{
    /// <summary>
    /// 轻量折线趋势图：自适应 Y 轴、网格、渐变填充与最新值标注。
    /// </summary>
    public class LineChartPanel : Control
    {
        private (DateTime Time, double Value)[] _points = Array.Empty<(DateTime Time, double Value)>();

        public Color LineColor { get; set; } = Color.FromArgb(37, 99, 235);
        public Color GridColor { get; set; } = Color.FromArgb(240, 240, 243);
        public Color AxisTextColor { get; set; } = Color.FromArgb(161, 161, 170);
        public Color ValueColor { get; set; } = Color.FromArgb(24, 24, 27);
        public string EmptyText { get; set; } = "等待目标 ID 的报文…";

        public int PointCount => _points.Length;

        public LineChartPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        public void SetPoints((DateTime Time, double Value)[] points)
        {
            _points = points ?? Array.Empty<(DateTime Time, double Value)>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(BackColor))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            var area = new Rectangle(52, 18, Math.Max(24, Width - 72), Math.Max(24, Height - 56));

            using var axisFont = new Font("Consolas", 8f);

            if (_points.Length < 2)
            {
                using var emptyFont = new Font("Segoe UI", 9f);
                TextRenderer.DrawText(g, EmptyText, emptyFont, ClientRectangle, AxisTextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            double min = _points.Min(p => p.Value);
            double max = _points.Max(p => p.Value);

            if (Math.Abs(max - min) < 0.001)
            {
                min -= 1;
                max += 1;
            }

            double padding = (max - min) * 0.18;
            min -= padding;
            max += padding;

            using (var gridPen = new Pen(GridColor))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = area.Top + area.Height * i / 4;
                    g.DrawLine(gridPen, area.Left, y, area.Right, y);

                    double value = max - (max - min) * i / 4;
                    TextRenderer.DrawText(g, FormatAxis(value), axisFont,
                        new Rectangle(0, y - 8, area.Left - 8, 16), AxisTextColor,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }

                for (int i = 1; i < 6; i++)
                {
                    int x = area.Left + area.Width * i / 6;
                    g.DrawLine(gridPen, x, area.Top, x, area.Bottom);
                }
            }

            using (var borderPen = new Pen(Color.FromArgb(228, 228, 231)))
            {
                g.DrawLine(borderPen, area.Left, area.Bottom, area.Right, area.Bottom);
                g.DrawLine(borderPen, area.Left, area.Top, area.Left, area.Bottom);
            }

            var points = new PointF[_points.Length];
            for (int i = 0; i < _points.Length; i++)
            {
                float rx = (float)i / (_points.Length - 1);
                float ry = (float)((_points[i].Value - min) / (max - min));
                points[i] = new PointF(area.Left + rx * area.Width, area.Bottom - ry * area.Height);
            }

            using (var fillPath = new GraphicsPath())
            {
                fillPath.AddLines(points);
                fillPath.AddLine(points[^1].X, area.Bottom, points[0].X, area.Bottom);
                fillPath.CloseFigure();

                using var fillBrush = new LinearGradientBrush(
                    new Rectangle(area.Left, area.Top, Math.Max(1, area.Width), Math.Max(1, area.Height)),
                    Color.FromArgb(70, LineColor),
                    Color.FromArgb(8, LineColor),
                    LinearGradientMode.Vertical);
                g.FillPath(fillBrush, fillPath);
            }

            using (var linePen = new Pen(LineColor, 2f) { LineJoin = LineJoin.Round })
            {
                g.DrawLines(linePen, points);
            }

            var last = points[^1];
            using (var dotBrush = new SolidBrush(LineColor))
            {
                g.FillEllipse(dotBrush, last.X - 3.5f, last.Y - 3.5f, 7f, 7f);
            }

            using (var valueFont = new Font("Consolas", 15f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, FormatAxis(_points[^1].Value), valueFont,
                    new Rectangle(area.Right - 120, area.Top - 12, 120, 24), ValueColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }

            var span = _points[^1].Time - _points[0].Time;
            string spanText = span.TotalSeconds < 60
                ? $"{span.TotalSeconds:F1} s"
                : $"{span.TotalMinutes:F1} min";

            TextRenderer.DrawText(g, $"{_points.Length} 点 · 跨度 {spanText}", axisFont,
                new Rectangle(area.Left, area.Bottom + 6, area.Width, 18), AxisTextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private static string FormatAxis(double value)
        {
            return Math.Abs(value) >= 100 ? value.ToString("F0") : value.ToString("F1");
        }
    }
}
