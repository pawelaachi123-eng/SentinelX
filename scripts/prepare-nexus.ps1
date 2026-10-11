param(
    [string]$UiRoot = 'nexus-ui'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ui = Join-Path $repo $UiRoot
$source = Join-Path $ui 'windows-host/SentinelX.WinUI/UI/Nexus'
$target = Join-Path $repo 'SentinelX.WinUI/UI/Nexus'
if (-not (Test-Path (Join-Path $source 'NexusBridge.cs'))) { throw "Missing NexusBridge.cs at $source" }
if (-not (Test-Path (Join-Path $source 'NexusWindow.cs'))) { throw "Missing NexusWindow.cs at $source" }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $source '*.cs') $target -Force

# IMPORTANT: An arbitrary remote WebView2 document must never gain native command permissions.
$bridge = Join-Path $target 'NexusBridge.cs'
$b = [IO.File]::ReadAllText($bridge)
$old = '        string? id = null;'
$guard = @'
        // Commands only from packaged app content or explicitly configured loopback dev server.
        if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var origin) ||
            !((origin.Scheme == Uri.UriSchemeHttps &&
                origin.Host.Equals("nexus.sentinel", StringComparison.OrdinalIgnoreCase)) ||
              (origin.IsLoopback && !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("SENTINEL_NEXUS_URL")))))
            return;
        string? id = null;
'@
if (-not $b.Contains($old)) { throw 'NexusBridge source changed; review required before patching security.' }
$b = $b.Replace($old, $guard.TrimEnd())
[IO.File]::WriteAllText($bridge, $b, [Text.UTF8Encoding]::new($false))

# Disallow remote pages in a privileged desktop WebView2 host.
$nwPath = Join-Path $target 'NexusWindow.cs'
$nw = [IO.File]::ReadAllText($nwPath)
$needle = '        var url = Environment.GetEnvironmentVariable("SENTINEL_NEXUS_URL");'
$secure = @'
        var url = Environment.GetEnvironmentVariable("SENTINEL_NEXUS_URL");
        if (!string.IsNullOrWhiteSpace(url) &&
            (!Uri.TryCreate(url, UriKind.Absolute, out var devUrl) ||
             !devUrl.IsLoopback ||
             (devUrl.Scheme != Uri.UriSchemeHttp && devUrl.Scheme != Uri.UriSchemeHttps)))
            throw new InvalidOperationException("SENTINEL_NEXUS_URL must be a localhost development URL.");
'@
if (-not $nw.Contains($needle)) { throw 'NexusWindow source changed; review required.' }
$nw = $nw.Replace($needle, $secure.TrimEnd())
# Prevent privileged WebView2 navigation to arbitrary third-party sites.
$navNeedle = '        _view.CoreWebView2.ProcessFailed += (_, _) => _view.Reload();'
$navGuard = @'
        _view.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var next) ||
                !((next.Scheme == Uri.UriSchemeHttps &&
                   next.Host.Equals("nexus.sentinel", StringComparison.OrdinalIgnoreCase)) ||
                  (next.IsLoopback && !string.IsNullOrWhiteSpace(
                     Environment.GetEnvironmentVariable("SENTINEL_NEXUS_URL")))))
                e.Cancel = true;
        };
        _view.CoreWebView2.ProcessFailed += (_, _) => _view.Reload();
'@
if (-not $nw.Contains($navNeedle)) { throw 'WebView2 navigation point changed; review required.' }
$nw = $nw.Replace($navNeedle, $navGuard.TrimEnd())
[IO.File]::WriteAllText($nwPath, $nw, [Text.UTF8Encoding]::new($false))

# Keep classic shell as opt-in rollback; set Nexus as the default graphical shell.
$appPath = Join-Path $repo 'SentinelX.WinUI/App.xaml.cs'
$app = [IO.File]::ReadAllText($appPath)
$old = 'shell = provider.GetRequiredService<ShellWindow>();'
$new = @'
shell = Environment.GetEnvironmentVariable("SENTINEL_UI") == "classic"
                ? provider.GetRequiredService<ShellWindow>()
                : (Window)provider.GetRequiredService<SentinelX.WinUI.UI.Nexus.NexusWindow>();
'@
if (-not $app.Contains($old)) { throw 'Unrecognized application shell code. No unsafe partial patch.' }
$app = $app.Replace($old, $new.TrimEnd())
[IO.File]::WriteAllText($appPath, $app, [Text.UTF8Encoding]::new($false))

$locatorPath = Join-Path $repo 'SentinelX.WinUI/Core/ServiceLocator.cs'
$locator = [IO.File]::ReadAllText($locatorPath)
$needle = '        services.AddTransient<ShellWindow>();'
$replacement = @'
        services.AddTransient<SentinelX.WinUI.UI.Nexus.NexusWindow>();
        services.AddTransient<ShellWindow>();
'@
if (-not $locator.Contains($needle)) { throw 'Unrecognized DI registrations; review before patching.' }
$locator = $locator.Replace($needle, $replacement.TrimEnd())
[IO.File]::WriteAllText($locatorPath, $locator, [Text.UTF8Encoding]::new($false))
Write-Host 'Prepared WinUI3 + Nexus WebView2 host with restricted origins.' -ForegroundColor Green
