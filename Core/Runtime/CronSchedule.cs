using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 ┬Ě pozycja 7 ÔÇö Cron Scheduler (parser + harmonogram).
/// <para>Obs┼éugiwane jest klasyczne 5 p├│l: minuta, godzina, dzie┼ä miesi─ůca, miesi─ůc, dzie┼ä tygodnia.
/// Ka┼╝de pole przyjmuje <c>*</c>, pojedyncze warto┼Ťci, zakresy (<c>a-b</c>), kroki (<c>*/n</c>,
/// <c>a-b/n</c>), listy (<c>a,b,c</c>) oraz nazwy miesi─Öcy i dni (pierwsze trzy litery, po angielsku,
/// jak w cronie). Skr├│ty <c>@hourly</c>, <c>@daily</c>, <c>@weekly</c>, <c>@monthly</c>, <c>@yearly</c>,
/// <c>@minutely</c> te┼╝ dzia┼éaj─ů.</para>
/// <para>Semantyka dnia jest standardowa: gdy dzie┼ä miesi─ůca i dzie┼ä tygodnia s─ů oba ograniczone,
/// wystarczy zgodno┼Ť─ç jednego z nich. Obliczanie nast─Öpnych termin├│w nie ┼Ťpi ÔÇö to czysta matematyka
/// na zegarze, wi─Öc jest testowalne bez czekania.</para>
/// </summary>
public sealed class CronSchedule
{
    private readonly SortedSet<int> minutes;
    private readonly SortedSet<int> hours;
    private readonly SortedSet<int> daysOfMonth;
    private readonly SortedSet<int> months;
    private readonly SortedSet<int> daysOfWeek;
    private readonly List<int> minutesOfDay;
    private readonly bool dayOfMonthRestricted;
    private readonly bool dayOfWeekRestricted;

    private CronSchedule(string expression, SortedSet<int> minutes, SortedSet<int> hours, SortedSet<int> daysOfMonth,
        SortedSet<int> months, SortedSet<int> daysOfWeek)
    {
        Expression = expression;
        this.minutes = minutes;
        this.hours = hours;
        this.daysOfMonth = daysOfMonth;
        this.months = months;
        this.daysOfWeek = daysOfWeek;
        dayOfMonthRestricted = daysOfMonth.Count < 31;
        dayOfWeekRestricted = daysOfWeek.Count < 7;
        minutesOfDay = (from hour in hours from minute in minutes select hour * 60 + minute).OrderBy(x => x).ToList();
    }

    public string Expression { get; }

    private static readonly string[] MonthNames = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[] DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    public static bool TryParse(string? expression, out CronSchedule? schedule, out string error)
    {
        schedule = null;
        error = "";
        string text = (expression ?? "").Trim();
        if (text.Length == 0) { error = "Podaj wyra┼╝enie crona, np. ÔÇ×*/15 * * * *ÔÇŁ."; return false; }
        text = text switch
        {
            "@minutely" => "* * * * *",
            "@hourly" => "0 * * * *",
            "@daily" or "@midnight" => "0 0 * * *",
            "@weekly" => "0 0 * * 0",
            "@monthly" => "0 0 1 * *",
            "@yearly" or "@annually" => "0 0 1 1 *",
            _ => text
        };
        string[] fields = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) { error = "Cron ma dok┼éadnie 5 p├│l (minuta godzina dzie┼ä miesi─ůc dzie┼ä tygodnia), a tu jest " + fields.Length + "."; return false; }

        if (!TryParseField(fields[0], 0, 59, null, out var minutes, out error)) { error = "Minuty: " + error; return false; }
        if (!TryParseField(fields[1], 0, 23, null, out var hours, out error)) { error = "Godziny: " + error; return false; }
        if (!TryParseField(fields[2], 1, 31, null, out var dom, out error)) { error = "Dni miesi─ůca: " + error; return false; }
        if (!TryParseField(fields[3], 1, 12, MonthNames, out var monthSet, out error)) { error = "Miesi─ůce: " + error; return false; }
        if (!TryParseField(fields[4], 0, 7, DayNames, out var dow, out error)) { error = "Dni tygodnia: " + error; return false; }
        if (dow.Contains(7)) { dow.Remove(7); dow.Add(0); }

