using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SentinelX.Views.Controls;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// 0.99 · SYSTEM POWIADOMIEŃ TOAST („chwalebne gazetki” w rogu ekranu).
/// Host jest elementem powłoki (Views/MainWindow.xaml), a dowolny serwis lub
/// ViewModel woła statyczne <see cref="UiToast.Show"/> — powiadomienie wpada
/// do aktywnego hosta z animacją sprężyny, znika samo po kilku sekundach,
/// a kliknięcie zamyka od razu. Maksymalnie 4 naraz — starsze wylatują.
/// Całość budowana w kodzie (bez szablonów XAML), kolory z motywu Sx*
/// z bezpiecznym fallbackiem, więc host nie wywali się nawet poza powłoką.
/// </summary>
public sealed class UiToastHost : Canvas
{
    private static UiToastHost? active;

    private const int MaxVisible = 4;
    private const double CardWidth = 352;

    public UiToastHost()
    {
        IsHitTestVisible = true; // karta łapie klik = zamknięcie; tło hosta jest przezroczyste
        Background = Brushes.Transparent;
        Loaded += (_, _) => active = this;
        Unloaded += (_, _) => { if (ReferenceEquals(active, this)) active = null; };
    }

    /// <summary>Pokazuje toast w aktywnym oknie (jeśli host istnieje).</summary>
    public static void Show(string title, string message, ToastKind kind) =>
        Show(title, message, kind, TimeSpan.FromSeconds(5.5));

    public static void Show(string title, string message, ToastKind kind, TimeSpan lifetime)
    {
        UiToastHost? host = active ?? FindHost();
        if (host is null)
        {
            return;
        }
        void Push() => host.Push(title, message, kind, lifetime);
        if (host.Dispatcher.CheckAccess())
        {
            Push();
        }
        else
        {
            host.Dispatcher.InvokeAsync(Push);
        }
    }

    private static UiToastHost? FindHost()
    {
        foreach (Window window in Application.Current.Windows)
        {
            if (window.FindName("ToastHost") is UiToastHost host)
            {
                return host;
            }
            if (LogicalTreeHelper.FindLogicalNode(window, "ToastHost") is UiToastHost nested)
            {
                return nested;
            }
        }
        return null;
    }

    private static SolidColorBrush BrushOr(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            return brush;
        }
        return (SolidColorBrush)new BrushConverter().ConvertFromString(fallback);
    }

    private void Push(string title, string message, ToastKind kind, TimeSpan lifetime)
    {
        // ————— karta: szkło + pasek rodzaju + ikona + teksty —————
        (Brush accent, string icon) = kind switch
        {
            ToastKind.Success => (BrushOr("SxSuccess", "#34D399"), "✔"),
            ToastKind.Warning => (BrushOr("SxWarning", "#FBBF24"), "!"),
            ToastKind.Error => (BrushOr("SxError", "#FB7185"), "✕"),
            _ => (BrushOr("SxAccentCyan", "#22D3EE"), "i"),
        };

        var card = new Border
        {
            Width = CardWidth,
            CornerRadius = new CornerRadius(14),
            Background = BrushOr("SxSurfaceGlass", "#B3151A26"),
            BorderBrush = BrushOr("SxBorderSubtle", "#1E2431"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 14, 12),
            Cursor = System.Windows.Input.Cursors.Hand,
            Effect = Application.Current?.TryFindResource("SxShadowRaised") as Effect
                ?? new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.45, Color = Colors.Black },
            LayoutTransform = new ScaleTransform(0.94, 0.94),
            RenderTransform = new TranslateTransform(0, -16),
            Opacity = 0,
        };

        var accentBar = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = accent,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var iconDot = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(((SolidColorBrush)accent).Color, 0),
                    new GradientStop(Color.FromArgb(60, ((SolidColorBrush)accent).Color.R, ((SolidColorBrush)accent).Color.G, ((SolidColorBrush)accent).Color.B), 1),
                },
            },
            Child = new TextBlock
            {
                Text = icon,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Foreground = BrushOr("SxTextOnAccent", "#04121A"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            Margin = new Thickness(0, 0, 12, 0),
        };

        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            FontSize = 13.5,
            Foreground = BrushOr("SxTextPrimary", "#EDF1FA"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var messageText = new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 52,
            Foreground = BrushOr("SxTextSecondary", "#98A2BC"),
            Margin = new Thickness(0, 3, 0, 0),
        };

        var textStack = new StackPanel();
        textStack.Children.Add(titleText);
        textStack.Children.Add(messageText);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(accentBar);
        row.Children.Add(iconDot);
        row.Children.Add(textStack);
        card.Child = row;

        // ————— wejście: sprężyna (przesuw + skala + fade) —————
        var slideIn = new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(340))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
        };
        var scaleIn = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(340))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 },
        };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slideIn);
        ((ScaleTransform)card.LayoutTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        ((ScaleTransform)card.LayoutTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
        card.BeginAnimation(OpacityProperty, fadeIn);

        // ————— wyjście: fade + przesuw w prawo, potem usunięcie i przeliczenie pozycji —————
        void Close()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            var slideOut = new DoubleAnimation(0, 26, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            fadeOut.Completed += (_, _) =>
            {
                Children.Remove(card);
                Relayout();
            };
            card.BeginAnimation(OpacityProperty, fadeOut);
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slideOut);
        }

        card.MouseLeftButtonDown += (_, _) => Close();

        // licznik życia: podświetlenie paska wygasa się razem z toastem
        DispatcherTimer life = new() { Interval = lifetime };
        life.Tick += (_, _) =>
        {
            life.Stop();
            Close();
        };

        Children.Add(card);
        Relayout();

        // limit: najstarsze toast padają natychmiast
        while (Children.Count > MaxVisible)
        {
            Children.RemoveAt(0);
        }
        Relayout();
        life.Start();
    }

    /// <summary>Ustawia pozycje Y kart — stos wyrównany do prawego górnego rogu hosta.</summary>
    private void Relayout()
    {
        double y = 18;
        for (int index = Children.Count - 1; index >= 0; index--)
        {
            if (Children[index] is Border card)
            {
                SetLeft(card, ActualWidth - card.Width - 22);
                SetTop(card, y);
                y += card.Padding.Top + card.Padding.Bottom + 62;
            }
        }
    }
}

/// <summary>Krótszy zapis: UiToast.Show(...) zamiast UiToastHost.Show(...).</summary>
public static class UiToast
{
    public static void Show(string title, string message, ToastKind kind) => UiToastHost.Show(title, message, kind);

    public static void Show(string title, string message, ToastKind kind, TimeSpan lifetime) =>
        UiToastHost.Show(title, message, kind, lifetime);
}
