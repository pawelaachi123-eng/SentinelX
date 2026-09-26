using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SentinelX.Tests;

/// <summary>0.94 · pamięć i kontekst: skoroszyt faktów sesji, wyszukiwanie nieostre
/// (odmiana + jedna literówka) i wybór linii kontekstu dla modelu. Wszystko lokalnie
/// i deterministycznie — bez sieci, bez maszyny, bez AI.</summary>
internal static class ContextRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // --- SessionFactBook: the follow-up memory of tool readings ---
        var facts = new SessionFactBook();
        Check(facts.Count == 0, "a fresh fact book is empty");
        Check(facts.Describe().Length == 0, "an empty fact book describes nothing");
        facts.Record("RAM", "8/16 GB");
        facts.Record("Dysk C", "120/500 GB");
        Check(facts.Count == 2, "records accumulate, got " + facts.Count);
        string digest = facts.Describe();
        Check(digest.Contains("RAM: 8/16 GB") && digest.Contains("Dysk C: 120/500 GB"), "facts are described for the model: " + digest);
        Check(digest.Contains("Ostatnie odczyty"), "the digest is labeled as session readings");
        var snapshot = facts.Snapshot();
        Check(snapshot.Count == 2 && snapshot[0].Label == "RAM", "the snapshot preserves insertion order");
        facts.RecordResponse("ile ramu teraz", "RAM: 10/16 GB · GPU 2 GB");
        Check(facts.Count == 3 && facts.Describe().Contains("ile ramu teraz"), "a tool response becomes a fact under a short label");
        facts.Record("RAM", "12/16 GB");
        Check(facts.Count == 3 && facts.Describe().Contains("12/16 GB") && !facts.Describe().Contains("8/16 GB"),
            "the same label replaces the older fact");
        for (int i = 0; i < 12; i++) facts.Record("tmp" + i, "v" + i);
        Check(facts.Count == 8, "the ring keeps only the newest 8 facts, got " + facts.Count);
        Check(!facts.Describe().Contains("tmp0"), "oldest facts fall out of the ring");
        facts.Clear();
        Check(facts.Count == 0 && facts.Describe().Length == 0, "clear empties the book");

        // --- fuzzy note search: exact counts never change, inflection and one typo widen the net ---
        string memoryDir = Path.Combine(directory, "memory");
        var memory = new ConversationMemoryService(memoryDir);
        memory.AddNote("kup mleko");
        memory.AddNote("kup chleb");
        memory.AddNote("spotkanie o 15:00 w sprawie budzetu");
        Check(memory.SearchNotes("mleko").Count == 1, "exact search finds one note");
        Check(memory.SearchNotes("chleb").Count == 1, "exact search finds the other note");
        Check(memory.SearchNotes("budzet").Count == 1, "exact substring still works");
        Check(memory.SearchNotes("mleka").Count == 1, "an inflected form (stem) finds „kup mleko”");
        Check(memory.SearchNotes("mlekk").Count == 1, "a single typo finds „kup mleko”");
        Check(memory.SearchNotes("mleko chleb").Count == 0, "multiple tokens must all match (AND semantics)");
        Check(memory.SearchNotes("nieistniejace slowo").Count == 0, "unknown text finds nothing");

        memory.AddNote("ustalenie projektu alfa");
        memory.AddNote("ustalenie projektu beta");
        memory.AddNote("ustalenie globalne");
        Check(memory.SearchNotes("ustalenie").Count == 3, "exact substring counts must stay exactly 3, got " + memory.SearchNotes("ustalenie").Count);

        // --- fuzzy conversation search ---
        memory.AddUserMessage("spotkanie przeniesione na jutro", "test");
        memory.AddAssistantMessage("Dobrze, notuję zmianę godziny.");
        Check(memory.SearchConversation("jutro").Count >= 1, "conversation search finds the turn");
        Check(memory.SearchConversation("jutrro").Count >= 1, "a typo still finds the turn");
        Check(memory.SearchConversation("zzzzqqq").Count == 0, "unknown conversation text finds nothing");

        // --- context assembly: notes in stable context, thread header in recent context ---
        string stable = memory.GetStableContext();
        Check(stable.Contains("kup mleko") && stable.Contains("Trwałe wspomnienia"), "stable context carries the notes: " + stable);
        string recent = memory.GetRecentContext();
        Check(recent.Contains("Wątek rozmowy"), "the recent context names the thread: " + recent);
        Check(recent.Contains("spotkanie przeniesione"), "the recent context carries the turns");

        // --- AiContextFilter: profile survives, relevance wins, follow-ups keep the thread ---
        string lines = "Wspomnienie: wczoraj kupilem chleb\nWspomnienie: pamiec RAM ma 16 GB\nWspomnienie: sprawa sprzed roku o ogrodzie";
        string picked = AiContextFilter.Select("ile ramu", lines, 200);
        Check(picked.Contains("16 GB"), "the relevant line survives the budget: " + picked);
        Check(!picked.Contains("ogrodzie"), "an unrelated old line is filtered out: " + picked);
        string withProfile = "name: Ala\nWspomnienie: stary temat o herbacie " + new string('x', 300);
        string profilePick = AiContextFilter.Select("ile ramu", withProfile, 120);
        Check(profilePick.Contains("name: Ala"), "profile fields are always kept: " + profilePick);
        string followUp = AiContextFilter.Select("a dlaczego tak", lines, 500);
        Check(followUp.Contains("chleb") && followUp.Contains("ogrodzie"), "follow-ups keep the whole thread: " + followUp);

        return Task.CompletedTask;
    }
}
