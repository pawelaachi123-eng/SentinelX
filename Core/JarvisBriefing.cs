using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentinelX.Core;

/// <summary>Dane do briefingu — komplet tego, co Sentinel potrafi powiedzieć o dniu i komputerze
/// bez modelu językowego. Wszystko pochodzi z lokalnych odczytów; pusta lista to pusta sekcja,
/// nigdy wymyślony wpis.</summary>
public sealed record BriefingData(
    DateTime Now,
    string UserName,
    IReadOnlyList<string> OverdueTasks,
    IReadOnlyList<string> TodayTasks,
    IReadOnlyList<string> TodayReminders,
    IReadOnlyList<string> DoneToday,
    IReadOnlyList<string> SystemFacts,
    IReadOnlyList<string> PinnedNotes)
{
    public static BriefingData Empty(DateTime now) =>
        new(now, "", [], [], [], [], [], []);
}

/// <summary>0.96 · „dzień dobry” i „dobranoc” — to, co ma każdy asystent: jedno polecenie,
/// które mówi, co dziś jest i w jakim stanie jest komputer. Czysta funkcja: bez sieci, bez modelu,
/// bez dostępu do dysku — dzięki temu jest w pełni testowalna i (TU WAŻNE) niczego nie wykonuje.</summary>
public static class JarvisBriefing
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string Morning(BriefingData data)
    {
        var lines = new List<string>
        {
            "Dzień dobry" + Greeting(data) + ". " + LongDate(data.Now) + " · " + data.Now.ToString("HH:mm", Pl) + ".",
        };
        var today = new List<string>();
        foreach (string task in data.OverdueTasks) today.Add("‼ " + task + "  (termin minął)");
        foreach (string task in data.TodayTasks) today.Add("· " + task);
        foreach (string reminder in data.TodayReminders) today.Add("⏰ " + reminder);
        lines.Add(Section(today.Count == 0
            ? "DZISIAJ — nic nie zaplanowałeś. Dodaj: „dodaj zadanie: treść” albo „przypomnij mi dziś o 18 o …”."
            : "DZISIAJ (" + today.Count + ")", today));

        if (data.PinnedNotes.Count > 0)
            lines.Add(Section("PRZYPOMNIENIA Z PAMIĘCI", data.PinnedNotes.Select(x => "· " + Truncate(x, 120)).ToList()));

        lines.Add(Section("KOMPUTER", data.SystemFacts.Select(x => "· " + x).ToList()));
        lines.Add("To wszystko są lokalne odczyty — bez sieci i bez modelu. „rutyna poranek” odpala Twoją sekwencję, „pomoc” pokazuje resztę.");
        return string.Join("\n\n", lines);
    }

    public static string Evening(BriefingData data)
    {
        var lines = new List<string>
        {
            "Dobranoc" + Greeting(data) + ". " + LongDate(data.Now) + " · " + data.Now.ToString("HH:mm", Pl) + ".",
        };
        lines.Add(Section("ZROBIONE DZISIAJ (" + data.DoneToday.Count + ")",
            data.DoneToday.Select(x => "✓ " + x).ToList(),
            empty: "Dziś nic nie odhaczyłeś — zadania czekają w Centrum → 📓."));
        lines.Add(Section("ZOSTAJE NA JUTRO (" + (data.TodayTasks.Count + data.OverdueTasks.Count) + ")",
            data.OverdueTasks.Select(x => "‼ " + x).Concat(data.TodayTasks.Select(x => "· " + x)).ToList(),
            empty: "Lista zadań jest pusta. Spokojnej nocy."));
        if (data.TodayReminders.Count > 0)
            lines.Add(Section("PRZYPOMNIENIA", data.TodayReminders.Select(x => "⏰ " + x).ToList()));
        lines.Add(Section("KOMPUTER", data.SystemFacts.Select(x => "· " + x).ToList()));
        lines.Add("Gdy będziesz gotowy: „zablokuj ekran”, „wygasz ekran” albo „uspij komputer” — zanim się zacznie, zdążysz powiedzieć „anuluj zamknięcie”.");
        return string.Join("\n\n", lines);
    }

    private static string Greeting(BriefingData data) =>
        data.UserName.Length == 0 ? "" : ", " + data.UserName;

    private static string LongDate(DateTime when)
    {
        string date = when.ToString("dddd, d MMMM yyyy", Pl);
        return char.ToUpper(date[0], Pl) + date[1..];
    }

    private static string Section(string title, List<string> items, string? empty = null)
    {
        if (items.Count == 0) return title + (empty == null ? "" : " — " + empty);
        return title + "\n" + string.Join("\n", items);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
