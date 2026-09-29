using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BatchPrintCN
{

    // M4 拆分（V4.2.3）：pdfium 渲染打印通道（页面绘制/缩放/灰度/拼版/加密 PDF/图片）。逻辑与 PrintCore.cs 原样一致，仅换文件。
    public partial class BatchPrinter
    {
        private bool UseLandscape(SizeF contentSize)
        {
            if (E.Orientation == OrientationMode.Portrait) return false;
            if (E.Orientation == OrientationMode.Landscape) return true;
            return contentSize.Width > contentSize.Height;
        }

        private Rectangle ApplyScale(Rectangle rect)
        {
            if (E.FitToPage) return rect;
            double p = E.ScalePercent;
            if (p < 1) p = 1;
            if (p > 400) p = 400;
            double f = p / 100.0;
            int w = Math.Max(1, (int)(rect.Width * f));
            int h = Math.Max(1, (int)(rect.Height * f));
            int x = rect.X + (rect.Width - w) / 2;
            int y = rect.Y + (rect.Height - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        private void DrawPage(Graphics g, Image src, Rectangle rect)
        {
            if (E.Color == ColorMode.BlackWhite)
            {
                Bitmap gray;
                if (src is Bitmap bmp) gray = ToGray(bmp);
                else { using (var copy = new Bitmap(src)) gray = ToGray(copy); }
                using (gray) g.DrawImage(gray, rect);
            }
            else
            {
                g.DrawImage(src, rect);
            }
        }

        private static Bitmap ToGray(Bitmap src)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb);
            float[][] matrix = {
                new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
                new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
                new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            };
            using (Graphics g = Graphics.FromImage(dst))
            using (var attrs = new ImageAttributes())
            {
                attrs.SetColorMatrix(new ColorMatrix(matrix), ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
            }
            return dst;
        }

        // M5：加密 PDF 引导——首次打开需要密码时回调 UI 输入密码，携带密码重试一次
        private PdfiumFile OpenPdfWithPassword(string file)
        {
            try { return PdfiumFile.Open(file); }
            catch (PdfPasswordRequiredException)
            {
                if (_askPassword == null) throw new Exception("PDF 已加密，需要密码才能打印：" + Path.GetFileName(file));
                string pwd = _askPassword(file);
                if (string.IsNullOrEmpty(pwd)) throw new Exception("已取消打印加密 PDF：" + Path.GetFileName(file));
                return PdfiumFile.Open(file, pwd);
            }
        }

        // F6（V4.3）：缩放模式——在"适配"结果之上按模式给出最终绘制矩形
        private Rectangle ApplyScaleMode(Rectangle fitRect, Size nativePx, float dpi, Rectangle bounds)
        {
            Rectangle Centered(double w, double h)
            {
                int iw = Math.Max(1, (int)Math.Round(w));
                int ih = Math.Max(1, (int)Math.Round(h));
                int x = bounds.X + (bounds.Width - iw) / 2;
                int y = bounds.Y + (bounds.Height - ih) / 2;
                return new Rectangle(x, y, iw, ih);
            }
            switch (E.EffectiveScale)
            {
                case ScaleMode.None:   // 不缩放：按原尺寸（渲染 dpi → 打印机百分之一英寸），超出页面即裁切
                    return Centered(nativePx.Width * 100.0 / dpi, nativePx.Height * 100.0 / dpi);
                case ScaleMode.ShrinkOnly:   // 缩小适配：只缩不放
                    {
                        double uw = nativePx.Width * 100.0 / dpi;
                        double uh = nativePx.Height * 100.0 / dpi;
                        double s = Math.Min(1.0, Math.Min(bounds.Width / uw, bounds.Height / uh));
                        return Centered(uw * s, uh * s);
                    }
                case ScaleMode.StretchFill:   // 拉伸适配：填满可打印区（不保纵横比）
                    return bounds;
                case ScaleMode.Custom:   // 自定义百分比（在适配结果上缩放，与旧版行为一致）
                    return ApplyScale(fitRect);
                default:   // 适合页面（现状默认）
                    return fitRect;
            }
        }

        private int PrintPdfFile(string file, PrintJob job)
        {
            int totalPages = 0;
            using (PdfiumFile pdf = OpenPdfWithPassword(file))
            {
                int total = pdf.PageCount;
                totalPages = total;
                // F6（V4.3）：页序统一走 PrintPlan——页码范围 → 小册子骑马订重排（优先，含 -1 空白补位）→ 奇偶页过滤
                int[] pages = PrintPlan.PageSequence(total, job.RangeText, job.PageIndexes, E);
                if (pages.Length == 0) throw new Exception("页码范围无效：" + job.RangeText);
                bool booklet = E.Booklet;   // 小册子优先：强制 2 版横排，忽略每页版数与奇偶页
                using (var doc = new PrintDocument())
                {
                    doc.DocumentName = job.DisplayName ?? job.Name;
                    ApplyPrinter(doc, job);
                    int n = booklet ? 2 : Math.Max(1, E.PagesPerSheet);
                    if (n == 1)
                    {
                        int pos = 0;
                        // 预计算每页方向；通过 QueryPageSettings 在每页 StartPage 之前下发纸张方向。
                        // 修复：横向 PDF 打印出来仍为纵向——原实现只在 PrintPage 里设 e.PageSettings.Landscape，
                        // 对当页（尤其单页 PDF 的第一页）生效太晚，打印机仍按默认纵向出纸。
                        bool[] pageLandscapes = pages.Select(i => UseLandscape(pdf.GetPageSizePt(i))).ToArray();
                        doc.QueryPageSettings += (s, e) =>
                        {
                            if (pos < pageLandscapes.Length) e.PageSettings.Landscape = pageLandscapes[pos];
                        };
                        doc.PrintPage += (s, e) =>
                        {
                            if (pos >= pages.Length) { e.HasMorePages = false; return; }
                            int pageIndex = pages[pos];
                            bool landscape = pageLandscapes[pos];
                            pos++;
                            e.PageSettings.Landscape = landscape;   // 双保险
                            using (Bitmap bmp = pdf.RenderPage(pageIndex, 300f))
                            {
                                Rectangle rect = ApplyScaleMode(FitTo(e.MarginBounds, bmp.Width, bmp.Height, landscape), bmp.Size, 300f, e.MarginBounds);
                                DrawPage(e.Graphics, bmp, rect);
                            }
                            e.HasMorePages = pos < pages.Length;
                        };
                    }
                    else
                    {
                        int cols, rows;
                        bool gridLandscape;
                        ComputeGrid(n, out cols, out rows, out gridLandscape);
                        if (booklet) gridLandscape = true;   // F6：小册子强制 2 版横排
                        if (E.Orientation == OrientationMode.Portrait) gridLandscape = false;
                        if (E.Orientation == OrientationMode.Landscape) gridLandscape = true;
                        int sheetCount = (pages.Length + n - 1) / n;
                        int sheet = 0;
                        doc.QueryPageSettings += (s, e) => e.PageSettings.Landscape = gridLandscape;
                        doc.PrintPage += (s, e) =>
                        {
                            if (sheet >= sheetCount) { e.HasMorePages = false; return; }
                            e.PageSettings.Landscape = gridLandscape;
                            int start = sheet * n;
                            int cellW = Math.Max(1, e.MarginBounds.Width / cols);
                            int cellH = Math.Max(1, e.MarginBounds.Height / rows);
                            for (int i = 0; i < n; i++)
                            {
                                int logical = start + i;
                                if (logical >= pages.Length) break;
                                if (pages[logical] < 0) continue;   // F6：小册子空白补位页（-1）跳过绘制
                                int c = i % cols;
                                int r = i / cols;
                                using (Bitmap bmp = pdf.RenderPage(pages[logical], 200f))
                                {
                                    Rectangle cell = new Rectangle(e.MarginBounds.X + c * cellW, e.MarginBounds.Y + r * cellH, cellW, cellH);
                                    Rectangle inner = new Rectangle(cell.X + 6, cell.Y + 6, Math.Max(1, cell.Width - 12), Math.Max(1, cell.Height - 12));
                                    Rectangle rect = ApplyScaleMode(FitTo(inner, bmp.Width, bmp.Height, bmp.Width > bmp.Height), bmp.Size, 200f, inner);
                                    DrawPage(e.Graphics, bmp, rect);
                                }
                            }
                            sheet++;
                            e.HasMorePages = sheet < sheetCount;
                        };
                    }
                    doc.Print();
                }
            }
            return totalPages;
        }

        private int PrintImage(PrintJob job)
        {
            if (E.PagesPerSheet > 1) _log("提示：图片按单页打印；每页版数仅对 PDF 多页文档生效。");
            using (var doc = new PrintDocument())
            {
                doc.DocumentName = job.Name;
                ApplyPrinter(doc, job);
                bool painted = false;
                using (var img = Image.FromFile(job.FilePath))
                {
                    bool landscape = UseLandscape(new SizeF(img.Width, img.Height));
                    doc.QueryPageSettings += (s, e) => e.PageSettings.Landscape = landscape;
                    doc.PrintPage += (s, e) =>
                    {
                        if (painted) { e.HasMorePages = false; return; }
                        painted = true;
                        e.PageSettings.Landscape = landscape;
                        // B-4（V4.4 修整）：图片通道改走 ApplyScaleMode，与 PDF/Office 同口径——
                        // 原来只用 ApplyScale（认 FitToPage/百分比），「不缩放/缩小适配/拉伸适配」三档对图片静默无效。
                        float dpi = img.HorizontalResolution > 0f ? img.HorizontalResolution : 96f;
                        Rectangle rect = ApplyScaleMode(FitTo(e.MarginBounds, img.Width, img.Height, landscape), img.Size, dpi, e.MarginBounds);
                        DrawPage(e.Graphics, img, rect);
                        e.HasMorePages = false;
                    };
                    doc.Print();
                }
            }
            return 1;
        }

        private static Rectangle FitTo(Rectangle bounds, int imgW, int imgH, bool landscape)
        {
            int availW = bounds.Width > 0 ? bounds.Width : 1;
            int availH = bounds.Height > 0 ? bounds.Height : 1;
            double scale = Math.Min((double)availW / imgW, (double)availH / imgH);
            int w = Math.Max(1, (int)(imgW * scale));
            int h = Math.Max(1, (int)(imgH * scale));
            int x = bounds.X + (availW - w) / 2;
            int y = bounds.Y + (availH - h) / 2;
            return new Rectangle(x, y, w, h);
        }

        private static void ComputeGrid(int n, out int cols, out int rows, out bool landscape)
        {
            switch (n)
            {
                case 2: cols = 2; rows = 1; landscape = true; break;
                case 4: cols = 2; rows = 2; landscape = false; break;
                case 6: cols = 3; rows = 2; landscape = true; break;
                case 9: cols = 3; rows = 3; landscape = false; break;    // F6（V4.3）：9 版 3×3
                case 16: cols = 4; rows = 4; landscape = false; break;   // F6（V4.3）：16 版 4×4
                default: cols = 1; rows = 1; landscape = false; break;
            }
        }
    }
}
