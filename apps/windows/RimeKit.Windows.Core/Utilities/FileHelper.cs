using System.Text;

namespace RimeKit.Windows.Core.Utilities;

public static class FileHelper
{
    public static void DeleteDirectoryWithBackoff(
        string path,
        int maxRetries = 15,
        int baseDelayMs = 200,
        int maxDelayMs = 5000)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string resolvedPath = System.IO.Path.GetFullPath(path);
        if (!System.IO.Directory.Exists(resolvedPath))
            return;

        RetryHelper.ExecuteWithBackoff(
            () =>
            {
                System.IO.Directory.Delete(resolvedPath, recursive: true);
                if (System.IO.Directory.Exists(resolvedPath))
                    throw new System.IO.IOException(
                        $"删除目录后目录仍然存在: {resolvedPath}");
            },
            maxRetries,
            baseDelayMs,
            maxDelayMs,
            ex => ex is System.IO.IOException or System.UnauthorizedAccessException);
    }

    public static void DeleteFileWithBackoff(
        string path,
        int maxRetries = 10,
        int baseDelayMs = 100,
        int maxDelayMs = 3000)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string resolvedPath = System.IO.Path.GetFullPath(path);
        if (!System.IO.File.Exists(resolvedPath))
            return;

        RetryHelper.ExecuteWithBackoff(
            () =>
            {
                System.IO.File.Delete(resolvedPath);
                if (System.IO.File.Exists(resolvedPath))
                    throw new System.IO.IOException(
                        $"删除文件后文件仍然存在: {resolvedPath}");
            },
            maxRetries,
            baseDelayMs,
            maxDelayMs,
            ex => ex is System.IO.IOException or System.UnauthorizedAccessException);
    }

    public static void CopyFileWithBackoff(
        string source,
        string destination,
        bool overwrite = true,
        int maxRetries = 10,
        int baseDelayMs = 200,
        int maxDelayMs = 5000)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("源路径和目标路径不能为空。");

        string resolvedSource = System.IO.Path.GetFullPath(source);
        string resolvedDestination = System.IO.Path.GetFullPath(destination);

        if (!System.IO.File.Exists(resolvedSource))
            throw new System.IO.FileNotFoundException(
                $"源文件不存在: {resolvedSource}");

        RetryHelper.ExecuteWithBackoff(
            () =>
            {
                string? destDir = System.IO.Path.GetDirectoryName(resolvedDestination);
                if (!string.IsNullOrWhiteSpace(destDir) && !System.IO.Directory.Exists(destDir))
                    System.IO.Directory.CreateDirectory(destDir);

                System.IO.File.Copy(resolvedSource, resolvedDestination, overwrite: true);

                if (!System.IO.File.Exists(resolvedDestination))
                    throw new System.IO.IOException(
                        $"复制后目标文件不存在: {resolvedDestination}");

                long sourceLen = new System.IO.FileInfo(resolvedSource).Length;
                long destLen = new System.IO.FileInfo(resolvedDestination).Length;
                if (sourceLen != destLen)
                    throw new System.IO.IOException(
                        $"复制后文件大小不一致: 源={sourceLen} 目标={destLen} 路径={resolvedDestination}");
            },
            maxRetries,
            baseDelayMs,
            maxDelayMs,
            ex => ex is System.IO.IOException or System.UnauthorizedAccessException);
    }

    public static void WriteTextWithVerification(
        string path,
        string content,
        Encoding? encoding = null,
        int maxRetries = 5,
        int baseDelayMs = 150,
        int maxDelayMs = 2000)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空。");

        string resolvedPath = System.IO.Path.GetFullPath(path);
        Encoding enc = encoding ?? new UTF8Encoding(false);

        RetryHelper.ExecuteWithBackoff(
            () =>
            {
                string? dir = System.IO.Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(resolvedPath, content, enc);

                if (!System.IO.File.Exists(resolvedPath))
                    throw new System.IO.IOException(
                        $"写入后文件不存在: {resolvedPath}");

                string readBack = System.IO.File.ReadAllText(resolvedPath, enc);
                if (readBack != content)
                    throw new System.IO.IOException(
                        $"写入后读回内容不一致: {resolvedPath}");
            },
            maxRetries,
            baseDelayMs,
            maxDelayMs,
            ex => ex is System.IO.IOException or System.UnauthorizedAccessException);
    }

    public static bool WaitForFileHandlesReleased(
        string path,
        int timeoutMs = 30000,
        int baseDelayMs = 200,
        int maxDelayMs = 2000)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        string resolvedPath = System.IO.Path.GetFullPath(path);
        if (!System.IO.File.Exists(resolvedPath))
            return true;

        int delay = baseDelayMs;
        int waited = 0;
        while (waited < timeoutMs)
        {
            try
            {
                using System.IO.FileStream fs = new(
                    resolvedPath,
                    System.IO.FileMode.Open,
                    System.IO.FileAccess.Read,
                    System.IO.FileShare.None);
                return true;
            }
            catch (System.IO.IOException)
            {
                System.Threading.Thread.Sleep(delay);
                waited += delay;
                delay = Math.Min(delay * 2, maxDelayMs);
            }
        }

        return false;
    }

    public static bool WaitForDirectoryHandlesReleased(
        string directoryPath,
        int timeoutMs = 30000,
        int baseDelayMs = 200,
        int maxDelayMs = 2000)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return true;

        string resolvedPath = System.IO.Path.GetFullPath(directoryPath);
        if (!System.IO.Directory.Exists(resolvedPath))
            return true;

        List<string> probeFiles = [];
        CollectProbeFiles(resolvedPath, probeFiles);

        if (probeFiles.Count == 0)
            return true;

        int delay = baseDelayMs;
        int waited = 0;
        while (waited < timeoutMs)
        {
            bool allAccessible = true;
            for (int i = probeFiles.Count - 1; i >= 0; i--)
            {
                string file = probeFiles[i];
                if (!System.IO.File.Exists(file))
                {
                    probeFiles.RemoveAt(i);
                    continue;
                }

                try
                {
                    using System.IO.FileStream fs = new(
                        file,
                        System.IO.FileMode.Open,
                        System.IO.FileAccess.Read,
                        System.IO.FileShare.None);
                }
                catch (System.IO.IOException)
                {
                    allAccessible = false;
                    break;
                }
            }

            if (probeFiles.Count == 0)
                return true;

            if (allAccessible)
                return true;

            System.Threading.Thread.Sleep(delay);
            waited += delay;
            delay = Math.Min(delay * 2, maxDelayMs);
        }

        return false;
    }

    private static void CollectProbeFiles(string directoryPath, List<string> probeFiles)
    {
        try
        {
            foreach (string file in System.IO.Directory.GetFiles(directoryPath))
            {
                string ext = System.IO.Path.GetExtension(file);
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".yaml", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "LOG", StringComparison.OrdinalIgnoreCase))
                {
                    probeFiles.Add(file);
                }
            }

            foreach (string subDir in System.IO.Directory.GetDirectories(directoryPath))
            {
                CollectProbeFiles(subDir, probeFiles);
            }
        }
        catch (System.IO.IOException)
        {
        }
        catch (System.UnauthorizedAccessException)
        {
        }
    }
}
