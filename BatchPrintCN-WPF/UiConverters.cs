using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BatchPrintCN
{
    /// <summary>U2-5（V4.2.5）：文件列表「份数」列 string(VM) ↔ double(NumberBox.Value) 双向转换；
    /// 空/非法值按 1 兜底，上限 999 与打印链路的 int 解析口径一致。</summary>
    public class CopiesTextToNumberConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double d;
            if (value is string s && double.TryParse(s, out d) && d >= 1) return d;
            return 1d;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var d = value as double?;
            if (d.HasValue && d.Value >= 1) return Math.Min(999, Math.Round(d.Value)).ToString();
            return "1";
        }
    }

    /// <summary>F5（V4.4）：true→Collapsed / false→Visible（"跟随全局"与"自定义 ●"、展开与收起箭头互斥显示）。</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility && (Visibility)value == Visibility.Collapsed;
        }
    }

    /// <summary>修整7（V4.4）：字符串为空/空白 → Visible（页码范围单元格里的灰字占位水印，用户输入后自动收起）。</summary>
    public class EmptyTextToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>V4.4 三批·修复B：Element Plus 按钮"内容文字固定字色"的模板选择器（Styles.xaml BtnContentSelector）。
    /// 纯文字按钮套 BtnContentTemplate（把 Foreground 钉回按钮自己那份拍板色）；按钮自带 ContentTemplate 的原样交回；
    /// 内容是元素且无自带模板的交回 null 走默认。实测：选择器返回 null 时 ContentPresenter 不会回落 ContentTemplate，
    /// 只"返回 null 表示不管"会把带图标按钮压成裸 TextBlock（图标消失、字色失控）——必须显式交回。</summary>
    public class BtnContentTemplateSelector : System.Windows.Controls.DataTemplateSelector
    {
        /// <summary>套给纯文字按钮的那份模板（Styles.xaml:BtnContentTemplate）。</summary>
        public System.Windows.DataTemplate TextTemplate { get; set; }

        public override System.Windows.DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var cp = container as System.Windows.Controls.ContentPresenter;
            var b = cp == null ? null : cp.TemplatedParent as System.Windows.Controls.Button;
            if (b == null) return null;
            // 按钮自带 ContentTemplate 必须原样交回：选择器返回 null 时 ContentPresenter 并不会回落到
            // ContentTemplate（实测：工具栏四个带图标按钮被压成自动生成的裸 TextBlock，图标消失、字色也不受控）。
            if (!(item is string)) return b.ContentTemplate;
            return b.ContentTemplate ?? TextTemplate;
        }
    }
}
