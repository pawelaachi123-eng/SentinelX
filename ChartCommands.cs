using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SentinelX;

/// <summary>
/// SEKCJA 19 — wykresy jako OBRAZ, nie znaki: „wykres: 3 5 8 4 | Sty Lut Mar Kwi” renderuje
/// słupkowy PNG (WPF DrawingVisual → RenderTargetBitmap) i zapisuje go w folderze danych aplikacji.
/// Odpowiedź podaje ścieżkę pliku, statystyki danych i jawnie mówi, że to statyczny obraz,
/// nie interaktywny wykres.
/// </summary>
public static class ChartCommands
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Domyślny folder zapisu: dane aplikacji / Charts.</summary>
    public static string DefaultDirectory() => Path.Combine(AppPaths.Root, "Charts");

    /// <summary>Wejście: „3 5 8 4 | Sty Lut Mar Kwi” (etykiety po |, opcjonalne). Zwraca komunikat
    /// z pełną ścieżką pliku. Parametr directory jest po to, by regresja nie pisała do danych
    /// aplikacji, tylko do folderu tymczasowego testu.</summary>
    public static string Handle(string payload, string? directory = null)
    {
        string[] parts = (payload ?? "").Split('|', 2);
        var values = new System.Collections.Generic.List<double>();
        foreach (Match m in Regex.Matches(parts[0] ?? "", "-?\\d+(?:[.,]\\d+)?"))
        {
            string token = m.Value.Replace(',', '.');
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) && Math.Abs(v) < 1e12)
                values.Add(v);
            if (values.Count >= 24) break;
        }
        if (values.Count < 2)
            return "Użycie: „wykres: 3 5 8 4 | Sty Lut Mar Kwi” (liczby, po kresce opcjonalne etykiety). Otrzymasz plik PNG w danych aplikacji.";

        var labels = new System.Collections.Generic.List<string>();
        if (parts.Length > 1)
            labels.AddRange(parts[1].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(values.Count).Select(x => x.Length > 12 ? x[..12] : x));
        while (labels.Count < values.Count) labels.Add((labels.Count + 1).ToString(Pl));

        string dir = directory ?? DefaultDirectory();
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "wykres-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", Pl) + ".png");
        RenderBarChart(values.ToArray(), labels.ToArray(), "Wykres z danych", file);
        long size = new FileInfo(file).Length;

        double min = values.Min(), max = values.Max(), avg = values.Average();
        return "WYKRES ZAPISANY: " + file + Environment.NewLine +
            "· dane: " + values.Count + " wartości · min " + N(min) + " · maks " + N(max) + " · średnia " + N(avg) + Environment.NewLine +
            "· plik: " + N(size / 1024.0) + " KB, " + 900 + "×" + 420 + " px" + Environment.NewLine +
            "· to statyczny obraz słupkowy (nie interaktywny); dane biorę wyłącznie z polecenia";
    }

    /// <summary>Render słupkowego PNG. Publiczne dla regresji — test podaje własną ścieżkę.</summary>
    public static void RenderBarChart(double[] values, string[] labels, string title, string path, int width = 900, int height = 420)
    {
        if (values.Length == 0) throw new ArgumentException("Brak danych do wykresu.");
        var visual = new DrawingVisual();
        double max = Math.Max(values.Max(v => Math.Abs(v)), 1e-9);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var titleText = Text(title, 18, Brushes.Black);
            dc.DrawText(titleText, new Point(16, 12));
            double left = 56, right = width - 16, top = 52, bottom = height - 44;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1);
            pen.Freeze();
            dc.DrawLine(pen, new Point(left, top), new Point(left, bottom));
            dc.DrawLine(pen, new Point(left, bottom), new Point(right, bottom));
            dc.DrawText(Text(N(max), 11, Brushes.DimGray), new Point(4, top - 14));
            dc.DrawText(Text("0", 11, Brushes.DimGray), new Point(40, bottom - 8));
            double slot = (right - left) / values.Length;
            double barWidth = Math.Max(6, slot * 0.65);
            var bar = new SolidColorBrush(Color.FromRgb(31, 162, 195));
            bar.Freeze();
            for (int i = 0; i < values.Length; i++)
            {
                double h = (bottom - top) * Math.Abs(values[i]) / max;
                if (h < 1) h = 1;
                var rect = new Rect(left + slot * i + (slot - barWidth) / 2, bottom - h, barWidth, h);
                dc.DrawRectangle(bar, null, rect);
                string label = i < labels.Length ? labels[i] : (i + 1).ToString(Pl);
                var labelText = Text(label, 11, Brushes.Black);
                dc.DrawText(labelText, new Point(rect.X + (barWidth - labelText.Width) / 2, bottom + 6));
                var valueText = Text(N(values[i]), 11, Brushes.DimGray);
                dc.DrawText(valueText, new Point(rect.X + (barWidth - valueText.Width) / 2, Math.Max(top - 2, rect.Y - 16)));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static FormattedText Text(string value, double size, Brush brush) =>
        new(value, Pl, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1.25);

    private static string N(double v) => v.ToString("0.##", Pl);
}
