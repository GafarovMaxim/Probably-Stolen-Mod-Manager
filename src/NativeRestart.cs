using System;
using UnityEngine;

namespace ProbablyModManager
{
 public sealed partial class Manager
 {
  private bool manualRestartDialog,manualExitConfirmed;
  private ManualRestartRequest manualRestartRequest;
  private float manualRequestStarted;
  private void InitializeRestartSession()=>Storage.Write("restart-session.json",new RestartSession{SessionId=sessionId,StartedUtc=DateTime.UtcNow.ToString("o")});
  private RestartHelperState RestartHelper()=>Storage.Read<RestartHelperState>("restart-helper.json");
  private void BuildManualRestart(CustomUIBuilder b)
  {
   b.AddHeader(T("Перезапустить игру?","Restart the game?"));b.BeginScrollFlexible();
   NativeLabel(b,T("Игра будет закрыта обычным способом и снова запущена через Steam. Выбранный список модов уже сохранён.","The game will close normally and launch again through Steam. Your selected mod list is already saved."),23);
   bool ready=RestartProtocol.HelperReady(RestartHelper(),sessionId,DateTime.UtcNow);
   if(!ready)NativeLabel(b,T("Помощник рестарта не подключён. Запусти «Probably Mod Manager.exe» из папки игры: если игра уже открыта, он просто подключится к ней. После этого открой это окно снова.","The restart helper is not connected. Launch Probably Mod Manager.exe from the game folder; it attaches to an already running game. Then reopen this dialog."),22,Warning);
   NativeLabel(b,T("Перезапуск доступен только из главного меню. Изменение списка само по себе игру не перезапускает.","Restart is available only from the main menu. Changing the mod list never triggers a restart."),20);b.End();b.BeginRow(12,TextAnchor.MiddleRight);b.AddFlexibleSpace();
   NativeButton(b,T("Отмена","Cancel"),()=>{manualRestartDialog=false;NativeInvalidate();},230);
   NativeButton(b,T("Перезапустить сейчас","Restart now"),RequestManualRestart,310,ready&&InMenu&&!applying);b.End();
  }
  private void RequestManualRestart()
  {
   if(!InMenu||applying||manualRestartRequest!=null||PublishingBusy)return;var helper=RestartHelper();
   if(!RestartProtocol.HelperReady(helper,sessionId,DateTime.UtcNow)){toast=T("Помощник недоступен; игра останется открытой.","Helper unavailable; game will stay open.");manualRestartDialog=false;NativeInvalidate();return;}
   var request=new ManualRestartRequest{Token=helper.Token,SessionId=sessionId,RequestId=Guid.NewGuid().ToString("N"),CreatedUtc=DateTime.UtcNow.ToString("o")};
   Save();journal.FlushAll();Storage.Write("restart-request.json",request);manualRestartRequest=request;manualRequestStarted=Time.unscaledTime;manualRestartDialog=false;toast=T("Ожидание подтверждения помощника…","Waiting for helper acknowledgement…");NativeInvalidate();
  }
  private void TickManualRestart()
  {
   if(manualRestartRequest==null)return;
   if(!InMenu){CancelManualRequest();toast=T("Перезапуск отменён: открыто сохранение.","Restart cancelled: a save is open.");return;}
   if(RestartProtocol.Acknowledged(RestartHelper(),manualRestartRequest,DateTime.UtcNow)){FinishManualRestart();return;}
   if(Time.unscaledTime-manualRequestStarted>8){CancelManualRequest();toast=T("Помощник не подтвердил запрос. Игра осталась открытой.","Helper did not acknowledge the request. Game remains open.");}
  }
  private void CancelManualRequest(){if(manualRestartRequest==null||manualExitConfirmed)return;manualRestartRequest.Cancelled=true;Storage.Write("restart-request.json",manualRestartRequest);manualRestartRequest=null;}
  private void FinishManualRestart()
  {
   // This is the only exit call: reachable only after an explicit UI confirmation and matching helper acknowledgement.
   if(manualRestartRequest==null||!InMenu||!RestartProtocol.Acknowledged(RestartHelper(),manualRestartRequest,DateTime.UtcNow))return;
   manualExitConfirmed=true;manualRestartRequest=null;Save();journal.FlushAll();Application.Quit();
  }
 }
}
