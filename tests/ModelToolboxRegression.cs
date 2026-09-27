using System;
using System.Linq;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 2 (pierwszy przyrost) — modele lokalne bez sieci i bez Ollamy.
/// Wszystkie liczby są funkcjami czystymi z danych katalogowych, więc test jest deterministyczny:
/// nie potrzebuje modelu, GPU ani połączenia. Sprawdzam też, że zwykłe zdanie o modelu nie jest
/// przechwytywane — to ta sama pułapka, która wcześniej zabierała pytania zaczynające się od „czy”.</summary>
internal static class ModelToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Require(string? value, string label) =>
        value ?? throw new InvalidOperationException(label + " was not handled as a model tool");

    private static string Handle(string command) =>
        Require(ModelToolbox.TryHandle(command, CommandText.Normalize(command)), "polecenie „" + command + "”");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        // ---------------- katalog i karta modelu ----------------
        string catalog = Handle("modele lokalne");
        Check(catalog.Contains("Katalog modeli lokalnych"), "katalog modeli odpowiada: " + catalog.Split('\n')[0]);
        Check(catalog.Contains("qwen2.5:7b") && catalog.Contains("nomic-embed-text"), "katalog zawiera modele rozmowy i embeddingów");
        Check(catalog.Contains("nic nie pobieram"), "katalog mówi wprost, że niczego nie pobiera");

        string card = Handle("model karta: qwen2.5:7b");
        Check(card.Contains("Karta modelu „qwen2.5:7b”"), "karta modelu nazywa model: " + card.Split('\n')[0]);
        Check(card.Contains("szacunek"), "karta jawnie oznacza wynik jako szacunek");

        string unknown = Handle("model karta: model-ktorego-nie-ma:99b");
        Check(unknown.Contains("Nie mam w katalogu"), "nieznany model nie jest zgadywany: " + unknown);

        // ---------------- matematyka pamięci (liczby, nie formatowanie) ----------------
        double q4 = ModelToolbox.WeightsGb(7.0, "q4_K_M");
        Check(Math.Abs(q4 - 4.24375) < 0.001, "wagi 7B w q4_K_M: " + q4.ToString(System.Globalization.CultureInfo.InvariantCulture));
        double f16 = ModelToolbox.WeightsGb(7.0, "f16");
        Check(Math.Abs(f16 - 14.0) < 0.001, "wagi 7B w f16 muszą dać 14 GB, jest " + f16);
        Check(ModelToolbox.WeightsGb(7.0, "q5_k_m") > q4, "q5 jest cięższe od q4");
        Check(Math.Abs(ModelToolbox.WeightsGb(7.0, "nie-znana") - q4) < 0.001, "nieznana kwantyzacja schodzi do domyślnego q4_K_M");

        double kv = ModelToolbox.KvCacheGb(28, 4, 128, 8192);
        Check(Math.Abs(kv - 0.469762048) < 0.001, "KV cache 28 warstw × 4 głowy × 128 × 8192: " + kv);
        Check(Math.Abs(ModelToolbox.KvCacheGb(28, 4, 128, 16384) - 2 * kv) < 0.001, "podwojenie kontekstu podwaja KV cache");

        // ---------------- dobór do pamięci ----------------
        string fit8 = Handle("model dopasuj: 8");
        Check(fit8.Contains("qwen2.5:7b"), "8 GB musi pomieścić model 7B: " + fit8.Replace(Environment.NewLine, " | "));
        Check(!fit8.Contains("qwen2.5:14b (~"), "14B nie może wejść w 8 GB");
        string fit48 = Handle("model dopasuj: 48");
        Check(fit48.Contains("qwen2.5:14b"), "48 GB mieści model 14B");
        Check(Handle("model dopasuj: 0").Contains("Podaj pamięć"), "zerowa pamięć jest odrzucana z podpowiedzią");
        Check(Handle("model dopasuj: 900").Contains("realną wartość"), "absurdalna pamięć jest odrzucana");

        string machine = Require(ModelToolbox.FitForMachine(16), "audyt maszyny");
        Check(machine.Contains("16 GB") && machine.Contains("2 GB"), "audyt odejmuje 2 GB na system: " + machine.Split('\n')[0]);
        Check(ModelToolbox.FitForMachine(0).Contains("Nie odczytałem"), "brak odczytu RAM nie udaje wyniku");

        // ---------------- role ----------------
        Check(Handle("model rola: kod").Contains("qwen2.5-coder:7b"), "rola „kod” wskazuje model kodu");
        Check(Handle("model rola: wizja").Contains("llava:7b"), "rola „wizja” wskazuje model obrazu");
        Check(Handle("model rola: embeddingi").Contains("nomic-embed-text"), "rola „embeddingi” wskazuje model wektorów");
        Check(Handle("model rola: gimnastyka").Contains("Nie znam roli"), "nieznana rola nie jest zgadywana");

        // ---------------- kwantyzacje ----------------
        string quants = Handle("kwantyzacje");
        Check(quants.Contains("q4_K_M") && quants.Contains("f16"), "tabela kwantyzacji zawiera skrajne warianty");
        string one = Handle("kwantyzacja: q4_K_M");
        Check(one.Contains("Kwantyzacja „q4_K_M”"), "opis kwantyzacji nazywa wariant");
        Check(Handle("kwantyzacja: q9_xyz").Contains("Nie znam kwantyzacji"), "nieznana kwantyzacja nie jest zgadywana");
        Check(Handle("kwantyzacja: Q4_k_m").Contains("q4_K_M"), "wielkość liter w kwantyzacji nie ma znaczenia");

        // ---------------- presety i polityka ----------------
        Check(Handle("presety modelu").Contains("zbalansowany"), "lista presetów zawiera wariant zbalansowany");
        string preset = Handle("preset modelu: precyzyjny");
        Check(preset.Contains("Preset „precyzyjny”") && preset.Contains("num_ctx") && preset.Contains("keep_alive"), "preset precyzyjny opisuje parametry");
        Check(Handle("preset modelu: turbo").Contains("Nie znam presetu"), "nieznany preset nie jest zgadywany");

        string policy = Require(ModelToolbox.Policy(new AiSettings()), "polityka modelu");
        Check(policy.Contains("qwen3:4b-instruct") && policy.Contains("qwen3:1.7b") && policy.Contains("gemma3:4b"),
            "polityka pokazuje wszystkie trzy role modelu z ustawień");
        Check(policy.Contains("kontekst") && policy.Contains("700"), "polityka pokazuje limit kontekstu i odpowiedzi");
        Check(policy.Contains("78"), "polityka pokazuje próg ciśnienia RAM");

        // ---------------- szablony promptów ----------------
        Check(Handle("prompt szablony").Contains("ekstrakcja"), "lista szablonów zawiera ekstrakcję danych");
        string prompt = Handle("prompt szablon: kod");
        Check(prompt.Contains("recenzentem kodu"), "szablon kodu ma treść: " + prompt.Split('\n')[0]);
        Check(prompt.Contains("nie wysyła go nigdzie"), "szablon mówi, że nigdzie nie jest wysyłany");
        Check(Handle("prompt szablon: horoskop").Contains("Nie znam szablonu"), "nieznany szablon nie jest zgadywany");
        foreach (string name in new[] { "testy", "dokumentacja", "tlumaczenie", "streszczenie", "ekstrakcja", "sql", "refaktor", "blad", "email" })
            Check(Handle("prompt szablon: " + name).Contains("wklej jako wiadomość systemową"), "szablon " + name + " ma treść");

        // ---------------- KV cache, kontekst, porównanie ----------------
        string kvText = Handle("model kv: qwen2.5:7b 8192");
        Check(kvText.Contains("KV cache") && kvText.Contains("4 głów KV"), "KV cache pokazuje wzór: " + kvText.Split('\n')[0]);
        Check(Handle("model kv: qwen2.5:7b").Contains("qwen2.5:7b"), "brak kontekstu używa wartości domyślnej");
        Check(Handle("model kv: qwen2.5:7b 12").Contains("liczbą tokenów"), "zbyt mały kontekst jest odrzucany");
        Check(Handle("model pamiec: 8192").Contains("KV cache"), "doradca kontekstu liczy KV cache");
        Check(Handle("model pamiec: 100").Contains("za mało"), "kontekst 100 tokenów jest odrzucany");
        string compare = Handle("model porownaj: qwen2.5:7b vs qwen2.5:14b");
        Check(compare.Contains("Porównanie „qwen2.5:7b” vs „qwen2.5:14b”"), "porównanie nazywa oba modele");
        Check(compare.Contains("Lżejszy i zwykle szybszy: qwen2.5:7b"), "porównanie wskazuje lżejszy model");
        Check(Handle("model porownaj: qwen2.5:7b vs qwen2.5:7b").Contains("ten sam model"), "ten sam model nie jest porównywany do siebie");

        // ---------------- kolejka i tryb bez modelu ----------------
        string queue = Handle("model kolejka");
        Check(queue.Contains("jedno zapytanie do modelu naraz"), "kolejka mówi o jednej ścieżce wykonania");
        Check(queue.Contains("180 s") && queue.Contains("przerwij"), "kolejka pokazuje limit czasu i anulowanie");
        string offline = Handle("model offline");
        Check(offline.Contains("Bez modelu NIE MA"), "tryb bez modelu mówi wprost, czego nie ma");
        Check(offline.Contains("Jarvis"), "tryb bez modelu wymienia to, co działa dalej");

        // ---------------- nie przechwytujemy zwykłych zdań ----------------
        foreach (string sentence in new[]
        {
            "model moich marzeń jest drogi", "lubię modele kolejowe", "kwantyzacja jest trudna do zrozumienia",
            "prompt to nie to samo co komenda", "opowiedz mi o modelach językowych", "karta graficzna jest droga",
        })
            Check(ModelToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem modelu: " + sentence);

        return Task.CompletedTask;
    }
}
