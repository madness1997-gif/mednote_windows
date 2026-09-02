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

function Add-PdfStreamObject([int]$Number, [string]$Dictionary, [string]$Content) {
    $length = $encoding.GetByteCount($Content)
    Add-PdfObject $Number "<< $Dictionary /Length $length >>`nstream`n$Content`nendstream"
}

Add-PdfText "%PDF-1.4`n%MedNote adversarial corpus`n"

$imageHex = [System.Text.StringBuilder]::new()
for ($row = 0; $row -lt 32; $row++) {
    for ($column = 0; $column -lt 32; $column++) {
        $value = if (([Math]::Floor($row / 4) + [Math]::Floor($column / 4)) % 2 -eq 0) { 32 } else { 224 }
        [void]$imageHex.AppendFormat("{0:X2}", $value)
    }
}

$tableContent = @(
    "0.8 w",
    "72 700 m 540 700 l S",
    "72 650 m 540 650 l S",
    "72 600 m 540 600 l S",
    "72 550 m 540 550 l S",
    "72 550 m 72 700 l S",
    "228 550 m 228 700 l S",
    "384 550 m 384 700 l S",
    "540 550 m 540 700 l S",
    "BT /F1 16 Tf 72 735 Td (MedNote table fixture) Tj ET",
    "BT /F1 12 Tf 84 670 Td (TSH) Tj ET",
    "BT /F1 12 Tf 240 670 Td (FT4) Tj ET",
    "BT /F1 12 Tf 396 670 Td (Status) Tj ET",
    "BT /F1 12 Tf 84 620 Td (2.10) Tj ET",
    "BT /F1 12 Tf 240 620 Td (15.4) Tj ET",
    "BT /F1 12 Tf 396 620 Td (Stable) Tj ET"
) -join "`n"
$scanContent = "q`n520 0 0 700 46 46 cm`n/Im0 Do`nQ"

Add-PdfObject 1 "<< /Type /Catalog /Pages 2 0 R >>"
Add-PdfObject 2 "<< /Type /Pages /Kids [8 0 R 9 0 R 10 0 R 11 0 R] /Count 4 >>"
Add-PdfObject 3 "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
Add-PdfStreamObject 4 "/Type /XObject /Subtype /Image /Width 32 /Height 32 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode" "$imageHex>"
Add-PdfStreamObject 5 "" $tableContent
Add-PdfStreamObject 6 "" $scanContent
Add-PdfStreamObject 7 "" ""
Add-PdfObject 8 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents 5 0 R >>"
Add-PdfObject 9 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 6 0 R >>"
Add-PdfObject 10 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /CropBox [300 400 300 400] /Resources << >> /Contents 7 0 R >>"
Add-PdfObject 11 "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200000 1] /Resources << >> /Contents 7 0 R >>"

$xrefOffset = $script:byteOffset
Add-PdfText "xref`n0 12`n"
Add-PdfText "0000000000 65535 f `n"
foreach ($offset in $offsets) {
    Add-PdfText (("{0:D10} 00000 n `n") -f $offset)
}

Add-PdfText "trailer`n<< /Size 12 /Root 1 0 R >>`nstartxref`n$xrefOffset`n%%EOF`n"
$directory = [System.IO.Path]::GetDirectoryName($Path)
if ($directory) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

[System.IO.File]::WriteAllBytes($Path, $encoding.GetBytes($builder.ToString()))
Write-Host "Generated 4-page adversarial PDF corpus at $Path ($($script:byteOffset) bytes)"
