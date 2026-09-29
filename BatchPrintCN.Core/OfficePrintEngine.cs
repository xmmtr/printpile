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

    // M4 拆分（V4.2.3）：Office 文档转 PDF 后交给 pdfium 通道打印的衔接段。逻辑原样搬移。
    public partial class BatchPrinter
    {
        private int PrintOfficeDocument(PrintJob job)
        {
            if (OfficeComPrinter.CanHandle(job))
            {
                var (ok, pages) = OfficeComPrinter.TryPrint(job, _printer, E, () => CancelRequested, _log, ConverterEngine);   // F5（V4.4）：传本文件生效参数
                if (ok)
                {
                    _log("已通过静默转 PDF 无感打印（引擎：" + ActualEngine(job) + "；双面/黑白/纸张/缩放/份数均已生效）。");
                    return pages;
                }
                throw new Exception("文档转换或打印失败，请用 Office/WPS 打开检查文件是否正常。");
            }
            throw new Exception("该格式不是 Office/WPS 支持打印的类型（支持 doc/docx/odt/rtf、xls/xlsx/ods/csv、ppt/pptx/odp、txt）。");
        }

    }
}
