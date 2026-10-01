using System.Text;

namespace SentinelX;

/// <summary>Incremental filter that keeps hidden reasoning (think tags) out of the live stream.
/// A tag split across two chunks is held back instead of leaked, and leftover reasoning is dropped on flush.</summary>
internal sealed class StreamThinkFilter
{
    private const string Open = "<think>";
    private const string Close = "</think>";
    private readonly StringBuilder buffer = new();
    private bool insideThink;

    public string Push(string chunk)
    {
        if (chunk.Length == 0) return "";
        buffer.Append(chunk);
        return Drain(final: false);
    }

    /// <summary>Emits whatever is safe to show at the end of a generation; unfinished reasoning stays hidden.</summary>
    public string Flush() => Drain(final: true);

    private string Drain(bool final)
    {
        var visible = new StringBuilder();
        while (true)
        {
            string text = buffer.ToString();
            if (!insideThink)
            {
                int open = text.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
                if (open >= 0)
                {
                    visible.Append(text.AsSpan(0, open));
                    buffer.Clear();
                    buffer.Append(text[(open + Open.Length)..]);
                    insideThink = true;
                    continue;
                }
                int hold = final ? 0 : HeldBackLength(text, Open);
                visible.Append(text.AsSpan(0, text.Length - hold));
                buffer.Clear();
                buffer.Append(text[(text.Length - hold)..]);
                return visible.ToString();
            }

            int close = text.IndexOf(Close, StringComparison.OrdinalIgnoreCase);
            if (close >= 0)
            {
                buffer.Clear();
                buffer.Append(text[(close + Close.Length)..]);
                insideThink = false;
                continue;
            }
            int hidden = final ? 0 : HeldBackLength(text, Close);
            buffer.Clear();
            buffer.Append(text[(text.Length - hidden)..]);
            return visible.ToString();
        }
    }

    /// <summary>Longest suffix of <paramref name="text"/> that could still become <paramref name="tag"/> in the next chunk.</summary>
    internal static int HeldBackLength(string text, string tag)
    {
        for (int length = Math.Min(text.Length, tag.Length - 1); length > 0; length--)
            if (text.EndsWith(tag[..length], StringComparison.OrdinalIgnoreCase)) return length;
        return 0;
    }
}
