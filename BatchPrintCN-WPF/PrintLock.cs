using System;
using System.ComponentModel;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.5-3（决9a，需求方 2026-09-25 拍板）：打印中"锁交互不锁滚动"的单一开关。
    ///
    /// 为什么不是 DataGrid.IsEnabled：修整19 原来是整表 IsEnabled=false（行内输入/勾选/参数按钮一起锁，省事），
    /// 但 WPF 的 IsEnabled 会沿可视树传染给内部 ScrollViewer → 打印中连滚动条都拖不动（需求方真机反馈"想看进展滑不动"）。
    /// 现在表格本体保持可用（滚动、选中、复制列头都不受影响），只有行内这几类控件（勾选框、份数/页码输入、
    /// 参数「修改」、失败「重试」）绑本开关的 Idle 属性置灰。
    ///
    /// 为什么是一个单例而不是窗口属性：单元格模板要绑一个"与可视树位置无关"的源——自检渲染会把窗口内容
    /// 摘到垫底 Border 上（RelativeSource AncestorType=MainWindow 在那条路上取不到），而 {x:Static Instance}
    /// 走的是绑定 Source，与树结构无关，实测两主题/渲染/真机都能取到当前态。
    /// 只在 UI 线程改写（SetControlsBusy 的唯一入口），事件通知也是同一线程，不需要加锁。
    /// </summary>
    public sealed class PrintLock : INotifyPropertyChanged
    {
        public static readonly PrintLock Instance = new PrintLock();

        private static readonly PropertyChangedEventArgs ArgBusy = new PropertyChangedEventArgs(nameof(Busy));
        private static readonly PropertyChangedEventArgs ArgIdle = new PropertyChangedEventArgs(nameof(Idle));

        private bool _busy;

        private PrintLock() { }

        /// <summary>是否处于"打印中锁定"态（与 MainWindow._printBusy 同源，由 SetControlsBusy 一处写入）。</summary>
        public bool Busy { get { return _busy; } }

        /// <summary>给界面绑的正面口径：空闲=true（可用），打印中=false（禁用）。</summary>
        public bool Idle { get { return !_busy; } }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>切换锁定态；两个属性名各通知一次（绑定写的是 Idle，读 Busy 的探针/代码也要拿到新值）。</summary>
        public void Set(bool busy)
        {
            if (_busy == busy) return;
            _busy = busy;
            var h = PropertyChanged;
            if (h != null) { h(this, ArgBusy); h(this, ArgIdle); }
        }
    }
}
