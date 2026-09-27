using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 · pozycja 36 — Binary Serializer (odpowiednik MessagePack bez zewnętrznego pakietu).
/// <para>Format jest jawny i wersjonowany: nagłówek „SXB1”, liczba pól (varint), a potem dla każdego
/// pola długość i treść nazwy, znacznik typu i wartość. Klucze są sortowane, więc ten sam słownik
/// zawsze daje identyczne bajty — to warunek porównywania i testowania.</para>
/// <para>Obsługiwane typy: null, bool, liczba całkowita (zigzag varint), liczba zmiennoprzecinkowa,
/// tekst UTF-8, bajty i lista tekstów. Nieznany typ jest jawnie odrzucany, a nie „serializowany na oko”.</para>
/// </summary>
public static class BinarySerializer
{
    private static readonly byte[] Magic = [(byte)'S', (byte)'X', (byte)'B', (byte)'1'];

    public static byte[] Encode(IReadOnlyDictionary<string, object?> values)
    {
        var buffer = new List<byte>(Magic);
        var ordered = values.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        WriteVarint(buffer, (ulong)ordered.Length);
        foreach (var pair in ordered)
        {
            byte[] name = Encoding.UTF8.GetBytes(pair.Key);
            WriteVarint(buffer, (ulong)name.Length);
            buffer.AddRange(name);
            WriteValue(buffer, pair.Value);
        }
        return buffer.ToArray();
    }

    private static void WriteValue(List<byte> buffer, object? value)
    {
        switch (value)
        {
            case null:
                buffer.Add(0);
                return;
            case bool flag:
                buffer.Add(flag ? (byte)2 : (byte)1);
                return;
            case long number:
                buffer.Add(3);
                WriteVarint(buffer, ZigZag(number));
                return;
            case int number:
                buffer.Add(3);
                WriteVarint(buffer, ZigZag(number));
                return;
            case double real:
                buffer.Add(4);
                buffer.AddRange(BitConverter.GetBytes(real));
                return;
            case string text:
                buffer.Add(5);
                byte[] utf8 = Encoding.UTF8.GetBytes(text);
                WriteVarint(buffer, (ulong)utf8.Length);
                buffer.AddRange(utf8);
                return;
            case byte[] bytes:
                buffer.Add(6);
                WriteVarint(buffer, (ulong)bytes.Length);
                buffer.AddRange(bytes);
                return;
            case IReadOnlyList<string> list:
                buffer.Add(7);
                WriteVarint(buffer, (ulong)list.Count);
                foreach (string item in list)
                {
                    byte[] itemBytes = Encoding.UTF8.GetBytes(item ?? "");
                    WriteVarint(buffer, (ulong)itemBytes.Length);
                    buffer.AddRange(itemBytes);
                }
                return;
            default:
                throw new NotSupportedException("Nie serializuję typu " + value.GetType().Name +
                    " — format obsługuje null, bool, liczby, tekst, bajty i listy tekstów.");
        }
    }

    public static bool TryDecode(byte[] data, out Dictionary<string, object?> values, out string error)
    {
        values = new Dictionary<string, object?>(StringComparer.Ordinal);
        error = "";
        if (data is null || data.Length < Magic.Length + 1) { error = "Dane są za krótkie, żeby być komunikatem SXB1."; return false; }
        for (int i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i]) { error = "Zły nagłówek — to nie jest komunikat SXB1."; return false; }

