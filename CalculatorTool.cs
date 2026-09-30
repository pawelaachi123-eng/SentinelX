using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Small, bounded arithmetic parser. It evaluates expressions only; it never executes user code.</summary>
public sealed class CalculatorTool
{
    private static readonly Regex Request = new(
        @"^(?:(?:oblicz|policz|wynik|ile to)\s+)?(?<expr>[0-9\s.,()+*/\-×xX]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public string? TryProcess(string command)
    {
        string raw = (command ?? "").Trim().TrimEnd('?', '!', '.');
        Match match;
        try { match = Request.Match(raw); }
        catch (RegexMatchTimeoutException) { return null; }
        if (!match.Success) return null;
        string expression = match.Groups["expr"].Value.Trim();
        bool explicitlyRequested = Regex.IsMatch(raw, @"^(?:oblicz|policz|wynik|ile to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!explicitlyRequested && !expression.Any(c => c is '+' or '-' or '*' or '/' or '×' or 'x' or 'X')) return null;
        if (expression.Length is < 1 or > 128) return "Działanie jest za długie; limit to 128 znaków.";
        expression = Regex.Replace(expression, @"(?<=\d)\s*[×xX]\s*(?=\d)", "*");
        expression = Regex.Replace(expression, @"(?<=\d),(?=\d)", ".");
        try
        {
            var parser = new DecimalExpressionParser(expression);
            decimal value = parser.Parse();
            string formatted = value.ToString("G29", CultureInfo.GetCultureInfo("pl-PL"));
            string verification = TryVerifyIntegerMultiplication(expression, value, out BigInteger exact)
                ? $"Kontrola niezależna: {exact.ToString(CultureInfo.InvariantCulture)} (BigInteger)."
                : "Obliczono deterministycznym parserem decimal; nie wykonywałem kodu ani nie korzystałem z modelu językowego.";
            return $"{formatted}\n{verification}";
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or OverflowException or ArgumentException)
        { return "Nie mogę bezpiecznie obliczyć tego działania: " + ex.Message; }
    }

    private static bool TryVerifyIntegerMultiplication(string expression, decimal value, out BigInteger exact)
    {
        exact = BigInteger.Zero;
        Match match = Regex.Match(expression, @"^\s*(?<a>[+-]?\d{1,100})\s*\*\s*(?<b>[+-]?\d{1,100})\s*$", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        BigInteger a = BigInteger.Parse(match.Groups["a"].Value, CultureInfo.InvariantCulture);
        BigInteger b = BigInteger.Parse(match.Groups["b"].Value, CultureInfo.InvariantCulture);
        exact = a * b;
        return exact >= (BigInteger)decimal.MinValue && exact <= (BigInteger)decimal.MaxValue && (decimal)exact == value;
    }

    private sealed class DecimalExpressionParser
    {
        private readonly string text;
        private int index;
        private int terms;
        public DecimalExpressionParser(string text) => this.text = text;

        public decimal Parse()
        {
            decimal value = Expression();
            SkipSpaces();
            if (index != text.Length) throw new FormatException("Nieoczekiwany znak w wyrażeniu.");
            return value;
        }

        private decimal Expression()
        {
            decimal value = Term();
            while (true)
            {
                SkipSpaces();
                if (Take('+')) value = checked(value + Term());
                else if (Take('-')) value = checked(value - Term());
                else return value;
            }
        }

        private decimal Term()
        {
            decimal value = Unary();
            while (true)
            {
                SkipSpaces();
                if (Take('*')) value = checked(value * Unary());
                else if (Take('/')) value = checked(value / Unary());
                else return value;
            }
        }

        private decimal Unary()
        {
            SkipSpaces();
            if (Take('+')) return Unary();
            if (Take('-')) return checked(-Unary());
            if (Take('('))
            {
                decimal inside = Expression();
                SkipSpaces();
                if (!Take(')')) throw new FormatException("Brakuje zamykającego nawiasu.");
                return inside;
            }
            return Number();
        }

        private decimal Number()
        {
            SkipSpaces();
            int start = index;
            bool dot = false;
            while (index < text.Length)
            {
                char c = text[index];
                if (char.IsAsciiDigit(c)) index++;
                else if (c == '.' && !dot) { dot = true; index++; }
                else break;
            }
            if (start == index || ++terms > 64) throw new FormatException("Brakuje liczby lub przekroczono limit 64 składników.");
            if (!decimal.TryParse(text.AsSpan(start, index - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
                throw new OverflowException("Liczba wykracza poza zakres decimal.");
            return value;
        }

        private bool Take(char expected)
        {
            if (index >= text.Length || text[index] != expected) return false;
            index++; return true;
        }

        private void SkipSpaces() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
    }
}
