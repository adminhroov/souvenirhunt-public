# Beam Pro 실행 앱

Beam Pro 기본 런처는 Application 메타데이터 `nreal_sdk=true`인 패키지를 숨긴다. 연결 기기의 `SearchLauncherQuickStep.apk`에서 `AppFilter`, `SessionCommitReceiver`, `AddWorkspaceItemsTask`의 필터를 확인했다. 게임 APK의 SDK 메타데이터를 제거하면 MyGlasses 인식에 영향을 줄 수 있어, 일반 Android 실행 앱을 별도 패키지로 제공한다.

- 게임: `com.hroov.souvenirhunt` — Unity/XREAL, 기존 MyGlasses 진입점 유지.
- 실행 아이콘: `com.hroov.souvenirhunt.launcher` — `SouvenirHunt 실행`. 글래스 없이도 시작 화면을 열 수 있다.
- 시작 화면: 게임 APK 안의 `BeamEntryActivity`, 독립 `:entry` 프로세스. 글래스 연결 상태를 확인하고 SDK의 `NRXRActivity`를 연다.
- 새로 실행: 사용자가 버튼을 누르면 같은 UID·정확히 게임 이름인 Unity 프로세스만 종료하고 종료 확인 후 다시 연다. config와 경품 로그 파일은 보존한다. 진행 중인 라운드는 초기화한다.
- 경품 요청 중에는 새로 실행을 막는다. Unity가 기록한 상태의 PID와 실제 실행 PID가 같을 때만 이 보호를 적용한다.
- 앱 간 진입 권한은 `signature`로 제한한다. **두 APK를 같은 인증서로 서명해야 한다.**

## 빌드

프로젝트 루트에서 `python tools/beam-launcher/build.py` 실행 후 Unity 메뉴 11로 게임 APK를 빌드한다. Unity 6000.0.79f1에 포함된 JDK·SDK를 사용한다. 다른 설치 경로는 `--android-player`로 지정한다.

출력:

- `Assets/Plugins/Android/SouvenirBeamEntry.aar` — 게임 APK에 포함되는 시작 화면.
- `Builds/SouvenirHunt_Launcher_1.0.apk` — Beam Pro 일반 앱 목록에 나타나는 실행 아이콘.

기본은 Unity와 같은 로컬 Android debug keystore. 출시 인증서는 `--keystore`, `SOUVENIR_KS_PASS`, `SOUVENIR_KEY_PASS`로 지정한다. 키·비밀번호를 소스에 저장하지 않는다. 게임 APK → 실행 APK 순서로 설치한다.

## 버전 B(10-29, 별도 패키지)

`python C:\Users\user\Dev\SouvenirHunt\tools\beam-launcher\build.py --variant B`(PowerShell, ASCII 정션 경로로 — Git Bash는 한글 경로로 풀려 aapt2 실패) → 실행 앱 `com.hroov.souvenirhunt.launcher.b` "B SouvenirHunt 실행"(대상 게임 `com.hroov.souvenirhunt.b` "B SouvenirHunt", 10-29 vc58부터 이름 앞에 B) = `Builds/SouvenirHunt_Launcher_B_1.0.apk`. `--variant A`(기본)는 기존 `SouvenirHunt_Launcher_1.0.apk`. 게임 B는 Unity 메뉴 11b(AutoRun `11b`). 브리지 매니페스트의 권한·taskAffinity는 `${applicationId}.permission.CONTROL`·`${applicationId}.entry`라 AAR 하나를 A·B가 같이 쓰고 패키지마다 따로 만들어진다. 실행 앱은 자기 패키지에서 `.launcher`를 뺀 게임을 연다. 설치는 게임 → 실행 앱 순(같은 debug 인증서). XREAL `NRXRActivity`의 taskAffinity(`xreal.unity`)는 A·B가 같지만 OS가 uid를 접두해 태스크는 섞이지 않는다 — 다만 두 XR 세션이 같은 안경을 잡지 않게 **번갈아 실행 전에 다른 쪽을 force-stop(또는 최근 앱에서 닫기)**.

## 시작 화면 대응(10-29 vc59 — 검은 화면 저위험 대응)

"다시 실행하면 안경만 검어짐"(B에서만) 조사 결론: Android·Unity는 정상, 검은 프로세스에서만 MyGlasses 서버 lib의 `65 get rsp error, 5001` = 안경 출력 링크가 세션에 안 붙음(원인 미확정). 원인과 관계없이 해가 없는 것만 `BeamEntryActivity`(브리지 AAR)에 넣었다.

