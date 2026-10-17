$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $PSScriptRoot ('bin/download-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$testRoot/scripts","$testRoot/catalogo" -Force | Out-Null
Copy-Item -LiteralPath "$appRoot/scripts/Download-Corpus.ps1" -Destination "$testRoot/scripts/Download-Corpus.ps1"
$rows = @(Import-Csv -LiteralPath "$appRoot/catalogo/PROVAS_MANIFEST.csv" -Encoding UTF8 | Where-Object { $_.exame -eq 'ENEM' -and $_.ano_edicao -eq '2009' } | Select-Object -First 2)
$manifestPath = "$testRoot/catalogo/PROVAS_MANIFEST.csv"
$destination = "$testRoot/Acervo"
$scriptPath = "$testRoot/scripts/Download-Corpus.ps1"
$checks = 0
function Check($condition, $message) { if (!$condition) { throw $message }; $script:checks++; Write-Host "PASS: $message" }
function Run-Download([bool]$success) {
    & powershell.exe -NoProfile -File $scriptPath -Destination $destination -Retries 1
    Check (($LASTEXITCODE -eq 0) -eq $success) 'Expected process result'
}
$rows | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8
Run-Download $true
$summary = Get-Content -LiteralPath "$destination/download-resultado.json" -Raw | ConvertFrom-Json
Check ($summary.baixados -eq 2 -and $summary.falhas -eq 0) 'Fresh official downloads'
Run-Download $true
$summary = Get-Content -LiteralPath "$destination/download-resultado.json" -Raw | ConvertFrom-Json
Check ($summary.existentes_verificados -eq 2 -and $summary.baixados -eq 0) 'Verified files are skipped on resume'
$key = $rows[1]; $keyPath = Join-Path $destination $key.arquivo_local
[IO.File]::WriteAllText($keyPath,'Synthetic corruption fixture')
Run-Download $true
Check ((Get-FileHash -LiteralPath $keyPath).Hash -eq $key.sha256) 'Corrupt fixture replaced only with verified download'
Check (@(Get-ChildItem -LiteralPath ([IO.Path]::GetDirectoryName($keyPath)) -Filter '*.divergente-*').Count -eq 1) 'Divergent original preserved'
$key.arquivo_local = 'ENEM/missing.pdf'; $key.url_final = 'https://download.inep.gov.br/estudar-unavailable-fixture-404.pdf'
@($key) | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8
Run-Download $false
$summary = Get-Content -LiteralPath "$destination/download-resultado.json" -Raw | ConvertFrom-Json
Check ($summary.falhas -eq 1 -and !(Test-Path -LiteralPath "$destination/ENEM/missing.pdf")) 'Unavailable file is recorded and never installed'
$key.url_final = 'https://example.invalid/private.pdf'
@($key) | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8
Run-Download $false
Check (!(Test-Path -LiteralPath "$destination/ENEM/missing.pdf")) 'Nonofficial host rejected'
$key.arquivo_local = '../outside.pdf'; $key.url_final = $rows[0].url_final
@($key) | Export-Csv -LiteralPath $manifestPath -NoTypeInformation -Encoding UTF8
Run-Download $false
Check (!(Test-Path -LiteralPath "$testRoot/outside.pdf")) 'Path traversal rejected'
Write-Host "PASS: $checks downloader checks. Fixtures retained in ignored bin directory."
