using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ProbablyModManager;

internal static class Program
{
 [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
 [return: MarshalAs(UnmanagedType.Bool)]
 private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

 private static void Require(bool value, string message)
 {
  if (!value) throw new Exception(message);
 }

 private static void Main(string[] args)
 {
  if (args.Length == 1 && args[0] == "--test-child") { System.Threading.Thread.Sleep(1200); return; }
  Require(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Checks require Windows.");
  bool x64 = IntPtr.Size == 8;
  Require(Marshal.SizeOf<NativeWindowsRestart.StartupInfo>() == (x64 ? 104 : 68), "STARTUPINFO size");
  Require(Marshal.SizeOf<NativeWindowsRestart.ProcessInformation>() == (x64 ? 24 : 16), "PROCESS_INFORMATION size");
  Require(Marshal.OffsetOf<NativeWindowsRestart.StartupInfo>("lpReserved2").ToInt32() == (x64 ? 72 : 52), "STARTUPINFO reserved pointer alignment");
  Require(Marshal.OffsetOf<NativeWindowsRestart.StartupInfo>("hStdError").ToInt32() == (x64 ? 96 : 64), "STARTUPINFO final handle alignment");
  Require(Marshal.OffsetOf<NativeWindowsRestart.ProcessInformation>("dwProcessId").ToInt32() == IntPtr.Size * 2, "PROCESS_INFORMATION PID alignment");

  string error;
  Require(!NativeWindowsRestart.TryStartProcess("relative.exe", "relative.exe", out error) && !string.IsNullOrEmpty(error), "Reject relative executable");
  string missing = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N") + ".exe");
  Require(!NativeWindowsRestart.TryStartProcess(missing, "\"" + missing + "\"", out error) && error.Contains("Win32"), "Missing executable must return Win32 failure");
  Require(!NativeWindowsRestart.TryStartProcess(missing, new string('x', 32767), out error) && error.Contains("too long"), "Reject oversized command line");
  Require(!NativeWindowsRestart.TryStartProcess(missing, "valid\0--hidden", out error), "Reject embedded NUL");

  string helper = Environment.ProcessPath;
  Require(File.Exists(helper) && helper.Contains(" "), "The harmless test executable must exist in the workspace path with spaces");
  var existing = new HashSet<int>();
  foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(helper)))
   using (process) existing.Add(process.Id);
  Require(NativeWindowsRestart.TryStartProcess(helper, "\"" + helper + "\" --test-child", out error), error);
  int children = 0;
  foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(helper)))
  {
   using (process)
   {
    if (existing.Contains(process.Id)) continue;
    children++;
    IntPtr handle = process.Handle;
    Require(process.WaitForExit(5000), "Harmless child did not exit.");
    uint exitCode;
    Require(GetExitCodeProcess(handle, out exitCode) && exitCode == 0, "Harmless child exit code.");
   }
  }
  Require(children == 1, "Exactly one harmless child must be observed.");
  Console.WriteLine("PASS: Win32 layouts, path/command validation, native failure, quoted CreateProcessW --test-child launch and clean child exit.");
 }
}
