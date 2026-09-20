namespace SentinelX.Models;
public sealed record PaletteEntry(string Title, string Description, string Keywords, string? PageKey = null, string? CommandText = null)
{
    public string Kind => PageKey != null ? "STRONA" : "POLECENIE";
    public string Hint => PageKey != null ? "Enter · przejdź" : "Enter · wstaw do edytora, bez wykonywania";
}
