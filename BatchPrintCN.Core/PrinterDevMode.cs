using System;
using System.Runtime.InteropServices;

namespace BatchPrintCN
{
    /// <summary>
    /// 打印机 DEVMODE 临时修改作用域（H1 修复支撑）：
    /// 构造时把所选打印机的"默认设置"临时改为目标 双面/黑白 配置，Dispose 时恢复原设置。
    /// 全部操作尽力而为：任何失败只静默跳过，绝不影响打印主流程（打印时 Office 会使用打印机当前默认设置）。
    /// </summary>
    public static class PrinterDevMode
    {
        private const int DM_IN_BUFFER = 8;
        private const int DM_OUT_BUFFER = 2;
        private const int DM_DUPLEX = 0x0002;
        private const int DM_COLOR = 0x0800;
        private const short DMDUP_SIMPLEX = 1;
        private const short DMDUP_VERTICAL = 2;
        private const short DMDUP_HORIZONTAL = 3;
        private const short DMCOLOR_MONOCHROME = 1;
        private const short DMCOLOR_COLOR = 2;

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DocumentProperties(IntPtr hWnd, IntPtr hPrinter, string pDeviceName,
            IntPtr pDevModeOutput, IntPtr pDevModeInput, int fMode);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        /// <summary>应用双面/黑白到所选打印机默认设置，返回的作用域对象 Dispose 时恢复原设置。</summary>
        public static IDisposable Apply(string printerName, DuplexMode duplex, ColorMode color)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return new NoopScope();
            if (duplex == DuplexMode.Simplex && color == ColorMode.Color) return new NoopScope(); // 与默认一致，无需改动
            try { return new DevModeScope(printerName, duplex, color); }
            catch { return new NoopScope(); }
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }

        private sealed class DevModeScope : IDisposable
        {
            private readonly string _name;
            private IntPtr _hPrinter;
            private IntPtr _original;   // 修改前 DEVMODE buffer（用于恢复）
            private int _size;
            private bool _disposed;

            public DevModeScope(string printerName, DuplexMode duplex, ColorMode color)
            {
                _name = printerName;
                if (!OpenPrinter(printerName, out _hPrinter, IntPtr.Zero)) throw new InvalidOperationException("OpenPrinter 失败");
                try
                {
                    int needed = DocumentProperties(IntPtr.Zero, _hPrinter, printerName, IntPtr.Zero, IntPtr.Zero, 0);
                    if (needed <= 0) throw new InvalidOperationException("DocumentProperties 查询大小失败");
                    _size = needed;

                    // 读取当前默认 DEVMODE → 保存为原样
                    _original = Marshal.AllocHGlobal(needed);
                    if (DocumentProperties(IntPtr.Zero, _hPrinter, printerName, _original, IntPtr.Zero, DM_OUT_BUFFER) < 0)
                        throw new InvalidOperationException("读取 DEVMODE 失败");

                    // 构造修改后的副本
                    IntPtr modified = Marshal.AllocHGlobal(needed);
                    try
                    {
                        CopyMemory(modified, _original, needed);
                        var dm = (DevMode)Marshal.PtrToStructure(modified, typeof(DevMode));
                        if (duplex != DuplexMode.Simplex)
                        {
                            dm.dmFields |= DM_DUPLEX;
                            dm.dmDuplex = duplex == DuplexMode.LongEdge ? DMDUP_VERTICAL : DMDUP_HORIZONTAL;
                        }
                        else
                        {
                            dm.dmFields |= DM_DUPLEX;
                            dm.dmDuplex = DMDUP_SIMPLEX;
                        }
                        if (color != ColorMode.Color)
                        {
                            dm.dmFields |= DM_COLOR;
                            dm.dmColor = DMCOLOR_MONOCHROME;
                        }
                        else
                        {
                            dm.dmFields |= DM_COLOR;
                            dm.dmColor = DMCOLOR_COLOR;
                        }
                        Marshal.StructureToPtr(dm, modified, false);

                        if (DocumentProperties(IntPtr.Zero, _hPrinter, printerName, modified, modified, DM_IN_BUFFER | DM_OUT_BUFFER) < 0)
                            throw new InvalidOperationException("写回 DEVMODE 失败");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(modified);
                    }
                }
                catch
                {
                    Cleanup();
                    throw;
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    // 恢复原设置
                    if (_hPrinter != IntPtr.Zero && _original != IntPtr.Zero)
                        DocumentProperties(IntPtr.Zero, _hPrinter, _name, _original, _original, DM_IN_BUFFER | DM_OUT_BUFFER);
                }
                catch { }
                finally { Cleanup(); }
            }

            private void Cleanup()
            {
                if (_original != IntPtr.Zero) { Marshal.FreeHGlobal(_original); _original = IntPtr.Zero; }
                if (_hPrinter != IntPtr.Zero) { try { ClosePrinter(_hPrinter); } catch { } _hPrinter = IntPtr.Zero; }
            }

            [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
            private static extern void CopyMemory(IntPtr dest, IntPtr src, int count);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public short dmOrientation;
            public short dmPaperSize;
            public short dmPaperLength;
            public short dmPaperWidth;
            public short dmScale;
            public short dmCopies;
            public short dmDefaultSource;
            public short dmPrintQuality;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }
    }
}
