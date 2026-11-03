using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ProbablyModManager
{
 internal static class NativeWindowsRestart
 {
  private const int MaxWindowsString = 32767;

  internal static bool TryStartReplacement(out string error)
  {
   error = null;
   if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
   {
    error = "Native restart is only available on Windows.";
    return false;
   }
   try
   {
    var path = new StringBuilder(MaxWindowsString + 1);
    uint length = GetModuleFileNameW(IntPtr.Zero, path, (uint)path.Capacity);
    if (length == 0)
    {
     error = WindowsError("Cannot locate the running executable", Marshal.GetLastWin32Error());
     return false;
    }
    if (length >= path.Capacity)
    {
     error = "The running executable path is too long.";
     return false;
    }
    string commandLine = Marshal.PtrToStringUni(GetCommandLineW());
    return TryStartProcess(path.ToString(), commandLine, out error);
   }
   catch (Exception ex) when (IsInteropUnavailable(ex))
   {
    error = "The Windows process API is unavailable.";
    return false;
   }
  }

  // Also used by standalone checks, which launch only a harmless test child.
  internal static bool TryStartProcess(string executable, string commandLine, out string error)
  {
   error = null;
   if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
   {
    error = "Native restart is only available on Windows.";
    return false;
   }
   try
   {
    if (string.IsNullOrWhiteSpace(executable) || executable.Length >= MaxWindowsString ||
        executable.IndexOf('\0') >= 0 || executable.IndexOf('"') >= 0 ||
        !Path.IsPathFullyQualified(executable) ||
        !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
    {
     error = "Restart requires an absolute Windows executable path.";
     return false;
    }
    if (string.IsNullOrWhiteSpace(commandLine) || commandLine.Length >= MaxWindowsString || commandLine.IndexOf('\0') >= 0)
    {
     error = "The restart command line is empty, invalid, or too long.";
     return false;
    }

    var startup = new StartupInfo { cb = (uint)Marshal.SizeOf<StartupInfo>() };
    ProcessInformation process;
    // CreateProcessW may modify this buffer; the original arguments stay intact.
    var writableCommandLine = new StringBuilder(commandLine, commandLine.Length + 1);
    if (!CreateProcessW(executable, writableCommandLine, IntPtr.Zero, IntPtr.Zero,
                        false, 0, IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out process))
    {
     error = WindowsError("Windows could not start the replacement process", Marshal.GetLastWin32Error());
     return false;
    }
    try { return true; }
    finally
    {
     if (process.hThread != IntPtr.Zero) CloseHandle(process.hThread);
     if (process.hProcess != IntPtr.Zero) CloseHandle(process.hProcess);
    }
   }
   catch (Exception ex) when (IsInteropUnavailable(ex))
   {
    error = "The Windows process API is unavailable.";
    return false;
   }
   catch (ArgumentException)
   {
    error = "The restart executable path or command line is invalid.";
    return false;
   }
   catch (NotSupportedException)
   {
    error = "The restart executable path is not supported.";
    return false;
   }
   catch (Win32Exception ex)
   {
    error = WindowsError("Windows could not start the replacement process", ex.NativeErrorCode);
    return false;
   }
  }

  private static bool IsInteropUnavailable(Exception ex) => ex is DllNotFoundException ||
   ex is EntryPointNotFoundException || ex is BadImageFormatException || ex is PlatformNotSupportedException;

  private static string WindowsError(string action, int code) =>
   action + " (Win32 " + code + "): " + new Win32Exception(code).Message;

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  internal struct StartupInfo
  {
   internal uint cb;
   internal IntPtr lpReserved, lpDesktop, lpTitle;
   internal uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
   internal ushort wShowWindow, cbReserved2;
   internal IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct ProcessInformation
  {
   internal IntPtr hProcess, hThread;
   internal uint dwProcessId, dwThreadId;
  }

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
  private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder filename, uint size);

  [DllImport("kernel32.dll", ExactSpelling = true)]
  private static extern IntPtr GetCommandLineW();

  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine,
   IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
   uint creationFlags, IntPtr environment, string currentDirectory,
   ref StartupInfo startupInfo, out ProcessInformation processInformation);

  [DllImport("kernel32.dll", ExactSpelling = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool CloseHandle(IntPtr handle);
 }
}
