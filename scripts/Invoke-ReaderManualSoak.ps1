param(
    [Parameter(Mandatory = $true)][string]$AppPath,
    [Parameter(Mandatory = $true)][string]$CorpusPath,
    [int]$MinutesPerFile = 30,
    [int]$SampleSeconds = 5,
    [string]$OutputPath = "artifacts/manual-soak"
)

$ErrorActionPreference = "Stop"
$app = (Resolve-Path $AppPath).Path
$corpus = (Resolve-Path $CorpusPath).Path
$files = Get-ChildItem $corpus -File -Filter *.pdf | Sort-Object Name
if (-not $files) {
    throw "No PDF files found in $corpus"
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultPath = Join-Path $OutputPath "reader-soak-$runStamp.csv"
$rows = [System.Collections.Generic.List[object]]::new()

Write-Host "Manual Reader soak started."
Write-Host "For each PDF: continuously scroll, jump pages, zoom, search, select text, rotate, and switch Single/Continuous."
Write-Host "This harness only samples process memory; it is intentionally NOT part of normal CI."

foreach ($file in $files) {
    Write-Host ""
    Write-Host "=== $($file.Name) ==="
    $started = Get-Date
    $deadline = $started.AddMinutes($MinutesPerFile)
    $process = Start-Process $app -ArgumentList "`"$($file.FullName)`"" -PassThru

    try {
        while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
            Start-Sleep -Seconds $SampleSeconds
            $process.Refresh()
            if ($process.HasExited) { break }

            $rows.Add([pscustomobject]@{
                Timestamp = (Get-Date).ToString("o")
                File = $file.Name
                ProcessId = $process.Id
                WorkingSetBytes = $process.WorkingSet64
                PrivateMemoryBytes = $process.PrivateMemorySize64
                ElapsedSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
            })
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }
    }

    $rows | Export-Csv $resultPath -NoTypeInformation -Encoding UTF8
}

Write-Host ""
Write-Host "Soak samples written to $resultPath"
