using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 2 · pozycje 46–120 — modele lokalne bez sieci. Ten moduł NIE pobiera modeli, NIE ładuje
/// ich i nie wysyła zapytań: liczy tylko na danych katalogowych, więc każdą liczbę da się sprawdzić
/// testem bez komputera z Ollamą. Wszystkie rozmiary są SZACUNKAMI i każda odpowiedź to mówi —
/// z podanym wzorem, żeby dało się ją policzyć ręcznie.
/// </summary>
public static class ModelToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private sealed record Entry(string Tag, double ParamsB, int Layers, int KvHeads, int HeadDim, int MaxContext, string Role, string Note);

    /// <summary>Katalog orientacyjny (wartości przybliżone, mogą się różnić między wersjami modeli).</summary>
    private static readonly Entry[] CatalogModels =
    [
        new("qwen3:1.7b", 1.7, 28, 4, 128, 32768, "szybkie", "domyślny model trybu gry (AiSettings.GamingModel)"),
        new("qwen3:4b-instruct", 4.0, 36, 8, 128, 32768, "rozmowa", "domyślny model pracy (AiSettings.IdleModel)"),
        new("gemma3:4b", 4.0, 34, 8, 256, 32768, "rozmowa", "domyślny model zapasowy (AiSettings.FallbackModel)"),
        new("gemma3:1b", 1.0, 26, 4, 256, 32768, "szybkie", "najmniejszy model rozmowy"),
        new("qwen2.5:0.5b", 0.5, 24, 2, 64, 32768, "szybkie", "start na słabym sprzęcie"),
        new("qwen2.5:1.5b", 1.5, 28, 2, 128, 32768, "szybkie", "tani model do narzędzi"),
        new("qwen2.5:3b", 3.0, 36, 2, 128, 32768, "szybkie", "kompromis szybkość/jakość"),
        new("qwen2.5:7b", 7.0, 28, 4, 128, 32768, "rozmowa i kod", "klasyk 7B, działa na 8 GB"),
        new("qwen2.5:14b", 14.0, 48, 8, 128, 32768, "jakość", "wyraźnie lepszy, potrzebuje 12–16 GB"),
        new("qwen2.5-coder:7b", 7.0, 28, 4, 128, 32768, "kod", "kod i refaktor w rozmiarze 7B"),
        new("llama3.2:3b", 3.0, 28, 8, 128, 131072, "rozmowa", "długi kontekst w małym rozmiarze"),
        new("llama3.1:8b", 8.0, 32, 8, 128, 131072, "rozmowa", "lubi 8–10 GB pamięci"),
        new("mistral:7b", 7.0, 32, 8, 128, 32768, "rozmowa", "stabilny, dobrze streszcza"),
        new("deepseek-r1:8b", 8.0, 32, 8, 128, 131072, "rozumowanie", "myśli na głos; wolniejszy, ale dokładniejszy w logice"),
        new("phi4:14b", 14.0, 40, 10, 128, 16384, "rozumowanie", "krótszy kontekst niż 14B Qwen"),
        new("llava:7b", 7.0, 32, 32, 128, 4096, "wizja", "obraz + tekst; dochodzi enkoder wizji (dodatkowa pamięć)"),
        new("nomic-embed-text", 0.137, 12, 12, 64, 8192, "embeddingi", "wektory do wyszukiwania treści, nie do rozmowy"),
    ];

    private static readonly (string Name, double BitsPerWeight, string Note)[] Quants =
    [
        ("q2_K", 2.6, "maksymalna oszczędność pamięci, wyraźna utrata jakości"),
        ("q3_K_M", 3.4, "mały rozmiar, nadal widać różnicę wobec q4"),
        ("q4_0", 4.5, "starszy wariant 4-bitowy"),
        ("q4_K_M", 4.85, "domyślny wybór: rozsądny rozmiar i jakość"),
        ("q5_K_M", 5.7, "trochę lepsza jakość, ~18% więcej pamięci niż q4"),
        ("q6_K", 6.6, "blisko oryginału, wyraźnie cięższy"),
        ("q8_0", 8.5, "prawie bezstratnie, ok. 1,75× więcej niż q4"),
        ("f16", 16.0, "pełna precyzja — dwa razy więcej pamięci niż q8"),
    ];

    /// <summary>Szacowany rozmiar wag: parametry × bity na wagę ÷ 8.</summary>
    public static double WeightsGb(double paramsB, string quantName)
    {
        double bits = 4.85;
        foreach (var quant in Quants)
            if (string.Equals(quant.Name, (quantName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) bits = quant.BitsPerWeight;
        return paramsB * bits / 8.0;
    }

    /// <summary>Szacowany KV cache: 2 (klucz i wartość) × warstwy × głowy KV × wymiar głowy × tokeny × 2 B.</summary>
    public static double KvCacheGb(int layers, int kvHeads, int headDim, int ctxTokens) =>
        2.0 * layers * kvHeads * headDim * ctxTokens * 2.0 / 1_000_000_000.0;

    private const double OverheadGb = 0.8;

    private static Entry? Find(string tag)
    {
        string wanted = (tag ?? "").Trim().Trim('"');
        if (wanted.Length == 0) return null;
        return CatalogModels.FirstOrDefault(x => string.Equals(x.Tag, wanted, StringComparison.OrdinalIgnoreCase))
            ?? CatalogModels.FirstOrDefault(x => x.Tag.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
            ?? CatalogModels.FirstOrDefault(x => x.Tag.Contains(wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string Gb(double value) => value.ToString("0.##", Pl) + " GB";

    public static string? TryHandle(string command, string text)
    {
        string raw = command ?? "";

        if (text is "modele lokalne" or "katalog modeli" or "katalog modeli lokalnych" or "katalog llm")
            return Catalog();
        var card = Regex.Match(text, @"^model karta[:\s]+(.+)$");
        if (card.Success) return Card(Payload(raw, "model karta"));
        var fit = Regex.Match(text, @"^model dopasuj[:\s]+(\d{1,3}(?:[.,]\d)?)\s*(?:gb)?$");
        if (fit.Success) return Fit(Parse(fit.Groups[1].Value));
        var role = Regex.Match(text, @"^(?:model rola|rola modelu)[:\s]+(.+)$");
        if (role.Success) return Role(Payload(raw, "model rola", "rola modelu"));
        if (text is "kwantyzacje" or "kwantyzacja" or "lista kwantyzacji") return QuantTable();
        // Tylko nazwa wariantu po słowie: „kwantyzacja jest trudna do zrozumienia” to zdanie, nie polecenie.
        var quant = Regex.Match(text, @"^kwantyzacja[:\s]+([a-z0-9_]{2,12})$");
        if (quant.Success) return Quant(Payload(raw, "kwantyzacja"));
        if (text is "presety modelu" or "parametry modelu" or "presety ai") return PresetTable();
        var preset = Regex.Match(text, @"^preset modelu[:\s]+(.+)$");
        if (preset.Success) return Preset(Payload(raw, "preset modelu"));
        if (text is "prompt szablony" or "szablony promptow") return Prompts();
        var prompt = Regex.Match(text, @"^prompt szablon[:\s]+(.+)$");
        if (prompt.Success) return Prompt(Payload(raw, "prompt szablon"));
        if (text is "model kolejka" or "model limity" or "polityka modelu") return Queue();
        if (text is "model polityka" or "model ustawienia domyslne") return Policy(new AiSettings());
        var kv = Regex.Match(text, @"^model kv[:\s]+(.+)$");
        if (kv.Success) return Kv(Payload(raw, "model kv"));
        var memory = Regex.Match(text, @"^model pamiec[:\s]+(\d{3,6})$");
        if (memory.Success) return ContextAdvice(int.Parse(memory.Groups[1].Value, CultureInfo.InvariantCulture));
        var compare = Regex.Match(text, @"^model porownaj[:\s]+(.+)$");
        if (compare.Success) return Compare(Payload(raw, "model porownaj"));
        if (text is "model offline" or "tryb bez modelu" or "bez modelu") return Offline();
        // 0.97 · SEKCJA 2 (drugi przyrost): dobór modelu do opisu zadania i licencje — dalej bez sieci.
        if (text is "model licencje" or "licencje modeli" or "model licencja") return Licenses();
        var recommend = Regex.Match(text, @"^(?:model do zadania|dobierz model)[:\s]+(.+)$");
        if (recommend.Success) return RecommendTask(Payload(raw, "model do zadania", "dobierz model"));
        return null;
    }

    /// <summary>Szacunek pobierania dla znanych modeli (domyślna kwantyzacja q4_K_M); null poza katalogiem —
    /// wtedy plan zgody mówi wprost, że rozmiaru nie znam. Używa go bramka „model pobierz”.</summary>
    public static string? DownloadEstimate(string model)
    {
        string tag = (model ?? "").Trim().ToLowerInvariant();
        Entry? found = CatalogModels.FirstOrDefault(x => x.Tag == tag);
        if (found is null) return null;
        double gb = WeightsGb(found.ParamsB, "q4_K_M");
        return found.Tag + ": szacowane pobieranie ~" + gb.ToString("0.0", Pl) + " GB wag w q4_K_M (dokładny postęp pokaże Ollama; rola: " + found.Role + ")";
    }

    /// <summary>„model do zadania: …” — rekomendacja z katalogu po słowach kluczowych opisu.
    /// To podpowiedź z danych katalogowych, nie benchmark na sprzęcie użytkownika.</summary>
    /// <summary>Do słów-kluczy: polskie znaki zdejmuję sam (Normalize nie gwarantuje ich utraty),
    /// więc „zdjęć” i „zdjec” to to samo polecenie. Echo opisu wraca oryginalne.</summary>
    private static string StripDiacritics(string input) => input
        .Replace("ą", "a").Replace("ć", "c").Replace("ę", "e").Replace("ł", "l")
        .Replace("ń", "n").Replace("ó", "o").Replace("ś", "s").Replace("ź", "z").Replace("ż", "z");

    private static string RecommendTask(string description)
    {
        string what = (description ?? "").Trim();
        if (what.Length == 0)
            return "Opisz zadanie, np. „model do zadania: pisanie kodu w C#” albo „model do zadania: streszczanie dokumentów”. Znam też: wizję/obrazy, embeddingi, rozumowanie, długi kontekst, słabszy sprzęt.";
        string lower = StripDiacritics(what.ToLowerInvariant());
        (string Tag, string Why) pick;
        string[] alternatives;
        if (lower.Contains("kod") || lower.Contains("programow") || lower.Contains("refaktor") || lower.Contains("bug") || lower.Contains("testy"))
        { pick = ("qwen2.5-coder:7b", "wytrenowany na kodzie — refaktor, wyjaśnianie i testy wychodzą z niego lepiej niż z modelu ogólnego"); alternatives = ["qwen2.5:7b", "qwen3:4b-instruct"]; }
        else if (lower.Contains("obraz") || lower.Contains("zdjec") || lower.Contains("wizja") || lower.Contains("ocr") || lower.Contains("fotograf"))
        { pick = ("llava:7b", "rozumie obraz + tekst; enkoder wizji dochodzi do wag, więc pamięci potrzebuje więcej niż sugeruje sam rozmiar"); alternatives = ["qwen2.5:7b (bez obrazów)", "llama3.2:3b (bez obrazów, lżejszy)"]; }
        else if (lower.Contains("wektor") || lower.Contains("embedding") || lower.Contains("semantyczn") || lower.Contains("wyszukiwan") && lower.Contains("dokument"))
        { pick = ("nomic-embed-text", "embeddingi do wyszukiwania semantycznego — to nie model rozmowy i nie próbuj nim gadać"); alternatives = ["qwen3:1.7b (rozmowa obok indeksu)"]; }
        else if (lower.Contains("rozumowan") || lower.Contains("logika") || lower.Contains("matematyk") || lower.Contains("dowod"))
        { pick = ("deepseek-r1:8b", "myśli krok po kroku na głos; wolniejszy, ale dokładniejszy w logice"); alternatives = ["qwen3:4b-instruct", "phi4:14b (potrzebuje 12+ GB)"]; }
        else if (lower.Contains("dlugi kontekst") || lower.Contains("dlugiego dokumentu") || lower.Contains("dlugi dokument") || lower.Contains("stron") || lower.Contains("pdf"))
        { pick = ("llama3.2:3b", "kontekst 128k w małym rozmiarze — całe dokumenty naraz"); alternatives = ["qwen3:4b-instruct (32k)", "llama3.1:8b"]; }
        else if (lower.Contains("szybko") || lower.Contains("slabym") || lower.Contains("laptop") || lower.Contains("stary komputer") || lower.Contains("malutko"))
        { pick = ("qwen3:1.7b", "najlżejszy model rozmowy — start na słabym sprzęcie, odpowiedzi natychmiastowe"); alternatives = ["qwen2.5:0.5b (minimum minimum)", "gemma3:1b"]; }
        else
        { pick = ("qwen3:4b-instruct", "brak mocnych sygnałów w opisie — domyślny model rozmowy; doprecyzuj zadanie, jeśli chodziło o coś innego"); alternatives = ["gemma3:4b", "mistral:7b"]; }
        double gb = WeightsGb(FindParams(pick.Tag), "q4_K_M");
        return "REKOMENDACJA DLA ZADANIA: „" + what + "”" + Environment.NewLine +
            "· wybór: " + pick.Tag + " — " + pick.Why + Environment.NewLine +
            "· pamięć: wagi w q4_K_M ~" + gb.ToString("0.0", Pl) + " GB + KV cache (zależny od kontekstu); dokładniej: „model karta: " + pick.Tag + "”" + Environment.NewLine +
            "· pasuje do Twojej maszyny? „model audyt” — liczę na podanym RAM, nie zgaduję" + Environment.NewLine +
            "· alternatywy: " + string.Join(" · ", alternatives) + Environment.NewLine +
            "· nie masz go zainstalowanego? Pobieranie wymaga zgody dwuetapowej: „model pobierz: " + pick.Tag + "”, potem dokładnie „… potwierdzam” · licencje: „model licencje”" + Environment.NewLine +
            "· to podpowiedź z katalogu, nie benchmark na Twoim sprzęcie";
    }

    private static double FindParams(string tag)
    {
        Entry? found = CatalogModels.FirstOrDefault(x => x.Tag == tag);
        return found?.ParamsB ?? 4.0;
    }

    /// <summary>„model licencje” — rodziny z katalogu i licencje, pod którymi znaleźły się w moich danych.
    /// Uczciwie: stan może się zmieniać, a Sentinel nie jest prawnikiem i nie ściąga niczego z sieci.</summary>
    private static string Licenses() =>
        "LICENCJE MODELI Z KATALOGU (stan, który znam — licencje zmieniają się między wersjami):" + Environment.NewLine +
        "· Apache 2.0 (użycie komercyjne swobodne): qwen3, qwen2.5 (też qwen2.5-coder), mistral, llava, nomic-embed-text" + Environment.NewLine +
        "· MIT: deepseek-r1, phi4" + Environment.NewLine +
        "· Llama Community License (dodatkowe warunki): llama3.1, llama3.2" + Environment.NewLine +
        "· Gemma Terms of Use (ograniczenia własne Google): gemma3 (1b i 4b)" + Environment.NewLine +
        "· Przed użyciem komercyjnym sprawdź kartę licencji u źródła: Sentinel niczego nie ściąga z sieci i nie jest prawnikiem." + Environment.NewLine +
        "· Karta konkretnego modelu: „model karta: <nazwa>” · co masz zainstalowane: „modele ai”.";

    private static string Payload(string raw, params string[] prefixes)
    {
        string text = (raw ?? "").Trim();
        foreach (string prefix in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = text[prefix.Length..].TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..];
            return rest.Trim();
        }
        return text;
    }

    private static double Parse(string value) =>
        double.TryParse((value ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;

    public static string Catalog()
    {
        var lines = CatalogModels.Select(x => "· " + x.Tag.PadRight(20) + " " + Gb(x.ParamsB) + " · " + x.Role + " · " + x.Note);
        return "Katalog modeli lokalnych (dane orientacyjne wpisane w kodzie — nic nie pobieram i nie sprawdzam, co masz zainstalowane):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· Co masz naprawdę: „modele ai”. Karta modelu: „model karta: qwen2.5:7b”. Dobór do pamięci: „model dopasuj: 8”. Katalog może być nieaktualny wobec Twojej Ollamy i mówię to wprost.";
    }

    public static string Card(string tag)
    {
        var model = Find(tag);
        if (model == null) return Unknown(tag);
        int ctx = model.MaxContext >= 8192 ? 8192 : model.MaxContext;
        double weights = WeightsGb(model.ParamsB, "q4_K_M");
        double kv = KvCacheGb(model.Layers, model.KvHeads, model.HeadDim, ctx);
        return "Karta modelu „" + model.Tag + "” (szacunek, nie pomiar):" + Environment.NewLine +
            "· parametry: " + model.ParamsB.ToString("0.#", Pl) + " mld · warstwy: " + model.Layers + " · głowy KV: " + model.KvHeads + " · wymiar głowy: " + model.HeadDim + Environment.NewLine +
            "· największy kontekst w katalogu: " + model.MaxContext.ToString("N0", Pl) + " tokenów" + Environment.NewLine +
            "· rola: " + model.Role + " · " + model.Note + Environment.NewLine +
            "· wagi w q4_K_M (4,85 bitu na wagę): ~" + Gb(weights) + Environment.NewLine +
            "· KV cache przy " + ctx.ToString("N0", Pl) + " tokenach: ~" + Gb(kv) + " (2 × warstwy × głowy KV × wymiar × tokeny × 2 B)" + Environment.NewLine +
            "· razem z narzutem " + Gb(OverheadGb) + ": ~" + Gb(weights + kv + OverheadGb) + Environment.NewLine +
            "· To wyliczenie z danych katalogowych. Realny rozmiar zależy od wersji pliku i sterownika — porównaj z „modele ai”.";
    }

    private static string Unknown(string tag) =>
        "Nie mam w katalogu modelu „" + (tag ?? "").Trim() + "”. Katalog jest orientacyjny — wpisz „modele lokalne”, żeby zobaczyć, co znam, a „modele ai”, żeby sprawdzić, co masz zainstalowane w Ollamie. Nie zgaduję rozmiaru nieznanego modelu.";

    public static string Fit(double budgetGb)
    {
        if (budgetGb <= 0) return "Podaj pamięć, np. „model dopasuj: 8” (w GB).";
        if (budgetGb > 512) return "To więcej niż jakikolwiek komputer — podaj realną wartość, np. „model dopasuj: 16”.";
        double comfortable = budgetGb * 0.85;
        var fits = new List<string>();
        var tight = new List<string>();
        var no = new List<string>();
        foreach (var model in CatalogModels)
        {
            double total = WeightsGb(model.ParamsB, "q4_K_M") + KvCacheGb(model.Layers, model.KvHeads, model.HeadDim, Math.Min(model.MaxContext, 8192)) + OverheadGb;
            string entry = model.Tag + " (~" + Gb(total) + ")";
            if (total <= comfortable) fits.Add(entry);
            else if (total <= budgetGb) tight.Add(entry);
            else no.Add(entry);
        }
        return "Dobór modeli do ~" + Gb(budgetGb) + " (q4_K_M, kontekst 8 192, narzut " + Gb(OverheadGb) + " — szacunek):" + Environment.NewLine +
            "· zmieści się spokojnie: " + (fits.Count > 0 ? string.Join(" · ", fits) : "nic z katalogu") + Environment.NewLine +
            "· na styk (ponad 85% pamięci): " + (tight.Count > 0 ? string.Join(" · ", tight) : "brak") + Environment.NewLine +
            "· nie zmieści się: " + (no.Count > 0 ? string.Join(" · ", no) : "brak") + Environment.NewLine +
            "· Zostaw 1–2 GB dla systemu i przeglądarki — Ollama zwalnia pamięć dopiero po wyładowaniu modelu („model kolejka”).";
    }

    public static string FitForMachine(double totalRamGb)
    {
        if (totalRamGb <= 0) return "Nie odczytałem ilości pamięci — wpisz samodzielnie: „model dopasuj: 8”.";
        double budget = Math.Max(1.0, totalRamGb - 2.0);
        return "Twój komputer ma ~" + Gb(totalRamGb) + " RAM. Po odjęciu 2 GB dla systemu zostaje ~" + Gb(budget) + " na model (szacunek — karta graficzna może przejąć część, ale nie zakładam, że masz GPU)." + Environment.NewLine +
            Fit(budget);
    }

    public static string Role(string role)
    {
        string wanted = (role ?? "").Trim().ToLowerInvariant();
        string[] tags;
        if (wanted.Contains("kod")) tags = ["qwen2.5-coder:7b", "qwen2.5:7b"];
        else if (wanted.Contains("rozmow") || wanted.Contains("chat")) tags = ["qwen3:4b-instruct", "gemma3:4b", "qwen2.5:7b"];
        else if (wanted.Contains("szyb") || wanted.Contains("slab")) tags = ["qwen3:1.7b", "gemma3:1b", "qwen2.5:1.5b"];
        else if (wanted.Contains("wizj") || wanted.Contains("obraz")) tags = ["llava:7b"];
        else if (wanted.Contains("embed") || wanted.Contains("wektor") || wanted.Contains("szukanie")) tags = ["nomic-embed-text"];
        else if (wanted.Contains("rozum") || wanted.Contains("logik") || wanted.Contains("matemat")) tags = ["deepseek-r1:8b", "phi4:14b"];
        else if (wanted.Contains("streszcz") || wanted.Contains("podsum") || wanted.Contains("dokument")) tags = ["mistral:7b", "qwen2.5:14b"];
        else tags = [];
        if (tags.Length == 0)
            return "Nie znam roli „" + (role ?? "").Trim() + "”. Znam: kod · rozmowa · szybkie · wizja · embeddingi · rozumowanie · streszczenia. Nic nie zgaduję — wybierz jedną z tych ról.";
        var lines = new List<string>();
        foreach (string tag in tags)
        {
            var model = Find(tag);
            if (model == null) continue;
            double total = WeightsGb(model.ParamsB, "q4_K_M") + KvCacheGb(model.Layers, model.KvHeads, model.HeadDim, 8192) + OverheadGb;
            lines.Add("· " + model.Tag + " — " + model.Note + " (~" + Gb(total) + " przy 8 192 tokenach)");
        }
        return "Rola „" + wanted + "” — propozycje z katalogu (kolejność = moja preferencja):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· To podpowiedź z danych katalogowych, nie test na Twoim sprzęcie. Wybór modelu zostaje przy Tobie: „ustaw model AI <nazwa>”.";
    }

    public static string QuantTable() =>
        "Kwantyzacje (bity na wagę → rozmiar wag; 7B w f16 to 14 GB, w q4_K_M ~4,2 GB):" + Environment.NewLine +
        string.Join(Environment.NewLine, Quants.Select(x => "· " + x.Name.PadRight(7) + " ~" + x.BitsPerWeight.ToString("0.##", Pl) + " bitu/wagę · " + x.Note)) + Environment.NewLine +
        "· Zasada: q4_K_M to domyślny kompromis; q5/q6 wybieraj, gdy masz zapas pamięci, q2/q3 tylko na słabym sprzęcie.";

    public static string Quant(string name)
    {
        string wanted = (name ?? "").Trim();
        foreach (var quant in Quants)
        {
            if (!string.Equals(quant.Name, wanted, StringComparison.OrdinalIgnoreCase)) continue;
            double size = WeightsGb(7.0, quant.Name);
            return "Kwantyzacja „" + quant.Name + "”:" + Environment.NewLine +
                "· " + quant.BitsPerWeight.ToString("0.##", Pl) + " bitu na wagę — " + quant.Note + Environment.NewLine +
                "· model 7B: ~" + Gb(size) + " wag (f16 dla porównania: " + Gb(WeightsGb(7.0, "f16")) + ")" + Environment.NewLine +
                "· Mniejsza kwantyzacja = mniej pamięci i zwykle niższa jakość; to nie jest darmowe.";
        }
        return "Nie znam kwantyzacji „" + wanted + "”. Wpisz „kwantyzacje”, żeby zobaczyć listę, którą znam. Nie zgaduję rozmiaru.";
    }

    public static string PresetTable() =>
        "Presety parametrów modelu (do wpisania w Ollamie albo w module, który je czyta — Sentinel ich nie wymusza):" + Environment.NewLine +
        "· szybki — temperatura 0,20 · top_p 0,85 · num_ctx 4096 · num_predict 400 · keep_alive 5m" + Environment.NewLine +
        "· zbalansowany — temperatura 0,40 · top_p 0,90 · num_ctx 8192 · num_predict 700 · keep_alive 10m" + Environment.NewLine +
        "· precyzyjny — temperatura 0,10 · top_p 0,95 · num_ctx 16384 · num_predict 1200 · keep_alive 10m" + Environment.NewLine +
        "· Szczegóły: „preset modelu: zbalansowany”. Domyślne wartości Sentinela: „model polityka”.";

    public static string Preset(string name)
    {
        string wanted = (name ?? "").Trim().ToLowerInvariant();
        string label = "", temperature = "", topP = "", keep = "";
        int ctx = 0, predict = 0;
        if (wanted.Contains("szyb")) { label = "szybki"; temperature = "0,20"; topP = "0,85"; ctx = 4096; predict = 400; keep = "5m"; }
        else if (wanted.Contains("zbalans") || wanted.Contains("normal")) { label = "zbalansowany"; temperature = "0,40"; topP = "0,90"; ctx = 8192; predict = 700; keep = "10m"; }
        else if (wanted.Contains("precyz") || wanted.Contains("doklad")) { label = "precyzyjny"; temperature = "0,10"; topP = "0,95"; ctx = 16384; predict = 1200; keep = "10m"; }
        if (label.Length == 0)
            return "Nie znam presetu „" + (name ?? "").Trim() + "”. Znam trzy: szybki · zbalansowany · precyzyjny („presety modelu”).";
        return "Preset „" + label + "”:" + Environment.NewLine +
            "· temperatura: " + temperature + " (niżej = bardziej powtarzalnie, wyżej = więcej swobody)" + Environment.NewLine +
            "· top_p: " + topP + " · num_ctx: " + ctx.ToString("N0", Pl) + " tokenów · num_predict: " + predict.ToString("N0", Pl) + " · keep_alive: " + keep + Environment.NewLine +
            "· Większy num_ctx to większy KV cache — sprawdź „model kv: qwen2.5:7b " + ctx + "”." + Environment.NewLine +
            "· To gotowy zestaw wartości do wykorzystania; Sentinel nie zmienia ustawień Ollamy bez Twojej wiedzy.";
    }

    public static string Prompts() =>
        "Szablony promptów (tekst do wklejenia; nic nie wysyłam):" + Environment.NewLine +
        string.Join(Environment.NewLine, PromptNames.Select(x => "· " + x.Key.PadRight(14) + " " + x.Value)) + Environment.NewLine +
        "· Pełny tekst: „prompt szablon: kod”. Szablon to początek rozmowy, nie polecenie Sentinela.";

    private static readonly (string Key, string Value)[] PromptNames =
    [
        ("kod", "przegląd kodu: ryzyka, brakujące przypadki, prostsza wersja"),
        ("testy", "wypisz testy brzegowe dla podanego kodu"),
        ("dokumentacja", "README/komentarz bez marketingowego lania wody"),
        ("tlumaczenie", "tłumaczenie PL↔EN z zachowaniem terminów technicznych"),
        ("streszczenie", "streszczenie tekstu z listą decyzji i pytań otwartych"),
        ("ekstrakcja", "wyciągnięcie danych do tabeli JSON o podanym schemacie"),
        ("sql", "zapytanie SQL z wyjaśnieniem i ostrzeżeniem o indeksach"),
        ("refaktor", "refaktor bez zmiany zachowania, krok po kroku"),
        ("blad", "analiza błędu: objaw, hipotezy, test rozstrzygający"),
        ("email", "krótka, rzeczowa wiadomość w języku polskim"),
    ];

    public static string Prompt(string name)
    {
        string wanted = (name ?? "").Trim().ToLowerInvariant();
        string? key = PromptNames.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, wanted, StringComparison.OrdinalIgnoreCase));
        if (key == null)
            key = PromptNames.Select(x => x.Key).FirstOrDefault(x => wanted.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (key == null)
            return "Nie znam szablonu „" + (name ?? "").Trim() + "”. Wpisz „prompt szablony”, żeby zobaczyć listę. Nie zgaduję treści promptu.";
        return "Szablon „" + key + "” — wklej jako wiadomość systemową:" + Environment.NewLine + PromptText(key) + Environment.NewLine +
            "· To zwykły tekst. Sentinel nie wysyła go nigdzie i nie zmienia Twoich ustawień modelu.";
    }

    private static string PromptText(string key) => key switch
    {
        "kod" => "Jesteś recenzentem kodu. Dla podanego fragmentu wypisz: (1) ryzyka błędów, (2) brakujące przypadki brzegowe, (3) prostszą wersję tego samego rozwiązania. Nie chwal, nie streszczaj — podawaj konkret z odwołaniem do linii.",
        "testy" => "Wypisz testy dla podanego kodu: przypadki brzegowe, wartości skrajne, dane, które psują założenia. Każdy test w formie: nazwa, wejście, oczekiwany wynik. Zaznacz, których przypadków nie da się sprawdzić bez środowiska docelowego.",
        "dokumentacja" => "Napisz dokumentację podanego kodu: co robi, jak używać, jakie ma ograniczenia. Bez marketingu i bez powtarzania nazw metod.",
        "tlumaczenie" => "Przetłumacz podany tekst, zachowując terminy techniczne i nazwy własne. Jeśli termin nie ma dobrego odpowiednika, zostaw oryginał w nawiasie. Podaj tylko tłumaczenie.",
        "streszczenie" => "Streść podany tekst rzeczowo: najważniejsze tezy, podjęte decyzje i pytania otwarte. Bez wstępu i bez oceny.",
        "ekstrakcja" => "Wyciągnij z podanego tekstu dane w formacie JSON zgodnym ze schematem, który podam. Brakujące pola ustaw na null i wypisz listę braków. Nie dodawaj pól spoza schematu.",
        "sql" => "Napisz zapytanie SQL do podanego zadania. Wyjaśnij je po polsku, wskaż, jakie indeksy są potrzebne, i ostrzeż, jeśli zapytanie może czytać całe tabele.",
        "refaktor" => "Zaproponuj refaktor podanego kodu bez zmiany zachowania. Podaj kroki od najmniejszego ryzyka, a przy każdym kroku napisz, jak sprawdzić, że nic się nie zepsuło.",
        "blad" => "Pomóż zdiagnozować błąd. Podaj: objaw, hipotezy w kolejności prawdopodobieństwa, test rozstrzygający dla każdej hipotezy. Nie podawaj poprawki, dopóki przyczyna nie jest potwierdzona.",
        "email" => "Napisz krótką, rzeczową wiadomość po polsku: konkret w pierwszym zdaniu, potem co potrzebuję od odbiorcy i do kiedy. Bez formułek otwierających i bez emoji.",
        _ => "",
    };

    public static string Policy(AiSettings settings)
    {
        int ram = settings.RamPressurePercent, cpu = settings.CpuPressurePercent, gpu = settings.GpuPressurePercent;
        return "Domyślne ustawienia modelu w Sentinelu (AiSettings — panel Ustawienia → ✨ AI pokazuje Twoje):" + Environment.NewLine +
            "· tryb gry: " + settings.GamingModel + " · tryb pracy: " + settings.IdleModel + " · zapasowy: " + settings.FallbackModel + Environment.NewLine +
            "· temperatura: " + settings.Temperature.ToString("0.##", Pl) + " · kontekst: " + settings.MaxContextTokens.ToString("N0", Pl) + " tokenów · limit odpowiedzi: " + settings.MaxResponseTokens.ToString("N0", Pl) + " tokenów" + Environment.NewLine +
            "· próg przejścia na model szybki: RAM " + ram + "% · CPU " + cpu + "% · GPU " + gpu + "%" + Environment.NewLine +
            "· Zmiana: „ustaw model AI <nazwa>” albo panel AI. Wartości są sprawdzane przy zapisie, więc zły zakres nie wejdzie po cichu.";
    }

    public static string Queue() =>
        "Jak Sentinel używa modelu (stan faktyczny, bez obiecywania):" + Environment.NewLine +
        "· jedna ścieżka wykonania (ActionEngine) — jedno zapytanie do modelu naraz; kolejne zadanie czeka, a nie konkuruje" + Environment.NewLine +
        "· limit czasu odpowiedzi: 180 s, potem uczciwy komunikat zamiast wiszącego okna" + Environment.NewLine +
        "· „przerwij” przerywa generowanie; to, co już przyszło, zostaje oznaczone jako urwane w połowie" + Environment.NewLine +
        "· przy błędzie modelu Sentinel próbuje modelu zapasowego i mówi, którego użył (maksymalnie dwie próby, bez pętli)" + Environment.NewLine +
        "· keep_alive: 10m w normalnej pracy, 0 w trybie gry (model zwalnia pamięć) — stąd różnica w pierwszej odpowiedzi" + Environment.NewLine +
        "· Ollama jest lokalna (127.0.0.1:11434). Sentinel nie wysyła niczego na zewnątrz i nie pobiera modeli bez Twojej decyzji.";

    public static string Kv(string argument)
    {
        string[] parts = (argument ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "Użyj: „model kv: qwen2.5:7b 8192” (model i kontekst w tokenach).";
        var model = Find(parts[0]);
        if (model == null) return Unknown(parts[0]);
        int ctx = 8192;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out ctx) || ctx < 128 || ctx > 1_000_000))
            return "Kontekst musi być liczbą tokenów, np. „model kv: " + model.Tag + " 8192”.";
        double kv = KvCacheGb(model.Layers, model.KvHeads, model.HeadDim, ctx);
        double weights = WeightsGb(model.ParamsB, "q4_K_M");
        return "KV cache dla „" + model.Tag + "” przy " + ctx.ToString("N0", Pl) + " tokenach:" + Environment.NewLine +
            "· ~" + Gb(kv) + " (2 × " + model.Layers + " warstw × " + model.KvHeads + " głów KV × " + model.HeadDim + " wymiaru × " + ctx.ToString("N0", Pl) + " tokenów × 2 B)" + Environment.NewLine +
            "· wagi q4_K_M: ~" + Gb(weights) + " · razem z narzutem: ~" + Gb(weights + kv + OverheadGb) + Environment.NewLine +
            "· Podwojenie kontekstu podwaja KV cache — dlatego „num_ctx 16384” nie jest darmowe.";
    }

    public static string ContextAdvice(int ctxTokens)
    {
        if (ctxTokens < 512) return "Kontekst mniejszy niż 512 tokenów to za mało na rozmowę — podaj np. „model pamiec: 8192”.";
        var lines = new List<string>();
        foreach (string tag in new[] { "qwen3:4b-instruct", "qwen2.5:7b", "qwen2.5:14b" })
        {
            var model = Find(tag);
            if (model == null) continue;
            double kv = KvCacheGb(model.Layers, model.KvHeads, model.HeadDim, ctxTokens);
            lines.Add("· " + model.Tag + ": KV cache ~" + Gb(kv) + ", razem z wagami ~" + Gb(WeightsGb(model.ParamsB, "q4_K_M") + kv + OverheadGb));
        }
        return "Pamięć przy kontekście " + ctxTokens.ToString("N0", Pl) + " tokenów (szacunek):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· Domyślny kontekst Sentinela to 4 096 tokenów, limit odpowiedzi 700 („model polityka”)." + Environment.NewLine +
            "· Większy kontekst = lepsza pamięć rozmowy, ale KV cache rośnie liniowo i zwalnia odpowiedź.";
    }

    public static string Compare(string argument)
    {
        string[] parts = Regex.Split(argument ?? "", @"\s+(?:vs|versus|i)\s+|\|");
        if (parts.Length < 2) return "Użyj: „model porownaj: qwen2.5:7b vs qwen2.5:14b”.";
        var left = Find(parts[0]);
        var right = Find(parts[1]);
        if (left == null) return Unknown(parts[0]);
        if (right == null) return Unknown(parts[1]);
        if (string.Equals(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase)) return "To ten sam model — porównanie nie ma sensu.";
        double leftTotal = WeightsGb(left.ParamsB, "q4_K_M") + KvCacheGb(left.Layers, left.KvHeads, left.HeadDim, 8192) + OverheadGb;
        double rightTotal = WeightsGb(right.ParamsB, "q4_K_M") + KvCacheGb(right.Layers, right.KvHeads, right.HeadDim, 8192) + OverheadGb;
        Entry lighter = leftTotal <= rightTotal ? left : right;
        return "Porównanie „" + left.Tag + "” vs „" + right.Tag + "” (q4_K_M, 8 192 tokenów, szacunek):" + Environment.NewLine +
            "· " + left.Tag + ": " + left.ParamsB.ToString("0.#", Pl) + " mld · " + left.Role + " · ~" + Gb(leftTotal) + " · " + left.Note + Environment.NewLine +
            "· " + right.Tag + ": " + right.ParamsB.ToString("0.#", Pl) + " mld · " + right.Role + " · ~" + Gb(rightTotal) + " · " + right.Note + Environment.NewLine +
            "· Lżejszy i zwykle szybszy: " + lighter.Tag + " (~" + Gb(Math.Abs(leftTotal - rightTotal)) + " różnicy)." + Environment.NewLine +
            "· Rozmiaru nie da się przeliczyć na jakość — tego nie zmierzę bez uruchomienia obu modeli na Twoim sprzęcie.";
    }

    public static string Offline() =>
        "Tryb bez modelu (Ollama wyłączona albo brak modelu) — to działa dalej, lokalnie:" + Environment.NewLine +
        "· narzędzia offline: matematyka i procenty, daty i kalendarz, tekst (słowa/znaki/palindromy/morse), kody PL (PESEL/NIP/IBAN), finanse (VAT, zniżka, raty), losowanie, hasła" + Environment.NewLine +
        "· narzędzia deweloperskie: diff, regex, semver, IP/podsieci, JWT, UUID/ULID, generatory SQL i C#, kodowania i skróty, budżet kontekstu" + Environment.NewLine +
        "· rdzeń: kolejka i cron, kopie z manifestem, integralność plików, sejf, dziennik zdarzeń, metryki, flagi" + Environment.NewLine +
        "· pamięć i historia rozmów, zadania, projekty, snapshoty diagnostyki, archiwum, wyszukiwanie tekstowe" + Environment.NewLine +
        "· Jarvis: okna, głośność i multimedia, zasilanie, schowek, pliki, timer i budzik, rutyny, briefing „dzień dobry”" + Environment.NewLine +
        "· Bez modelu NIE MA: swobodnej rozmowy, streszczeń AI, tłumaczeń i pisania tekstów. Wtedy mówię wprost, że Ollama nie odpowiada — nie udaję odpowiedzi.";
}
