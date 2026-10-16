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
using System.IO;
using MiniSoftware;

namespace BatchPrintCN
{
    /// <summary>
    /// V3.1 第二转换引擎：MiniPdf（mini-software/MiniPdf，Apache 2.0，纯托管，无需安装 Office/WPS）。
    /// 仅支持 .docx/.xlsx/.pptx；.doc/.xls/.ppt 等老格式不支持（由调用方回退 COM）。
    /// </summary>
    public static class MiniPdfConvert
    {
        public static bool CanHandle(string inputPath)
        {
            string ext = Path.GetExtension(inputPath ?? "").ToLowerInvariant();
            return ext == ".docx" || ext == ".xlsx" || ext == ".pptx";
        }

        /// <summary>MiniPdf 静默转换。返回 null=成功，非 null=错误消息。</summary>
        public static string Export(string inputPath, string outputPath)
        {
            try
            {
                FileLogger.Write("Worker 引擎：MiniPdf（纯托管，无 Office 依赖）");
                MiniPdf.ConvertToPdf(inputPath, outputPath, null);
                return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0 ? null : "MiniPdf 导出未生成 PDF 文件。";
            }
            catch (Exception ex)
            {
                return "MiniPdf 转换失败：" + ex.Message;
            }
        }
    }
}
