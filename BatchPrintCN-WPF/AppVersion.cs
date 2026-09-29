using System;

namespace BatchPrintCN
{
    /// <summary>
    /// 统一版本号来源：所有界面显示版本号的地方都从这里取，
    /// 避免左下角 / 关于页 / 标题栏等各处硬编码不一致。
    /// 规则：显示「主.次.修订」三段（如 2.2.1），若修订为 0 则显示两段（如 2.2）。
    /// </summary>
    public static class AppVersion
    {
        public static string Display
        {
            get
            {
                try
                {
                    var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (v == null) return "";
                    if (v.Build <= 0 && v.Revision <= 0)
                        return "v" + v.Major + "." + v.Minor;
                    if (v.Revision <= 0)
                        return "v" + v.Major + "." + v.Minor + "." + v.Build;
                    return "v" + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
                }
                catch { return ""; }
            }
        }

        /// <summary>完整版本（含修订号），用于"关于"页等需要精确版本的地方。</summary>
        public static string Full
        {
            get
            {
                try
                {
                    var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return v == null ? "" : v.ToString();
                }
                catch { return ""; }
            }
        }
    }
}
