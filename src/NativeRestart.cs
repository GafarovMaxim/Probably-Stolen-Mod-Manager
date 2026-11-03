using System;
using UnityEngine;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private bool nativeRestartStarting;
  // Publishing still uses the companion's session handshake.
  private void InitializeRestartSession()=>Storage.Write("restart-session.json",new RestartSession{SessionId=sessionId,StartedUtc=DateTime.UtcNow.ToString("o")});
  private RestartHelperState RestartHelper()=>Storage.Read<RestartHelperState>("restart-helper.json");
  private void RequestManualRestart()
  {
   if(!InMenu||applying||PublishingBusy||nativeRestartStarting||Application.platform!=RuntimePlatform.WindowsPlayer)return;
   nativeRestartStarting=true;bool started=false;
   try
   {
    Storage.Write("manager.json",data);SavePublicationDrafts();journal.FlushAll();
    if(!NativeWindowsRestart.TryStartReplacement(out var error))
    {
     nativeRestartStarting=false;toast=T("Не удалось перезапустить игру. Игра останется открытой: ","Restart failed. The game will stay open: ")+error;journal.Add("error",toast);NativeInvalidate();return;
    }
    started=true;
    // The replacement process exists; keep the guard set even if quitting is cancelled.
    Application.Quit();
   }
   catch(Exception ex)
   {
    nativeRestartStarting=started;
    toast=started?T("Новый процесс запущен. Закрой текущее окно игры вручную: ","Replacement process started. Close this game window manually: ")+ex.Message:T("Перезапуск отменён; игра останется открытой: ","Restart cancelled; the game will stay open: ")+ex.Message;
    journal.Add("error",toast,ex.ToString());NativeInvalidate();
   }
  }
 }
}
