using System.ComponentModel;
using System.Diagnostics;

namespace RimeKit.Windows.Core.Utilities;

public static class ProcessHelper
{
    public static bool StopProcessesWithBackoff(
        string[] processNames,
        int timeoutMs = 30000,
        int baseDelayMs = 200,
        int maxDelayMs = 2000)
    {
        int delay = baseDelayMs;
        int waited = 0;
        while (waited < timeoutMs)
        {
            bool anyAlive = false;
            foreach (string processName in processNames)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(processName);
                    foreach (Process proc in processes)
                    {
                        try
                        {
                            if (!proc.HasExited)
                                proc.Kill(entireProcessTree: true);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException
                            or Win32Exception or NotSupportedException)
                        {
                            Debug.WriteLine(
                                $"[ProcessHelper] 终止 {processName} 失败: {ex.Message}");
                        }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                    if (Process.GetProcessesByName(processName).Length > 0)
                        anyAlive = true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    Debug.WriteLine(
                        $"[ProcessHelper] 检查 {processName} 失败: {ex.Message}");
                }
            }

            if (!anyAlive)
                return true;

            System.Threading.Thread.Sleep(delay);
            waited += delay;
            delay = Math.Min(delay * 2, maxDelayMs);
        }

        bool stillAlive = false;
        foreach (string processName in processNames)
        {
            try
            {
                if (Process.GetProcessesByName(processName).Length > 0)
                    stillAlive = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                Debug.WriteLine(
                    $"[ProcessHelper] 最终检查 {processName} 失败: {ex.Message}");
            }
        }

        return !stillAlive;
    }

    public static void TerminateProcess(Process process)
    {
        if (process.HasExited)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
            if (!WaitForExitWithBackoff(process, timeoutMs: 5000, baseDelayMs: 200, maxDelayMs: 2000))
            {
                Debug.WriteLine(
                    $"[ProcessHelper] 进程 {process.Id} 在 Kill 后 5 秒仍未退出");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or Win32Exception or NotSupportedException)
        {
            Debug.WriteLine(
                $"[ProcessHelper] 终止进程失败: {ex.Message}");
        }
    }

    public static bool WaitForExitWithBackoff(
        Process? process,
        int timeoutMs,
        int baseDelayMs = 200,
        int maxDelayMs = 2000)
    {
        if (process is null || process.HasExited)
            return true;

        int delay = baseDelayMs;
        int waited = 0;
        while (waited < timeoutMs && !process.HasExited)
        {
            System.Threading.Thread.Sleep(delay);
            waited += delay;
            delay = Math.Min(delay * 2, maxDelayMs);
        }

        return process.HasExited;
    }
}
