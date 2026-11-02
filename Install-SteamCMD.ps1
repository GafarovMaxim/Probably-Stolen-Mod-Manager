param([Parameter(Mandatory=$true)][string]$GameDirectory)
$ErrorActionPreference='Stop'
$game=[IO.Path]::GetFullPath($GameDirectory)
if(-not (Test-Path -LiteralPath (Join-Path $game 'Probably Stolen.exe'))){throw 'Select the Probably Stolen game directory.'}
$tools=Join-Path $game 'Modding\ProbablyModManager\Tools\SteamCMD'
if(Test-Path -LiteralPath (Join-Path $tools 'steamcmd.exe')){Write-Output "SteamCMD is already installed: $tools";exit 0}
New-Item -ItemType Directory -Path $tools -Force | Out-Null
$archive=Join-Path $tools 'steamcmd-official.zip'
Invoke-WebRequest -Uri 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $tools
Write-Output 'Installing the official SteamCMD runtime using anonymous read-only startup. No Workshop item will be created.'
& (Join-Path $tools 'steamcmd.exe') +login anonymous +quit
if($LASTEXITCODE -ne 0){throw 'SteamCMD startup failed; review its console before retrying.'}
Write-Output 'SteamCMD installed. Sign-in for publication takes place in its own interactive console.'
