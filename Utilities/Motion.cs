using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SentinelX.Utilities;

/// <summary>
/// 0.93 · NOWOCZESNE GUI: ruch widoków w jednym miejscu.
/// Każda animacja sprawdza dwa warunki przed startem:
/// 1. <c>SxAnimationsEnabled</c> — przełącznik „Animacje" z ustawień i tryb gry
///    (nadpisywany w Services/Desktop/DesktopService.cs),
/// 2. <see cref="SystemParameters.ClientAreaAnimation"/> — animacje wyłączone w Windows.
/// Kod-behind widoków pozostaje pusty: zachowanie włącza się atrybutem w XAML.
/// </summary>
public static class Motion
{
    private const string AnimationsKey = "SxAnimationsEnabled";

    /// <summary>True tylko, gdy produkt i system pozwalają teraz na ruch.</summary>
    public static bool AnimationsOn
    {
        get
        {
            if (!SystemParameters.ClientAreaAnimation) return false;
            var app = Application.Current;
            if (app == null) return false;
            return app.TryFindResource(AnimationsKey) is not false;
        }
    }

    private static Storyboard? Board(string key) => Application.Current?.TryFindResource(key) as Storyboard;

    /// <summary>Odtwarza nazwany storyboard z Themes/Animations.xaml na wskazanym elemencie.
    /// Nie sprawdza bramki animacji — wołający robi to przez <see cref="AnimationsOn"/>.</summary>
    public static void Play(FrameworkElement element, string storyboardKey)
    {
        EnsureTransform(element);
        Board(storyboardKey)?.Begin(element);
    }

