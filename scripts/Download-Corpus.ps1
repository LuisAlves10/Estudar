[CmdletBinding()]
param(
    [string]$Destination,
    [ValidateSet('ENEM','PAS','UnB')][string]$Exam,
    [int]$Year,
    [ValidateRange(0,10000)][int]$Limit = 0,
    [ValidateRange(1,5)][int]$Retries = 3,
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$appRoot = Split-Path -Parent $PSScriptRoot
if (!$Destination) {
    $workspace = Split-Path -Parent $appRoot
    $Destination = if (Test-Path -LiteralPath (Join-Path $workspace 'Dados/PROVAS_MANIFEST.csv')) { $workspace } else { Join-Path $appRoot 'Acervo' }
}
$destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowedHosts = @('download.inep.gov.br','cdn.cebraspe.org.br','www.cebraspe.org.br','www.gov.br','www.unb.br')
$manifest = Import-Csv -LiteralPath (Join-Path $appRoot 'catalogo/PROVAS_MANIFEST.csv') -Encoding UTF8
$rows = @($manifest | Where-Object { (!$Exam -or $_.exame -eq $Exam) -and (!$Year -or [int]$_.ano_edicao -eq $Year) })
if ($Limit) { $rows = @($rows | Select-Object -First $Limit) }
if ($rows.Count -eq 0) { throw 'Nenhum documento corresponde aos filtros.' }
function Get-SafeTarget($relative) {
    if ([IO.Path]::IsPathRooted($relative)) { throw 'O manifesto contém um caminho absoluto.' }
    $target = [IO.Path]::GetFullPath((Join-Path $destinationRoot $relative))
    if (!$target.StartsWith($destinationRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Caminho fora do acervo.' }
    if ([IO.Path]::GetExtension($target) -ne '.pdf') { throw 'O catálogo deve apontar para PDFs.' }
    return $target
}
function Test-Document($path, $row) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    if ((Get-Item -LiteralPath $path).Length -ne [long]$row.tamanho_bytes) { return $false }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $row.sha256
}
function Assert-OfficialUrl([uri]$uri) {
    if ($uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.DnsSafeHost -notin $allowedHosts -or !$uri.IsDefaultPort) { throw 'URL não autorizada pelo catálogo de fontes oficiais.' }
}
$handler = New-Object Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$client = New-Object Net.Http.HttpClient($handler)
$client.Timeout = [TimeSpan]::FromMinutes(4)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('Estudar-Corpus/2.0')
$report = New-Object 'System.Collections.Generic.List[object]'
$failed = 0; $downloaded = 0; $existing = 0; $position = 0
try {
    foreach ($row in $rows) {
        $position++
        Write-Progress -Activity 'Acervo oficial' -Status "$position de $($rows.Count): $($row.titulo)" -PercentComplete (100 * $position / $rows.Count)
        $target = Get-SafeTarget $row.arquivo_local
        if ($row.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$row.tamanho_bytes -le 0) { throw 'Integridade ausente no manifesto.' }
        if (Test-Document $target $row) { $existing++; Write-Host "[OK] $($row.arquivo_local)"; continue }
        if ($VerifyOnly) { $failed++; $report.Add([pscustomobject]@{arquivo=$row.arquivo_local;status='ausente_ou_divergente'}); continue }
        $url = if ($row.url_final) { $row.url_final } else { $row.url_download }
        $source = [uri]$url; Assert-OfficialUrl $source
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        $partial = $target + '.partial'; $success = $false
        for ($attempt=1; $attempt -le $Retries; $attempt++) {
            $response = $null
            try {
                $uri = $source
                for ($redirect=0; $redirect -le 5; $redirect++) {
                    Assert-OfficialUrl $uri
                    $response = $client.GetAsync($uri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                    if ([int]$response.StatusCode -notin @(301,302,303,307,308)) { break }
                    if (!$response.Headers.Location -or $redirect -eq 5) { throw 'Redirecionamentos excedidos.' }
                    $uri = New-Object Uri($uri, $response.Headers.Location); $response.Dispose(); $response = $null
                }
                if (!$response.IsSuccessStatusCode) { throw "Servidor respondeu HTTP $([int]$response.StatusCode)." }
                $inputStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                $outputStream = [IO.File]::Open($partial, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try {
                    $buffer = New-Object byte[] 131072; $bytes = 0L
                    while (($count = $inputStream.Read($buffer,0,$buffer.Length)) -gt 0) {
                        $outputStream.Write($buffer,0,$count); $bytes += $count
                        if ($bytes -gt [long]$row.tamanho_bytes) { throw 'O arquivo excede o tamanho esperado.' }
                        Write-Progress -Id 2 -ParentId 0 -Activity 'Baixando documento' -Status $row.arquivo_local -PercentComplete ([Math]::Min(100,100*$bytes/[long]$row.tamanho_bytes))
                    }
                    $outputStream.Flush($true)
                } finally { $outputStream.Dispose(); $inputStream.Dispose() }
                if (!(Test-Document $partial $row)) { throw 'Tamanho ou SHA-256 divergente. O original foi preservado.' }
                $check = [IO.File]::OpenRead($partial)
                try { $signature = New-Object byte[] 5; [void]$check.Read($signature,0,5) } finally { $check.Dispose() }
                if ([Text.Encoding]::ASCII.GetString($signature) -ne '%PDF-') { throw 'A resposta não é um PDF.' }
                if (Test-Path -LiteralPath $target) { [IO.File]::Move($target, $target + '.divergente-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')) }
                [IO.File]::Move($partial,$target); $success = $true; $downloaded++; Write-Host "[BAIXADO] $($row.arquivo_local)"; break
            } catch {
                $message = $_.Exception.Message
                Write-Host "[TENTATIVA $attempt/$Retries] $($row.arquivo_local): $message"
                if ($attempt -lt $Retries) { Start-Sleep -Seconds ([Math]::Min(8,2*$attempt)) }
            } finally { if ($response) { $response.Dispose() } }
        }
        if (!$success) { $failed++; $report.Add([pscustomobject]@{arquivo=$row.arquivo_local;status='falha';motivo=$message}) }
    }
} finally { $client.Dispose(); $handler.Dispose(); Write-Progress -Activity 'Acervo oficial' -Completed; Write-Progress -Id 2 -Activity 'Baixando documento' -Completed }
$summary = [pscustomobject]@{verificado_utc=[DateTime]::UtcNow.ToString('o');catalogados=$rows.Count;existentes_verificados=$existing;baixados=$downloaded;falhas=$failed;erros=@($report.ToArray())}
[IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destinationRoot 'download-resultado.json') -Encoding UTF8
Write-Host "Concluído: $existing existentes íntegros, $downloaded baixados, $failed pendentes."
if ($failed) { exit 1 }
