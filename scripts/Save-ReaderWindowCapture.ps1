param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

# Capture the actual native window after the render probe, for visual review.
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ReaderWindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
'@
$app = Get-Process -Id $ProcessId -ErrorAction Stop
$app.Refresh()
$rect = New-Object ReaderWindowCapture+Rect
if ($app.MainWindowHandle -eq 0 -or -not [ReaderWindowCapture]::GetWindowRect($app.MainWindowHandle, [ref]$rect)) {
    throw "Reader window handle is unavailable for capture."
}
$bitmap = New-Object System.Drawing.Bitmap(($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$dc = $graphics.GetHdc()
try {
    if (-not [ReaderWindowCapture]::PrintWindow($app.MainWindowHandle, $dc, 2)) { throw "PrintWindow failed." }
} finally {
    $graphics.ReleaseHdc($dc)
    $graphics.Dispose()
}
try {
    New-Item (Split-Path $OutputPath -Parent) -ItemType Directory -Force | Out-Null
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $bitmap.Dispose() }