    // ---------------------------------------------------------------- wejście
    public static readonly DependencyProperty EntranceProperty = DependencyProperty.RegisterAttached(
        "Entrance", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEntranceChanged));
    public static void SetEntrance(DependencyObject d, bool value) => d.SetValue(EntranceProperty, value);
    public static bool GetEntrance(DependencyObject d) => (bool)d.GetValue(EntranceProperty);

    private static void OnEntranceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnEntranceLoaded;
        if (e.NewValue is true) element.Loaded += OnEntranceLoaded;
    }

    private static void OnEntranceLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= OnEntranceLoaded; // wejście odtwarzamy raz na pojawienie widoku
        EnsureTransform(element);
        if (!AnimationsOn) { element.Opacity = 1; return; }
        Board("SxPageIn")?.Begin(element);
    }

    // ------------------------------------------------- wejście jednej bańki
    /// <summary>Wejście jednej bańki rozmowy: fade + wjazd, odtwarzane raz
    /// (pierwsze Loaded). Kolejne powroty na stronę nie animują historii od nowa.</summary>
    public static readonly DependencyProperty BubbleInProperty = DependencyProperty.RegisterAttached(
        "BubbleIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnBubbleInChanged));
    public static void SetBubbleIn(DependencyObject d, bool value) => d.SetValue(BubbleInProperty, value);
    public static bool GetBubbleIn(DependencyObject d) => (bool)d.GetValue(BubbleInProperty);

    private static void OnBubbleInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnBubbleInLoaded;
        if (e.NewValue is true) element.Loaded += OnBubbleInLoaded;
    }

    private static void OnBubbleInLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= OnBubbleInLoaded;
        EnsureTransform(element);
        if (!AnimationsOn) { element.Opacity = 1; return; }
        Board("SxFadeSlideIn")?.Begin(element);
    }

    // ------------------------------------------------------- wejście modalne
    /// <summary>Wejście okna modalnego (paleta, panel gotowości): fade + sprężynowa skala.</summary>
    public static readonly DependencyProperty ModalInProperty = DependencyProperty.RegisterAttached(
        "ModalIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnModalInChanged));
    public static void SetModalIn(DependencyObject d, bool value) => d.SetValue(ModalInProperty, value);
    public static bool GetModalIn(DependencyObject d) => (bool)d.GetValue(ModalInProperty);

    private static void OnModalInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnModalInLoaded;
        if (e.NewValue is true) element.Loaded += OnModalInLoaded;
    }

    private static void OnModalInLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= OnModalInLoaded;
        EnsureTransform(element);
        if (!AnimationsOn) { element.Opacity = 1; return; }
        Board("SxModalIn")?.Begin(element);
    }

    // ------------------------------------------------------------------ hover
    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached(
        "Hover", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverChanged));
    public static void SetHover(DependencyObject d, bool value) => d.SetValue(HoverProperty, value);
    public static bool GetHover(DependencyObject d) => (bool)d.GetValue(HoverProperty);

    private static void OnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.MouseEnter -= OnHoverEnter;
        element.MouseLeave -= OnHoverLeave;
        if (e.NewValue is not true) return;
        EnsureTransform(element);
        element.MouseEnter += OnHoverEnter;
        element.MouseLeave += OnHoverLeave;
    }

    private static void OnHoverEnter(object sender, System.Windows.Input.MouseEventArgs args)
    {
        if (sender is FrameworkElement element && AnimationsOn) Board("SxLiftIn")?.Begin(element);
    }

    private static void OnHoverLeave(object sender, System.Windows.Input.MouseEventArgs args)
    {
        if (sender is FrameworkElement element) Board("SxLiftOut")?.Begin(element);
    }

    // ------------------------------------------------------------------ puls
    public static readonly DependencyProperty PulseProperty = DependencyProperty.RegisterAttached(
        "Pulse", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnPulseChanged));
    public static void SetPulse(DependencyObject d, bool value) => d.SetValue(PulseProperty, value);
    public static bool GetPulse(DependencyObject d) => (bool)d.GetValue(PulseProperty);

    private static void OnPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnPulseLoaded;
        element.Unloaded -= OnPulseUnloaded;
        if (e.NewValue is not true) return;
        element.Loaded += OnPulseLoaded;
        element.Unloaded += OnPulseUnloaded;
    }

    private static void OnPulseLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        EnsureTransform(element);
        if (!AnimationsOn) { element.BeginAnimation(UIElement.OpacityProperty, null); return; }
        Board("SxPulseSoft")?.Begin(element);
    }

    private static void OnPulseUnloaded(object sender, RoutedEventArgs args)
    {
        // Pętla nie może zostać po usunięciu elementu z drzewa.
        if (sender is FrameworkElement element) element.BeginAnimation(UIElement.OpacityProperty, null);
    }

    // --------------------------------------------------------------- połysk
    public static readonly DependencyProperty ShimmerProperty = DependencyProperty.RegisterAttached(
        "Shimmer", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnShimmerChanged));
    public static void SetShimmer(DependencyObject d, bool value) => d.SetValue(ShimmerProperty, value);
    public static bool GetShimmer(DependencyObject d) => (bool)d.GetValue(ShimmerProperty);

    private static void OnShimmerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnShimmerLoaded;
        element.Unloaded -= OnShimmerUnloaded;
        if (e.NewValue is not true) return;
        element.Loaded += OnShimmerLoaded;
        element.Unloaded += OnShimmerUnloaded;
    }

    private static void OnShimmerLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        EnsureTransform(element);
        if (!AnimationsOn) return;
        Board("SxShimmerSlide")?.Begin(element);
    }

    private static void OnShimmerUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element)
            element.BeginAnimation(TranslateTransform.XProperty, null);
    }

    // ------------------------------------------- prążki paska postępu
    /// <summary>Włącza przesuwające się prążki w wypełnieniu ProgressBar.
    /// Element „stripes" pochodzi z szablonu SxProgressBar (Themes/Controls.xaml).</summary>
    public static readonly DependencyProperty StripesProperty = DependencyProperty.RegisterAttached(
        "Stripes", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnStripesChanged));
    public static void SetStripes(DependencyObject d, bool value) => d.SetValue(StripesProperty, value);
    public static bool GetStripes(DependencyObject d) => (bool)d.GetValue(StripesProperty);

    private static void OnStripesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProgressBar bar) return;
        bar.Loaded -= OnStripesLoaded;
        bar.Unloaded -= OnStripesUnloaded;
        if (e.NewValue is not true) return;
        bar.Loaded += OnStripesLoaded;
        bar.Unloaded += OnStripesUnloaded;
    }

    private static void OnStripesLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not ProgressBar bar) return;
        FrameworkElement? stripes = bar.Template?.FindName("stripes", bar) as FrameworkElement;
        if (stripes == null) return;
        EnsureTransform(stripes);
        if (!AnimationsOn) { stripes.BeginAnimation(TranslateTransform.XProperty, null); return; }
        Board("SxStripeSlide")?.Begin(stripes);
    }

    private static void OnStripesUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not ProgressBar bar) return;
        if (bar.Template?.FindName("stripes", bar) is FrameworkElement stripes)
            stripes.BeginAnimation(TranslateTransform.XProperty, null);
    }

    // ------------------------------------------------- tło ambientne (aurora)
    /// <summary>Numer warstwy aurory (0-2). Każda warstwa dryfuje własnym rytmem.</summary>
    public static readonly DependencyProperty AuroraProperty = DependencyProperty.RegisterAttached(
        "Aurora", typeof(int), typeof(Motion), new PropertyMetadata(-1, OnAuroraChanged));
    public static void SetAurora(DependencyObject d, int value) => d.SetValue(AuroraProperty, value);
    public static int GetAurora(DependencyObject d) => (int)d.GetValue(AuroraProperty);

    private static readonly string[] AuroraBoards = { "SxAuroraDriftA", "SxAuroraDriftB", "SxAuroraDriftC" };

    private static void OnAuroraChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnAuroraLoaded;
        element.Unloaded -= OnAuroraUnloaded;
        if (e.NewValue is not int index || index < 0 || index >= AuroraBoards.Length) return;
        element.Loaded += OnAuroraLoaded;
        element.Unloaded += OnAuroraUnloaded;
    }

    private static void OnAuroraLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        EnsureTransform(element);
        if (!AnimationsOn) return;
        int index = GetAurora(element);
        if (index >= 0 && index < AuroraBoards.Length) Board(AuroraBoards[index])?.Begin(element);
    }

    private static void OnAuroraUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.BeginAnimation(TranslateTransform.XProperty, null);
        element.BeginAnimation(TranslateTransform.YProperty, null);
        element.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        element.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    /// <summary>
    /// Konwencja drzewa transformacji dla animowanych elementów:
    /// TransformGroup[0] = ScaleTransform, [1] = TranslateTransform.
    /// Storyboardy w Themes/Animations.xaml adresują dokładnie te indeksy.
    /// </summary>
    public static void EnsureTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group && group.Children.Count >= 2
            && group.Children[0] is ScaleTransform && group.Children[1] is TranslateTransform) return;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { new ScaleTransform(1, 1), new TranslateTransform(0, 0) }
        };
    }

    // ------------------------------------------------- wejście kafelka (0.99 · NOC)
    /// <summary>Wejście kafelka (metryki, kafelek statusu): fade + krótki wjazd + skala.</summary>
    public static readonly DependencyProperty TileInProperty = DependencyProperty.RegisterAttached(
        "TileIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnTileInChanged));
    public static void SetTileIn(DependencyObject d, bool value) => d.SetValue(TileInProperty, value);
    public static bool GetTileIn(DependencyObject d) => (bool)d.GetValue(TileInProperty);

    private static void OnTileInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnTileInLoaded;
        if (e.NewValue is true) element.Loaded += OnTileInLoaded;
    }

    private static void OnTileInLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= OnTileInLoaded;
        EnsureTransform(element);
        if (!AnimationsOn) { element.Opacity = 1; return; }
        Board("SxTileIn")?.Begin(element);
    }

    // ------------------------------------------------- unszanie dekoracji (0.99 · NOC)
    /// <summary>Ciągłe, delikatne unszanie (herb, ornamenty) — tylko gdy animacje włączone.</summary>
    public static readonly DependencyProperty FloatProperty = DependencyProperty.RegisterAttached(
        "Float", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFloatChanged));
    public static void SetFloat(DependencyObject d, bool value) => d.SetValue(FloatProperty, value);
    public static bool GetFloat(DependencyObject d) => (bool)d.GetValue(FloatProperty);

    private static void OnFloatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= OnFloatLoaded;
        if (e.NewValue is true) element.Loaded += OnFloatLoaded;
    }

    private static void OnFloatLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        element.Loaded -= OnFloatLoaded;
        EnsureTransform(element);
        if (AnimationsOn) Board("SxBob")?.Begin(element);
    }

}
