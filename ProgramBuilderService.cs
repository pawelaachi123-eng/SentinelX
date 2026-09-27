using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SentinelX;

public sealed record ProgramBuildResult(string Status, string Message, string ProjectDirectory, string? Executable, string Evidence);

/// <summary>Reproducible local templates. Generated code does not receive arbitrary shell access.</summary>
public sealed class ProgramBuilderService
{
    private readonly string root;
    public static string[] Templates => ["Notatnik", "Kalkulator", "Pomodoro"];
    public ProgramBuilderService(string? root = null) => this.root = root ?? Path.Combine(AppPaths.Root, "Programs");
    public async Task<ProgramBuildResult> BuildAsync(string template, IProgress<string>? progress, CancellationToken token)
    {
        if (!Templates.Contains(template)) return new("FAILED", "Wybierz Notatnik, Kalkulator albo Pomodoro. Inne programy nie są jeszcze obsługiwane przez ten kreator.", "", null, "Nie utworzono plików.");
        string directory = Path.Combine(root, template + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(directory);
        try
        {
            progress?.Report("Tworzę projekt: " + template);
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.csproj"), Project, token);
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), GenerateSource(template), token);
            await File.WriteAllTextAsync(Path.Combine(directory, "README.txt"), $"{template} — lokalny szablon Sentinel X.\nŹródła: Program.cs.\nArtefakt: publish\\SentinelProgram.exe.\nKompilacja i test zapisane w build.log oraz build-proof.json.\n", token);
            progress?.Report("Kompiluję projekt do Windows x64…");
            var result = await RunAsync("dotnet", ["publish", "Program.csproj", "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", "publish", "-v:minimal"], directory, token);
            await File.WriteAllTextAsync(Path.Combine(directory, "build.log"), result.Output, token);
            if (result.ExitCode != 0) return new("FAILED", "Kompilacja nie powiodła się. Zapisano źródła i build.log.", directory, null, result.Output[^Math.Min(2500, result.Output.Length)..]);
            string exe = Path.Combine(directory, "publish", "SentinelProgram.exe");
            if (!File.Exists(exe)) throw new IOException("Kompilator nie utworzył pliku .exe.");
            progress?.Report("Uruchamiam izolowany test wygenerowanego programu…");
            var test = await RunAsync(exe, ["--self-test"], directory, token);
            string proofPath = Path.Combine(directory, "program-test.json");
            if (test.ExitCode != 0 || !File.Exists(proofPath)) return new("FAILED", "Program skompilował się, ale test nie został potwierdzony.", directory, exe, test.Output);
            using var proof = JsonDocument.Parse(await File.ReadAllTextAsync(proofPath, token));
            if (!proof.RootElement.GetProperty("passed").GetBoolean()) throw new InvalidOperationException("Test programu nie przeszedł.");
            var output = new ProgramBuildResult("VERIFIED", "Program zbudowany; test jego logiki i startu WPF przeszedł.", directory, exe, "build.log + program-test.json; test nie zastępuje ręcznej oceny wszystkich interakcji.");
            await File.WriteAllTextAsync(Path.Combine(directory, "build-proof.json"), JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }), token);
            progress?.Report("VERIFIED • " + exe); return output;
        }
        catch (OperationCanceledException) { progress?.Report("CANCELLED • źródła pozostały w " + directory); throw; }
        catch (Exception ex) { AppLog.Write(ex); return new("FAILED", ex.Message, directory, null, "Nie zgłoszono gotowego programu."); }
    }
    private static async Task<(int ExitCode, string Output)> RunAsync(string exe, string[] arguments, string directory, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var start = new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Nie udało się uruchomić procesu kompilacji.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        return (process.ExitCode, await stdout + "\n" + await stderr);
    }
    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF><Nullable>enable</Nullable><AssemblyName>SentinelProgram</AssemblyName></PropertyGroup></Project>
        """;
    private static string GenerateSource(string template) => Source.Replace("__TEMPLATE__", template);
    private const string Source = """"
        using System;
        using System.IO;
        using System.Text.Json;
        using System.Windows;
        using System.Windows.Controls;
        using System.Windows.Media;
        using System.Windows.Threading;
        using Microsoft.Win32;
        public static class Program
        {
            const string Kind = "__TEMPLATE__";
            [STAThread] public static void Main(string[] args)
            {
                var app = new Application();
                var window = new Window { Title = Kind + " • Sentinel", Width = 640, Height = 480, MinWidth = 420, MinHeight = 300, Background = B("#101521"), Foreground = B("#F1F5FC"), FontFamily = new FontFamily("Segoe UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen };
                var panel = new StackPanel { Margin = new Thickness(24) }; window.Content = new ScrollViewer { Content = panel };
                panel.Children.Add(new TextBlock { Text = Kind, FontSize = 28, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,20) });
                if (Kind == "Notatnik")
                {
                    var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) }; panel.Children.Add(input);
                    AddButton(panel, "Zapisz jako…", () => { var dialog = new SaveFileDialog { Filter = "Tekst|*.txt", FileName = "notatka.txt" }; if(dialog.ShowDialog(window) == true) { try { File.WriteAllText(dialog.FileName, input.Text); } catch(Exception ex) { MessageBox.Show(ex.Message); } } });
                }
                else if (Kind == "Kalkulator")
                {
                    var left = new TextBox { Text="2", Margin=new Thickness(0,0,0,12) }; var right = new TextBox { Text="3", Margin=new Thickness(0,0,0,12) };
                    var operation = new ComboBox { ItemsSource = new[]{"+","-","×","÷"}, SelectedIndex=0, Margin=new Thickness(0,0,0,12) }; var result = new TextBlock { FontSize=28 };
                    panel.Children.Add(left); panel.Children.Add(operation); panel.Children.Add(right);
                    AddButton(panel,"Oblicz",()=> { if(double.TryParse(left.Text,out var a)&&double.TryParse(right.Text,out var b)) { try{result.Text=Calculate(a,b,operation.SelectedIndex).ToString();}catch(Exception ex){result.Text=ex.Message;} }else result.Text="Wpisz dwie liczby."; }); panel.Children.Add(result);
                }
                else
                {
                    int seconds=25*60; var label=new TextBlock { Text="25:00", FontSize=68 }; panel.Children.Add(label); var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
                    timer.Tick+=(_,_)=>{seconds=Math.Max(0,seconds-1); label.Text=FormatTime(seconds); if(seconds==0)timer.Stop();};
                    AddButton(panel,"Start",()=>timer.Start()); AddButton(panel,"Pauza",()=>timer.Stop()); AddButton(panel,"Reset",()=>{timer.Stop();seconds=1500;label.Text=FormatTime(seconds);}); window.Closed+=(_,_)=>timer.Stop();
                }
                if(Array.IndexOf(args,"--self-test")>=0)
                {
                    bool passed=Calculate(2,3,0)==5 && Calculate(6,3,3)==2 && FormatTime(1500)=="25:00" && panel.Children.Count>1;
                    window.Show(); window.UpdateLayout(); passed &= window.IsLoaded;
                    File.WriteAllText("program-test.json",JsonSerializer.Serialize(new{passed,template=Kind,checks="arithmetic, timer format, WPF window loaded"})); window.Close(); Environment.ExitCode=passed?0:1; return;
                }
                app.Run(window);
            }
            static double Calculate(double a,double b,int op)=>op switch{0=>a+b,1=>a-b,2=>a*b,3=>b==0?throw new InvalidOperationException("Nie dziel przez zero."):a/b,_=>throw new ArgumentException()};
            static string FormatTime(int seconds)=>$"{seconds/60:00}:{seconds%60:00}";
            static Brush B(string value)=>(Brush)new BrushConverter().ConvertFromString(value)!;
            static void AddButton(Panel panel,string title,Action action){var b=new Button{Content=title,Padding=new Thickness(14,9,14,9),Margin=new Thickness(0,12,0,0)};b.Click+=(_,_)=>action();panel.Children.Add(b);}
        }
        """";
}
