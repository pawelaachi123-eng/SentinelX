using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace SentinelX.WinUI.UI.Controls;

/// <summary>
/// Lightweight telemetry line chart: one polyline + one fill polygon, redrawn only
/// when the values or the size change. No charting library, no per-frame work.
/// </summary>
public sealed partial class TelemetryChart : UserControl
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IList<double>), typeof(TelemetryChart),
            new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(TelemetryChart),
            new PropertyMetadata(null, OnBrushChanged));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(TelemetryChart),
            new PropertyMetadata(null, OnBrushChanged));

    private readonly Polyline line = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
    private readonly Polygon fill = new() { Opacity = 0.25 };
    private INotifyCollectionChanged? tracked;

    public IList<double>? Values
    {
        get => (IList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public TelemetryChart()
    {
        InitializeComponent();
        Plot.Children.Add(fill);
        Plot.Children.Add(line);
        SizeChanged += (_, _) => Redraw();
        Unloaded += (_, _) => Track(null);
    }

    private static void OnValuesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TelemetryChart chart)
        {
            chart.Track(e.NewValue as INotifyCollectionChanged);
            chart.Redraw();
        }
    }

    private static void OnBrushChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TelemetryChart chart) chart.Redraw();
    }

    private void Track(INotifyCollectionChanged? source)
    {
        if (tracked != null) tracked.CollectionChanged -= OnCollection;
        tracked = source;
        if (tracked != null) tracked.CollectionChanged += OnCollection;
    }

    private void OnCollection(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var values = Values;
        double width = ActualWidth, height = ActualHeight;
        if (values == null || values.Count < 2 || width <= 4 || height <= 4)
        {
            line.Visibility = Visibility.Collapsed;
            fill.Visibility = Visibility.Collapsed;
            return;
        }

        line.Visibility = Visibility.Visible;
        fill.Visibility = Visibility.Visible;
        if (LineBrush != null) line.Stroke = LineBrush;
        if (FillBrush != null) fill.Fill = FillBrush;

        var points = new PointCollection();
        double step = width / (values.Count - 1);
        for (int i = 0; i < values.Count; i++)
        {
            double value = Math.Clamp(values[i], 0, 100);
            points.Add(new Point(i * step, 2 + (height - 4) * (1 - value / 100)));
        }

        line.Points = points;

        var area = new PointCollection();
        foreach (var point in points) area.Add(point);
        area.Add(new Point(width, height));
        area.Add(new Point(0, height));
        fill.Points = area;
    }
}