- **시작 게이트**: 안경 디스플레이가 **3D(3840×1080)로 같은 displayId 3초 연속**일 때만 [콘텐츠 시작] 활성 — 연결 직후·2D→3D 전환 중에는 "글래스 준비 중… 잠시 뒤 시작할 수 있어요". 안전판: 2D(1920×1080)만 15초 넘게 계속이면 허용(시스템이 3D로 바꿔 주지 않는 경우 버튼이 영영 꺼지지 않게 — `FALLBACK_2D_MS`). 로그 `SouvenirEntry: glasses <id> <w>x<h> ready|not ready`.
- **상대 변형 종료(vc59~60) → vc61 제거**: 시작 직전 상대 게임 패키지에 `ActivityManager.killBackgroundProcesses`를 불렀지만 Android 14부터 자기 앱만 대상이라 **상대 게임을 끄지 못했다**(logcat `Invalid packageName` — 에뮬레이터 검증은 우리 로그 줄만 봄). 10-29 감사로 코드·권한(`KILL_BACKGROUND_PROCESSES`)·두 패키지 `queries`를 지웠다. 운영: 한쪽을 실행하기 전에 다른 쪽을 force-stop하거나 최근 앱에서 닫는다.
- **매니페스트(10-29 감사, vc61)**: `<uses-sdk android:minSdkVersion="31">`(없으면 병합기가 targetSdk < 4로 보고 READ_PHONE_STATE를 게임 APK에 암묵 추가) · `<application android:allowBackup="false" android:fullBackupContent="false" android:dataExtractionRules="@xml/data_extraction_rules">`(클라우드 백업·기기 이전 모두 제외 — 기기 config의 경품 토큰·prize_log 보호). 리소스는 `bridge/res/xml/data_extraction_rules.xml`, `build.py`가 AAR에 `res/`·`R.txt`로 넣는다.
- **displayId 기록**: 시작 때 안경 displayId를 `files/souvenir-entry.json`(Unity가 1초마다 덮어쓰는 상태 파일과 별도)에 기록. 지난 시작 때와 다르고 게임 pid가 살아 있으면 끄고 새로 시작(`Glasses display changed a -> b, fresh start of pid N`).
- **linkLost 강조**: 게임(`XrRecoveryWatch`)이 자기 로그에서 서명(`clawLinkLossSignature`, 기본 "get rsp error")을 5초에 3줄 이상 보면 상태 파일에 `linkLost=true`(`publish` 5인자) → 시작 화면에 "안경 화면 연결이 끊어졌어요" + [▶ 처음부터 다시 실행 (권장)] 강조. Beam Pro 오버레이에도 같은 안내(자동 재시작 없음).
- **에뮬레이터 디버그 분기**: `Build.HARDWARE`가 ranchu/goldfish면(10-29 감사 vc61: `Build.FINGERPRINT` 부분 문자열 판정은 실기기 오판 여지로 뺌) 기본 화면이 아닌 첫 디스플레이를 안경으로 본다 — `adb shell settings put global overlay_display_devices 3840x1080/320`(2D 흉내 `1920x1080/320`, 끝나면 `settings delete global overlay_display_devices`). 10-29 `Moyo_Pixel9_API36_B`에서 위 동작 확인(게임 B arm64는 변환으로 설치·실행되나 안경이 없어 SDK가 약 16초 뒤 스스로 종료).
- 앱 안 [종료]: `ClawSessionBase.QuitLikeSdk`가 `QuitApplication` 2초 뒤에도 살아 있으면 `killProcess`(살아남은 XR 프로세스가 다음 실행의 링크를 잡지 않게).

## 검은 화면 진단

`XrRecoveryWatch`는 `[XRHealth]`에 XR 디스플레이 실행 여부, 카메라 활성 상태, 착용·화면·복귀 이벤트를 기록한다. 착용·복귀 후 추적이 준비되면 정면으로 재정렬하며, 검정 전환과 카운트다운 중에는 정렬을 미룬다. SDK의 네이티브 세션을 임의로 정지·재시작하지 않는다.

검은 화면이 재현되면 **재시작하기 전에** `powershell -File tools/capture_xr_recovery.ps1`로 로그를 저장한다(10-29: 버전 B는 `-Package B` — events 버퍼·window·SurfaceFlinger·nebula pid·안경 화면 캡처 포함, 저장 뒤 logcat 버퍼 32M). SDK 자동 로그도 켜져 있으며 기기 `Android/data/com.hroov.souvenirhunt/cache/auto_log/`에 저장된다. 로그에 참가자 정보가 들어갈 수 있으므로 외부 공유 전 확인한다.

재실행 버튼은 고장 난 출력 세션을 새 프로세스로 복구하는 운영 수단이다. 반복 탈착 시의 근본 원인 해결 여부는 글래스 실기기 검증으로 판단해야 한다.
