using System.IO;
using System.Linq;
using SentinelX.Core;
using SentinelX.Services.Intent;

namespace SentinelX.Tests;

/// <summary>Typo tolerance and abbreviations: what gets repaired, what must never be touched,
/// and the guarantee that destructive commands are outside the repair catalogue.</summary>
internal static class UnderstandingRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static Task RunAsync(string directory)
    {
        // --- edit distance: transposition is one edit, not two ---
        Check(CommandUnderstanding.Distance("abc", "acb") == 1, "adjacent transposition must cost one edit");
        Check(CommandUnderstanding.Distance("kot", "kot") == 0, "identical strings have distance zero");
        Check(CommandUnderstanding.Distance("kot", "koty") == 1, "one insertion costs one edit");
        Check(CommandUnderstanding.Distance("kot", "rok") == 2, "two substitutions cost two edits");
        Check(CommandUnderstanding.Distance("", "abc") == 3, "empty string distance is the other length");

        // --- similarity is length-relative and never lets near misses tie with good matches ---
        Check(CommandUnderstanding.Similarity("ramuu", "ramu") >= 0.8, "one trailing letter must stay above the repair threshold");
        Check(CommandUnderstanding.Similarity("ramuu", "ram") < 0.8, "a two-edit jump on a short word must stay below the threshold");
        Check(CommandUnderstanding.PhraseSimilarity("wlacz kalcuator", "wlacz kalkulator") >= 0.85, "one mistyped word must not sink the phrase");

        // --- documented abbreviations: deterministic, complete, and always a real command ---
        Check(IntentCatalog.Abbreviations.Count >= 18, "abbreviation table must stay at least 18 entries");
        foreach (var (key, expansion) in IntentCatalog.Abbreviations)
            Check(expansion.Length > 2 && expansion.Split(' ').Length >= 1 && expansion == expansion.Trim(),
                "abbreviation " + key + " must expand to a clean command");
        Check(CommandUnderstanding.Repair("cs").Text == "wlacz cs2", "cs must expand to launching CS2");
        Check(CommandUnderstanding.Repair("mz").Text == "uruchom menedzer zadan", "mz must expand to the task manager");
        Check(CommandUnderstanding.Repair("pm").Text == "co pamietasz", "pm must expand to the memory summary");
        Check(CommandUnderstanding.Repair("sn").Success && CommandUnderstanding.Repair("sn").Confidence == 1.0,
            "abbreviations are deterministic and reported with full confidence");
        Check(CommandUnderstanding.Repair("cs").Summary.Contains("skrót"), "an abbreviation repair must say it is a short form");

        // --- typos ---
        var wordRepair = CommandUnderstanding.Repair("ile mam ramuu");
        Check(wordRepair.Success && wordRepair.Text == "ile mam ramu", "a doubled trailing letter must be repaired to the known phrase: " + wordRepair.Text);
        Check(wordRepair.Summary.Contains("ramuu"), "the repair summary must name the changed word");
        var phraseRepair = CommandUnderstanding.Repair("wlacz kalcuator");
        Check(phraseRepair.Success && phraseRepair.Text == "wlacz kalkulator", "a mistyped app name must be repaired: " + phraseRepair.Text);
        Check(CommandUnderstanding.Repair("ststus pamieci").Text == "status pamieci", "transposed letters must be repaired");
        Check(CommandUnderstanding.Repair("wlacz discorda").Text == "wlacz discord", "a wrong inflection of a known app must be repaired");
        Check(CommandUnderstanding.Repair("diagnostyka komputer").Text == "diagnostyka komputera", "a missing ending must be completed");
        Check(CommandUnderstanding.Repair("ktora godizna").Text == "ktora godzina", "transposition inside a word must be repaired");
        Check(CommandUnderstanding.Repair("top procsy").Text == "top procesy", "a mistyped second word must be repaired");

        // --- Polish diacritics and punctuation must not break matching ---
        Check(CommandUnderstanding.Repair("Która godizna?").Text == "ktora godzina", "diacritics and a question mark must be normalized first");

        // --- exact commands and free conversation must be left alone ---
        Check(!CommandUnderstanding.Repair("ile mam ramu").Success, "an exact command needs no repair");
        Check(!CommandUnderstanding.Repair("snapshoty").Success, "an exact single-word command needs no repair");
        Check(!CommandUnderstanding.Understand("snapshoty").Success,
            "znane polecenie nie może być przepisane na inne (snapshoty ≠ snapshot)");
        Check(!CommandUnderstanding.Extract("snapshoty").Success, "dokładne polecenie nie jest przepisywane");
        Check(!CommandUnderstanding.Repair("napisz mi wiersz o jesieni").Success, "free conversation must not be turned into a command");
        Check(!CommandUnderstanding.Repair("opowiedz mi cos ciekawego o historii polski").Success, "a long sentence must not be repaired");
        Check(!CommandUnderstanding.Repair("co").Success, "a two-letter word must not be guessed");
        Check(!CommandUnderstanding.Repair("policz 12,5*4").Success, "argument commands are handled by the toolbox, not repaired");
        Check(!CommandUnderstanding.Repair("ile dni do 24.12").Success, "dates must survive repair untouched");
        Check(!CommandUnderstanding.Repair("haslo 20").Success, "a password length must survive repair untouched");
        Check(!CommandUnderstanding.Repair("").Success && !CommandUnderstanding.Repair("   ").Success, "empty input must not be repaired");
        Check(!CommandUnderstanding.Repair("pierwsza linia\ndruga linia").Success, "multiline input must not be repaired");

        // --- the safety contract: repair can never reach a destructive command ---
        foreach (string phrase in IntentCatalog.Phrases)
            foreach (string dangerous in new[] { "usun", "zamknij", "wylacz", "czysc", "kill", "sformatuj", "potwierdz", "zatrzymaj" })
                Check(!phrase.Contains(dangerous, StringComparison.Ordinal),
                    "destructive verb " + dangerous + " must never be in the repair catalogue, found in: " + phrase);
        Check(!CommandUnderstanding.Repair("zamknij notatnk").Success, "a mistyped destructive command must stay unhandled");
        Check(!CommandUnderstanding.Repair("usun wszystko").Success, "a destructive command must never be invented by repair");
        Check(!CommandUnderstanding.Repair("potwierdz akcje").Success, "confirmations must never be produced by repair");
        // 0.97: naprawa nie może zgubić treści zdania — słowo zakresu nie jest literówką.
        Check(!CommandUnderstanding.Repair("ile mam ramu dzis").Success, "a scope word is content, not a typo");
        Check(!CommandUnderstanding.Repair("ile mam ramu teraz").Success, "„teraz” must not be repaired away");

        // --- 0.91: the grey zone asks instead of guessing ---
        var suggestions = CommandUnderstanding.Suggest("ile mam ramu dzis");
        Check(suggestions.Count > 0 && suggestions[0] == "ile mam ramu", "a near command must be suggested, not guessed: " + string.Join("|", suggestions));
        Check(CommandUnderstanding.Suggest("ile mam ramu").Count == 0, "an exact command must not produce a suggestion");
        Check(CommandUnderstanding.Suggest("zamknij notatnk").Count == 0, "destructive stems may never be suggested");
        Check(CommandUnderstanding.Suggest("usun wszystko").Count == 0, "destructive input may never be suggested");
        Check(CommandUnderstanding.Suggest("napisz mi wiersz o jesieni").Count == 0, "free conversation must not produce suggestions");
        Check(CommandUnderstanding.Suggest("").Count == 0, "empty input must not produce suggestions");
        Check(CommandUnderstanding.Suggest("napisz mi wiersz o jesieni i o zimie i o wiośnie i o lecie też").Count == 0, "long input must not produce suggestions");
        var topSuggestion = CommandUnderstanding.Suggest("ile mam ramu dzis")[0];
        Check(!topSuggestion.Contains("usun") && !topSuggestion.Contains("zamknij") && !topSuggestion.Contains("wylacz"), "a suggestion may never point at a destructive command");

        // --- 0.91: the lessons journal records repairs locally and stays readable ---
        var journal = new UnderstandingJournal(Path.Combine(directory, "journal"));
        journal.Append("ile mam ramuu", "ile mam ramu", "ramuu → ramu");
        journal.Append("ile mam ramuu", "ile mam ramu", "ramuu → ramu");
        journal.Append("ststus pamieci", "status pamieci", "ststus → status");
        Check(journal.TotalCount == 3, "the journal must count every repair");
        var topPair = journal.TopPairs(1).Single();
        Check(topPair.From == "ile mam ramuu" && topPair.To == "ile mam ramu" && topPair.Count == 2, "the most frequent correction must come first");
        Check(journal.Report().Contains("Zapisane poprawki: 3"), "the lekcje report must name the count: " + journal.Report());
        Check(journal.Recent(2).Count == 2, "recent entries must respect the limit");
        journal.Append("", "cos", "");
        Check(journal.TotalCount == 3, "empty repairs must not be recorded");

        // --- the catalogue is real: every phrase is a command the product handles ---
        Check(IntentCatalog.Phrases.Count >= 180, "the catalogue must cover the real command surface, got " + IntentCatalog.Phrases.Count);
        Check(IntentCatalog.Phrases.All(x => x.Length > 1 && x == x.Trim() && !x.Contains("  ")), "phrases must be clean normalized text");
        Check(IntentCatalog.Vocabulary.Contains("kalkulator") && IntentCatalog.Vocabulary.Contains("ramu"), "vocabulary must contain the repair targets");
        Check(!IntentCatalog.Phrases.Contains("szukaj"), "a bare search stem must not be repairable into a web search");

        // ============================ 0.94 · rozumienie zdań ============================
        // --- polite sentences with a known command inside are decoded, with a visible note ---
        var polite = CommandUnderstanding.Understand("sprawdź proszę ile mam ramu");
        Check(polite.Success && polite.Text == "ile mam ramu", "a polite sentence with an embedded command must be decoded: " + polite.Text);
        Check(polite.Summary.Contains("prosze") || polite.Summary.Contains("sprawdz"), "the note must say what was skipped: " + polite.Summary);
        Check(CommandUnderstanding.Understand("powiedz mi która godzina").Text == "ktora godzina", "inquiry verbs around a command must be stripped");
        Check(CommandUnderstanding.Understand("sprawdź proszę status pamięci").Text == "status pamieci", "polite multiword command inside a sentence");
        Check(CommandUnderstanding.Understand("mógłbyś sprawdzić ile dni do 24.12 proszę").Text == "ile dni do 24.12", "arguments must survive extraction: " + CommandUnderstanding.Understand("mógłbyś sprawdzić ile dni do 24.12 proszę").Text);

        // --- verb synonyms fold towards the safe catalogue ---
        var launch = CommandUnderstanding.Understand("odpal discorda");
        Check(launch.Success && launch.Text is "odpal discord" or "wlacz discord" or "uruchom discord", "a synonym verb + inflection must launch discord: " + launch.Text);
        Check(launch.Text.Contains("discord") && !launch.Text.Contains("discorda"), "the inflection must be repaired to the base app name");
        Check(CommandUnderstanding.Understand("zobacz która godzina").Text == "ktora godzina", "„zobacz” folds to the show verb");

        // --- inflection and single typos inside sentences (stems + fuzzy) ---
        Check(CommandUnderstanding.Understand("sprawdź który godzina").Text == "ktora godzina", "an inflected adjective must stem-match: " + CommandUnderstanding.Understand("sprawdź który godzina").Text);
        Check(CommandUnderstanding.Understand("powiedz mi ile mam ramuu").Text == "ile mam ramu", "a typo inside a polite sentence must still be repaired");

        // --- an exact command stays untouched: no noise notes ---
        Check(!CommandUnderstanding.Understand("ile mam ramu").Success, "an exact command must not produce a rewrite note");
        Check(CommandUnderstanding.Understand("zrob zadanie przetestowac centrum").Text == "zrob zadanie przetestowac centrum" || !CommandUnderstanding.Understand("zrob zadanie przetestowac centrum").Success,
            "a clean argument command must pass through without mangling");

        // --- refusals: scope words, conversation, negation and destruction never auto-execute ---
        Check(!CommandUnderstanding.Understand("ile mam ramu dziś").Success, "a scope word keeps the sentence in the question lane (Czy chodziło Ci o…)");
        Check(!CommandUnderstanding.Understand("ile mam ramu teraz").Success, "„teraz” is content too — the grey zone asks first");
        Check(!CommandUnderstanding.Understand("napisz mi wiersz o tym ile mam ramu").Success, "a request for text must never be stolen by a command inside it");
        Check(!CommandUnderstanding.Understand("ile mam ramu i usun wspomnienia").Success, "destruction anywhere disables auto-execution");
        Check(!CommandUnderstanding.Understand("nie pokazuj ram").Success, "negation is never auto-executed");
        Check(!CommandUnderstanding.Understand("zamknij notatnik").Success, "destructive verbs never fold into launching");
        Check(!CommandUnderstanding.Understand("").Success && !CommandUnderstanding.Understand("   ").Success, "empty input is not understood as anything");

        // --- extraction only ever lands on the safe catalogue ---
        var extracted = CommandUnderstanding.Extract("powiedz mi co pamietasz");
        Check(extracted.Success && extracted.Text == "co pamietasz", "extraction rebuilds the catalogue phrase");
        Check(IntentCatalog.Phrases.Contains(extracted.Text), "the extraction target must be a known safe phrase");
        Check(CommandUnderstanding.Extract("pokaz pamiec").Success == false, "„pokaż pamięć” is a memory command — it must NOT be turned into RAM metrics");

        // --- stems and fuzzy words used by search and extraction ---
        Check(Core.CommandLexicon.Stem("pamieci") == Core.CommandLexicon.Stem("pamiec"), "inflection stems must unify");
        Check(Core.CommandLexicon.WordsMatch("discorda", "discord"), "an inflected app name matches its base form");
        Check(Core.CommandLexicon.WordsMatch("ktory", "ktora"), "a declined pronoun matches");
        Check(!Core.CommandLexicon.WordsMatch("ram", "ramu"), "very short words must be exact");
        Check(Core.CommandLexicon.IsOutsideWord("sprawdz") && Core.CommandLexicon.IsOutsideWord("prosze") && !Core.CommandLexicon.IsOutsideWord("dziś"), "inquiry/filler words are ignorable, scope words are not");

        // --- the dry-run explains and executes nothing ---
        string explain = CommandUnderstanding.Explain("sprawdź proszę ile mam ramuu");
        Check(explain.Contains("ROZUMIENIE") && explain.Contains("wykonane zostanie"), "the dry-run must show the verdict");
        Check(explain.Contains("ile mam ramu"), "the dry-run must name the decoded command");
        Check(CommandUnderstanding.Explain("").Contains("pusty wpis"), "the dry-run handles empty input");

        // --- 0.94: new natural aliases resolve deterministically and stay safe ---
        Check(ReadOnlyIntentCatalog.TryResolve("jak działa procesor", out var cpuIntent) && cpuIntent == ReadOnlyIntent.Cpu, "natural phrasing must resolve");
        Check(ReadOnlyIntentCatalog.TryResolve("ile mam miejsca na dysku", out var diskIntent) && diskIntent == ReadOnlyIntent.Disks, "disk phrasing must resolve");
        Check(!ReadOnlyIntentCatalog.TryResolve("pokaż pamięć", out _), "the memory command must not be stolen by RAM aliases");
        Check(!ReadOnlyIntentCatalog.TryResolve("ile mam ramu i usuń wspomnienia", out _), "compound input must not resolve as a read");

        return Task.CompletedTask;
    }
}
