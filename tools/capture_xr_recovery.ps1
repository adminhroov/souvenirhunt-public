param([string]$Serial = '', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('..\Logs\xr-recovery-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$capturePath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $capturePath -Force | Out-Null
$adbArgs = @()
if ($Serial) { $adbArgs = @('-s', $Serial) }
function Save-AdbOutput([string]$Name, [string[]]$Command) {
    $result = & adb @adbArgs @Command 2>&1
    $code = $LASTEXITCODE
    $result | Out-File -LiteralPath (Join-Path $capturePath $Name) -Encoding utf8
    if ($code -ne 0) { Write-Warning "$Name : adb exit $code" }
}
Save-AdbOutput 'logcat.txt' @('logcat','-d','-v','threadtime','-b','main,system,crash')
Save-AdbOutput 'displays.txt' @('shell','dumpsys','display')
Save-AdbOutput 'activities.txt' @('shell','dumpsys','activity','activities')
Save-AdbOutput 'game-package.txt' @('shell','dumpsys','package','com.hroov.souvenirhunt')
Save-AdbOutput 'myglasses-package.txt' @('shell','dumpsys','package','com.xreal.evapro.nebula')
Save-AdbOutput 'firmware.txt' @('shell','getprop','ro.build.display.id')
Save-AdbOutput 'game-process.txt' @('shell','pidof','com.hroov.souvenirhunt')
Write-Output "XR diagnostics saved: $capturePath"
Write-Output 'Capture once while the glasses are black, before restarting. Logs are local and may contain participant data.'
