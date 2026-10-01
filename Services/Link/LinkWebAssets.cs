using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace SentinelX.Services.Link;

internal sealed record LinkAsset(byte[] Bytes, string ContentType, string ETag);

/// <summary>The phone UI ships inside SentinelX.exe as embedded resources (Phone/web/*): nothing to install or update separately.</summary>
internal static class LinkWebAssets
{
    private const string Prefix = "phone/";
    private static readonly Lazy<IReadOnlyDictionary<string, LinkAsset>> Assets = new(Load);

    public static int Count => Assets.Value.Count;

    public static LinkAsset? Find(string path)
    {
        if (path == "/" || path.Length == 0) path = "/index.html";
        return Assets.Value.TryGetValue(path, out LinkAsset? asset) ? asset : null;
    }

    private static IReadOnlyDictionary<string, LinkAsset> Load()
    {
        var result = new Dictionary<string, LinkAsset>(StringComparer.Ordinal);
        Assembly assembly = typeof(LinkWebAssets).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            byte[] bytes = memory.ToArray();
            string file = name[Prefix.Length..];
            string tag = "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant() + "\"";
            result["/" + file] = new LinkAsset(bytes, ContentTypeOf(file), tag);
        }
        return result;
    }

    private static string ContentTypeOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".webmanifest" => "application/manifest+json",
        ".json" => "application/json; charset=utf-8",
        _ => "application/octet-stream"
    };
}
