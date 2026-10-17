[CmdletBinding()]
param([string]$Destination = (Join-Path $PSScriptRoot 'Executavel'), [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$executablePath = Join-Path $Destination 'Estudar.exe'
$runningApp = Get-Process -Name Estudar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executablePath }
if ($runningApp) { throw 'Feche o Estudar antes de recompilar.' }
dotnet publish (Join-Path $PSScriptRoot 'Estudo.Windows/Estudo.Windows.csproj') -c Release -r win-x64 --self-contained ($SelfContained.IsPresent.ToString().ToLowerInvariant()) -p:DebugType=None -p:DebugSymbols=false -o $Destination --nologo
if ($LASTEXITCODE -ne 0) { throw 'A publicação falhou; confira o resultado da compilação.' }
Write-Output "Publicado em $executablePath"
foreach ($folder in @('catalogo','scripts','licenses')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination $Destination -Recurse -Force }
foreach ($file in @('baixar-provas.bat','README.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $Destination -Force }
