namespace ProbablyModManager
{
 public static class ButtonLocalization
 {
  public static string McmCaption(string language)
  {
   switch((language??"en").ToLowerInvariant()){case "ru":return "MCM-меню";case "zh":case "zh-hans":case "zh-hant":return "MCM 菜单";case "ja":return "MCM メニュー";case "ko":return "MCM 메뉴";case "fr":return "Menu MCM";case "de":return "MCM-Menü";case "es":return "Menú MCM";case "pl":return "Menu MCM";default:return "MCM Menu";}
  }
 }
}
