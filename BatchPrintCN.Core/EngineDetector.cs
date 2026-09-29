using System;
using System.Collections.Generic;

namespace BatchPrintCN
{
    /// <summary>
    /// F1 引擎自动检测：探测本机是否安装 WPS Office / Microsoft Office（COM 引擎可用性）。
    /// 结果缓存（内存级），可手动重新探测。
    /// </summary>
    public static class EngineDetector
    {
        private static readonly string[] ComIds =
        {
            "Kwps.Application", "Word.Application", "Ket.Application",
            "Excel.Application", "Kwpp.Application", "PowerPoint.Application"
        };

        private static bool _checked;
        private static bool _hasCom;
        private static string _detectedId = "";

        /// <summary>是否已检测到本机 COM 引擎（WPS 或 Office）。</summary>
        public static bool HasCom { get { return _hasCom; } }
        public static string DetectedId { get { return _detectedId; } }
        public static bool Checked { get { return _checked; } }

        /// <summary>探测本机 COM 引擎（WPS/Office 任一存在即视为可用）。</summary>
        public static void Detect()
        {
            _hasCom = false;
            _detectedId = "";
            try
            {
                foreach (string id in ComIds)
                {
                    if (Type.GetTypeFromProgID(id) != null)
                    {
                        _hasCom = true;
                        _detectedId = id;
                        break;
                    }
                }
            }
            catch { }
            _checked = true;
        }

        /// <summary>探测结果展示文本：已检测到 WPS Office / Microsoft Office / 未检测到 Office/WPS。</summary>
        public static string Describe()
        {
            if (!_checked) return "";
            if (!_hasCom) return "未检测到 Office/WPS";
            if (_detectedId.StartsWith("Kw", StringComparison.OrdinalIgnoreCase)) return "已检测到：WPS Office（将用 COM）";
            return "已检测到：Microsoft Office（将用 COM）";
        }

        public static List<string> DetectedIds()
        {
            var list = new List<string>();
            foreach (string id in ComIds)
                try { if (Type.GetTypeFromProgID(id) != null) list.Add(id); } catch { }
            return list;
        }
    }
}
