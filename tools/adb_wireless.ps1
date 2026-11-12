param(
    [switch]$Install,          # 연결 후 Builds\SouvenirHunt.apk 설치
    [switch]$Launch,           # 설치/연결 후 앱 실행
    [string]$Ip = ""           # 알려진 IP 지정(생략 시 USB로 조회 → 마지막 저장값 → mDNS 순)
)
# Beam Pro 무선 adb 연결 헬퍼 (09-03).
#  1) 이미 무선으로 붙어 있으면 그대로 사용
#  2) USB로 붙어 있으면 Wi-Fi IP를 읽어 `adb tcpip 5555` 후 무선 연결(USB는 뽑아도 됨)
#  3) USB가 없으면 마지막 저장 IP(tools\beampro_last_ip.txt):5555 → 실패 시 mDNS(_adb-tls-connect) 검색
#  주의: tcpip 5555 모드는 기기 재부팅 시 꺼진다 → 재부팅 후엔 USB를 한 번 꽂고 이 스크립트를 다시 실행.
#  Android 11+ '무선 디버깅' 토글(개발자 옵션)이 켜져 있고 이 PC와 페어링돼 있으면 mDNS 경로로 재부팅 후에도 붙는다.
#  09-14: 이 PC는 Beam Pro(X4000)와 페어링 완료 → 재부팅 후엔 USB 대신 기기에서 '무선 디버깅'만 켜고 이 스크립트 실행.
#         mDNS 자동 연결 기기는 serial이 "adb-<시리얼>-xxxx._adb-tls-connect._tcp"(포트 없음)라 무선으로 분류한다.
#         connect가 10013(액세스 권한 소켓)으로 실패하면 PC의 VPN(WireGuard 등)이 LAN을 막는 것 — VPN을 끄고 재실행.
$ErrorActionPreference = "Continue"
# 10-15: Android SDK platform-tools adb(v37) 우선 — Unity 내장 adb(v36)와 섞어 쓰면 adb 서버 버전이 맞지 않아
#        서로 서버를 죽이고 다시 띄우며 설치가 멈추거나 끊긴다(10-13 실제 발생). 둘 다 없으면 PATH의 adb.
$adb = @("$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
         "C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe") |
       Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $adb) { $adb = "adb" }
$root = Split-Path -Parent $PSScriptRoot
$apk = Join-Path $root "Builds\SouvenirHunt.apk"
$ipFile = Join-Path $PSScriptRoot "beampro_last_ip.txt"
$pkg = "com.hroov.souvenirhunt"

function Get-Devices {
    & $adb devices | Select-Object -Skip 1 | Where-Object { $_.Trim() -ne "" } | ForEach-Object {
        $p = $_ -split "\s+"; [pscustomobject]@{ Serial = $p[0]; State = $p[1] }
    }
}

$wirelessPattern = ":\d+$|_adb-tls-connect"   # ip:port(tcpip 5555) 또는 무선 디버깅 mDNS 자동 연결

function Get-Wireless { param($list) $list | Where-Object { $_.Serial -match $wirelessPattern -and $_.State -eq "device" } | Sort-Object { $_.Serial -notmatch ":5555$" } | Select-Object -First 1 }

& $adb start-server | Out-Null
$devs = @(Get-Devices)
if (-not (Get-Wireless $devs)) { Start-Sleep -Seconds 3; $devs = @(Get-Devices) }   # 무선 디버깅 mDNS 자동 연결은 서버 시작 후 몇 초 걸림
$wireless = Get-Wireless $devs
$usb = $devs | Where-Object { $_.Serial -notmatch $wirelessPattern -and $_.State -eq "device" } | Select-Object -First 1
$offline = $devs | Where-Object { $_.State -eq "offline" }
if ($offline) { & $adb reconnect offline | Out-Null; Start-Sleep -Seconds 2; $devs = @(Get-Devices); $wireless = Get-Wireless $devs }

