using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net;

namespace ProbablyModManager
{
 public sealed class LogMessage
 {
  public string Utc="", LastUtc="", Level="", Message="", Stack="", Source="";
  public int Count=1;
 }
 public sealed class LogDiagnosis
 {
  public string Kind="unknown", Evidence="", CodeOwner="";
  public List<string> StackMods=new List<string>(), MentionedMods=new List<string>();
 }
 public static class DiagnosticAnalyzer
 {
  // Ownership is established from actual loaded types, never from a guessed namespace equal to a mod ID.
  public static LogDiagnosis Analyze(LogMessage row,IReadOnlyList<ModInfo> mods)
  {
   var result=new LogDiagnosis();var all=(row.Message??"")+"\n"+(row.Stack??"");
   var lower=all.ToLowerInvariant();
   foreach(var pair in new[]{new[]{"nullreferenceexception","null"},new[]{"missingmethodexception","api"},new[]{"typeloadexception","api"},new[]{"filenotfoundexception","file"},new[]{"directorynotfoundexception","file"},new[]{"indexoutofrangeexception","index"},new[]{"argumentoutofrangeexception","index"},new[]{"unauthorizedaccessexception","access"},new[]{"dependency","dependency"},new[]{"prerequisite","dependency"}})
    if(lower.Contains(pair[0])){result.Kind=pair[1];break;}
   if(lower.Contains("the character with unicode value")&&lower.Contains("font asset"))result.Kind="font";
   if(lower.Contains("can't remove localizestringevent")&&lower.Contains("tmplocalizer"))result.Kind="component";
   var frames=(row.Stack??"").Split('\n').Where(line=>!string.IsNullOrWhiteSpace(line)).ToList();
   for(int i=0;i<frames.Count;i++)
   {
    var line=frames[i].Trim();var owners=mods.Where(m=>(m.TypeNames??Array.Empty<string>()).Any(t=>TypeFrame(line,t))).ToList();
    foreach(var m in owners)if(!result.StackMods.Contains(m.Id))result.StackMods.Add(m.Id);
    // Only the first concrete frame can identify the code where the exception surfaced.
    if(i==0&&owners.Count==1){result.CodeOwner=owners[0].Id;result.Evidence=line;}
   }
   foreach(var mod in mods)
    if((!string.IsNullOrWhiteSpace(mod.Id)&&Regex.IsMatch(row.Message??"",@"(?<![\w.\-])"+Regex.Escape(mod.Id)+@"(?![\w.\-])",RegexOptions.IgnoreCase))||
       (!string.IsNullOrEmpty(mod.LoadError)&&(row.Message??"").Contains(mod.LoadError)))result.MentionedMods.Add(mod.Id);
   return result;
  }
  private static bool TypeFrame(string line,string type)
  {
   if(string.IsNullOrEmpty(type))return false;
   foreach(var name in new[]{type,type.Replace('+','.'),type.Replace('+','/')})
   {int start=line.IndexOf(name,StringComparison.Ordinal);if(start<0)continue;int end=start+name.Length;if((start==0||!IsName(line[start-1]))&&end<line.Length&&(line[end]=='.'||line[end]==':'))return true;}
   return false;
  }
  private static bool IsName(char c)=>char.IsLetterOrDigit(c)||c=='_'||c=='.';
 }
 public sealed class WorkshopChange
 {
  public string Date="",Text="";
 }
 public static class WorkshopChangeParser
 {
  public static List<WorkshopChange> Parse(string html)
  {
   var result=new List<WorkshopChange>();
   foreach(var chunk in Regex.Split(html??"",@"<div\s+class=""detailBox workshopAnnouncement[^""]*""",RegexOptions.IgnoreCase).Skip(1))
   {
    var date=Regex.Match(chunk,@"<div\s+class=""changelog headline""[^>]*>(.*?)</div>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
    var body=Regex.Match(chunk,@"<p\s+id=""\d+""[^>]*>(.*?)</p>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
    if(body.Success)result.Add(new WorkshopChange{Date=Plain(date.Groups[1].Value),Text=Plain(body.Groups[1].Value)});
   }
   return result.Take(20).ToList();
  }
  private static string Plain(string value)
  {
   value=Regex.Replace(value,@"<(script|style)\b[^>]*>.*?</\1>","",RegexOptions.Singleline|RegexOptions.IgnoreCase);
   value=Regex.Replace(value,@"<br\s*/?>|</(?:p|div|li|h[1-6])>","\n",RegexOptions.IgnoreCase);
   return WebUtility.HtmlDecode(Regex.Replace(value,"<[^>]+>","")).Trim();
  }
 }
}
