param([string]$Serial = '', [string]$OutputDirectory = '', [string]$Package = 'A')
# 10-29: -Package A (default, com.hroov.souvenirhunt) | B (com.hroov.souvenirhunt.b) | full package name.
# Adds events buffer, window/SurfaceFlinger dumps, MyGlasses (nebula) pid, glasses physical-display screencap,
# and enlarges the logcat ring buffer to 32M AFTER saving (so the next black-screen repro keeps more history).
# 10-29 audit: collection runs with ErrorActionPreference=Continue (one adb stderr line no longer aborts the capture),
# 'adb start-server' first, the device serial is pinned with -s (auto-picked when exactly one device), pids for
# nebula + nebula:space + both games (A/B) + both :entry processes, and ro.build.fingerprint / ro.hardware.
$ErrorActionPreference = 'Stop'
switch ($Package) {
    'A' { $Package = 'com.hroov.souvenirhunt' }
    'B' { $Package = 'com.hroov.souvenirhunt.b' }
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ('..\Logs\xr-recovery-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$capturePath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $capturePath -Force | Out-Null
# 10-29: from here on adb stderr must not abort the capture (PowerShell 5.1 turns 2>&1 lines into errors)
$ErrorActionPreference = 'Continue'
# 10-29: Android SDK adb only (a different adb version on PATH restarts the server and drops wireless devices)
$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
if (-not (Test-Path -LiteralPath $adb)) { $adb = 'adb' }
& $adb start-server 2>&1 | Out-Null
if (-not $Serial) {
    # 10-29: pin the serial so every command hits the same device ('more than one device' otherwise)
    $devices = @(& $adb devices 2>&1 | ForEach-Object { [string]$_ } | Where-Object { $_ -match '^(\S+)\s+device$' } | ForEach-Object { ($_ -split '\s+')[0] })
    if ($devices.Count -eq 1) { $Serial = $devices[0]; Write-Output "Device: $Serial" }
    elseif ($devices.Count -gt 1) { Write-Error ('More than one device (' + ($devices -join ', ') + ') - pass -Serial'); exit 1 }
    else { Write-Warning 'No adb device in state "device" - captures will be empty' }
}
$adbArgs = @()
if ($Serial) { $adbArgs = @('-s', $Serial) }
function Save-AdbOutput([string]$Name, [string[]]$Command) {
    $result = @()
    try {
        $result = & $adb @adbArgs @Command 2>&1 | ForEach-Object { [string]$_ }
        $code = $LASTEXITCODE
        $result | Out-File -LiteralPath (Join-Path $capturePath $Name) -Encoding utf8
        if ($code -ne 0) { Write-Warning "$Name : adb exit $code" }
    } catch { Write-Warning "$Name : $($_.Exception.Message)" }
    return $result
}
Save-AdbOutput 'logcat.txt' @('logcat','-d','-v','threadtime','-b','main,system,crash') | Out-Null
Save-AdbOutput 'logcat-events.txt' @('logcat','-d','-v','threadtime','-b','events') | Out-Null
Save-AdbOutput 'displays.txt' @('shell','dumpsys','display') | Out-Null
Save-AdbOutput 'activities.txt' @('shell','dumpsys','activity','activities') | Out-Null
Save-AdbOutput 'windows.txt' @('shell','dumpsys','window','windows') | Out-Null
Save-AdbOutput 'surfaceflinger-list.txt' @('shell','dumpsys','SurfaceFlinger','--list') | Out-Null
$ids = Save-AdbOutput 'surfaceflinger-display-ids.txt' @('shell','dumpsys','SurfaceFlinger','--display-id')
Save-AdbOutput 'game-package.txt' @('shell','dumpsys','package',$Package) | Out-Null
Save-AdbOutput 'myglasses-package.txt' @('shell','dumpsys','package','com.xreal.evapro.nebula') | Out-Null
Save-AdbOutput 'firmware.txt' @('shell','getprop','ro.build.display.id') | Out-Null
# 10-29: build fingerprint + hardware (emulator vs Beam Pro, firmware build)
Save-AdbOutput 'build-props.txt' @('shell','echo fingerprint=$(getprop ro.build.fingerprint); echo hardware=$(getprop ro.hardware)') | Out-Null
Save-AdbOutput 'game-process.txt' @('shell','pidof',$Package) | Out-Null
Save-AdbOutput 'nebula-process.txt' @('shell','pidof','com.xreal.evapro.nebula') | Out-Null
# 10-29: pids of every related process (name=pid, empty = not running) + 'ps -A' rows as a fallback when pidof misses
$procNames = 'com.xreal.evapro.nebula','com.xreal.evapro.nebula:space','com.hroov.souvenirhunt','com.hroov.souvenirhunt:entry','com.hroov.souvenirhunt.b','com.hroov.souvenirhunt.b:entry'
$procScript = (($procNames | ForEach-Object { "echo $_=`$(pidof $_)" }) -join '; ') + "; echo; ps -A | grep -E 'souvenirhunt|nebula'"
Save-AdbOutput 'processes.txt' @('shell',$procScript) | Out-Null
# Glasses physical display (XREAL, pnpId MRG) -> screencap -d <id> on the device, then pull (binary-safe)
$glassesId = $null
foreach ($line in $ids) {
    $s = [string]$line
    if ($s -match '^Display (\d+)' -and ($s -match 'MRG' -or $s -match 'XREAL')) { $glassesId = $Matches[1]; break }
}
if ($glassesId) {
    & $adb @adbArgs shell screencap -d $glassesId -p /sdcard/xr_glasses_capture.png 2>&1 | Out-Null
    & $adb @adbArgs pull /sdcard/xr_glasses_capture.png (Join-Path $capturePath 'glasses.png') 2>&1 | Out-Null
    & $adb @adbArgs shell rm -f /sdcard/xr_glasses_capture.png 2>&1 | Out-Null
    Write-Output "Glasses display $glassesId captured"
} else { Write-Warning 'Glasses physical display id not found (surfaceflinger-display-ids.txt)' }
& $adb @adbArgs logcat -G 32M 2>&1 | Out-Null
Write-Output "XR diagnostics saved: $capturePath ($Package)"
Write-Output 'Capture once while the glasses are black, before restarting. Logs are local and may contain participant data.'
Write-Output 'logcat ring buffer set to 32M for the next reproduction.'
