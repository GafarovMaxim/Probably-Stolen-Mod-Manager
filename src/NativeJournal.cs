using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private RectTransform journalListRoot,journalDetailRoot;
  private CustomUIElement journalSummary,journalPage,journalRepeats;
  private bool journalPaused;
  private List<LogMessage> JournalFiltered()
  {
   var rows=previousSession?archived:journal.Rows;
   return rows.Where(r=>levelFilter==0||(levelFilter==1?r.Level=="error":r.Level=="warning"))
    .Where(r=>(r.Message+" "+r.Stack+" "+r.Source).IndexOf(logSearch,StringComparison.OrdinalIgnoreCase)>=0).Reverse().ToList();
  }
  private void BuildJournal(CustomUIBuilder b)
  {
   b.BeginRow(10);
   NativeInput(b,T("Поиск: текст, мод, метод…","Search: message, mod, method…"),logSearch,value=>{logSearch=value;nativeLogPage=0;nativeLogSearchAt=Time.unscaledTime+.4f;});
   NativeSelect(b,new[]{T("Все уровни","All levels"),T("Ошибки","Errors"),T("Предупреждения","Warnings")},levelFilter,value=>{levelFilter=value;nativeLogPage=0;RefreshJournalList(true);},210);
   NativeButton(b,previousSession?T("Прошлая сессия","Previous session"):T("Текущая сессия","Current session"),()=>{previousSession=!previousSession;archived=previousSession?journal.Previous():new List<LogMessage>();nativeSelectedLog=null;nativeLogPage=0;NativeInvalidate();},240);
   NativeButton(b,journalPaused?T("Продолжить","Resume"):T("Пауза списка","Pause list"),()=>{journalPaused=!journalPaused;NativeInvalidate();},170);b.End();
   journalSummary=NativeLabel(b,"",19);
   b.BeginRow(20);b.BeginColumn(8);Size(b.CurrentContainer,nativeWidth*.43f,-1,0,1);
   b.BeginScrollFlexible();journalListRoot=b.CurrentContainer;StyleScroll(journalListRoot.GetComponentInParent<ScrollRect>());b.End();
   b.BeginRow(8);NativeButton(b,"←",()=>{if(nativeLogPage>0)nativeLogPage--;RefreshJournalList(true);},65);
   journalPage=NativeLabel(b,"",19);NativeButton(b,"→",()=>{if((nativeLogPage+1)*60<JournalFiltered().Count)nativeLogPage++;RefreshJournalList(true);},65);b.End();
   NativeButton(b,T("Экспорт журнала ZIP","Export log ZIP"),()=>{ExportSession();NativeInvalidate();});b.End();
   b.BeginScrollFlexible();journalDetailRoot=b.CurrentContainer;StyleScroll(journalDetailRoot.GetComponentInParent<ScrollRect>());b.End();b.End();
   RefreshJournalList(true);
  }
  private void RefreshJournalList(bool filterChanged=false)
  {
   if(journalListRoot==null||journalDetailRoot==null||tab!=2||applying||report.Length>0||nativeConfirmation!=null)return;
   var rows=previousSession?archived:journal.Rows;var found=JournalFiltered();
   var scroll=journalListRoot.GetComponentInParent<ScrollRect>();float offset=scroll.verticalNormalizedPosition;
   nativeLogPage=Mathf.Clamp(nativeLogPage,0,Math.Max(0,(found.Count-1)/60));var b=Into(journalListRoot);
   foreach(var row in found.Skip(nativeLogPage*60).Take(60))
   {
    var selected=row;string first=(row.Message??"").Split('\n')[0];
    var button=NativeButton(b,LocalTime(row.LastUtc)+"  "+LevelName(row.Level)+"  ×"+row.Count+"  "+first,()=>{nativeSelectedLog=selected;BuildJournalDetail(true);});
    button.SetPreferredSize(-1,56);if(button.text!=null){button.text.alignment=TMPro.TextAlignmentOptions.MidlineLeft;button.text.color=row.Level=="error"?new Color32(246,143,129,255):row.Level=="warning"?Warning:Mint;}
   }
   if(found.Count==0)NativeLabel(b,T("Событий не найдено","No matching events"));
   journalSummary.SetText(T("Ошибок: ","Errors: ")+rows.Where(r=>r.Level=="error").Sum(r=>(long)r.Count)+" · "+T("уникальных: ","unique: ")+rows.Count(r=>r.Level=="error")+" · "+T("предупреждений: ","warnings: ")+rows.Where(r=>r.Level=="warning").Sum(r=>(long)r.Count)+" · "+T("найдено: ","matching: ")+found.Count+(journalPaused?T(" · список на паузе"," · list paused"):""));
   journalPage.SetText((nativeLogPage+1)+" / "+Math.Max(1,(found.Count+59)/60));
   if(nativeSelectedLog==null||(filterChanged&&!found.Contains(nativeSelectedLog)))nativeSelectedLog=found.FirstOrDefault();
   // New messages update only the list and counter; the selected error and its ScrollRect stay alive.
   if(filterChanged||journalDetailRoot.childCount==0)BuildJournalDetail(filterChanged);
   CustomUIBuilder.RebuildLayout(journalListRoot);Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=filterChanged?1:offset;nativeLogTotal=journal.Total;
  }
  private string LevelName(string level)=>level=="error"?T("ОШИБКА","ERROR"):level=="warning"?T("ВНИМАНИЕ","WARNING"):T("СОБЫТИЕ","INFO");
  private static string LocalTime(string value)=>DateTime.TryParse(value,null,System.Globalization.DateTimeStyles.RoundtripKind,out var time)?time.ToLocalTime().ToString("HH:mm:ss"):value;
  private void BuildJournalDetail(bool reset)
  {
   if(journalDetailRoot==null)return;var scroll=journalDetailRoot.GetComponentInParent<ScrollRect>();var offset=scroll.verticalNormalizedPosition;var b=Into(journalDetailRoot);var row=nativeSelectedLog;
   if(row==null){NativeLabel(b,T("Выбери событие слева. Здесь появятся сообщение, причина и стек.","Select an event to inspect its message, cause and stack."));return;}
   var analysis=DiagnosticAnalyzer.Analyze(row,mods);
   b.AddHeader(LevelName(row.Level));journalRepeats=NativeLabel(b,"",18);UpdateJournalRepeats();
   NativeLabel(b,T("СООБЩЕНИЕ ДВИЖКА","ENGINE MESSAGE"),20,Mint);
   ReadOnlyLog(b,row.Message,150);
   b.AddSeparator();NativeLabel(b,T("ПРИЧИНА","CAUSE"),21,Mint);NativeLabel(b,Cause(analysis.Kind),21);
   NativeLabel(b,T("МОД И ОСНОВАНИЯ","MOD AND EVIDENCE"),21,Mint);
   if(analysis.CodeOwner.Length>0)
   {
    var mod=mods.First(m=>m.Id==analysis.CodeOwner);NativeLabel(b,mod.Name+"  v"+mod.Version,23,Warning);
    NativeLabel(b,T("Первый кадр стека принадлежит коду этого мода. Это место сбоя; первопричиной также могут быть входные данные или другой мод.","The first stack frame belongs to this mod. This identifies the failure location; input data or another mod can still be the root cause."),19);
    NativeLabel(b,analysis.Evidence,17);
   }
   else NativeLabel(b,T("Мод-виновник не установлен по доступному логу.","The available log does not establish a responsible mod."),21,Warning);
   foreach(var id in analysis.StackMods.Concat(analysis.MentionedMods).Distinct())
   {
    var mod=mods.FirstOrDefault(m=>m.Id==id);if(mod==null)continue;string evidence=analysis.StackMods.Contains(id)?T("код в стеке","code in stack"):T("упоминание, без доказанной связи","mentioned, not proven responsible");
    NativeButton(b,mod.Name+" · "+evidence,()=>{Select(id);selectedRows.Clear();selectedRows.Add(id);tab=0;NativeInvalidate();});
   }
   if(!string.IsNullOrEmpty(row.Source)&&row.Source!="Unattributed")NativeLabel(b,T("Источник записи: ","Recorded source: ")+row.Source,18);
   b.AddSeparator();NativeLabel(b,T("ЧТО ПРОВЕРИТЬ","WHAT TO CHECK"),21,Mint);NativeLabel(b,Remedy(analysis.Kind),20);
   NativeLabel(b,T("ПОЛНЫЙ СТЕК","FULL STACK TRACE"),21,Mint);
   if(string.IsNullOrWhiteSpace(row.Stack))NativeLabel(b,T("Игра не передала стек для этой записи.","The game did not supply a stack trace for this entry."),19);else ReadOnlyLog(b,row.Stack,260);
   b.BeginRow(8);NativeButton(b,T("Копировать ошибку","Copy error"),()=>{GUIUtility.systemCopyBuffer=EventReport(row);toast=T("Ошибка и диагностика скопированы","Error and diagnosis copied");});NativeButton(b,T("Полный отчёт","Full report"),()=>{reportTitle=T("Отчёт об ошибке","Error report");report=EventReport(row);NativeInvalidate();});b.End();
   NativeButton(b,T("Ранние сообщения Player.log","Startup Player.log"),()=>{reportTitle="Player.log";report=ReportRedactor.Redact(string.IsNullOrWhiteSpace(journal.Bootstrap)?T("Ранние сообщения недоступны","Startup messages unavailable"):journal.Bootstrap,home);NativeInvalidate();});
   CustomUIBuilder.RebuildLayout(journalDetailRoot);Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=reset?1:offset;
  }
  private void UpdateJournalRepeats(){if(journalRepeats?.IsAlive==true&&nativeSelectedLog!=null)journalRepeats.SetText(T("Первое: ","First: ")+LocalTime(nativeSelectedLog.Utc)+" · "+T("последнее: ","last: ")+LocalTime(nativeSelectedLog.LastUtc)+" · ×"+nativeSelectedLog.Count);}
  private void ReadOnlyLog(CustomUIBuilder b,string text,float height)
  {
   var field=NativeInput(b,"",text??"",_=>{},true);field.SetPreferredSize(-1,height);
   if(field.input!=null){field.input.readOnly=true;field.input.characterLimit=0;if(field.input.textComponent!=null){field.input.textComponent.richText=false;field.input.textComponent.fontSize=18;}if(field.input.verticalScrollbar!=null)StyleScrollbar(field.input.verticalScrollbar);}
  }
  private string Cause(string kind)
  {
   switch(kind){case "font":return T("В выбранном шрифте нет глифа, а подходящий CJK fallback не был доступен. TextMeshPro заменил символ пробелом.","The selected font lacks the glyph and a suitable CJK fallback was unavailable. TextMeshPro substituted a space.");case "component":return T("Unity запретила удалить компонент локализации: TMPLocalizer требует LocalizeStringEvent. Нужно сохранять компонент и отключать лишний обработчик, а не удалять зависимость.","Unity refused to remove a required localization component. TMPLocalizer requires LocalizeStringEvent; preserve the component and disable the copied handler.");case "null":return T("NullReferenceException: код обратился к отсутствующему объекту. В сообщении обычно не указано, почему объект не был создан.","NullReferenceException: code accessed a null object. The message usually does not explain why it was absent.");case "api":return T("Метод или тип не найден: возможное различие версий API игры, библиотеки или мода.","A method or type is missing: game, library or mod API versions may differ.");case "file":return T("Не найден файл или каталог. Точный путь указан в исходном сообщении.","A file or directory is missing. The original message contains the path.");case "index":return T("Обращение за пределы коллекции или недопустимый индекс.","A collection index was outside its valid range.");case "access":return T("Операционная система отказала в доступе к ресурсу.","The operating system denied access to a resource.");case "dependency":return T("Сообщение связано с зависимостью мода. Проверь исходный текст: это может быть ошибка или предупреждение о метаданных.","The message concerns a mod dependency. Check the original text: this can be a failure or a metadata warning.");default:return T("По тексту нельзя надёжно установить первопричину. Ниже сохранены исходное сообщение и полный стек.","The text does not establish a root cause reliably. The original message and full stack are preserved below.");}
  }
  private string Remedy(string kind)
  {
   if(kind=="font")return T("Проверь подключение штатных китайского, японского и корейского fallback-шрифтов. После обновления менеджера перезапусти игру; старые записи останутся в предыдущем журнале.","Check the game CJK fallback fonts. Restart after updating the manager; old warnings remain in the previous log.");
   if(kind=="component")return T("Проверь код клонирования кнопок. Исправление совместимости MCM в менеджере сохраняет обязательные компоненты.","Check button cloning code. The manager MCM compatibility fix preserves required components.");
   if(kind=="api"||kind=="dependency")return T("Сверь версии игры, мода и требуемых библиотек; наличие зависимостей и порядок загрузки. Сохрани этот отчёт для автора.","Compare game, mod and library versions; check dependencies and load order. Save this report for the author.");
   if(kind=="file"||kind=="access")return T("Проверь указанный путь, наличие файла и разрешения доступа. Не меняй права и не переустанавливай моды без проверки исходного сообщения.","Check the reported path, file and access permissions before changing permissions or reinstalling mods.");
   return T("Сохрани шаги воспроизведения и первое появление ошибки. Сравни профили на тестовом сохранении; передай автору стек и версии из отчёта.","Record reproduction steps and the first occurrence. Compare profiles on a test save; send the stack and versions from this report to the author.");
  }
  private string EventReport(LogMessage row)
  {
   var a=DiagnosticAnalyzer.Analyze(row,mods);string diagnosis=Cause(a.Kind)+"\n"+T("Владелец первого кадра: ","First-frame code owner: ")+(a.CodeOwner.Length>0?a.CodeOwner:T("не установлен","unknown"))+"\n"+a.Evidence+"\n"+Remedy(a.Kind);
   return ReportRedactor.Redact((previousSession?"ARCHIVED EVENT; environment below is current.\n":"")+diagnosis+"\n\n"+journal.MessageReport(row,EnvironmentReport(),home),home);
  }
 }
}
