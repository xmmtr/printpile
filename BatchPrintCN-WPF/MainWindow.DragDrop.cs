using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

using System.Windows.Input;
using Microsoft.Win32;

namespace BatchPrintCN
{

    // V4.2.3 M4 拆分：文件添加/移除/拖拽/Ctrl+V 粘贴（原样搬移，零行为变化）。
    public partial class MainWindow
    {
        private void AddFiles(IEnumerable<string> paths)
        {
            if (paths == null) return;
            if (_printBusy) return;   // 修整19（五批）：添加的唯一收口——按钮、拖拽、Ctrl+V 三条路都进这里，打印中一律不接受
            int added = 0;
            foreach (string path in paths)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    PrintJob job = FileClassifier.CreateJob(path);
                    if (job == null) continue;
                    if (_rows.Any(r => string.Equals(r.Job.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
                    _rows.Add(new RowVm(job));
                    added++;
                }
                catch (Exception ex) { AppendLog(L("s.LogAddFailed") + path + " → " + ex.Message); }
            }
            AppendLog(L("s.LogAdded").Replace("{0}", added.ToString()).Replace("{1}", _rows.Count.ToString()));
            UpdateStatus();
        }

        /// <summary>修整19 断言用：走真实 AddFiles 收口（返回调用后的行数，用来证明"打印中不接受新文件"）。</summary>
        internal int AddFilesForTest(IEnumerable<string> paths) { AddFiles(paths); return _rows.Count; }

        private void OnAddFiles(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = L("s.AddFiles"),
                Multiselect = true,
                Filter = "支持的文件|*.pdf;*.doc;*.docx;*.odt;*.rtf;*.xls;*.xlsx;*.ods;*.csv;*.ppt;*.pptx;*.odp;*.txt;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp;*.emf;*.wmf|所有文件|*.*"
            };
            if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
        }

        private void OnAddFolder(object sender, RoutedEventArgs e)
        {
            // 添加文件夹：只弹一次选文件夹对话框；选定后直接递归添加该目录下全部支持打印的文件，无预览弹窗。
            // V4.2.3 M2：改用 WPF 自带的 Microsoft.Win32.OpenFolderDialog（.NET 8+），不再依赖 WinForms FolderBrowserDialog。
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L("s.AddFolder"), Multiselect = false };
            if (dlg.ShowDialog(this) != true) return;
            try { AddFiles(Directory.GetFiles(dlg.FolderName, "*", SearchOption.AllDirectories)); }
            catch (Exception ex) { MessageBox.Show(this, L("s.Error") + ": " + ex.Message, L("s.Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            var selected = Grid.SelectedItems.Cast<RowVm>().ToList();
            foreach (RowVm r in selected) _rows.Remove(r);
            ResetContinue();
            AppendLog(L("s.LogRemoved").Replace("{0}", selected.Count.ToString()));
            UpdateStatus();
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            _rows.Clear();
            ResetContinue();
            AppendLog(L("s.LogCleared"));
            UpdateStatus();
        }

        /// <summary>列表被改动后，放弃"继续打印"状态，回到"开始打印"。</summary>
        private void ResetContinue()
        {
            if (_worker != null && _worker.IsAlive) return;
            if (_remaining != null && _remaining.Count > 0)
            {
                _remaining = null;
                BtnStart.Content = L("s.StartPrint");
            }
        }

        // 修整19（V4.4 五批）：拖拽与 Ctrl+V 是两条"不进按钮"的入口，SetControlsBusy 关掉 AllowDrop 只是不给反馈，
        // 真正挡住要在事件里各判一道（_printBusy）——打印途中不接受新文件。
        private void OnDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (_printBusy) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (_printBusy) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) AddFiles(e.Data.GetData(DataFormats.FileDrop) as string[]);
        }

        /// <summary>R5：Ctrl+V 从剪贴板粘贴文件（已复制/剪切的文件直接加入打印列表）。修整19：打印途中不接收。</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_printBusy) return;
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteFromClipboard();
                e.Handled = true;
            }
        }

        private void PasteFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                    AddFiles(Clipboard.GetFileDropList().Cast<string>());
            }
            catch { /* 剪贴板被其它进程占用时静默忽略 */ }
        }

    }
}
