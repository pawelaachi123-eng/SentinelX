using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.98 · WiFi i szukanie w sieci — bez prawdziwego internetu: handler zwraca przykładowy
/// HTML DuckDuckGo Lite. Sprawdzam: domyślne WYŁĄCZONE (żadne zapytanie nie wychodzi bez zgody),
/// parsowanie wyników (tytuł, zdekodowany adres, fragment), tarczę SSRF (localhost i http zawsze
/// odrzucone), trwałość wyłącznika i routing przez realny router.</summary>
internal static class WebAccessRegression
{
    /// <summary>Miniatura HTML DuckDuckGo Lite z trzema wynikami (drugi to przekierowanie uddg=).</summary>
    internal const string SampleLiteHtml =
        "<html><body><table>" +
        "<tr><td>1.&nbsp;</td><td><a rel=\"nofollow\" href=\"//duckduckgo.com/l/?uddg=https%3A%2F%2Fcreate.roblox.com%2Fdocs%2Fdatastore&amp;rut=aaa\" class='result-link'>DataStore Guide</a></td></tr>" +
        "<tr><td></td><td class='result-snippet'>Zapisuj <b>dane</b> z pcall i BindToClose.</td></tr>" +
        "<tr><td>2.&nbsp;</td><td><a rel=\"nofollow\" href=\"https://devforum.roblox.com/t/perf\" class='result-link'>Performance tips</a></td></tr>" +
        "<tr><td></td><td class='result-snippet'>Use StreamingEnabled on big maps.</td></tr>" +
        "<tr><td>3.&nbsp;</td><td><a rel=\"nofollow\" href=\"/internal/link\" class='result-link'>Internal anchor</a></td></tr>" +
        "</table></body></html>";

    internal sealed class FakeWebHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "lite.duckduckgo.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleLiteHtml, Encoding.UTF8, "text/html") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string settings = Path.Combine(directory, "settings");

        // ————— domyślnie WYŁĄCZONE: nic nie wychodzi bez zgody —————
        var offline = new WebAccessService(new FakeWebHandler(), settingsDirectory: settings);
        Check(!offline.Enabled, "domyślnie wyłączone");
        Check((await offline.SearchAsync("test")).Contains("WYŁĄCZONE") && (await offline.SearchAsync("test")).Contains("wifi on"),
            "szukanie przy wyłączonym WiFi odmawia i wskazuje „wifi on”");
        Check((await offline.FetchTextAsync("https://example.com/x")).Contains("WYŁĄCZONE"), "pobieranie strony też odmawia");
        Check(offline.Status().Contains("📶"), "status pokazuje ikonę 📶");

        // ————— włączenie: parsowanie wyników —————
        string toggled = offline.Toggle(true);
        Check(toggled.Contains("WŁĄCZONE"), "włączenie zwraca status WŁĄCZONE");
        var fresh = new WebAccessService(new FakeWebHandler(), settingsDirectory: settings);
        Check(fresh.Enabled, "stan wyłącznika przeżył restart (zapis w ustawieniach)");

        string results = await fresh.SearchAsync("roblox datastore");
        Check(results.Contains("WYNIKI SZUKANIA"), "szukanie zwraca wyniki: " + results.Split('\n')[0]);
        Check(results.Contains("https://create.roblox.com/docs/datastore"), "przekierowanie uddg zdekodowane do prawdziwego adresu");
        Check(results.Contains("DataStore Guide") && results.Contains("Performance tips"), "tytuły sparsowane");
        Check(results.Contains("Zapisuj dane z pcall"), "fragment bez tagów HTML");
        Check(!results.Contains("/internal/link"), "adresy wewnętrzne wyszukiwarki pominięte");

        // ————— tarcza SSRF i https-only —————
        string local = await fresh.FetchTextAsync("https://127.0.0.1/secret");
        Check(local.Contains("zablokowane") || local.Contains("SSRF"), "localhost zablokowany nawet przy włączonym WiFi");
        Check(fresh.IsPrivateHost("localhost") && fresh.IsPrivateHost("10.0.0.1") && fresh.IsPrivateHost("192.168.1.5"),
            "sieci prywatne klasyfikowane");
        Check(!fresh.IsPrivateHost("create.roblox.com"), "publiczna domena przepuszczona");
        string insecure = await fresh.FetchTextAsync("http://example.com");
        Check(insecure.Contains("https"), "http odrzucony — tylko https");

        // ————— routing przez realny router —————
        var router = new CommandRouter(new SystemMonitor(), new SystemInfoService(),
            new LocalAiService(new GamingModeService(), settingsDirectory: Path.Combine(directory, "ai")),
            new ConversationMemoryService(Path.Combine(directory, "memory")), web: fresh);
        string statusViaRouter = await router.ProcessAsync("wifi");
        Check(statusViaRouter.Contains("WŁĄCZONE"), "„wifi” przez router pokazuje stan");
        string turnedOff = await router.ProcessAsync("wifi off");
        Check(turnedOff.Contains("WYŁĄCZONE"), "„wifi off” wyłącza");
        string refused = await router.ProcessAsync("szukaj w sieci: cokolwiek");
        Check(refused.Contains("WYŁĄCZONE"), "szukanie przez router szanuje wyłącznik: " + refused.Split('\n')[0]);
        await router.ProcessAsync("wifi on");
        string back = await router.ProcessAsync("szukaj w sieci: roblox");
        Check(back.Contains("WYNIKI SZUKANIA"), "po „wifi on” szukanie przez router działa");
        string off2 = await router.ProcessAsync("wifi off"); // posprzątaj: shared? nie — instancja testowa
        Check(off2.Contains("WYŁĄCZONE"), "wyłączam po teście (czystość stanu testowego)");

        File.WriteAllText(Path.Combine(directory, "web-access.txt"),
            "PASS\nwifi default OFF, toggle persistence, DDG-lite parse (title/url/snippet), uddg decode, SSRF guard, https-only, router wiring verified\n");
    }
}
