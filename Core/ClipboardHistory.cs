using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core;

/// <summary>0.96 · historia schowka: pierścień ostatnich tekstów, które Sentinel skopiował
/// („kopiuj: …”), z możliwością powrotu do dowolnego z nich („schowek 2”).
/// <para>Świadomie wyłącznie w pamięci sesji: historia znika po zamknięciu aplikacji i nigdy nie
/// trafia na dysk — schowek bywa nośnikiem haseł. Sentinel nie podgląda też cudzej zawartości:
/// zapamiętuje tylko to, co sam skopiował na Twoje polecenie.</para></summary>
public static class ClipboardHistory
{
    private const int Capacity = 12;
    private const int MaxEntryLength = 4000;

    private static readonly object Gate = new();
    private static readonly List<string> Items = [];

    /// <summary>Zapamiętuje tekst. Duplikaty nie zajmują drugiego miejsca — wędrują na początek.</summary>
    public static void Record(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string value = text.Length > MaxEntryLength ? text[..MaxEntryLength] : text;
        lock (Gate)
        {
            Items.RemoveAll(x => string.Equals(x, value, StringComparison.Ordinal));
            Items.Insert(0, value);
            while (Items.Count > Capacity) Items.RemoveAt(Items.Count - 1);
        }
    }

    public static IReadOnlyList<string> Entries()
    {
        lock (Gate) return Items.ToArray();
    }

    /// <summary>Wpis numer N (liczone od 1 = najnowszy). null, gdy takiego wpisu nie ma.</summary>
    public static string? Get(int number)
    {
        lock (Gate) return number >= 1 && number <= Items.Count ? Items[number - 1] : null;
    }

    public static void Clear()
    {
        lock (Gate) Items.Clear();
    }

    /// <summary>Czytelna lista do odpowiedzi w czacie.</summary>
    public static string Describe()
    {
        var items = Entries();
        if (items.Count == 0)
            return "Historia schowka jest pusta w tej sesji. Skopiuj coś: „kopiuj: tekst”, a trafi tu na bieżąco (tylko pamięć sesji — nic nie zapisuję na dysku).";
        var lines = items.Select((x, i) =>
        {
            string preview = x.Length > 60 ? x[..59] + "…" : x;
            preview = preview.Replace("\r", " ").Replace("\n", " ");
            return $"{i + 1}. {preview} ({x.Length} zn.)";
        });
        return "Historia schowka (" + items.Count + " · najnowszy pierwszy):\n" + string.Join("\n", lines) +
            "\nWróć do wpisu: „schowek 2” (wklejam go z powrotem do schowka).";
    }
}
