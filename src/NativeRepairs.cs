using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private Harmony compatibilityHarmony;
  private MethodInfo mcmOpen;
  private bool compatibilityInstalled;
  private float nextRepairTick;
  private readonly List<KeyValuePair<TMP_FontAsset,TMP_FontAsset>> fontLinks=new List<KeyValuePair<TMP_FontAsset,TMP_FontAsset>>();
  private readonly List<TMP_FontAsset> globalFontLinks=new List<TMP_FontAsset>();
  private bool fontDiagnosticsWritten;
  private void InitializeRepairs()
  {
   AddCjkFallbacks();InstallMcmRepair();LocalizationSettings.SelectedLocaleChanged+=RepairsLocaleChanged;
  }
  private void RepairsLocaleChanged(Locale locale){AddCjkFallbacks();RepairMcmButton();}
  private void TickRepairs()
  {
   if(Time.unscaledTime<nextRepairTick)return;nextRepairTick=Time.unscaledTime+2;
   if(!compatibilityInstalled)InstallMcmRepair();AddCjkFallbacks();
   if(InMenu)RepairMcmButton();
  }
  private void AddCjkFallbacks()
  {
   var loader=FontLoader.Instance;if(loader==null)return;
   var candidates=new[]{loader.simplifiedChineseFont,loader.japaneseFont,loader.koreanFont}.Where(f=>f!=null).Distinct().ToList();
   foreach(var fallback in candidates)if(TMP_Settings.fallbackFontAssets!=null&&!TMP_Settings.fallbackFontAssets.Contains(fallback)){TMP_Settings.fallbackFontAssets.Add(fallback);globalFontLinks.Add(fallback);}
   foreach(var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>().Where(f=>f!=null&&!candidates.Contains(f)))
   {
    if(font.fallbackFontAssetTable==null)font.fallbackFontAssetTable=new List<TMP_FontAsset>();
    foreach(var fallback in candidates)
    {
     if(font.fallbackFontAssetTable.Contains(fallback)||FontReaches(fallback,font,new HashSet<TMP_FontAsset>()))continue;
     font.fallbackFontAssetTable.Add(fallback);fontLinks.Add(new KeyValuePair<TMP_FontAsset,TMP_FontAsset>(font,fallback));
    }
   }
   if(!fontDiagnosticsWritten&&candidates.Count>0){fontDiagnosticsWritten=true;journal.Add("info","CJK fallback fonts connected: "+string.Join(", ",candidates.Select(f=>f.name))+"; Chinese sample available="+(loader.notoSans!=null&&loader.notoSans.HasCharacter('中',true,true)));}
  }
  private static bool FontReaches(TMP_FontAsset source,TMP_FontAsset target,HashSet<TMP_FontAsset> seen)
  {
   if(source==target)return true;if(source==null||!seen.Add(source))return false;
   return source.fallbackFontAssetTable?.Any(f=>FontReaches(f,target,seen))==true;
  }
  private void InstallMcmRepair()
  {
   if(compatibilityInstalled)return;
   var assembly=ModLoader.Mods.FirstOrDefault(m=>m.Manifest.ID=="probably_mcm"&&m.IsEnabled)?.Assembly;
   var type=assembly?.GetType("ProbablyMCM.Patches.MainMenuPatch");
   var ensure=type?.GetMethod("EnsureButton",BindingFlags.Static|BindingFlags.Public);
   if(ensure==null)return;
   mcmOpen=assembly.GetType("ProbablyMCM.UI.ModSettingsWindow")?.GetMethod("Open",BindingFlags.Static|BindingFlags.Public);
   compatibilityHarmony=new Harmony("probably_mod_manager.mcm_compatibility");
   compatibilityHarmony.Patch(ensure,prefix:new HarmonyMethod(typeof(Manager).GetMethod(nameof(McmEnsurePrefix),BindingFlags.Static|BindingFlags.NonPublic)));
   compatibilityInstalled=true;RepairMcmButton();
  }
  private static bool McmEnsurePrefix(MainMenuUIController __0)
  {
   if(Instance==null)return true;
   try{Instance.RepairMcmButton(__0);return false;}catch(Exception e){Instance.journal.Add("warning","MCM button compatibility: "+e.Message);return true;}
  }
  private void RepairMcmButton(MainMenuUIController menu=null)
  {
   if(!compatibilityInstalled||mcmOpen==null)return;menu=menu??MainMenuUIController.Instance;if(menu==null)return;
   var template=menu.creditButton!=null?menu.creditButton:menu.moddingButton;if(template==null||template.transform.parent==null)return;
   var parent=template.transform.parent;var existing=parent.Find("ProbablyMCM_MainMenuButton");GameObject buttonObject;
   if(existing==null)
   {
    buttonObject=Instantiate(template,parent);buttonObject.name="ProbablyMCM_MainMenuButton";buttonObject.transform.SetSiblingIndex(template.transform.GetSiblingIndex()+1);
    var button=buttonObject.GetComponent<Button>();if(button!=null){button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{menu.OnAnyButtonClick();mcmOpen.Invoke(null,new object[]{null});});}
   }
   else buttonObject=existing.gameObject;
   // Keep required components. Disable the copied localization event instead of destroying it.
   foreach(var localizer in buttonObject.GetComponentsInChildren<TMPLocalizer>(true))localizer.ChangeFontOnly=true;
   foreach(var localization in buttonObject.GetComponentsInChildren<LocalizeStringEvent>(true)){localization.enabled=false;localization.OnUpdateString.RemoveAllListeners();}
   string language=LocalizationSettings.SelectedLocale?.Identifier.Code??"en";
   string caption=ButtonLocalization.McmCaption(language);
   var loaded=ModLoader.Mods.FirstOrDefault(m=>m.Manifest.ID=="probably_mcm");
   if(loaded!=null&&loaded.Localization.TryGetValue(language,out var table)&&table.TryGetValue("mcm_esc_button",out var translated)&&!string.IsNullOrWhiteSpace(translated))caption=translated;
   var text=buttonObject.GetComponentInChildren<TMP_Text>(true);if(text!=null&&text.text!=caption)text.text=caption;
   buttonObject.SetActive(true);
  }
  private void DisposeRepairs()
  {
   LocalizationSettings.SelectedLocaleChanged-=RepairsLocaleChanged;
   compatibilityHarmony?.UnpatchAll("probably_mod_manager.mcm_compatibility");
   foreach(var pair in fontLinks)if(pair.Key!=null)pair.Key.fallbackFontAssetTable?.Remove(pair.Value);fontLinks.Clear();
   foreach(var font in globalFontLinks)TMP_Settings.fallbackFontAssets?.Remove(font);globalFontLinks.Clear();
  }
 }
}
