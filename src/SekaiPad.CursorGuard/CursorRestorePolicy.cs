using System;
using System.IO;

namespace SekaiPad.CursorGuard;

/// <summary>
/// Crash recovery must not erase its marker unless Windows actually restored
/// the pointer. The injected callback makes failure paths testable without
/// changing any system cursors or HKCU settings.
/// </summary>
public static class CursorRestorePolicy
{
    public static bool TryRestoreMarker(
        string marker,
        Func<bool> restore,
        int maxAttempts = 3,
        Action? beforeRetry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        ArgumentNullException.ThrowIfNull(restore);
        if (maxAttempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        if (!File.Exists(marker))
            return true;

        for (int attempt = 0; attempt < maxAttempts; ++attempt)
        {
            bool succeeded;
            try { succeeded = restore(); }
            catch { succeeded = false; }

            if (succeeded)
            {
                try { File.Delete(marker); }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                return !File.Exists(marker);
            }

            if (attempt + 1 < maxAttempts)
                beforeRetry?.Invoke();
        }

        // Keep the marker so the user can diagnose or retry a failed
        // cursor recovery instead of losing the only recovery evidence.
        return false;
    }
}
