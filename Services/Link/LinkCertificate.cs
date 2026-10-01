using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace SentinelX.Services.Link;

/// <summary>Self-signed certificate created once per PC. The phone pins its SHA-256 fingerprint, so no certificate authority
/// is involved. The private key is stored only in %LOCALAPPDATA%\SentinelX\Link, wrapped with DPAPI of the current Windows user.</summary>
public sealed class LinkCertificate : IDisposable
{
    private sealed class Stored
    {
        public Stored() { }
        public string Pfx { get; set; } = "";
        public string Password { get; set; } = "";
    }

    private readonly string path;
    private bool disposed;

    public X509Certificate2 Certificate { get; }
    public byte[] FingerprintBytes { get; }
    public string Fingerprint { get; }

    public LinkCertificate(string? directory = null)
    {
        string dir = directory ?? Path.Combine(AppPaths.Root, "Link");
        path = Path.Combine(dir, "cert.dpapi");
        Certificate = LoadOrCreate(dir);
        FingerprintBytes = SHA256.HashData(Certificate.RawData);
        Fingerprint = Convert.ToHexString(FingerprintBytes).ToLowerInvariant();
    }

    private X509Certificate2 LoadOrCreate(string dir)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 128 * 1024)
            {
                byte[] wrapped = File.ReadAllBytes(path);
                byte[] clear = LinkProtection.Unprotect(wrapped);
                Stored? stored = JsonSerializer.Deserialize<Stored>(clear);
                CryptographicOperations.ZeroMemory(clear);
                if (stored != null && stored.Pfx.Length > 0)
                {
                    X509Certificate2 loaded = Import(Convert.FromBase64String(stored.Pfx), stored.Password);
                    if (loaded.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30)) return loaded;
                    loaded.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException or PlatformNotSupportedException)
        { /* unreadable or foreign file: a new certificate is created below */ }
        return CreateAndSave(dir);
    }

    private X509Certificate2 CreateAndSave(string dir)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=SentinelX Link", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("sentinelx.local");
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        byte[] pfx = created.Export(X509ContentType.Pfx, password);
        try { Save(dir, pfx, password); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
        { AppLog.Write(ex); } // still usable for this run; the phone will have to trust the next certificate again
        // Windows SChannel cannot use the ephemeral key of CreateSelfSigned directly; the PFX round trip gives it a usable key.
        return Import(pfx, password);
    }

    private void Save(string dir, byte[] pfx, string password)
    {
        Directory.CreateDirectory(dir);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(new Stored { Pfx = Convert.ToBase64String(pfx), Password = password });
        byte[] wrapped = LinkProtection.Protect(clear);
        CryptographicOperations.ZeroMemory(clear);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, wrapped);
        File.Move(temp, path, true);
    }

#pragma warning disable SYSLIB0057 // the loader API is preferred on .NET 9+, but this constructor is the one compiled and proven in this repo
    private static X509Certificate2 Import(byte[] pfx, string password) => new(pfx, password, X509KeyStorageFlags.DefaultKeySet);
#pragma warning restore SYSLIB0057

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Certificate.Dispose();
    }
}

/// <summary>DPAPI of the current Windows user: only this user on this PC can unwrap the private key.</summary>
internal static class LinkProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private const uint UiForbidden = 0x1;

    public static byte[] Protect(byte[] clear) => Transform(clear, protect: true);
    public static byte[] Unprotect(byte[] wrapped) => Transform(wrapped, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI wymaga Windows.");
        GCHandle pin = GCHandle.Alloc(input, GCHandleType.Pinned);
        var source = new DataBlob { Length = input.Length, Data = pin.AddrOfPinnedObject() };
        DataBlob output = default;
        IntPtr description = IntPtr.Zero;
        try
        {
            bool ok = protect
                ? CryptProtectData(ref source, "SentinelX Link", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref source, out description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            pin.Free();
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            if (description != IntPtr.Zero) LocalFree(description);
        }
    }
}
