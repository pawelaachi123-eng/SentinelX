using System.IO;
using System.Text.RegularExpressions;
namespace SentinelX.Services.Maintenance;
public sealed record SteamGame(string Name,string AppId);
public static class SteamResolver
{
 public static IReadOnlyList<SteamGame> Find(string query,string steamRoot){
  if(query.Length is <1 or >100)return [];
  var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.GetFullPath(steamRoot)};
  string libraries=Path.Combine(steamRoot,"steamapps","libraryfolders.vdf");
  if(File.Exists(libraries)&&new FileInfo(libraries).Length<1048576){
   foreach(Match m in Regex.Matches(File.ReadAllText(libraries),@"""path""\s+""([^""]+)""")){
    string value=m.Groups[1].Value.Replace(@"\\",@"\");
    if(Path.IsPathFullyQualified(value))roots.Add(Path.GetFullPath(value));
   }
  }
  var games=new List<SteamGame>();
  foreach(var root in roots.Take(16)){
   string apps=Path.Combine(root,"steamapps");if(!Directory.Exists(apps))continue;
   foreach(string path in Directory.EnumerateFiles(apps,"appmanifest_*.acf").Take(2048)){
    if(new FileInfo(path).Length>1048576)continue;string text=File.ReadAllText(path);
    string id=Regex.Match(text,@"""appid""\s+""([0-9]+)""").Groups[1].Value;
    string name=Regex.Match(text,@"""name""\s+""([^""]{1,200})""").Groups[1].Value;
    if(uint.TryParse(id,out uint app)&&app>0&&app.ToString()==id&&name.Contains(query,StringComparison.OrdinalIgnoreCase))games.Add(new(name,id));
   }
  }return games.DistinctBy(x=>x.AppId).OrderBy(x=>x.Name).Take(50).ToArray();
 }
}
