using System.IO;
using System.Text.Json;
namespace SentinelX.Services.Maintenance;
public static class DirectoryRepair
{
 public static string Run(string[] folders,string backups,Action<string>? create=null){
  create??=p=>Directory.CreateDirectory(p); // replaced with lambda below
  string backup=Path.Combine(backups,"repair-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
  string log=Path.Combine(backup,"repair.jsonl");var created=new List<string>();
  void Record(string phase,string result)=>File.AppendAllText(log,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,phase,result})+"\n");
  Record("detect","started");
  File.WriteAllText(Path.Combine(backup,"directories.json"),JsonSerializer.Serialize(folders.Select(p=>new{path=Path.GetFileName(p),exists=Directory.Exists(p)})));
  Record("backup","succeeded");
  try{
   foreach(string folder in folders){if(!Directory.Exists(folder)){created.Add(folder);create(folder);}}
   Record("repair","succeeded");
   if(folders.Any(p=>!Directory.Exists(p)))throw new IOException("verify");
   Record("verify","succeeded");Record("result","succeeded");return backup;
  }catch{
   foreach(string folder in created.AsEnumerable().Reverse())if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
   Record("rollback",created.All(p=>!Directory.Exists(p))?"succeeded":"preserved_nonempty_directory");
   Record("result","failed");throw;
  }
 }
}
