using MedNote.Core;
using System.Diagnostics;

namespace MedNote.Windows.App.Infrastructure;

/// <summary>
/// Optional process-level hook used by the Windows CI smoke test. It stays
/// inert in normal launches and keeps test plumbing out of the Reader model.
/// </summary>
internal static class RenderProbe
{
    private const string EnvironmentVariableName = "MEDNOTE_RENDER_PROBE";
    private const string TargetPageEnvironmentVariableName = "MEDNOTE_RENDER_TARGET_PAGE";
    private static readonly long StartedAt;

    static RenderProbe()
    {
        StartedAt = Stopwatch.GetTimestamp();
    }

    public static void Initialize()
    {
        // Calling this from App.OnLaunched gives the CI probe an end-to-end
        // process/open/navigation/render clock instead of a first-use clock.
    }

    public static int? TargetPage(int pageCount)
    {
        var raw = Environment.GetEnvironmentVariable(TargetPageEnvironmentVariableName);
        return int.TryParse(raw, out var page) && page >= 1
            ? Math.Clamp(page, 1, Math.Max(1, pageCount))
            : null;
    }

    public static void SignalPagePresented(int pageNumber, int pageCount, RenderedPdfPage rendered)
    {
        var probePath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(probePath))
        {
            return;
        }

        var targetPage = TargetPage(pageCount);
        if (targetPage is not null && targetPage.Value != pageNumber)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(probePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var process = Process.GetCurrentProcess();
            var elapsedMilliseconds = Stopwatch.GetElapsedTime(StartedAt).TotalMilliseconds;
            File.WriteAllText(
                probePath,
                $"surface=direct2d;page={pageNumber};pages={pageCount};width={rendered.PixelWidth};height={rendered.PixelHeight};bytes={rendered.BgraBytes.Length};workingSet={process.WorkingSet64};elapsedMs={elapsedMilliseconds:F0}");
        }
        catch
        {
            // Diagnostics must never affect page rendering.
        }
    }
}
