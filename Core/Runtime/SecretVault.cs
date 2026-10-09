using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 ┬Ě pozycja 29 oraz SEKCJA 3 ┬Ě pozycja 131 ÔÇö lokalny sejf na sekrety.
/// <para>Szyfrowanie: AES-256-GCM (poufno┼Ť─ç + wykrywanie manipulacji) z kluczem wyprowadzonym z has┼éa
/// przez PBKDF2-HMAC-SHA256 (210 000 iteracji, losowa s├│l). Ka┼╝dy wpis ma w┼éasny losowy nonce,
/// a nazwa wpisu jest dodatkowymi danymi uwierzytelniaj─ůcymi (AAD) ÔÇö podmiana nazwy psuje odszyfrowanie.</para>
/// <para>Twarda zasada: has┼éo nigdy nie jest przyjmowane z tre┼Ťci polecenia, bo trafi┼éoby do historii
/// czatu i audytu. Has┼éo podaje host (pole w interfejsie) przez <see cref="PassphraseProvider"/> albo
/// test bezpo┼Ťrednio przez API. Sentinel m├│wi to wprost, zamiast po cichu zapisywa─ç sekret w logu.</para>
/// </summary>
public sealed class SecretVault
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const string VerifierText = "SENTINEL-VAULT-1";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly object gate = new();
    private byte[]? key;

    public SecretVault(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(AppPaths.MemoryDirectory, "Vault.json");
    }

    public string Path { get; }
    public string? LastError { get; private set; }

    /// <summary>Host ustawia tutaj bezpieczne ┼║r├│d┼éo has┼éa (pole w UI). Null = brak ┼║r├│d┼éa.</summary>
    public Func<string?>? PassphraseProvider { get; set; }

    public bool Exists => File.Exists(Path);

    public bool IsUnlocked
    {
        get { lock (gate) return key is not null; }
    }

    private class VaultDocument
    {
        public int Version { get; set; } = 1;
        public string Kdf { get; set; } = "PBKDF2-HMAC-SHA256";
        public int Iterations { get; set; } = SecretVault.Iterations;
        public string Salt { get; set; } = "";
        public string Verifier { get; set; } = "";
        public string VerifierNonce { get; set; } = "";
        public string VerifierTag { get; set; } = "";
        public List<VaultEntry> Entries { get; set; } = new();
    }

    private class VaultEntry
    {
        public string Name { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Cipher { get; set; } = "";
        public string Note { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    public bool Create(string passphrase)
    {
        if (Exists) { LastError = "Sejf ju┼╝ istnieje ÔÇö u┼╝yj odblokowania."; return false; }
        if (!IsStrong(passphrase, out string reason)) { LastError = reason; return false; }
        try
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] derived = Derive(passphrase, salt, Iterations);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] plain = Encoding.UTF8.GetBytes(VerifierText);
            byte[] cipher = new byte[plain.Length];
            byte[] tag = new byte[TagSize];
            using (var aes = new AesGcm(derived, TagSize))
                aes.Encrypt(nonce, plain, cipher, tag);

            var document = new VaultDocument
            {
                Salt = Convert.ToBase64String(salt),
                Verifier = Convert.ToBase64String(cipher),
                VerifierNonce = Convert.ToBase64String(nonce),
                VerifierTag = Convert.ToBase64String(tag)
            };
            Save(document);
            lock (gate) key = derived;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = "Nie utworzy┼éem sejfu: " + ex.Message;
            return false;
        }
    }

    public bool Unlock(string? passphrase)
    {
        if (passphrase is null) { LastError = "Brak has┼éa do sejfu."; return false; }
        try
        {
            if (!File.Exists(Path)) { LastError = "Sejf nie istnieje ÔÇö najpierw go utw├│rz."; return false; }
            var document = JsonSerializer.Deserialize<VaultDocument>(File.ReadAllText(Path));
            if (document is null || document.Salt.Length == 0) { LastError = "Plik sejfu jest nieczytelny."; return false; }
            byte[] salt = Convert.FromBase64String(document.Salt);
            byte[] derived = Derive(passphrase, salt, document.Iterations <= 0 ? Iterations : document.Iterations);
            byte[] plain = new byte[Convert.FromBase64String(document.Verifier).Length];
            using (var aes = new AesGcm(derived, TagSize))
                aes.Decrypt(Convert.FromBase64String(document.VerifierNonce), Convert.FromBase64String(document.Verifier), Convert.FromBase64String(document.VerifierTag), plain);
            if (Encoding.UTF8.GetString(plain) != VerifierText) { LastError = "Has┼éo nie pasuje."; return false; }
            lock (gate) key = derived;
            LastError = null;
            return true;
        }
        catch (CryptographicException)
        {
            LastError = "Has┼éo nie pasuje (albo plik sejfu zosta┼é zmieniony poza aplikacj─ů ÔÇö AES-GCM to wykrywa).";
            return false;
        }
        catch (Exception ex)
        {
            LastError = "Nie odblokowa┼éem sejfu: " + ex.Message;
            return false;
        }
    }

    public void Lock()
    {
        lock (gate)
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            key = null;
        }
    }

    public bool Add(string name, string secret, string note = "")
    {
        string clean = SanitizeName(name, out string error);
        if (clean.Length == 0) { LastError = error; return false; }
        if (string.IsNullOrEmpty(secret)) { LastError = "Sekret jest pusty."; return false; }
        lock (gate)
        {
            if (key is null) { LastError = "Sejf jest zablokowany."; return false; }
            try
            {
                var document = Read();
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
                byte[] plain = Encoding.UTF8.GetBytes(secret);
                byte[] cipher = new byte[plain.Length];
                byte[] tag = new byte[TagSize];
                using (var aes = new AesGcm(key, TagSize))
                    aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(clean));
                document.Entries.RemoveAll(x => string.Equals(x.Name, clean, StringComparison.OrdinalIgnoreCase));
                document.Entries.Add(new VaultEntry
                {
                    Name = clean,
                    Nonce = Convert.ToBase64String(nonce),
                    Tag = Convert.ToBase64String(tag),
                    Cipher = Convert.ToBase64String(cipher),
                    Note = note.Length > 200 ? note[..200] : note,
                    UpdatedAt = DateTimeOffset.Now.ToString("O")
                });
                Save(document);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = "Nie zapisa┼éem sekretu: " + ex.Message;
                return false;
            }
        }
    }

    public string? Get(string name)
    {
        string clean = SanitizeName(name, out _);
        if (clean.Length == 0) { LastError = "Podaj nazw─Ö sekretu."; return null; }
        lock (gate)
        {
            if (key is null) { LastError = "Sejf jest zablokowany."; return null; }
            var document = Read();
            var entry = document.Entries.FirstOrDefault(x => string.Equals(x.Name, clean, StringComparison.OrdinalIgnoreCase));
            if (entry is null) { LastError = "Nie ma sekretu o nazwie ÔÇ×" + clean + "ÔÇŁ."; return null; }
            try
            {
                byte[] cipher = Convert.FromBase64String(entry.Cipher);
                byte[] plain = new byte[cipher.Length];
                using (var aes = new AesGcm(key, TagSize))
                    aes.Decrypt(Convert.FromBase64String(entry.Nonce), cipher, Convert.FromBase64String(entry.Tag), plain, Encoding.UTF8.GetBytes(entry.Name));
                LastError = null;
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                LastError = "Wpis ÔÇ×" + clean + "ÔÇŁ nie przechodzi weryfikacji AES-GCM ÔÇö zosta┼é zmieniony poza aplikacj─ů. Nie zwracam zmanipulowanej tre┼Ťci.";
                return null;
            }
        }
    }

    public bool Remove(string name)
    {
        string clean = SanitizeName(name, out _);
        lock (gate)
        {
            var document = Read();
            int removed = document.Entries.RemoveAll(x => string.Equals(x.Name, clean, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) { LastError = "Nie ma sekretu o nazwie ÔÇ×" + clean + "ÔÇŁ."; return false; }
            Save(document);
            return true;
        }
    }

    public IReadOnlyList<(string Name, string Note, string UpdatedAt)> Names()
    {
        lock (gate)
            return Read().Entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => (x.Name, x.Note, x.UpdatedAt)).ToArray();
    }

    public string Describe()
    {
        var names = Names();
        var lines = new List<string>
        {
            "Sejf: " + Path + " ┬Ě " + (Exists ? "plik istnieje" : "brak pliku") + " ┬Ě " +
            (IsUnlocked ? "odblokowany" : "zablokowany") + " ┬Ě wpis├│w: " + names.Count + " ┬Ě AES-256-GCM, PBKDF2 " + Iterations + " iteracji"
        };
        if (names.Count > 0) lines.AddRange(names.Select(x => "┬Ě " + x.Name + (x.Note.Length > 0 ? " ÔÇö " + x.Note : "")));
        if (LastError is not null) lines.Add("Uwaga: " + LastError);
        lines.Add("Has┼éo do sejfu podaje panel (pole has┼éa) albo host ÔÇö nigdy tre┼Ť─ç polecenia, bo polecenia trafiaj─ů do historii.");
        return string.Join(Environment.NewLine, lines);
    }

    private VaultDocument Read()
    {
        try
        {
            if (!File.Exists(Path)) return new VaultDocument();
            return JsonSerializer.Deserialize<VaultDocument>(File.ReadAllText(Path)) ?? new VaultDocument();
        }
        catch (Exception ex)
        {
            LastError = "Nie odczyta┼éem sejfu: " + ex.Message;
            return new VaultDocument();
        }
    }

    private void Save(VaultDocument document)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
        string payload = JsonSerializer.Serialize(document, Json);
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, payload);
        File.Move(temporary, Path, true);
    }

    private static byte[] Derive(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, KeySize);

    private static bool IsStrong(string? passphrase, out string reason)
    {
        reason = "";
        if (passphrase is null || passphrase.Length < 8) { reason = "Has┼éo sejfu musi mie─ç co najmniej 8 znak├│w."; return false; }
        if (passphrase.Length > 512) { reason = "Has┼éo jest za d┼éugie (limit 512 znak├│w)."; return false; }
        return true;
    }

    private static string SanitizeName(string? name, out string error)
    {
        error = "";
        string clean = (name ?? "").Trim();
        if (clean.Length == 0) { error = "Podaj nazw─Ö sekretu."; return ""; }
        if (clean.Length > 80) { error = "Nazwa sekretu jest za d┼éuga (limit 80 znak├│w)."; return ""; }
        if (clean.Any(char.IsControl)) { error = "Nazwa sekretu nie mo┼╝e zawiera─ç znak├│w steruj─ůcych."; return ""; }
        return clean;
    }
}
