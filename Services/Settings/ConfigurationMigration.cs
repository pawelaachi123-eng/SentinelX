using System.Collections.Generic;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SentinelX.Services.Settings;

/// <summary>
/// Migracja ustawień po aktualizacji: starszy JSON jest uzupełniany o nowe pola i domyślne
/// wartości. Bez utraty ustawień użytkownika.
/// </summary>
public static class ConfigurationMigration
{
    private static readonly Dictionary<int, Func<JsonObject, JsonObject>> Migrators = new()
    {
        [1] = MigrateFromV0,
        // Kolejne migratory dorzucamy tutaj w miarę rozwoju.
    };
    private const int CurrentVersion = 1;

    public static string Migrate(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        try
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null) return json;
            int version = root["SchemaVersion"] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
            while (version < CurrentVersion)
            {
                version++;
                if (Migrators.TryGetValue(version, out var mig)) root = mig(root);
            }
            root["SchemaVersion"] = CurrentVersion;
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    private static JsonObject MigrateFromV0(JsonObject root)
    {
        // v0 → v1: ujednolicenie lokalizacji VoiceSelectedMicrophone i dodanie domyślnych Automation budżetów.
        if (root["Voice"] is JsonObject voice && voice["selectedMic"] != null && voice["SelectedMicrophoneName"] == null)
        {
            voice["SelectedMicrophoneName"] = voice["selectedMic"]?.GetValue<string>();
        }
        return root;
    }
}
