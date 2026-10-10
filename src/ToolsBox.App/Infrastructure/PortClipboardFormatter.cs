using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ToolsBox.App.Infrastructure;

/// <summary>剪贴板绑定的目标为 object，必须显式返回字符串，不能只设置 MultiBinding.StringFormat。</summary>
public sealed class PortClipboardFormatter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        string.Format(culture, (string)parameter,
            values.Select(value => value == DependencyProperty.UnsetValue ? null : value).ToArray());

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("端口剪贴板格式仅支持单向输出。");
}
