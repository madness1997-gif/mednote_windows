param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$encoding = [System.Text.Encoding]::ASCII
$builder = [System.Text.StringBuilder]::new()
$offsets = [System.Collections.Generic.List[long]]::new()
$script:byteOffset = 0L

function Add-PdfText([string]$Value) {
    [void]$builder.Append($Value)
    $script:byteOffset += $encoding.GetByteCount($Value)
}

function Add-PdfObject([int]$Number, [string]$Body) {
    $offsets.Add($script:byteOffset)
    Add-PdfText "$Number 0 obj`n$Body`nendobj`n"
}

function Add-PdfStreamObject([int]$Number, [string]$Content) {
    $length = $encoding.GetByteCount($Content)
    Add-PdfObject $Number "<< /Length $length >>`nstream`n$Content`nendstream"
}

Add-PdfText "%PDF-1.4`n%MedNote rotation corpus`n"

# Four oversized corner markers make the rendered orientation machine-readable:
# top-left red, top-right green, bottom-right blue, bottom-left black.
$content = @(
    "q",
    "1 0 0 rg 0 380 120 120 re f",
    "0 1 0 rg 180 380 120 120 re f",
    "0 0 1 rg 180 0 120 120 re f",
    "0 0 0 rg 0 0 120 120 re f",
    "Q"
) -join "`n"

Add-PdfObject 1 "<< /Type /Catalog /Pages 2 0 R >>"
Add-PdfObject 2 "<< /Type /Pages /Kids [4 0 R 5 0 R 6 0 R 7 0 R] /Count 4 >>"
Add-PdfStreamObject 3 $content
Add-PdfObject 4 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 500] /Resources << >> /Contents 3 0 R >>"
Add-PdfObject 5 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 500] /Rotate 90 /Resources << >> /Contents 3 0 R >>"
Add-PdfObject 6 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 500] /Rotate 180 /Resources << >> /Contents 3 0 R >>"
Add-PdfObject 7 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 500] /Rotate 270 /Resources << >> /Contents 3 0 R >>"

$xrefOffset = $script:byteOffset
Add-PdfText "xref`n0 8`n"
Add-PdfText "0000000000 65535 f `n"
foreach ($offset in $offsets) {
    Add-PdfText (("{0:D10} 00000 n `n") -f $offset)
}

Add-PdfText "trailer`n<< /Size 8 /Root 1 0 R >>`nstartxref`n$xrefOffset`n%%EOF`n"
$directory = [System.IO.Path]::GetDirectoryName($Path)
if ($directory) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

[System.IO.File]::WriteAllBytes($Path, $encoding.GetBytes($builder.ToString()))
Write-Host "Generated 4-page rotation PDF corpus at $Path ($($script:byteOffset) bytes)"
