param(
  [Parameter(Mandatory = $true)]
  [string]$OutputDirectory,

  [string]$ManifestPath = (Join-Path $PSScriptRoot "pdfium-regression-corpus.json")
)

$ErrorActionPreference = "Stop"
$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
  throw "Unsupported PDF corpus manifest schema: $($manifest.schemaVersion)"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($document in $manifest.documents) {
  $destination = Join-Path $OutputDirectory $document.fileName
  $expectedHash = $document.sha256.ToLowerInvariant()
  if (Test-Path $destination) {
    $cachedHash = (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($cachedHash -eq $expectedHash) {
      Write-Host "Verified cached PDF corpus fixture: $($document.id)"
      continue
    }

    Remove-Item $destination -Force
  }

  $temporary = "$destination.download"
  Remove-Item $temporary -Force -ErrorAction SilentlyContinue
  try {
    foreach ($attempt in 1..4) {
      try {
        Invoke-WebRequest -Uri $document.url -OutFile $temporary -MaximumRetryCount 2 -RetryIntervalSec 2
        $actualHash = (Get-FileHash $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
          throw "SHA-256 mismatch for $($document.id): expected $expectedHash, received $actualHash"
        }

        Move-Item $temporary $destination -Force
        Write-Host "Downloaded and verified PDF corpus fixture: $($document.id)"
        break
      } catch {
        Remove-Item $temporary -Force -ErrorAction SilentlyContinue
        if ($attempt -eq 4) {
          throw
        }

        Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
      }
    }
  } finally {
    Remove-Item $temporary -Force -ErrorAction SilentlyContinue
  }
}
