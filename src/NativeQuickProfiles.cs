using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private bool quickProfileDialog;
  private string quickProfileName="";
  private Profile pendingProfile;
  private List<string> pendingProfileOrder;

  private Profile ActiveProfile()=>data.Profiles.FirstOrDefault(p=>string.Equals(p.Name,data.ActiveProfileName,StringComparison.Ordinal));
  private List<string> ProfileOrder(Profile profile)=>(profile.Order.Contains(Id)?profile.Order:new[]{Id}.Concat(profile.Order)).Distinct().ToList();
  private Profile CurrentProfile(string name)
  {
   var order=EnabledOrder();
   return new Profile{Name=name,GameVersion=PlayerStore.VERSION,Order=order.ToList(),Versions=mods.Where(m=>order.Contains(m.Id)).GroupBy(m=>m.Id).ToDictionary(g=>g.Key,g=>g.First().Version)};
  }
  private void BuildQuickProfiles(CustomUIBuilder b)
  {
   var profiles=data.Profiles.ToList();var active=ActiveProfile();var order=EnabledOrder();
   var labels=new List<string>{T("Текущий набор","Current selection")};
   labels.AddRange(profiles.Select(p=>p.Name+(p==active&&!ProfileOrder(p).SequenceEqual(order)?T(" • изменён"," • modified"):"")));
   labels.Add(T("Сохранить как…","Save as…"));
   // A modified selection stays on item 0 so selecting its saved profile can restore it again.
   int selected=active==null||!ProfileOrder(active).SequenceEqual(order)?0:profiles.IndexOf(active)+1;
   var dropdown=NativeSelect(b,labels,selected,value=>
   {
    if(value==labels.Count-1)OpenQuickProfileDialog();
    else if(value>0&&value<=profiles.Count)NativeActivateProfile(profiles[value-1]);
    else {data.ActiveProfileName="";Save();NativeInvalidate();}
   },220,InMenu);
   if(dropdown.text!=null){dropdown.text.enableWordWrapping=false;dropdown.text.overflowMode=TextOverflowModes.Ellipsis;dropdown.text.richText=false;}
   var save=NativeButton(b,T("Сохранить","Save"),QuickSaveProfile,120);
   save.root.AddComponent<NativeContextTarget>().Open=point=>Act(()=>ShowContext(point,new[]{new KeyValuePair<string,Action>(T("Сохранить как…","Save as…"),OpenQuickProfileDialog)}));
  }
  private void NativeActivateProfile(Profile profile)
  {
   if(!InMenu||profile==null||!data.Profiles.Contains(profile))return;
   pendingProfile=null;pendingProfileOrder=null;
   var target=ProfileOrder(profile);findings=Planner.Validate(mods,target,Id);
   if(findings.Any(f=>f.Blocks))
   {
    reportTitle=T("Проверка профиля","Profile validation");report=string.Join("\n\n",findings.Select(f=>f.Title+"\n"+f.Detail));NativeInvalidate();return;
   }
   profileIndex=data.Profiles.IndexOf(profile);
   if(target.SequenceEqual(EnabledOrder())){data.ActiveProfileName=profile.Name;Save();NativeInvalidate();return;}
   pendingProfile=profile;pendingProfileOrder=target.ToList();
   NativeApplyOrder(target);
   // Immediate failures do not leave a candidate that a later unrelated apply could activate.
   if(!applying){pendingProfile=null;pendingProfileOrder=null;}
   NativeInvalidate();
  }
  private void CompleteProfileActivation()
  {
   if(pendingProfile!=null&&pendingProfileOrder!=null&&data.Profiles.Contains(pendingProfile)&&applyTarget.SequenceEqual(pendingProfileOrder)&&EnabledOrder().SequenceEqual(pendingProfileOrder))data.ActiveProfileName=pendingProfile.Name;
   pendingProfile=null;pendingProfileOrder=null;
  }
  private void OpenQuickProfileDialog(){quickProfileName="";quickProfileDialog=true;NativeInvalidate();}
  private void BuildQuickProfileDialog(CustomUIBuilder b)
  {
   b.AddHeader(T("Сохранить набор как профиль","Save selection as profile"));
   NativeLabel(b,T("Сохраняются текущий состав, порядок и версии модов.","Stores the current selection, order and mod versions."));
   var input=NativeInput(b,T("Название профиля…","Profile name…"),quickProfileName,value=>quickProfileName=value);
   if(input.input!=null)input.input.characterLimit=100;
   b.BeginRow(12);
   NativeButton(b,T("Сохранить","Save"),()=>{if(TryCreateCurrentProfile(quickProfileName)){quickProfileDialog=false;quickProfileName="";}NativeInvalidate();},220);
   NativeButton(b,T("Отмена","Cancel"),()=>{quickProfileDialog=false;quickProfileName="";NativeInvalidate();},220);b.End();b.AddFlexibleSpace();
  }
  private bool TryCreateCurrentProfile(string draft)
  {
   string name=(draft??"").Trim();
   if(name.Length==0){toast=T("Введи название профиля","Enter a profile name");return false;}
   if(name.Length>100){toast=T("Название должно быть не длиннее 100 символов","Use a profile name of at most 100 characters");return false;}
   if(data.Profiles.Any(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))){toast=T("Такое имя уже существует","Profile name already exists");return false;}
   if(data.Profiles.Count>=100){toast=T("Лимит профилей: 100","Profile limit: 100");return false;}
   var profile=CurrentProfile(name);string oldActive=data.ActiveProfileName;int oldIndex=profileIndex;var history=data.History.ToList();
   try
   {
    data.Profiles.Add(profile);data.ActiveProfileName=name;profileIndex=data.Profiles.Count-1;data.HistoryAdd(T("Сохранён профиль: ","Profile saved: ")+name);
    Storage.Write("manager.json",data);toast=T("Профиль сохранён: ","Profile saved: ")+name;return true;
   }
   catch(Exception ex){data.Profiles.Remove(profile);data.ActiveProfileName=oldActive;profileIndex=oldIndex;data.History=history;ProfileSaveFailed(ex);return false;}
  }
  private void QuickSaveProfile()
  {
   var profile=ActiveProfile();if(profile==null){OpenQuickProfileDialog();return;}
   var replacement=CurrentProfile(profile.Name);int index=data.Profiles.IndexOf(profile);var history=data.History.ToList();
   try
   {
    // Write the previous profile successfully before changing any in-memory or persisted profile data.
    Storage.Write("profile-backups/"+Guid.NewGuid().ToString("N")+".json",profile);
    data.Profiles[index]=replacement;data.HistoryAdd(T("Обновлён профиль: ","Profile updated: ")+profile.Name);
    Storage.Write("manager.json",data);toast=T("Профиль обновлён: ","Profile updated: ")+profile.Name;
   }
   catch(Exception ex){data.Profiles[index]=profile;data.History=history;ProfileSaveFailed(ex);}
   NativeInvalidate();
  }
  private void ProfileSaveFailed(Exception ex){toast=T("Не удалось сохранить профиль: ","Profile save failed: ")+ex.Message;journal.Add("error",toast);}
 }
}