        int offset = Magic.Length;
        try
        {
            ulong count = ReadVarint(data, ref offset);
            if (count > 10_000) { error = "Liczba pól (" + count + ") przekracza limit 10000."; return false; }
            for (ulong index = 0; index < count; index++)
            {
                ulong nameLength = ReadVarint(data, ref offset);
                if (offset + (int)nameLength > data.Length) { error = "Urwany komunikat w nazwie pola."; return false; }
                string name = Encoding.UTF8.GetString(data, offset, (int)nameLength);
                offset += (int)nameLength;
                if (offset >= data.Length) { error = "Urwany komunikat w wartości pola „" + name + "”."; return false; }
                byte tag = data[offset++];
                switch (tag)
                {
                    case 0:
                        values[name] = null;
                        break;
                    case 1:
                        values[name] = false;
                        break;
                    case 2:
                        values[name] = true;
                        break;
                    case 3:
                        values[name] = UnZigZag(ReadVarint(data, ref offset));
                        break;
                    case 4:
                        if (offset + 8 > data.Length) { error = "Urwany double w polu „" + name + "”."; return false; }
                        values[name] = BitConverter.ToDouble(data, offset);
                        offset += 8;
                        break;
                    case 5:
                    {
                        ulong length = ReadVarint(data, ref offset);
                        if (offset + (int)length > data.Length) { error = "Urwany tekst w polu „" + name + "”."; return false; }
                        values[name] = Encoding.UTF8.GetString(data, offset, (int)length);
                        offset += (int)length;
                        break;
                    }
                    case 6:
                    {
                        ulong length = ReadVarint(data, ref offset);
                        if (offset + (int)length > data.Length) { error = "Urwane bajty w polu „" + name + "”."; return false; }
                        var copy = new byte[length];
                        Array.Copy(data, offset, copy, 0, (int)length);
                        values[name] = copy;
                        offset += (int)length;
                        break;
                    }
                    case 7:
                    {
                        ulong items = ReadVarint(data, ref offset);
                        var list = new List<string>();
                        for (ulong item = 0; item < items; item++)
                        {
                            ulong length = ReadVarint(data, ref offset);
                            if (offset + (int)length > data.Length) { error = "Urwany element listy w polu „" + name + "”."; return false; }
                            list.Add(Encoding.UTF8.GetString(data, offset, (int)length));
                            offset += (int)length;
                        }
                        values[name] = list;
                        break;
                    }
                    default:
                        error = "Nieznany znacznik typu " + tag + " w polu „" + name + "” — przerywam, nie zgaduję.";
                        return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "Nie odczytałem komunikatu: " + ex.GetType().Name + " — " + ex.Message;
            return false;
        }
    }

    public static string ToHex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    public static bool TryFromHex(string hex, out byte[] data, out string error)
    {
        data = [];
        error = "";
        string clean = (hex ?? "").Replace(" ", "").Replace("-", "").Trim();
        if (clean.Length == 0) { error = "Podaj bajty w zapisie szesnastkowym."; return false; }
        if (clean.Length % 2 != 0) { error = "Zapis szesnastkowy musi mieć parzystą liczbę znaków."; return false; }
        try
        {
            data = Convert.FromHexString(clean);
            return true;
        }
        catch (FormatException)
        {
            error = "To nie jest poprawny zapis szesnastkowy.";
            return false;
        }
    }

    public static string Describe(byte[] data)
    {
        var preview = new List<string>();
        foreach (byte value in data)
        {
            preview.Add(value.ToString("X2", CultureInfo.InvariantCulture));
            if (preview.Count >= 24) break;
        }
        return "Komunikat SXB1: " + data.Length + " bajtów" + Environment.NewLine +
            "· hex: " + ToHex(data) + Environment.NewLine +
            "· podgląd: " + string.Join(" ", preview) + (data.Length > 24 ? " …" : "");
    }

    private static void WriteVarint(List<byte> buffer, ulong value)
    {
        while (value >= 0x80)
        {
            buffer.Add((byte)(value | 0x80));
            value >>= 7;
        }
        buffer.Add((byte)value);
    }

    private static ulong ReadVarint(byte[] data, ref int offset)
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte current = data[offset++];
            result |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) return result;
            shift += 7;
            if (shift > 63) throw new InvalidOperationException("varint jest dłuższy niż 64 bity");
        }
    }

    private static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));
    private static long UnZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
}
