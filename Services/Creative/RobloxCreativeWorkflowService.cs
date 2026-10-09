using SentinelX.Services.History;

namespace SentinelX.Services.Creative;

/// <summary>Turns a natural-language Roblox brief into local files, runs Blender when installed,
/// and opens the generated .rbxlx in Roblox Studio when installed. It never publishes or logs in.</summary>
public sealed class RobloxCreativeWorkflowService
{
    private readonly RobloxGameProjectGenerator generator;
    private readonly BlenderAutomationService blender;
    private readonly RobloxStudioLauncher studio;
    private readonly bool openApplications;

    public RobloxCreativeWorkflowService(RobloxGameProjectGenerator generator, BlenderAutomationService blender, RobloxStudioLauncher studio)
        : this(generator, blender, studio, openApplications: true) { }

    internal RobloxCreativeWorkflowService(RobloxGameProjectGenerator generator, BlenderAutomationService blender,
        RobloxStudioLauncher studio, bool openApplications)
    {
        this.generator = generator;
        this.blender = blender;
        this.studio = studio;
        this.openApplications = openApplications;
    }

    public async Task<string> CreateAsync(RobloxGameProjectSpec spec, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        RobloxGameProjectResult project = await generator.GenerateAsync(spec, token).ConfigureAwait(false);
        if (!project.Success) return project.Message;

        var report = new System.Text.StringBuilder(project.Message);
        if (openApplications)
        {
            BlenderBuildResult blenderBuild = await blender.BuildSceneAsync(project.BlenderScriptPath, token).ConfigureAwait(false);
            report.AppendLine().AppendLine().Append("BLENDER: ").Append(blenderBuild.Message);
            if (blenderBuild.Success)
            {
                report.AppendLine().Append("Scena: ").Append(blenderBuild.BlendPath)
                    .AppendLine().Append("FBX do ręcznego importu: ").Append(blenderBuild.FbxPath);
                ActionExecutionResult openBlend = blender.OpenGeneratedScene(blenderBuild.BlendPath);
                report.AppendLine().Append("Okno Blender: ").Append(openBlend.Message);
                ActionEvidenceCapture.Record(new ActionHistoryEntry
                {
                    ActionId = "SX-BLENDER-OPEN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    Timestamp = DateTime.Now,
                    ActionType = "BLENDER_OPEN",
                    Command = "Otwórz wygenerowaną scenę w Blenderze",
                    Status = openBlend.Status,
                    Message = openBlend.Message,
                    Evidence = openBlend.Evidence,
                    RecoveryAdvice = "Jeśli okno się nie pojawiło, otwórz plik .blend z podanej ścieżki ręcznie."
                });
            }

            RobloxStudioOpenResult studioResult = await studio.OpenGeneratedPlaceAsync(project.PlacePath, token).ConfigureAwait(false);
            report.AppendLine().Append("ROBLOX STUDIO: ").Append(studioResult.Message);
        }
        else
        {
            report.AppendLine().AppendLine("Automatyczne uruchomienie aplikacji zewnętrznych wyłączono w tym trybie testowym.");
        }
        report.AppendLine().AppendLine("Następny krok: w Studio sprawdź Errors/Output i Play; w Blenderze obejrzyj geometrię. Wygenerowanie pliku nie jest certyfikatem działania gry.");
        return report.ToString();
    }
}
