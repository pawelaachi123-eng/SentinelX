using Microsoft.UI.Xaml;
using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using SentinelX.Models;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool visible = value != null && value is not string s || value is string text && text.Length > 0;
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase)) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Visible when value.ToString() equals the ConverterParameter (e.g. DetailKind sections).</summary>
public sealed class EnumMatchToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value?.ToString()?.Equals(parameter as string, StringComparison.Ordinal) == true
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class IntZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool isZero = value is int i && i == 0 || value is long l && l == 0;
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase)) isZero = !isZero;
        return isZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class CoreStateToBrushConverter : IValueConverter
{
    private static readonly Dictionary<SentinelCoreState, SolidColorBrush> Brushes = new()
    {
        [SentinelCoreState.Idle] = new(Windows.UI.Color.FromArgb(0xFF, 0x00, 0xD4, 0xFF)),
        [SentinelCoreState.Listening] = new(Windows.UI.Color.FromArgb(0xFF, 0x5E, 0xEA, 0xD4)),
        [SentinelCoreState.Recognizing] = new(Windows.UI.Color.FromArgb(0xFF, 0x7D, 0xE8, 0xFF)),
        [SentinelCoreState.Thinking] = new(Windows.UI.Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8)),
        [SentinelCoreState.Executing] = new(Windows.UI.Color.FromArgb(0xFF, 0x00, 0xD4, 0xFF)),
        [SentinelCoreState.Success] = new(Windows.UI.Color.FromArgb(0xFF, 0x34, 0xD3, 0x99)),
        [SentinelCoreState.Warning] = new(Windows.UI.Color.FromArgb(0xFF, 0xF5, 0xA5, 0x24)),
        [SentinelCoreState.Error] = new(Windows.UI.Color.FromArgb(0xFF, 0xF0, 0x66, 0x5E)),
        [SentinelCoreState.Offline] = new(Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x7A, 0x99))
    };

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is SentinelCoreState state && Brushes.TryGetValue(state, out var brush)
            ? brush
            : Brushes[SentinelCoreState.Idle];

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class CoreStateToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            SentinelCoreState.Listening => "SENTINEL NASŁUCHUJE",
            SentinelCoreState.Recognizing => "SENTINEL ROZPOZNAJE",
            SentinelCoreState.Thinking => "SENTINEL MYŚLI",
            SentinelCoreState.Executing => "SENTINEL WYKONUJE",
            SentinelCoreState.Success => "SENTINEL GOTOWY",
            SentinelCoreState.Warning => "SENTINEL · UWAGA",
            SentinelCoreState.Error => "SENTINEL · BŁĄD",
            SentinelCoreState.Offline => "SENTINEL OFFLINE",
            _ => "SENTINEL READY"
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class ActionStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string text && Enum.TryParse<ActionStatus>(text, out var parsed)) value = parsed;
        var resources = Application.Current.Resources;
        return value switch
        {
            ActionStatus.Failed => resources["SxError"],
            ActionStatus.Verified => resources["SxSuccess"],
            ActionStatus.Unverified or ActionStatus.Cancelled or ActionStatus.WaitingPermission => resources["SxWarning"],
            ActionStatus.Running or ActionStatus.Verifying => resources["SxAccent"],
            _ => resources["SxTextMuted"]
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush? TrueBrush { get; set; }
    public Brush? FalseBrush { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language) =>
        (value is true ? TrueBrush : FalseBrush) ?? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x64, 0x74, 0x8F));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class DateToShortConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        DateTimeOffset dto => dto.LocalDateTime.ToString("dd.MM HH:mm"),
        DateTime dt => dt.ToString("dd.MM HH:mm"),
        _ => ""
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>WinUI Binding has no StringFormat — one-way string.Format(parameter, value).</summary>
public sealed class StringFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        parameter is string format && !string.IsNullOrEmpty(format)
            ? string.Format(CultureInfo.CurrentCulture, format, value)
            : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
