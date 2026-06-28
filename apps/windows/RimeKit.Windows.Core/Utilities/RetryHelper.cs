namespace RimeKit.Windows.Core.Utilities;

public static class RetryHelper
{
    public static T ExecuteWithBackoff<T>(
        Func<T> action,
        int maxRetries,
        int baseDelayMs,
        int maxDelayMs,
        Func<Exception, bool>? isRetryable = null)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (attempt < maxRetries - 1 && (isRetryable?.Invoke(ex) ?? IsTransient(ex)))
            {
                long delayLong = baseDelayMs * (1L << attempt);
                int delay = (int)Math.Min(delayLong, maxDelayMs);
                System.Threading.Thread.Sleep(delay);
            }
        }

        return action();
    }

    public static void ExecuteWithBackoff(
        Action action,
        int maxRetries,
        int baseDelayMs,
        int maxDelayMs,
        Func<Exception, bool>? isRetryable = null)
    {
        ExecuteWithBackoff(
            () => { action(); return true; },
            maxRetries,
            baseDelayMs,
            maxDelayMs,
            isRetryable);
    }

    public static bool IsTransient(Exception ex)
    {
        return ex is System.IO.IOException
            or System.UnauthorizedAccessException;
    }

    public static bool IsRetryableIOException(Exception ex)
    {
        const int sharingViolation = -2147024864;
        return IsTransient(ex)
            || (ex is System.IO.IOException && ex.HResult == sharingViolation);
    }
}
