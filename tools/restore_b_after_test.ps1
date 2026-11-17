<#
 restore_b_after_test.ps1 — 11-12 하얀 텍스처 시험 때 Beam Pro의 B 앱에 넣은 시험용 설정을 되돌리고, 시험 코드가 빠진 B APK를 설치한다.

 되돌리는 것(B 기기 config, /sdcard/Android/data/com.hroov.souvenirhunt.b/files/config.txt):
   - vipIntroEnabled=false (안구 인증 연출 끔)  → 지움(기본값 = 켬)
   - diagCapture=true (화면 진단 캡처 켬)      → 지움(기본값 = 끔)
   방법: 시험 직전 백업 config.txt.bak_1112 와 비교해, 위 시험용 줄 말고 다른 차이가 없을 때만 백업을 그대로 되돌려 놓는다
        (경품 토큰 줄은 기기 안에서만 복사 — PC로 가져오거나 출력하지 않음). 다른 차이가 있으면 아무것도 바꾸지 않고 멈춘다.
 정리: B의 files/diag 폴더(시험 때 진단 캡처가 만든 이미지·실행 번호)를 지운다.
 설치: Builds\SouvenirHunt_B_0.4.0_vc65.apk (vc64의 시험용 진단 캡처 코드를 뺀 빌드, 콘텐츠는 vc63과 같음). 파일이 아직 없으면 -WaitApkMinutes 만큼 기다린다.
       Builds\SouvenirHunt_0.3.2_vc57.apk 가 있으면 A도 설치(11-16 A에 배출 뒤 가운데 복귀 추가).

 사용: PowerShell에서  & "C:\Users\user\Dev\SouvenirHunt\tools\restore_b_after_test.ps1"   (-Serial 192.168.68.51:5555 로 기기 지정 가능)
