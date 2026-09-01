using MedNote.Core;

namespace MedNote.Windows.App.Infrastructure;

/// <summary>
/// Optional process-level hook used by the Windows CI smoke test. It stays
/// inert in normal launches and keeps test plumbing out of the Reader model.
/// </summary>
internal static class RenderProbe
{
    private const string EnvironmentVariableName = "MEDNOTE_RENDER_PROBE";

    public static void SignalPageRendered(int pageNumber, RenderedPdfPage rendered)
    {
        var probePath = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(probePath))
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

            File.WriteAllText(
                probePath,
                $"page={pageNumber};width={rendered.PixelWidth};height={rendered.PixelHeight};bytes={rendered.PngBytes.Length}");
        }
        catch
        {
            // Diagnostics must never affect page rendering.
        }
    }
}