if (-not $wireless) {
    if ($Ip -eq "" -and $usb) {
        $Ip = (& $adb -s $usb.Serial shell "ip -f inet addr show wlan0" | Select-String -Pattern "inet (\d+\.\d+\.\d+\.\d+)").Matches.Groups[1].Value
        if ($Ip) {
            Write-Output "USB($($usb.Serial)) → Wi-Fi IP $Ip, tcpip 5555 활성화"
            & $adb -s $usb.Serial tcpip 5555 | Out-Null
            Start-Sleep -Seconds 3
            Set-Content -Path $ipFile -Value $Ip -Encoding ascii
        } else { Write-Output "USB 기기의 Wi-Fi IP를 못 읽음 — 기기 Wi-Fi 확인" }
    }
    if ($Ip -eq "" -and (Test-Path $ipFile)) { $Ip = (Get-Content $ipFile | Select-Object -First 1).Trim() }
    if ($Ip -ne "") {
        Write-Output ("connect " + $Ip + ":5555 → " + (& $adb connect ("{0}:5555" -f $Ip)))
    }
    $devs = @(Get-Devices)
    $wireless = Get-Wireless $devs
    if (-not $wireless) {
        Write-Output "5555 실패 → mDNS(무선 디버깅) 검색"
        # 출력 예: adb-RHLM56K1186707-q5wfTq	_adb-tls-connect._tcp	192.168.68.52:38535 (끝 점 없음)
        $svc = & $adb mdns services | Select-String -Pattern "_adb-tls-connect\._tcp\.?\s+(\d+\.\d+\.\d+\.\d+:\d+)" | Select-Object -First 1
        if ($svc) {
            $addr = $svc.Matches.Groups[1].Value
            Write-Output ("connect " + $addr + " → " + (& $adb connect $addr))
            $devs = @(Get-Devices)
            $wireless = Get-Wireless $devs
        }
    }
}

# 무선 디버깅(TLS)으로만 붙었으면 tcpip 5555도 켜 둔다 — 무선 디버깅 토글이 꺼져도 재부팅 전까지 IP:5555로 연결 가능
if ($wireless -and $wireless.Serial -match "_adb-tls-connect") {
    $wip = (& $adb -s $wireless.Serial shell "ip -f inet addr show wlan0" | Select-String -Pattern "inet (\d+\.\d+\.\d+\.\d+)").Matches.Groups[1].Value
    if ($wip) {
        Set-Content -Path $ipFile -Value $wip -Encoding ascii
        if (-not ((Get-Devices) | Where-Object { $_.Serial -eq "$($wip):5555" -and $_.State -eq "device" })) {
            & $adb -s $wireless.Serial tcpip 5555 | Out-Null
            Start-Sleep -Seconds 3
            Write-Output ("tcpip 5555 활성화 → connect " + $wip + ":5555 → " + (& $adb connect "$($wip):5555"))
            $w5555 = (Get-Devices) | Where-Object { $_.Serial -eq "$($wip):5555" -and $_.State -eq "device" } | Select-Object -First 1
            if ($w5555) { $wireless = $w5555 }
        }
    }
}

if (-not $wireless) {
    Write-Output "무선 연결 실패. 현재 장치:"; $devs | ForEach-Object { Write-Output ("  " + $_.Serial + " " + $_.State) }
    Write-Output "→ 기기 Wi-Fi가 PC와 같은 네트워크인지 확인. 재부팅했으면 기기에서 개발자 옵션 > '무선 디버깅'을 켠 뒤 재실행(이 PC는 09-14 페어링됨 — 페어링이 풀렸으면 'adb pair <ip>:<페어링포트> <코드>' 1회). USB 연결로도 가능."
    Write-Output "→ connect 오류가 10013이면 PC의 VPN(WireGuard 등)을 끄고 재실행."
    exit 1
}
Write-Output ("무선 연결됨: " + $wireless.Serial)

if ($Install) {
    if (-not (Test-Path $apk)) { Write-Output "APK 없음: $apk (메뉴 11 빌드 먼저)"; exit 1 }
    $before = (& $adb -s $wireless.Serial shell dumpsys package $pkg | Select-String -Pattern "versionCode=(\d+)" | Select-Object -First 1)
    Write-Output ("설치 전 " + $(if ($before) { $before.Matches.Value } else { "미설치" }))
    $res = & $adb -s $wireless.Serial install -r $apk 2>&1
    Write-Output ("install: " + ($res | Select-Object -Last 1))
    $after = (& $adb -s $wireless.Serial shell dumpsys package $pkg | Select-String -Pattern "versionCode=(\d+)" | Select-Object -First 1)
    Write-Output ("설치 후 " + $(if ($after) { $after.Matches.Value } else { "확인 실패" }))
}
if ($Launch) {
    & $adb -s $wireless.Serial shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 | Out-Null
    Write-Output "앱 실행 요청 (글래스 착용 필요)"
}
