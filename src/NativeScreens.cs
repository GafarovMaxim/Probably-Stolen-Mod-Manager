using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private bool nativeAccepted;
  private LogMessage nativeSelectedLog;
  private int nativeLogPage;
  private long nativeLogTotal;
  private float nativeNextLogRefresh;
  private void BuildApproval(CustomUIBuilder b)
  {
   b.AddHeader(T("Разрешения выбранных модов","Review mod capabilities"));b.BeginScrollFlexible();
   foreach(var pair in checks??new List<KeyValuePair<LoadedMod,ModEnableCheck>>())
   {NativeLabel(b,pair.Key.Manifest.Name+" / "+pair.Key.Manifest.ModVersion,25,Mint);NativeLabel(b,pair.Key.Manifest.Capabilities.Count>0?string.Join(", ",pair.Key.Manifest.Capabilities.Select(ModCapability.DisplayName)):T("Дополнительные разрешения не нужны","No additional capabilities"),22);b.AddSpace(12);}
   bool consent=checks?.Any(p=>p.Value.NeedsPrompt)==true;
   if(consent){NativeLabel(b,T("Подтверждай только моды доверенных авторов. Проверка игры не гарантирует безопасность кода.","Only approve mods from authors you trust. The game scan cannot guarantee safe code."),21,Warning);b.AddToggle(T("Я просмотрел разрешения и доверяю этим модам","I reviewed these capabilities and trust these mods"),nativeAccepted,value=>{nativeAccepted=value;NativeInvalidate();});}
   NativeLabel(b,T("Изменение списка вступит в силу после ручного перезапуска игры.","Changes take effect after you manually restart the game."),21);b.End();b.BeginRow(12);
   NativeButton(b,T("Назад","Back"),()=>{applying=false;checks=null;pendingProfile=null;pendingProfileOrder=null;staged=EnabledOrder();NativeInvalidate();},220);
   NativeButton(b,T("Подтвердить","Approve and apply"),()=>{CommitApply();nativeAccepted=false;NativeInvalidate();},300,InMenu&&(!consent||nativeAccepted));b.End();
  }
  private void BuildConfirmation(CustomUIBuilder b)
  {
   b.AddHeader(T("Зависимые моды","Dependent mods"));b.BeginScrollFlexible();NativeLabel(b,confirmationText,23);b.End();b.BeginRow(12);
   NativeButton(b,T("Отмена","Cancel"),()=>{nativeConfirmation=null;NativeInvalidate();},240);
   NativeButton(b,T("Выключить вместе","Disable together"),()=>{var action=nativeConfirmation;nativeConfirmation=null;action?.Invoke();NativeInvalidate();},320,InMenu);b.End();
  }
  private void BuildReport(CustomUIBuilder b)
  {
   b.AddHeader(reportTitle);b.BeginScrollFlexible();var field=NativeInput(b,"",report,value=>report=value,true);field.SetPreferredSize(-1,nativeHeight-120);if(field.input!=null)field.input.readOnly=true;b.End();b.BeginRow(12);
   NativeButton(b,T("Копировать","Copy"),()=>{GUIUtility.systemCopyBuffer=report;toast=T("Текст скопирован","Copied");},250);
   NativeButton(b,T("Закрыть","Close"),()=>{report="";NativeInvalidate();},250);b.End();
  }
  private void BuildProfiles(CustomUIBuilder b)
  {
   b.BeginRow(24);b.BeginColumn(8);Size(b.CurrentContainer,340,-1,0,1);b.BeginScrollFlexible();
   for(int i=0;i<data.Profiles.Count;i++){int index=i;var profile=data.Profiles[i];var button=NativeButton(b,profile.Name+" ("+profile.Order.Count+")",()=>{profileIndex=index;NativeInvalidate();});var context=button.root.AddComponent<NativeContextTarget>();context.Open=point=>Act(()=>{profileIndex=index;ProfileContext(profile,point);});}b.End();
   NativeInput(b,T("Название профиля…","Profile name…"),profileName,value=>profileName=value);NativeButton(b,T("Сохранить текущий набор","Save current selection"),SaveProfile);NativeButton(b,T("Импорт JSON","Import JSON"),()=>{showSettings=true;importJson="";NativeInvalidate();});b.End();
   b.BeginScrollFlexible();if(data.Profiles.Count==0)NativeLabel(b,T("Профилей пока нет. Сохрани текущий набор слева.","No profiles yet. Save the current selection on the left."));if(data.Profiles.Count>0)
   {
    profileIndex=Mathf.Clamp(profileIndex,0,data.Profiles.Count-1);var p=data.Profiles[profileIndex];b.AddHeader(p.Name);NativeButton(b,T("Удалить профиль","Delete profile"),()=>DeleteProfile(p),250);NativeLabel(b,p.GameVersion,20);b.AddSeparator();NativeLabel(b,T("Отличия от активного набора","Difference from active selection"),23,Mint);var diff=Planner.ProfileDifference(p,mods,EnabledOrder());NativeLabel(b,diff=="="?T("Состав и версии совпадают.","Selection and versions match."):diff,22,diff=="="?Muted:Warning);
    b.BeginRow(12);NativeButton(b,T("Активировать профиль","Activate profile"),()=>NativeActivateProfile(p),300,InMenu);
    NativeButton(b,T("Экспорт","Export"),()=>{reportTitle=p.Name;report=JsonConvert.SerializeObject(p,Formatting.Indented);NativeInvalidate();},200);b.End();
    b.AddSeparator();NativeLabel(b,T("Зафиксированные версии","Pinned versions"),23,Mint);foreach(var id in p.Order)NativeLabel(b,(mods.FirstOrDefault(m=>m.Id==id)?.Name??id)+"  "+(p.Versions.TryGetValue(id,out var version)?version:"—"),21);
    NativeLabel(b,T("Профиль хранит состав и версии, а не файлы модов.","A profile stores metadata, not mod binaries."),20,Warning);

   }
   if(showSettings){b.AddSeparator();NativeLabel(b,T("Вставь JSON профиля","Paste profile JSON"));NativeInput(b,"JSON",importJson,value=>importJson=value,true);b.BeginRow(12);NativeButton(b,T("Импортировать","Import"),()=>{ImportProfile();NativeInvalidate();},260);NativeButton(b,T("Отмена","Cancel"),()=>{showSettings=false;NativeInvalidate();},200);b.End();}b.End();b.End();
  }
  private void SaveProfile()
  {
   if(TryCreateCurrentProfile(profileName)){profileName="";NativeInvalidate();}
  }
  private void ImportProfile()
  {
   try{if(importJson.Length>512000)throw new InvalidOperationException("Profile JSON is too large.");var p=JsonConvert.DeserializeObject<Profile>(importJson,new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MaxDepth=16});if(p==null||p.Order==null||string.IsNullOrWhiteSpace(p.Name)||p.Order.Count>10000||p.Order.Any(id=>!ModLoader.IsValidModId(id)))throw new InvalidOperationException("Invalid profile.");p.Order=p.Order.Distinct().ToList();p.Versions=p.Versions??new Dictionary<string,string>();if(data.Profiles.Count>=100)throw new InvalidOperationException("Profile limit: 100");if(data.Profiles.Any(x=>x.Name==p.Name))p.Name+=" (import)";data.Profiles.Add(p);profileIndex=data.Profiles.Count-1;Save();showSettings=false;importJson="";toast=T("Профиль импортирован. Проверь различия перед активацией.","Profile imported. Review differences before activation.");}catch(Exception ex){toast=ex.Message;}
  }
  private float nativeLogSearchAt=-1;
  private void ExportSession()
  {
   try{var path=journal.Export(EnvironmentReport(),data.History,home);reportTitle=T("Отчёт сохранён","Report saved");report=path+"\n\n"+T("Архив содержит журнал, окружение и историю. Перед публикацией проверь содержимое. Сохранения не включены.","Archive includes logs, environment and history. Review before sharing. Save files are not included.");}catch(Exception ex){toast=ex.Message;journal.Add("error","Report export failed",ex.ToString());}
  }
 }
}
