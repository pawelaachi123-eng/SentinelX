using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SentinelX.Converters;

public sealed class BooleanInvertConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is bool b ? !b : value;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is bool b ? !b : value;
}

/// <summary>True → Collapsed; False → Visible.</summary>
public sealed class CollapsedWhenTrueConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
