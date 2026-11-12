# SouvenirHunt 기기 로그 수집 — [Spawn]/[Perf]/[Cycle]/[Reset]/[Render]/[ConfigLoader]/Exception 만 추려 logs/yyyyMMdd_HHmm.txt 로 저장
# 사용: powershell -ExecutionPolicy Bypass -File tools\pull_logs.ps1 [-Device 192.168.68.51:5555] [-Seconds 60] [-Clear]
#   -Seconds N : N초 동안 실시간 수집 (0이면 지금까지 쌓인 버퍼만 덤프)
#   -Clear     : 수집 전에 logcat 버퍼를 비움 (사이클 리허설 시작 시)
param(
    [string]$Device = "",
    [int]$Seconds = 0,
    [switch]$Clear
)
$ErrorActionPreference = "Continue"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outDir = Join-Path $root "logs"
New-Item -ItemType Directory -Force $outDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd_HHmm"
$out = Join-Path $outDir "$stamp.txt"

# adb 는 PATH 에 없는 게 보통이다 — Unity 가 딸려 보내는 platform-tools 로 폴백
$adb = (Get-Command adb -ErrorAction SilentlyContinue).Source
if (-not $adb) {
    $adb = Join-Path $env:ProgramFiles "Unity\Hub\Editor\6000.0.79f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
}
if (-not (Test-Path $adb)) {
    Write-Host "adb 를 찾을 수 없습니다. PATH 에 넣거나 아래 경로를 확인하세요:`n  $adb"
    exit 1
}

$adbArgs = @()
if ($Device -ne "") { $adbArgs += @("-s", $Device) }

$devices = & $adb devices
if (-not ($devices -match "device$")) {
    Write-Host "기기 없음. 무선: adb pair <ip>:<port> → adb connect 192.168.68.51:5555 (재부팅 후엔 USB로 adb tcpip 5555)"
    exit 1
}
# 링버퍼 확장 — Beam Pro 기본값은 256KiB뿐인데 NREAL 런처(HOME)가 WifiInfo/Bluetooth 폴링을
# 초당 수십 줄 뱉어, 앱 로그가 2분이면 밀려 사라진다(08-27 플레이테스트에서 스폰 구간 전량 유실).
# 기기 재부팅하면 기본값으로 돌아가므로 매번 설정한다.
& $adb @adbArgs logcat -G 16M | Out-Null
$bufSize = (& $adb @adbArgs logcat -g) -match "^main:" -replace ".*ring buffer is ([\d.]+ [KMG]iB).*", '$1'
Write-Host "logcat 링버퍼: $bufSize"

if ($Clear) { & $adb @adbArgs logcat -c; Write-Host "logcat 버퍼 비움" }

# SP_\d\d 는 [Spawn] 블록의 이어지는 배치 상세 줄 (Debug.Log 1회 = 여러 줄)
$pattern = "\[Spawn\]|SP_\d\d|\[Perf\]|\[Cycle\]|\[Reset\]|\[Render\]|\[Recenter\]|\[ConfigLoader\]|\[MeshLibrary\]|Exception|Error"
if ($Seconds -gt 0) {
    # 링버퍼를 16MB 로 키웠으므로 실시간 스트리밍(Start-Job)이 필요 없다 — 기다렸다 한 번에 덤프하면 된다.
    # 여기서 버퍼를 비우지 않는 것이 중요하다: 방금 터진 예외를 잡으려고 -Seconds 60 을 돌리는 경우,
    # 비워버리면 정작 그 예외가 사라진다. 비우려면 -Clear 를 명시해야 한다(위에서 이미 처리).
    Write-Host "$Seconds 초 수집 중... (글래스 착용 상태여야 [Perf] 로그가 나옵니다)"
    Start-Sleep -Seconds $Seconds
}
$lines = & $adb @adbArgs logcat -d -v time -s Unity
$hits = $lines | Where-Object { $_ -match $pattern }
$hits | Out-File -FilePath $out -Encoding utf8

$perf = $hits | Where-Object { $_ -match "\[Perf\]" }
$cycles = ($hits | Where-Object { $_ -match "\[Cycle\] END" }).Count
$exc = ($hits | Where-Object { $_ -match "Exception" }).Count
Write-Host "저장: $out"
Write-Host "  [Perf] $($perf.Count)건, 사이클 종료 $cycles 회, Exception $exc 건"
if ($perf.Count -eq 0) { Write-Host "  NO DATA — 글래스 미착용/앱 정지 상태일 수 있음" }
else { $perf | Select-Object -Last 3 | ForEach-Object { Write-Host "  $_" } }
