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

## 검은 화면 진단

`XrRecoveryWatch`는 `[XRHealth]`에 XR 디스플레이 실행 여부, 카메라 활성 상태, 착용·화면·복귀 이벤트를 기록한다. 착용·복귀 후 추적이 준비되면 정면으로 재정렬하며, 검정 전환과 카운트다운 중에는 정렬을 미룬다. SDK의 네이티브 세션을 임의로 정지·재시작하지 않는다.

검은 화면이 재현되면 **재시작하기 전에** `powershell -File tools/capture_xr_recovery.ps1`로 로그를 저장한다. SDK 자동 로그도 켜져 있으며 기기 `Android/data/com.hroov.souvenirhunt/cache/auto_log/`에 저장된다. 로그에 참가자 정보가 들어갈 수 있으므로 외부 공유 전 확인한다.

재실행 버튼은 고장 난 출력 세션을 새 프로세스로 복구하는 운영 수단이다. 반복 탈착 시의 근본 원인 해결 여부는 글래스 실기기 검증으로 판단해야 한다.
