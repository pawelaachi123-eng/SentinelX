using SentinelX.Models.Plugins;

namespace SentinelX.Services.Plugins;

/// <summary>
/// Każdy plugin DLL implementuje ten interfejs (przez klasę publiczną z bezparametrowym konstruktorem).
/// Plugin jest odizolowany: może rejestrować narzędzia/skille/widgety, ale nigdy nie dostaje
/// bezpośredniego dostępu do wnętrza procesu poza API, które mu wystawimy.
/// </summary>
public interface ISentinelPlugin : IDisposable
{
    PluginManifest Manifest { get; }
    Task StartAsync(IPluginContext context, CancellationToken token);
    Task StopAsync(CancellationToken token);
    Task<PluginHealthLevel> CheckHealthAsync(CancellationToken token);
}

/// <summary>
/// API wystawiane pluginowi. Wszystko co może zrobić, idzie przez ten interfejs — łatwo kontrolować uprawnienia.
/// </summary>
public interface IPluginContext
{
    IServiceProvider Services { get; }
    SentinelX.Models.Plugins.PluginPermissions GrantedPermissions { get; }
    string DataDirectory { get; }
    void RegisterTool(string id, string description, Func<object?, CancellationToken, Task<object?>> handler);
    void RegisterSkill(string id, string description);
    void RegisterWidget(string id);
    void RegisterAutomationTrigger(string id, string description);
    void RegisterIntegration(string providerId);
    void Log(string message);
}
