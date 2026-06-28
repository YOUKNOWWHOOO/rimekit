using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RimeKit.Windows.Core.Utilities;

namespace RimeKit.Windows.Core;

internal static class ResumableDownloader
{
    private const int DefaultMaxRetries = 3;
    private const int RetryBaseDelayMs = 1000;
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = false };

    public static (string Version, string Note) DownloadToFile(string downloadUrl, string targetPath)
    {
        List<string> urls = GitHubProxyHelper.BuildFallbackUrls(downloadUrl);
        return ExecuteDownloadWithFallback(
            urls,
            (url, index) => DownloadToFileInternal(url, targetPath, GitHubProxyHelper.GetMaxAttempts(url, index), GitHubProxyHelper.IsGitHubUrl(url)),
            () => CleanupPartial(targetPath));
    }

    public static string DownloadToString(string url)
    {
        List<string> urls = GitHubProxyHelper.BuildFallbackUrls(url);
        return ExecuteDownloadWithFallback(
            urls,
            (currentUrl, index) => DownloadToStringInternal(currentUrl, GitHubProxyHelper.GetMaxAttempts(currentUrl, index)),
            () => { });
    }

    public static (string Version, byte[] Payload) DownloadToBytes(string url, TimeSpan? timeout = null)
    {
        List<string> urls = GitHubProxyHelper.BuildFallbackUrls(url);
        return ExecuteDownloadWithFallback(
            urls,
            (currentUrl, index) => DownloadToBytesInternal(currentUrl, timeout, GitHubProxyHelper.GetMaxAttempts(currentUrl, index)),
            () => { });
    }

    private static T ExecuteDownloadWithFallback<T>(
        List<string> urls,
        Func<string, int, T> downloadAction,
        Action cleanupAction)
    {
        List<Exception> errors = new();
        for (int i = 0; i < urls.Count; i++)
        {
            try
            {
                return downloadAction(urls[i], i);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
            {
                errors.Add(ex);
                if (i < urls.Count - 1)
                {
                    cleanupAction();
                    System.Diagnostics.Debug.WriteLine($"[Downloader] URL failed ({urls[i]}), trying next: {ex.Message}");
                }
            }
        }

        throw new IOException(string.Join(" | ", errors.Select(e => e.Message)));
    }

    private static (string Version, string Note) DownloadToFileInternal(
        string downloadUrl, string targetPath, int maxAttempts, bool enableSpeedCheck)
    {
        string metadataPath = targetPath + ".metadata";
        string partialPath = targetPath + ".partial";

        long totalBytes = -1;
        string remoteETag = string.Empty;
        string remoteLastModified = string.Empty;
        bool serverSupportsRanges = false;

        try
        {
            using HttpClient headClient = CreateClient(TimeSpan.FromSeconds(15));
            using HttpResponseMessage headResponse = headClient.Send(new HttpRequestMessage(HttpMethod.Head, downloadUrl));
            headResponse.EnsureSuccessStatusCode();
            totalBytes = headResponse.Content.Headers.ContentLength ?? -1;
            remoteETag = headResponse.Headers.ETag?.Tag ?? string.Empty;
            remoteLastModified = headResponse.Content.Headers.LastModified?.ToString() ?? string.Empty;
            serverSupportsRanges = string.Equals(headResponse.Headers.AcceptRanges?.ToString(), "bytes", StringComparison.OrdinalIgnoreCase)
                || headResponse.Content.Headers.ContentRange is not null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloader] HEAD request failed for resume: {ex.Message}");
        }

        long existingPartialSize = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        bool metadataValid = false;
        if (File.Exists(metadataPath) && existingPartialSize > 0)
        {
            try
            {
                using JsonDocument meta = JsonDocument.Parse(File.ReadAllText(metadataPath, Encoding.UTF8));
                string storedUrl = meta.RootElement.GetProperty("url").GetString() ?? string.Empty;
                string storedETag = meta.RootElement.TryGetProperty("etag", out JsonElement etagEl) ? etagEl.GetString() ?? string.Empty : string.Empty;
                long storedTotal = meta.RootElement.TryGetProperty("total", out JsonElement totalEl) ? totalEl.GetInt64() : -1;
                if (string.Equals(storedUrl, downloadUrl, StringComparison.Ordinal) &&
                    (string.IsNullOrEmpty(remoteETag) || string.Equals(storedETag, remoteETag, StringComparison.Ordinal)) &&
                    (storedTotal <= 0 || existingPartialSize <= storedTotal))
                {
                    metadataValid = true;
                    totalBytes = storedTotal > 0 ? storedTotal : totalBytes;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Downloader] Metadata parse failed: {ex.Message}");
            }
        }

        if (!metadataValid)
        {
            TryDelete(partialPath);
            TryDelete(metadataPath);
            existingPartialSize = 0;
        }

        if (existingPartialSize > 0 && totalBytes > 0 && existingPartialSize >= totalBytes)
        {
            TryDelete(metadataPath);
            TryMoveFile(partialPath, targetPath);
            string version = remoteETag.Length > 0 ? remoteETag : remoteLastModified.Length > 0 ? remoteLastModified : "downloaded";
            return (version, $"已下载归档：{Path.GetFileName(targetPath)}");
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using HttpClient client = CreateClient(TimeSpan.FromMinutes(5));
                using HttpRequestMessage request = new(HttpMethod.Get, downloadUrl);
                if (attempt > 0 && existingPartialSize > 0 && metadataValid)
                {
                    request.Headers.Range = new RangeHeaderValue(existingPartialSize, totalBytes > 0 ? totalBytes - 1 : null);
                }

                using HttpResponseMessage response = client.Send(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden)
                {
                    TryDelete(partialPath);
                    TryDelete(metadataPath);
                    throw new InvalidOperationException($"下载失败（{response.StatusCode}）：{downloadUrl}");
                }

                response.EnsureSuccessStatusCode();

                FileMode fileMode;
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    fileMode = FileMode.Append;
                }
                else
                {
                    fileMode = FileMode.Create;
                    existingPartialSize = 0;
                }

                if (totalBytes <= 0 && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    totalBytes = response.Content.Headers.ContentLength ?? -1;
                }

                if (string.IsNullOrEmpty(remoteETag))
                {
                    remoteETag = response.Headers.ETag?.Tag ?? response.Content.Headers.LastModified?.ToString() ?? string.Empty;
                }

                WriteMetadata(metadataPath, downloadUrl, remoteETag, totalBytes);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
                using (Stream sourceStream = response.Content.ReadAsStream())
                using (FileStream targetStream = new(partialPath, fileMode, FileAccess.Write, FileShare.Read))
                {
                    if (enableSpeedCheck && (totalBytes < 0 || totalBytes > GitHubProxyHelper.MinFileSizeForSpeedCheck))
                    {
                        CopyWithSpeedCheck(sourceStream, targetStream, totalBytes, existingPartialSize);
                    }
                    else
                    {
                        sourceStream.CopyTo(targetStream);
                    }
                }

                long finalSize = new FileInfo(partialPath).Length;
                long previousSize = existingPartialSize;
                existingPartialSize = finalSize;

                if (totalBytes > 0 && finalSize < totalBytes)
                {
                    continue;
                }

                if (totalBytes <= 0)
                {
                    bool? zipResult = TryVerifyZip(partialPath);
                    if (zipResult == true)
                    {
                    }
                    else if (zipResult == false)
                    {
                        continue;
                    }
                    else if (finalSize > 0)
                    {
                    }
                    else
                    {
                        continue;
                    }
                }

                TryDelete(metadataPath);
                FileHelper.WaitForFileHandlesReleased(partialPath, timeoutMs: 3000);
                TryMoveFile(partialPath, targetPath);
                string finalVersion = remoteETag.Length > 0 ? remoteETag : remoteLastModified.Length > 0 ? remoteLastModified : "downloaded";
                return (finalVersion, $"已下载归档：{Path.GetFileName(targetPath)}");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
            {
                if (attempt == maxAttempts - 1)
                {
                    TryDelete(partialPath);
                    TryDelete(metadataPath);
                    throw;
                }

                existingPartialSize = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                int delayMs = RetryBaseDelayMs * (int)Math.Pow(2, attempt);
                if (delayMs > 10000) delayMs = 10000;
                Thread.Sleep(delayMs);
            }
        }

        throw new IOException($"下载失败：{downloadUrl}");
    }

    private static string DownloadToStringInternal(string url, int maxAttempts)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using HttpClient client = CreateClient(TimeSpan.FromSeconds(60));
                string body = client.GetStringAsync(url).GetAwaiter().GetResult();
                return body;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (attempt == maxAttempts - 1)
                {
                    throw;
                }

                int delayMs = RetryBaseDelayMs * (int)Math.Pow(2, attempt);
                if (delayMs > 10000) delayMs = 10000;
                Thread.Sleep(delayMs);
            }
        }

        throw new IOException($"下载失败：{url}");
    }

    private static (string Version, byte[] Payload) DownloadToBytesInternal(string url, TimeSpan? timeout, int maxAttempts)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using HttpClient client = CreateClient(timeout ?? TimeSpan.FromMinutes(5));
                using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                byte[] payload = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                string version = response.Headers.ETag?.Tag
                    ?? response.Content.Headers.LastModified?.ToString()
                    ?? "downloaded";
                return (version, payload);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (attempt == maxAttempts - 1)
                {
                    throw;
                }

                int delayMs = RetryBaseDelayMs * (int)Math.Pow(2, attempt);
                if (delayMs > 10000) delayMs = 10000;
                Thread.Sleep(delayMs);
            }
        }

        throw new IOException($"下载失败：{url}");
    }

    private static void CopyWithSpeedCheck(Stream source, Stream target, long totalBytes, long existingSize)
    {
        byte[] buffer = new byte[65536];
        long bytesRead = 0;
        DateTime startTime = DateTime.UtcNow;
        long threshold = GitHubProxyHelper.SpeedThresholdBytesPerSecond;
        TimeSpan window = GitHubProxyHelper.SpeedSampleWindow;
        long remaining = totalBytes > 0 ? totalBytes - existingSize : long.MaxValue;
        bool speedChecked = false;

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            target.Write(buffer, 0, read);
            bytesRead += read;

            if (!speedChecked && remaining > GitHubProxyHelper.MinFileSizeForSpeedCheck)
            {
                double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                if (elapsed >= window.TotalSeconds)
                {
                    speedChecked = true;
                    double speed = bytesRead / elapsed;
                    if (speed < threshold)
                    {
                        throw new IOException($"下载速度过慢（{speed / 1024:F1} KB/s，阈值 {threshold / 1024} KB/s），将尝试下一个镜像。");
                    }
                }
            }
        }
    }

    private static void CleanupPartial(string targetPath)
    {
        TryDelete(targetPath + ".partial");
        TryDelete(targetPath + ".metadata");
    }

    private static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        HttpClient client = new(new SocketsHttpHandler())
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RimeKit.Windows/1.0");
        return client;
    }

    private static void WriteMetadata(string metadataPath, string url, string etag, long total)
    {
        string json = JsonSerializer.Serialize(
            new { url, etag, total },
            MetadataJsonOptions);
        File.WriteAllText(metadataPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool? TryVerifyZip(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using System.IO.Compression.ZipArchive zip = new(fs, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
            return zip.Entries.Count > 0 ? true : false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloader] TryVerifyZip failed: {ex.Message}");
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloader] TryDelete({path}) failed: {ex.Message}");
        }
    }

    private static void TryMoveFile(string source, string destination)
    {
        try
        {
            FileHelper.CopyFileWithBackoff(source, destination, overwrite: true);
            FileHelper.DeleteFileWithBackoff(source, maxRetries: 5, baseDelayMs: 100, maxDelayMs: 2000);
        }
        catch (IOException)
        {
            try { FileHelper.DeleteFileWithBackoff(source, maxRetries: 3, baseDelayMs: 100, maxDelayMs: 1000); } catch (IOException) { }
            throw;
        }
    }
}