        schedule = new CronSchedule(text, minutes, hours, dom, monthSet, dow);
        return true;
    }

    private static bool TryParseField(string field, int min, int max, string[]? names, out SortedSet<int> values, out string error)
    {
        values = new SortedSet<int>();
        error = "";
        string[] parts = field.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { error = "puste pole"; return false; }
        foreach (string rawPart in parts)
        {
            string part = rawPart.Trim();
            if (part is "*" or "?")
            {
                for (int value = min; value <= max; value++) values.Add(value);
                continue;
            }
            int step = 1;
            int slash = part.IndexOf('/');
            if (slash >= 0)
            {
                string stepText = part[(slash + 1)..];
                if (!int.TryParse(stepText, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step < 1)
                {
                    error = "krok ÔÇ×" + stepText + "ÔÇŁ nie jest liczb─ů dodatni─ů";
                    return false;
                }
                part = part[..slash];
                if (part.Length == 0) part = "*";
            }
            int from, to;
            int dash = part.IndexOf('-', 1);
            if (part == "*")
            {
                from = min;
                to = max;
            }
            else if (dash > 0)
            {
                if (!TryValue(part[..dash], min, max, names, out from)) { error = "pocz─ůtek zakresu ÔÇ×" + part[..dash] + "ÔÇŁ poza " + min + "ÔÇô" + max; return false; }
                if (!TryValue(part[(dash + 1)..], min, max, names, out to)) { error = "koniec zakresu ÔÇ×" + part[(dash + 1)..] + "ÔÇŁ poza " + min + "ÔÇô" + max; return false; }
                if (to < from) { error = "zakres ÔÇ×" + part + "ÔÇŁ jest odwr├│cony"; return false; }
            }
            else
            {
                if (!TryValue(part, min, max, names, out from)) { error = "warto┼Ť─ç ÔÇ×" + part + "ÔÇŁ poza " + min + "ÔÇô" + max; return false; }
                to = slash >= 0 ? max : from;
            }
            for (int value = from; value <= to; value += step) values.Add(value);
        }
        if (values.Count == 0) { error = "pole nie wskazuje ┼╝adnej warto┼Ťci"; return false; }
        return true;
    }

    private static bool TryValue(string text, int min, int max, string[]? names, out int value)
    {
        value = 0;
        string token = text.Trim().ToLowerInvariant();
        if (token.Length == 0) return false;
        if (names != null && token.Length >= 3)
        {
            int index = Array.FindIndex(names, x => x == token[..3]);
            if (index >= 0) { value = names == MonthNames ? index + 1 : index; return true; }
        }
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return false;
        return value >= min && value <= max;
    }

    /// <summary>Kolejny termin po podanym momencie (┼Ťci┼Ťle p├│┼║niejszy, co do minuty).</summary>
    public DateTimeOffset? Next(DateTimeOffset after)
    {
        var start = new DateTimeOffset(after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Offset).AddMinutes(1);
        for (int day = 0; day < 4 * 366; day++)
        {
            var date = start.AddDays(day);
            // 0.97 ┬Ě Miesi─ůc jest cz─Ö┼Ťci─ů terminu. Bez tego sprawdzenia ÔÇ×0 0 31 2 *ÔÇŁ zwraca┼é
            // 31 stycznia (dzie┼ä 31 istnieje w kalendarzu, a miesi─ůc nie by┼é w og├│le pytany),
            // czyli harmonogram k┼éama┼é i nigdy nie dawa┼é uczciwego ÔÇ×brak terminuÔÇŁ.
            if (!months.Contains(date.Month) || !MatchesDay(date)) continue;
            foreach (int minuteOfDay in minutesOfDay)
            {
                var candidate = new DateTimeOffset(date.Year, date.Month, date.Day, minuteOfDay / 60, minuteOfDay % 60, 0, after.Offset);
                if (candidate >= start) return candidate;
            }
        }
        return null;
    }

    public IReadOnlyList<DateTimeOffset> Next(int count, DateTimeOffset after)
    {
        var results = new List<DateTimeOffset>();
        var cursor = after;
        for (int i = 0; i < Math.Clamp(count, 1, 50); i++)
        {
            var next = Next(cursor);
            if (next is null) break;
            results.Add(next.Value);
            cursor = next.Value;
        }
        return results;
    }

    /// <summary>Czy harmonogram wypada w danym momencie (minuta co do minuty).</summary>
    public bool Matches(DateTimeOffset moment) =>
        months.Contains(moment.Month) && minutes.Contains(moment.Minute) && hours.Contains(moment.Hour) && MatchesDay(moment);

    private bool MatchesDay(DateTimeOffset moment)
    {
        bool dom = daysOfMonth.Contains(moment.Day);
        bool dow = daysOfWeek.Contains((int)moment.DayOfWeek);
        if (dayOfMonthRestricted && dayOfWeekRestricted) return dom || dow;
        if (dayOfMonthRestricted) return dom;
        if (dayOfWeekRestricted) return dow;
        return true;
    }

    /// <summary>Ludzki opis po polsku ÔÇö dok┼éadnie tego, co wynika z wyra┼╝enia, bez zgadywania intencji.</summary>
    public string Describe()
    {
        int[] minuteList = minutes.ToArray();
        int[] hourList = hours.ToArray();
        bool everyMinute = minutes.Count == 60;
        int minuteStep = 0;
        bool stepMinutes = minuteList.Length > 1 && IsArithmetic(minuteList, out minuteStep) && minuteList[0] == 0 && hourList.Length == 24 &&
            !dayOfMonthRestricted && !dayOfWeekRestricted;

        if (everyMinute && hourList.Length == 24 && !dayOfMonthRestricted && !dayOfWeekRestricted && months.Count == 12)
            return "co minut─Ö, ca┼éy czas";
        if (stepMinutes)
            return "co " + minuteStep + " minut" + DaySuffix();

        if (minutes.Count == 1 && hourList.Length == 1 && !dayOfMonthRestricted && !dayOfWeekRestricted && months.Count == 12)
            return "codziennie o " + Time(hourList[0], minuteList[0]);

        string when = minuteList.Length == 1 && hourList.Length == 1
            ? " o " + Time(hourList[0], minuteList[0])
            : " w minutach " + string.Join(", ", minuteList) + " o godzinach " + string.Join(", ", hourList);

        if (dayOfWeekRestricted && !dayOfMonthRestricted)
            return "w " + string.Join(", ", daysOfWeek.OrderBy(x => x).Select(DescribeDay)) + when;
        if (dayOfMonthRestricted && !dayOfWeekRestricted)
            return "dnia " + string.Join(", ", daysOfMonth) + ". miesi─ůca" + when;
        if (dayOfMonthRestricted && dayOfWeekRestricted)
            return "dnia " + string.Join(", ", daysOfMonth) + ". miesi─ůca albo w " +
                string.Join(", ", daysOfWeek.OrderBy(x => x).Select(DescribeDay)) + when;
        return "harmonogram " + Expression + when;
    }

    private string DaySuffix()
    {
        if (months.Count < 12 && dayOfMonthRestricted) return " przez dni " + string.Join(", ", daysOfMonth) + " miesi─ůca";
        if (months.Count < 12) return " w miesi─ůcach " + string.Join(", ", months.Select(MonthName));
        if (dayOfWeekRestricted) return " w " + string.Join(", ", daysOfWeek.OrderBy(x => x).Select(DescribeDay));
        if (dayOfMonthRestricted) return " przez dni " + string.Join(", ", daysOfMonth) + " miesi─ůca";
        return "";
    }

    private static bool IsArithmetic(int[] values, out int step)
    {
        step = 0;
        if (values.Length < 2) return false;
        step = values[1] - values[0];
        if (step <= 0) return false;
        for (int i = 2; i < values.Length; i++)
            if (values[i] - values[i - 1] != step) return false;
        return true;
    }

    private static string Time(int hour, int minute) => hour.ToString("D2", CultureInfo.InvariantCulture) + ":" + minute.ToString("D2", CultureInfo.InvariantCulture);

    internal static string DescribeDay(int day) => day switch
    {
        0 => "niedziele",
        1 => "poniedzia┼éki",
        2 => "wtorki",
        3 => "┼Ťrody",
        4 => "czwartki",
        5 => "pi─ůtki",
        _ => "soboty"
    };

    private static string MonthName(int month) => month switch
    {
        1 => "stycze┼ä", 2 => "luty", 3 => "marzec", 4 => "kwiecie┼ä", 5 => "maj", 6 => "czerwiec",
        7 => "lipiec", 8 => "sierpie┼ä", 9 => "wrzesie┼ä", 10 => "pa┼║dziernik", 11 => "listopad", _ => "grudzie┼ä"
    };

    public string DescribeNext(int count = 5, DateTimeOffset? from = null)
    {
        var moment = from ?? DateTimeOffset.Now;
        var next = Next(count, moment);
        if (next.Count == 0) return "Nie umiem znale┼║─ç nast─Öpnego terminu w horyzoncie 4 lat ÔÇö wyra┼╝enie jest poprawne, ale praktycznie nieosi─ůgalne (np. 31 lutego).";
        return "Najbli┼╝sze terminy:" + Environment.NewLine +
            string.Join(Environment.NewLine, next.Select(x => "┬Ě " + x.ToString("yyyy-MM-dd HH:mm (dddd)", CultureInfo.GetCultureInfo("pl-PL"))));
    }
}