#>
param(
  [string]$Serial,
  [int]$WaitApkMinutes = 30
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
if (-not (Test-Path $adb)) { $adb = "adb" }
$pkg = "com.hroov.souvenirhunt.b"
$cfg = "/sdcard/Android/data/$pkg/files/config.txt"
$bak = "$cfg.bak_1112"
$apk = Join-Path $root "Builds\SouvenirHunt_B_0.4.0_vc65.apk"
$apkA = Join-Path $root "Builds\SouvenirHunt_0.3.2_vc57.apk"
$logDir = Join-Path $root "Logs"; New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ("restore_b_" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".txt")
function Say([string]$m) { $line = (Get-Date -Format "HH:mm:ss") + " " + $m; Write-Host $line; Add-Content -LiteralPath $log -Value $line -Encoding UTF8 }
function Adb { $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"; try { & $adb @args 2>&1 | ForEach-Object { "$_" } } finally { $ErrorActionPreference = $prev } }
function Sh([string]$c) { Adb -s $Serial shell $c }

# 1) 기기 찾기: 지정 → 이미 붙은 기기 → 마지막 무선 IP
if (-not $Serial) {
  $dev = @(Adb devices | Where-Object { $_ -match "^\S+\s+device$" } | ForEach-Object { ($_ -split "\s+")[0] })
  if ($dev.Count -eq 0) {
    $ipFile = Join-Path $PSScriptRoot "beampro_last_ip.txt"
    if (Test-Path $ipFile) { $ip = (Get-Content $ipFile -Raw).Trim(); Adb connect "$($ip):5555" | Out-Null; Start-Sleep 2
      $dev = @(Adb devices | Where-Object { $_ -match "^\S+\s+device$" } | ForEach-Object { ($_ -split "\s+")[0] }) }
  }
  if ($dev.Count -eq 0) { Say "기기 없음 — Beam Pro를 USB로 연결하거나 무선 adb를 켠 뒤 다시 실행"; exit 2 }
  $Serial = $dev[0]
}
Say "기기: $Serial"

# 2) config 비교 → 시험용 줄만 다르면 백업으로 되돌림
$hasBak = (Sh "[ -f $bak ] && echo yes") -join ""
if ($hasBak -notmatch "yes") { Say "백업 $bak 없음 — 되돌리지 않음(수동 확인 필요)"; exit 3 }
$diff = @(Sh "diff $bak $cfg")
$removed = @($diff | Where-Object { $_ -match "^< " } | ForEach-Object { $_.Substring(2).TrimEnd() })
$added   = @($diff | Where-Object { $_ -match "^> " } | ForEach-Object { $_.Substring(2).TrimEnd() })
$onlyAdded   = @($added   | Where-Object { $removed -notcontains $_ })
$onlyRemoved = @($removed | Where-Object { $added -notcontains $_ })
$allowed = { param($l) ($l -eq "") -or ($l -eq "diagCapture=true") -or ($l -eq "vipIntroEnabled=false") -or ($l -like "# 11-12*") }
$unexpected = @($onlyAdded | Where-Object { -not (& $allowed $_) }) + $onlyRemoved
if ($unexpected.Count -gt 0) {
  $safe = $unexpected | ForEach-Object { if ($_ -match "prizeToken") { "prizeToken=<가림>" } else { $_ } }
  Say ("시험용 줄 말고 다른 차이가 있어 되돌리지 않음: " + ($safe -join " | ")); exit 4
}
if ($diff.Count -eq 0) { Say "config가 이미 시험 전과 같음" }
else {
  Sh "cp $cfg $cfg.bak_1116_test && cp $bak $cfg" | Out-Null
  Say ("config 되돌림(시험용 줄 " + $onlyAdded.Count + "개 제거, 되돌리기 전 파일은 config.txt.bak_1116_test)")
}
$left = @(Sh "grep -E '^(vipIntroEnabled|diagCapture)=' $cfg")
Say ("확인: " + $(if ($left.Count -eq 0 -or ($left -join "") -eq "") { "인증·진단 시험 줄 없음 → 안구 인증 켬(기본값)" } else { "남은 줄 " + ($left -join ", ") }))

# 2-1) 시험 흔적 정리: 진단 캡처 폴더
$hasDiag = (Sh "[ -d /sdcard/Android/data/$pkg/files/diag ] && echo yes") -join ""
if ($hasDiag -match "yes") { Sh "rm -r /sdcard/Android/data/$pkg/files/diag" | Out-Null; Say "진단 캡처 폴더(files/diag) 지움" }

# 3) 시험 코드 뺀 B APK 설치
$waited = 0
while (-not (Test-Path $apk) -and $waited -lt $WaitApkMinutes * 60) { if ($waited -eq 0) { Say "APK 빌드 대기: $apk" }; Start-Sleep 15; $waited += 15 }
if (Test-Path $apk) {
  Sh "am force-stop $pkg" | Out-Null
  $r = (Adb -s $Serial install -r $apk) -join " "
  Say "설치: $r"
} else { Say "APK가 없어 설치는 건너뜀 — 빌드 뒤 이 스크립트를 다시 실행" }
$vc = (Sh "dumpsys package $pkg | grep -m1 -oE 'versionCode=[0-9]+'") -join ""
Say "B 버전: $vc"

# 4) A: 배출 뒤 가운데 복귀를 넣은 빌드가 있으면 설치
$waitedA = 0
while (-not (Test-Path $apkA) -and $waitedA -lt $WaitApkMinutes * 60) { if ($waitedA -eq 0) { Say "A APK 빌드 대기: $apkA" }; Start-Sleep 15; $waitedA += 15 }
if (Test-Path $apkA) {
  Sh "am force-stop com.hroov.souvenirhunt" | Out-Null
  $ra = (Adb -s $Serial install -r $apkA) -join " "
  Say "A 설치: $ra"
  $vca = (Sh "dumpsys package com.hroov.souvenirhunt | grep -m1 -oE 'versionCode=[0-9]+'") -join ""
  Say "A 버전: $vca"
} else { Say "A APK가 없어 A 설치는 건너뜀" }
Say "끝 (기록: $log)"
