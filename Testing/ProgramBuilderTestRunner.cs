using System.IO;
using System.Text.Json;

namespace SentinelX;
internal static class ProgramBuilderTestRunner
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var builder = new ProgramBuilderService(Path.Combine(directory, "projects"));
        var results = new List<ProgramBuildResult>();
        foreach (string template in ProgramBuilderService.Templates)
        {
            var result = await builder.BuildAsync(template, null, CancellationToken.None);
            results.Add(result);
            await File.WriteAllTextAsync(Path.Combine(directory, "builder-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (result.Status != "VERIFIED") throw new InvalidOperationException(template + ": " + result.Message + "\n" + result.Evidence);
        }
    }
}
