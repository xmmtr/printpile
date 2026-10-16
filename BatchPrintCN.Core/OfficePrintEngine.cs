// This file is part of PrintPile (https://printpile.cn).
//
// Copyright (C) 2026 the PrintPile developers
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
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
