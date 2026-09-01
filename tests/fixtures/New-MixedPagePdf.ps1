param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [ValidateRange(1, 10000)]
    [int]$PageCount = 3000
)

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

Add-PdfText "%PDF-1.4`n%MedNote native stress fixture`n"

$kids = [System.Text.StringBuilder]::new()
for ($pageIndex = 0; $pageIndex -lt $PageCount; $pageIndex++) {
    [void]$kids.Append("$($pageIndex + 4) 0 R ")
}

Add-PdfObject 1 "<< /Type /Catalog /Pages 2 0 R >>"
Add-PdfObject 2 "<< /Type /Pages /Kids [$kids] /Count $PageCount >>"
Add-PdfObject 3 "<< /Length 0 >>`nstream`nendstream"

$pageSizes = @(
    @(612, 792),
    @(792, 612),
    @(595, 842)
)
for ($pageIndex = 0; $pageIndex -lt $PageCount; $pageIndex++) {
    $size = $pageSizes[$pageIndex % $pageSizes.Count]
    $objectNumber = $pageIndex + 4
    Add-PdfObject $objectNumber "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 $($size[0]) $($size[1])] /Resources << >> /Contents 3 0 R >>"
}

$xrefOffset = $script:byteOffset
$objectCount = $PageCount + 3
Add-PdfText "xref`n0 $($objectCount + 1)`n"
Add-PdfText "0000000000 65535 f `n"
foreach ($offset in $offsets) {
    Add-PdfText (("{0:D10} 00000 n `n") -f $offset)
}

Add-PdfText "trailer`n<< /Size $($objectCount + 1) /Root 1 0 R >>`nstartxref`n$xrefOffset`n%%EOF`n"
$directory = [System.IO.Path]::GetDirectoryName($Path)
if ($directory) {
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

[System.IO.File]::WriteAllBytes($Path, $encoding.GetBytes($builder.ToString()))
Write-Host "Generated $PageCount-page PDF fixture at $Path ($($script:byteOffset) bytes)"
