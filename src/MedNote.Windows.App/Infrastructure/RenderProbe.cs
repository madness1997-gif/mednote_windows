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
    private const string PasswordEnvironmentVariableName = "MEDNOTE_PDF_PASSWORD";
    private const string RotationEnvironmentVariableName = "MEDNOTE_RENDER_ROTATION";
    private const string SimulateSurfaceLossEnvironmentVariableName = "MEDNOTE_SIMULATE_SURFACE_LOSS";
    private static readonly long StartedAt;
    private static int _targetPresentations;

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

    public static string? StartupPassword =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName))
            ? null
            : Environment.GetEnvironmentVariable(PasswordEnvironmentVariableName);

    public static int? StartupRotation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariableName)))
            {
                return null;
            }

            var raw = Environment.GetEnvironmentVariable(RotationEnvironmentVariableName);
            return int.TryParse(raw, out var rotation)
                ? ReaderMath.NormalizeRotation(rotation)
                : null;
        }
    }

    public static bool SignalPagePresented(
        int pageNumber,
        int pageCount,
        int rotation,
        RenderedPdfPage rendered)
    {
        var probePath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(probePath))
        {
            return false;
        }

        var targetPage = TargetPage(pageCount);
        if (targetPage is not null && targetPage.Value != pageNumber)
        {
            return false;
        }

        var normalizedRotation = ReaderMath.NormalizeRotation(rotation);
        if (StartupRotation is { } expectedRotation && normalizedRotation != expectedRotation)
        {
            // Opening a persisted document can briefly realize its previous
            // orientation before MainWindow applies the CI-requested rotation.
            return false;
        }

        var presentationCount = Interlocked.Increment(ref _targetPresentations);
        if (presentationCount == 1
            && Environment.GetEnvironmentVariable(SimulateSurfaceLossEnvironmentVariableName) == "1")
        {
            return true;
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
            var inkSamples = CountInkSamples(rendered);
            var cornerSignature = ReadCornerSignature(rendered);
            File.WriteAllText(
                probePath,
                $"surface=direct2d;page={pageNumber};pages={pageCount};width={rendered.PixelWidth};height={rendered.PixelHeight};bytes={rendered.BgraBytes.Length};workingSet={process.WorkingSet64};elapsedMs={elapsedMilliseconds:F0};presentations={presentationCount};inkSamples={inkSamples};rotation={normalizedRotation};corners={cornerSignature}");
        }
        catch
        {
            // Diagnostics must never affect page rendering.
        }

        return false;
    }

    private static int CountInkSamples(RenderedPdfPage rendered)
    {
        const long maximumSamples = 8_192;
        var pixelCount = checked((long)rendered.PixelWidth * rendered.PixelHeight);
        var step = Math.Max(1L, pixelCount / maximumSamples);
        var samples = 0;
        for (var pixel = 0L; pixel < pixelCount; pixel += step)
        {
            var row = pixel / rendered.PixelWidth;
            var column = pixel % rendered.PixelWidth;
            var offset = checked((int)(row * rendered.Stride + column * 4L));
            if (rendered.BgraBytes[offset] < 248
                || rendered.BgraBytes[offset + 1] < 248
                || rendered.BgraBytes[offset + 2] < 248)
            {
                samples++;
            }
        }

        return samples;
    }

    private static string ReadCornerSignature(RenderedPdfPage rendered)
    {
        var corners = new[]
        {
            ReadColor(rendered, 0.1d, 0.1d),
            ReadColor(rendered, 0.9d, 0.1d),
            ReadColor(rendered, 0.9d, 0.9d),
            ReadColor(rendered, 0.1d, 0.9d),
        };
        return new string(corners);
    }

    private static char ReadColor(RenderedPdfPage rendered, double horizontalRatio, double verticalRatio)
    {
        var column = Math.Clamp(
            (int)Math.Round((rendered.PixelWidth - 1u) * horizontalRatio),
            0,
            checked((int)rendered.PixelWidth - 1));
        var row = Math.Clamp(
            (int)Math.Round((rendered.PixelHeight - 1u) * verticalRatio),
            0,
            checked((int)rendered.PixelHeight - 1));
        var offset = checked((int)((long)row * rendered.Stride + column * 4L));
        var blue = rendered.BgraBytes[offset];
        var green = rendered.BgraBytes[offset + 1];
        var red = rendered.BgraBytes[offset + 2];

        if (red >= 160 && green <= 120 && blue <= 120)
        {
            return 'R';
        }

        if (green >= 160 && red <= 120 && blue <= 120)
        {
            return 'G';
        }

        if (blue >= 160 && red <= 120 && green <= 120)
        {
            return 'B';
        }

        if (red <= 96 && green <= 96 && blue <= 96)
        {
            return 'K';
        }

        return red >= 220 && green >= 220 && blue >= 220 ? 'W' : '?';
    }
}
