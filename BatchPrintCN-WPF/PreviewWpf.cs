using System;
using System.Globalization;
using System.Drawing.Printing;
using System.Windows;
using System.Windows.Media;

namespace BatchPrintCN
{
    public class PaperPreviewWpf : FrameworkElement
    {
        private DuplexMode _duplex = DuplexMode.Simplex;
        private OrientationMode _orientation = OrientationMode.Auto;
        private ColorMode _color = ColorMode.Color;
        private PaperKind _paperKind = PaperKind.A4;
        private string _paperName = "A4";
        private string _scaleText = "缩放到合适页面";
        private int _pages = 1;

        public void Update(DuplexMode duplex, OrientationMode orientation, ColorMode color, string paperName, PaperKind kind, string scaleText, int pages)
        {
            _duplex = duplex; _orientation = orientation; _color = color; _paperName = paperName; _paperKind = kind; _scaleText = scaleText; _pages = pages;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Brush area;
            try { area = (Brush)FindResource("PreviewAreaBrush"); }
            catch { area = new SolidColorBrush(Color.FromRgb(240, 242, 245)); }
            dc.DrawRectangle(area, null, new Rect(0, 0, ActualWidth, ActualHeight));

            Size mm = PaperSizeMm();
            bool landscape = _orientation == OrientationMode.Landscape || (_orientation == OrientationMode.Auto && mm.Width > mm.Height);
            if (landscape) mm = new Size(mm.Height, mm.Width);

            double scale = Math.Min((ActualWidth - 70) / mm.Width, (ActualHeight - 150) / mm.Height);
            if (scale <= 0) return;
            double pw = mm.Width * scale, ph = mm.Height * scale;
            double px = (ActualWidth - pw) / 2, py = (ActualHeight - ph) / 2 + 4;

            bool showBack = _duplex != DuplexMode.Simplex;
            if (showBack)
            {
                double bx = px + (_duplex == DuplexMode.LongEdge ? 12 : 0);
                double by = py + (_duplex == DuplexMode.ShortEdge ? -12 : 0);
                var backRect = new Rect(bx, by, pw, ph);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 150, 155, 160)), new Pen(Brushes.Gray, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, backRect);
                DrawText(dc, "背面", 9, new Point(backRect.X + 5, backRect.Y + 2), Brushes.Gray, dpi);
            }

            var page = new Rect(px, py, pw, ph);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, new Rect(px + 5, py + 7, pw, ph));
            dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(120, 130, 140)), 1), page);
            DrawContent(dc, page, dpi);
            DrawBadge(dc, page, dpi);

            string label;
            Brush labelBrush;
            if (showBack && _duplex == DuplexMode.LongEdge) { label = "长边翻页  ◀ ▶"; labelBrush = new SolidColorBrush(Color.FromRgb(0, 100, 180)); }
            else if (showBack) { label = "短边翻页  ▲ ▼"; labelBrush = new SolidColorBrush(Color.FromRgb(180, 80, 0)); }
            else { label = "单面"; labelBrush = Brushes.DimGray; }
            DrawText(dc, label, 11, new Point(px + 10, py + ph + 8), labelBrush, dpi, FontWeights.Bold);

            string info = _paperName + " · " + (landscape ? "横向" : "纵向") + " · " + ColorText() + " · " + _scaleText + (_pages > 1 ? " · 每页" + _pages + "版" : "");
            DrawText(dc, info, 10, new Point(px, py + ph + 30), Brushes.Gray, dpi);
        }

        private void DrawContent(DrawingContext dc, Rect r, double dpi)
        {
            bool color = _color == ColorMode.Color;
            Brush accent = new SolidColorBrush(color ? Color.FromRgb(220, 60, 90) : Color.FromRgb(150, 150, 150));
            Brush line1 = new SolidColorBrush(color ? Color.FromRgb(70, 110, 200) : Color.FromRgb(130, 130, 130));
            Brush line2 = new SolidColorBrush(color ? Color.FromRgb(90, 150, 90) : Color.FromRgb(165, 165, 165));

            if (_pages > 1)
            {
                // F6（V4.3）：9 版 3×3、16 版 4×4（与 PdfPrintEngine.ComputeGrid 同口径）
                int rows = _pages == 2 ? 1 : _pages == 9 ? 3 : _pages == 16 ? 4 : 2;
                int cols = _pages == 2 ? 2 : _pages == 9 ? 3 : _pages == 16 ? 4 : _pages == 6 ? 3 : 2;
                double cw = r.Width / cols, ch = r.Height / rows;
                for (int yy = 0; yy < rows; yy++)
                    for (int xx = 0; xx < cols; xx++)
                    {
                        var cell = new Rect(r.X + xx * cw + 4, r.Y + yy * ch + 4, Math.Max(4, cw - 8), Math.Max(4, ch - 8));
                        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(150, 160, 170)), 0.8), cell);
                        dc.DrawRectangle(line1, null, new Rect(cell.X + cell.Width / 8, cell.Y + cell.Height / 6, cell.Width * 3 / 4, Math.Max(2, cell.Height / 6)));
                        dc.DrawRectangle(line2, null, new Rect(cell.X + cell.Width / 8, cell.Y + cell.Height * 3 / 5, cell.Width * 3 / 4, Math.Max(2, cell.Height / 8)));
                    }
                return;
            }

            dc.DrawRectangle(accent, null, new Rect(r.X + r.Width / 8, r.Y + r.Height / 9, r.Width * 3 / 4, Math.Max(4, r.Height / 14)));
            var rand = new Random(7);
            double y = r.Y + r.Height / 3;
            for (int i = 0; i < 7; i++)
            {
                int wMul = rand.Next(5, 9);
                Brush b = i % 3 == 0 ? line1 : i % 3 == 1 ? line2 : Brushes.LightGray;
                dc.DrawRectangle(b, null, new Rect(r.X + r.Width / 8, y, r.Width * wMul / 12, Math.Max(3, r.Height / 34)));
                y += r.Height / 16;
                if (y > r.Bottom - r.Height / 4) break;
            }
        }

        private void DrawBadge(DrawingContext dc, Rect r, double dpi)
        {
            var ft = MakeText(_paperName, 13, dpi, FontWeights.Bold);
            double bw = ft.Width + 16, bh = ft.Height + 8;
            double bx = r.Right - bw - 10, by = r.Top + 8;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 238, 170)), new Pen(new SolidColorBrush(Color.FromRgb(210, 150, 40)), 1), new Rect(bx, by, bw, bh), 3, 3);
            dc.DrawText(ft, new Point(bx + 8, by + 4));
        }

        private FormattedText MakeText(string text, double size, double dpi, FontWeight weight)
        {
            return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, weight, FontStretches.Normal),
                size, Brushes.Black, dpi);
        }

        private void DrawText(DrawingContext dc, string text, double size, Point p, Brush brush, double dpi, FontWeight? weight = null)
        {
            var ft = MakeText(text, size, dpi, weight ?? FontWeights.Normal);
            ft.SetForegroundBrush(brush);
            dc.DrawText(ft, p);
        }

        private string ColorText() { return _color == ColorMode.BlackWhite ? "黑白" : "彩色"; }

        private Size PaperSizeMm()
        {
            switch (_paperKind)
            {
                case PaperKind.A3: return new Size(297, 420);
                case PaperKind.A5: return new Size(148, 210);
                case PaperKind.B5: return new Size(176, 250);
                case PaperKind.Letter: return new Size(216, 279);
                case PaperKind.Legal: return new Size(216, 356);
                default: return new Size(210, 297);
            }
        }
    }
}
