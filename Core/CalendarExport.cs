using System.Globalization;
using System.Text;
using System.IO;

namespace SentinelX;

/// <summary>0.97 · local .ics export (#1002 event creator, #1003 recurring events). Writes a file the user can open anywhere.</summary>
public static class CalendarExport
{
    public sealed record Entry(string Title, DateTime When, TimeSpan Duration, string Note)
    {
        public Entry(string title, DateTime when) : this(title, when, TimeSpan.FromMinutes(30), "") { }
    }

    public static string Build(IEnumerable<Entry> entries, string calendarName = "SentinelX")
    {
        var builder = new StringBuilder();
        string stamp = DateTime.Now.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        builder.Append("BEGIN:VCALENDAR\r\n");
        builder.Append("VERSION:2.0\r\n");
        builder.Append("PRODID:-//SentinelX//Lokalny asystent//PL\r\n");
        builder.Append("CALSCALE:GREGORIAN\r\n");
        builder.Append("X-WR-CALNAME:").Append(Escape(calendarName)).Append("\r\n");
        int index = 0;
        foreach (Entry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Title)) continue;
            string start = entry.When.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            string end = (entry.When + entry.Duration).ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            builder.Append("BEGIN:VEVENT\r\n");
            builder.Append("UID:").Append(Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)).Append("@sentinelx.local\r\n");
            builder.Append("DTSTAMP:").Append(stamp).Append("\r\n");
            builder.Append("DTSTART:").Append(start).Append("\r\n");
            builder.Append("DTEND:").Append(end).Append("\r\n");
            builder.Append("SUMMARY:").Append(Escape(entry.Title)).Append("\r\n");
            if (entry.Note.Length > 0) builder.Append("DESCRIPTION:").Append(Escape(entry.Note)).Append("\r\n");
            builder.Append("CATEGORIES:SentinelX\r\n");
            builder.Append("END:VEVENT\r\n");
            index++;
        }
        builder.Append("END:VCALENDAR\r\n");
        return index == 0 ? "" : builder.ToString();
    }

    public static string Save(string directory, IEnumerable<Entry> entries, string fileName, out string path, out int count)
    {
        path = ""; count = 0;
        string content = Build(entries);
        if (content.Length == 0) return "Nie mam żadnego terminu do wyeksportowania — najpierw dodaj zadanie z datą albo przypomnienie.";
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        count = content.Split("BEGIN:VEVENT", StringSplitOptions.None).Length - 1;
        return "Kalendarz zapisany lokalnie (" + count + " terminów):\n" + path + "\nTo zwykły plik .ics — otworzysz go w Kalendarzu Windows, Outlooku albo telefonie. Nic nie wysłałem do internetu.";
    }

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal).Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
