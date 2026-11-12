using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// config.txt 로더 (Phase 0). 키=값 형식(JSON 아님 — 인수인계 §2-2).
    /// 어떤 입력 오류에도 예외를 던지지 않는다: 줄 단위 무시 + 기본값 폴백 (§4-2 방어 규칙).
    /// 파일이 없으면 DefaultConfigText로 시드 — 운영자는 앱을 한 번 실행하면 편집할 파일이 생긴다.
    /// 09-03: SetValues — 운영자 패널 스테퍼가 값 몇 개를 파일에 되써 넣는 경로(주석·다른 키 보존, 원자적 저장).
    /// </summary>
    public static class ConfigLoader
    {
        static readonly Regex KeyPattern = new(@"^[A-Za-z0-9_.]+$", RegexOptions.Compiled);

        /// <summary>
        /// 지정한 키들만 config.txt에 기록한다(첫 일치 줄 교체, 중복 제거, 없는 키는 끝에 추가). 다른 줄은 원문 보존.
        /// 임시 파일에 쓴 뒤 교체(중단돼도 config가 잘리지 않음). 절대 예외를 던지지 않는다 — 실패 시 false + 메시지.
        /// 반영은 호출자가 세션.ResetGame()(config 재로드)으로.
        /// </summary>
        public static bool SetValues(IReadOnlyDictionary<string, string> values, out string message)
        {
            message = "";
            if (values == null || values.Count == 0) { message = "변경할 값 없음"; return false; }
            foreach (var kv in values)
            {
                if (string.IsNullOrEmpty(kv.Key) || !KeyPattern.IsMatch(kv.Key)) { message = $"키 형식 오류: {kv.Key}"; return false; }
                if (kv.Value == null || kv.Value.IndexOf('\n') >= 0 || kv.Value.IndexOf('\r') >= 0) { message = $"값 형식 오류: {kv.Key}"; return false; }
            }

            string path = ConfigPath;
            try
            {
                string original = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : DefaultConfigText;
                string newline = original.Contains("\r\n") ? "\r\n" : "\n";
                var lines = new List<string>(original.Replace("\r\n", "\n").Split('\n'));
                if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1); // 끝 개행

                var remaining = new Dictionary<string, string>();
                foreach (var kv in values) remaining[kv.Key] = kv.Value;
                var seen = new HashSet<string>();
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i].Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = t.Substring(0, eq).Trim();
                    if (!values.ContainsKey(key)) continue;
                    if (seen.Contains(key)) { lines.RemoveAt(i); i--; continue; } // 중복 줄 제거
                    seen.Add(key);
                    lines[i] = $"{key}={values[key]}";
                    remaining.Remove(key);
                }
                if (remaining.Count > 0)
                {
                    lines.Add("");
                    lines.Add($"# 운영자 패널에서 변경 {DateTime.Now:yyyy-MM-dd HH:mm}");
                    foreach (var kv in remaining) lines.Add($"{kv.Key}={kv.Value}");
                }

                string text = string.Join(newline, lines) + newline;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);

                message = $"{values.Count}개 키 저장";
                Debug.Log($"[ConfigLoader] SetValues: {string.Join(", ", values.Keys)} → {path}");
                return true;
            }
            catch (Exception e)
            {
                message = e.GetType().Name + ": " + e.Message;
                Debug.LogWarning("[ConfigLoader] SetValues 실패 — 기존 값 유지: " + message);
                return false;
            }
        }

        public const string FileName = "config.txt";

        public static string ConfigPath => Path.Combine(Application.persistentDataPath, FileName);

        // 인수인계 §4-2의 "StreamingAssets 기본 config 복사"를 임베드 문자열 시드로 단순화.
        // (Android에서 StreamingAssets 읽기는 async UnityWebRequest 필요 — 운영자 체감 동일, 부팅 코드 단순)
        // 에디터 셋업 스크립트가 같은 내용을 StreamingAssets/config.txt로도 내보내 참조본을 남긴다.
        public const string DefaultConfigText =
@"# XR 기념품 찾기 운영 설정 — 메모장으로 수정 후 UTF-8로 저장
# 한 줄이 잘못돼도 그 줄만 무시되고 기본값이 쓰입니다. 앱은 죽지 않습니다.
timeLimitSeconds=180
targetCount=10
countdownSeconds=3
warningTimeSeconds=30
useFixedLayout=false

# 실제 부스 설치 모드: true면 가상 가구가 안 보이게 됨(현실 부스가 대신 보임). 수집품 가림은 유지됨
envOcclusionMode=false

# 방 구조물(바닥·벽·창문·천장등)을 보이게 할지. 글래스 FOV가 좁아 큰 평면의 잘린 경계가 드러나므로 기본 숨김.
# 가구(책상·모니터·캐비닛)는 이 설정과 무관하게 항상 보입니다.
showStructures=false

# 성능 점검용: true면 화면 상단에 FPS 표시 (운영 중엔 false)
showFps=false

# 렌더 품질(60FPS 튜닝용, 리셋 시 즉시 반영): bloom은 성능 위해 기본 OFF(글로우 이미션은 유지), renderScale 0.5~1.0(XREAL 글래스는 무시 — 10-15 실측), hdr는 빌드 고정(OFF)
bloom=false
renderScale=0.8
hdr=false

# 사운드(10-12부터 켬 — VIP 인증·보상 효과음). false면 전부 무음. 음원이 없는 자리는 원래 무음
audio=true

# 시간초과(10개 못 채움)여도 보상 안내 화면을 열지: 기본 false(전체 수집 시에만 보상). 발주처 확정 시 변경
rewardOnPartial=false

# 보상: 문구와 meshIndex만 수정. meshIndex는 앱 내부 메시 배열의 순번(0부터)
reward1.text=ICIS 생수
reward1.meshIndex=0
reward2.text=I LOVE KOREA 키링
reward2.meshIndex=1
reward3.text=신라약과
reward3.meshIndex=2

# ── 인형뽑기 미션형(Claw 씬) ── 제한 시간 안에 키링 큐브를 정해진 개수만큼 배출구에 넣으면 성공(10-28)
# 집게는 한 번에 한 개만 잡힘. 세라믹·유리 그릇은 방해물(넣어도 점수·진행 없음), 캡슐은 무득점, 폭탄은 집게가 닿는 순간 폭발 — 기계 안 물체가 흩어짐(감점 없음)
# 제한 시간(초) 10~300 / 성공 = 키링 큐브 이만큼 넣기 1~20(큐브 수보다 많으면 큐브 수로) / 남은 n초부터 적색 점멸
clawTimeLimitSeconds=60
clawTargetCount=3
clawWarningTimeSeconds=10
# 미션 성공 보상 화면에 보일 상품 이름(키링 큐브가 3D로 돌며 함께 보임). 비우면 기본값. En = 영어 화면일 때
clawRewardText=광화문 루빅큐브 키링
clawRewardTextEn=Gwanghwamun Rubik's Cube Keyring
# 화면 언어(11-10): ko(기본) | en. 실행 앱(Beam Pro)에서 한국어/English를 고르면 그것이 우선. 빌드 불필요
clawLanguage=ko
# 시점 기울기(11-10, ClawGame과 같은 시점): 기계가 드러날 때부터 화면을 이 각도(0~30°, 0 = 끔)만큼 아래로 숙임. 빌드 불필요
clawViewTiltDeg=13.2
# 기계 안 개수(10-28 기본 = 받침 더미 없이 바닥에 상품 20개: 큐브 10·세라믹 5·유리 5, 10-29 폭탄 3 → 0 — 사용자: 폭탄 제거, 값만 올리면 되살아남): 세라믹 그릇 0~20 · 유리 그릇 0~20 · 키링 큐브 0~20 · 캡슐 0~50 · 폭탄 0~8 (합계는 배치 상한 안으로 자동 조정)
# 상품(큐브·그릇)은 집게가 닿는 안전 구역(벽 쪽 끝·배출구 옆 제외)에 놓이고 다시 놓인다
clawCeramicCount=5
clawGlassCount=5
clawKeyringCount=10
clawFillerCount=0
clawBombCount=0
# 폭발 세기: 옆으로 퍼지는 속도 m/s(0.5~4) / 튀는 높이 비율(0.1~0.9, 바닥~캐리지 아래 대비)
clawBombForce=1.6
clawBombLift=0.65
# 조이스틱: false=패드 중심에서 떨어진 거리(기본), true=처음 댄 지점을 중심으로
clawStickRelative=false
# Beam Pro 회전 방향(left=왼쪽으로 90° 눕힘, 기본). 실기기에서 조이스틱 축이 반대로 느껴지면 right로
clawPadRotation=left
# 조작감: 캐리지 속도 m/s(0.1~1.0, 10-28 0.35 → 0.42), 집게 하강 속도 m/s(0.2~2.0), 아이템 속도 상한(1~8)
clawCarriageSpeed=0.42
clawHeadSpeed=0.6
clawMaxItemSpeed=3.5
# 아이템 때깔(비주얼): 테두리 빛 배율 0~3(0 = 끔) / 광택(환경 반사) 배율 0~2(0 = 끔). 글래스에서 보며 맞춤(빌드 불필요)
clawRim=1
clawReflection=1
# 성능: 부스 full(원본) | lite(가벼운 재질, 노멀맵·반사 없음) | off(부스 안 그림) / 기계 full | lite / 텍스처 이방성 필터 0~16(낮을수록 가벼움)
clawBooth=lite
clawMachine=lite
clawAniso=4
# 조준 표시 색(집게에서 내려오는 옅은 빛 원뿔·잡기 범위를 비추는 은은한 스포트라이트): white(기본) / gold / cyan / pink / lime / white-cyan 또는 #RRGGBB. 빌드 불필요
clawGuideColor=white
# 조준 스포트라이트 밝기 배율 0.5~3(1 = 10-22 은은함, 기본 2.2 — 클수록 잡히는 자리의 상품과 바닥 빛 웅덩이가 밝게. 흰색으로 날아가지 않게 셰이더가 상한을 둠). 빌드 불필요
clawGuideSpotIntensity=2.2
# 안경 화면 링크 끊김 감시(10-29): 이 앱 로그에 이 글자가 5초에 3줄 이상이면 Beam Pro에 [처음부터 다시 실행] 안내(자동 재시작 없음). 비우면 끔. 빌드 불필요
clawLinkLossSignature=get rsp error
# 조명(자연광): daylight(코드로 만든 낮 하늘, 기본) / studio(예전 스튜디오 조명) / lebombo(실내 자연 채광 사진) / warehouse(형광등+창 사진). 빌드 불필요
clawLighting=daylight
# 받침 더미: false(기본, 10-28) = 바닥에 상품만 / true = 바닥을 움직이지 않는 상품 더미로 채우고 그 위에 실물 한 층(10-20~10-26 — 잡거나 폭발 옆이면 진짜로 바뀜). 빌드 불필요
clawBed=false
# 받침 맨 윗층: true(기본) = 받침 더미 맨 위 한 층을 진짜 캡슐로(집게·폭발에 움직임) / false = 받침 전부 고정(예전 — 성능이 모자랄 때). 빌드 불필요
clawRealTopLayer=true
# 영상 뒤 디지털 디졸브 전환: 검정(현실) → 그리드 → 픽셀 분해 → 기계·부스 등장 → [START]. false면 예전처럼 안내 → 기계가 솟음
# 기계 다가오기(10-28): 디졸브가 끝나면 기계·부스가 clawApproachDistance m(0~8, 0 = 끔) 멀리서 clawApproachSeconds초(0.3~4) 동안
#   점점 가속하며(clawApproachPower 1~5 — 1 = 등속, 클수록 끝에 몰아서 빠름) 제자리로 다가온 뒤 [START]. 빌드 불필요
clawApproachDistance=3
clawApproachSeconds=1.5
clawApproachPower=3

# ── MR VIP 인증 연출 ── 체험자마다 맨 처음(게임 전): Beam Pro [시연 시작] → 홍채·시선 인증(정면 EYE SCAN 가운데 응시) → ACCESS GRANTED → 게임
# vipStartButton=false면 [시연 시작] 대기 없이 바로 홍채·시선(10-26 이전). 안경 센서가 벗었다고 알리면 [시연 시작]을 한 번 더 눌러야 시작
# vipCardStep=true면 그 앞에 예전 STEP 01 카드키 인증(가슴 앞 스캐너 패널 — [인증] 또는 Beam Pro 수평 유지, 2초) — 기본 false(10-26 제거, 되돌리기용)
# vipIntroEnabled=false면 연출 없이 바로 게임. 자세 감지(카드키 단계만): 수평에서 vipPoseTiltDeg(5~60)도 안을 vipPoseHoldSeconds(0.2~3)초 유지
# 시선: 정면 홍채 중심에서 vipGazeAngleDeg(2~15)도 안을 vipGazeSeconds(0.5~5)초 연속 응시(벗어나면 처음부터)
revealEnabled=true
vipIntroEnabled=true
vipCardStep=false
vipStartButton=true
vipPoseDetect=true
vipPoseTiltDeg=25
vipPoseHoldSeconds=0.8
vipGazeSeconds=2
vipGazeAngleDeg=6

# ── 안경 착용 감지·자동 처음으로(10-26) ── 안경 근접 센서가 벗음을 알리면 홍채 인증을 멈추고, Beam Pro 화면에 카운트다운 →
#   clawWearResetSeconds(0~600, 0 = 안 감)초 안에 다시 안 쓰면 처음 화면(홍채 인증)으로. 결과 화면에서 [보상 확인] 전이면 처음으로 가지 않음.
#   앱이 백그라운드에 clawResumeResetSeconds(0~600, 0 = 끔)초 이상 있다 돌아오면(다시 실행) 처음으로. clawWearDetect=false면 착용 무시
clawWearDetect=true
clawWearResetSeconds=30
clawResumeResetSeconds=3

# ── 경품 연동(오스리움) ── 게임 시작 시 참가자 DID 조회 → [보상 확인] 시 경품(NFT) 지급 요청. 결과 팝업은 진행자 화면에 뜸
# 진행자는 기기를 건네기 전에 반드시 진행자 화면에서 [로그아웃] → 다음 참가자 [이벤트 로그인](안 하면 앞 참가자 DID로 요청됨)
# prizeToken은 API 정의서의 토큰을 붙여 넣음. 경품 = 키링 큐브 상품(10-28부터 그릇은 점수가 없어 경품도 키링만) —
# nftId는 마켓 상품 카드 맨 위 번호. prizeNftIdKeyring이 0이면 요청 안 함. prizeNftIdCeramic·Glass는 읽기만(10-28부터 안 씀 — 되돌리기용)
# 10-28: 셋 다 25(오스리움 호출 테스트용 번호 — 이전 20 = 광화문 루빅큐브 키링)
prizeEnabled=false
prizeServer=https://dataspace.authrium.com
prizeToken=
prizeNftIdCeramic=25
prizeNftIdGlass=25
prizeNftIdKeyring=25
prizeTimeoutSeconds=8
";

        /// <summary>10-15에 지운 모드1·2(2게임 체제) 키 — 기기 config에 남아 있어도 조용히 무시(gate*는 접두어로)</summary>
        static readonly System.Collections.Generic.HashSet<string> RetiredKeys = new()
        {
            "clawChances", "clawTargetMin", "clawTargetMax", "clawBallCount", "clawGrabMax", "clawGrabLayers", "clawAimSeconds",
            "clawShakeSeconds", "clawShakeWeakG", "clawShakeMediumG", "clawShakeStrongG", "clawDropWeak", "clawDropMediumMin",
            "clawDropMediumMax", "clawDropStrongMin", "clawDropStrongMax", "clawShakeDebug", "clawModeSelect", "clawDefaultMode",
            "prizeNftIdGate", "prizeNftIdCount",
            "clawGuideStyle", // 10-19 조준 표시 시안 고르기용(시안 3으로 확정 후 은퇴)
        };

        static readonly Regex RewardKey = new(@"^reward(\d+)\.(text|meshIndex)$", RegexOptions.Compiled);

        public static GameConfig Load()
        {
            var cfg = new GameConfig();
            string path = ConfigPath;

            string[] lines;
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, DefaultConfigText, new UTF8Encoding(false));
                    Debug.Log($"[ConfigLoader] config.txt 없음 → 기본값으로 생성: {path}");
                    return Finish(cfg);
                }
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception e)
            {
                cfg.warnings.Add($"파일 읽기 실패 → 전체 기본값 사용 ({e.GetType().Name})");
                return Finish(cfg);
            }

            var rewardMap = new SortedDictionary<int, RewardEntry>();
            int retiredKeys = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                // 10-29 감사: 한 줄에서 예외가 나도 Load 전체(부팅)가 멈추지 않게 줄 단위로 잡고 경고만
                try
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        cfg.warnings.Add($"{i + 1}행(길이 {line.Length}) 형식 오류 무시"); // 10-29 감사: 줄 원문은 찍지 않음(토큰 조각 노출 방지)
                        continue;
                    }

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    var rm = RewardKey.Match(key);
                    if (rm.Success)
                    {
                        if (!int.TryParse(rm.Groups[1].Value, out int idx)) // 10-29 감사: 자릿수 초과 예외로 Load 전체가 실패하던 것
                        {
                            cfg.warnings.Add($"{i + 1}행(길이 {line.Length}) 보상 번호 범위 초과 무시");
                            continue;
                        }
                        if (!rewardMap.TryGetValue(idx, out var entry))
                            rewardMap[idx] = entry = new RewardEntry();
                        if (rm.Groups[2].Value == "text")
                            entry.text = value;
                        else if (int.TryParse(value, out int mi))
                            entry.meshIndex = mi; // 범위 검증은 MeshLibrary를 아는 RewardTable에서
                        else
                            cfg.warnings.Add($"{i + 1}행 {key} 숫자 아님 → 0 사용");
                        continue;
                    }

                    switch (key)
                    {
                        case "timeLimitSeconds":
                            cfg.timeLimitSeconds = ParseClamp(cfg, key, value, cfg.timeLimitSeconds, 30, 300, i);
                            break;
                        case "targetCount":
                            // 상한은 씬을 아는 GameManager.ApplyConfig에서 스폰 포인트·풀 크기로 clamp
                            cfg.targetCount = ParseClamp(cfg, key, value, cfg.targetCount, 1, 99, i);
                            break;
                        case "countdownSeconds":
                            cfg.countdownSeconds = ParseClamp(cfg, key, value, cfg.countdownSeconds, 1, 10, i);
                            break;
                        case "warningTimeSeconds":
                            cfg.warningTimeSeconds = ParseClamp(cfg, key, value, cfg.warningTimeSeconds, 0, 300, i);
                            break;
                        case "useFixedLayout":
                            if (bool.TryParse(value, out bool b)) cfg.useFixedLayout = b;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.useFixedLayout} 유지");
                            break;
                        case "envOcclusionMode":
                            if (bool.TryParse(value, out bool occl)) cfg.envOcclusionMode = occl;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.envOcclusionMode} 유지");
                            break;
                        case "showFps":
                            if (bool.TryParse(value, out bool fps)) cfg.showFps = fps;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.showFps} 유지");
                            break;
                        case "showStructures":
                            if (bool.TryParse(value, out bool ss)) cfg.showStructures = ss;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.showStructures} 유지");
                            break;
                        case "bloom":
                            if (bool.TryParse(value, out bool bl)) cfg.bloom = bl;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.bloom} 유지");
                            break;
                        case "hdr":
                            if (bool.TryParse(value, out bool hd)) cfg.hdr = hd;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.hdr} 유지");
                            break;
                        case "audio":
                            if (bool.TryParse(value, out bool au)) cfg.audio = au;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.audio} 유지");
                            break;
                        case "rewardOnPartial":
                            if (bool.TryParse(value, out bool rp)) cfg.rewardOnPartial = rp;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.rewardOnPartial} 유지");
                            break;
                        case "renderScale":
                            cfg.renderScale = ParseClampFloat(cfg, key, value, cfg.renderScale, 0.5f, 1.0f, i);
                            break;

                        // ── 인형뽑기 미니게임(Claw 씬, 09-03) ──
                        // ── 인형뽑기 미션형(10-15 복원 — 09-03~09-15 키 이름 그대로) ──
                        case "clawTimeLimitSeconds":
                            cfg.clawTimeLimitSeconds = ParseClamp(cfg, key, value, cfg.clawTimeLimitSeconds, 10, 300, i);
                            break;
                        case "clawTargetCount":
                            cfg.clawTargetCount = ParseClamp(cfg, key, value, cfg.clawTargetCount, 1, 20, i);
                            break;
                        case "clawCeramicCount":
                            cfg.clawCeramicCount = ParseClamp(cfg, key, value, cfg.clawCeramicCount, 0, 20, i);
                            break;
                        case "clawGlassCount":
                            cfg.clawGlassCount = ParseClamp(cfg, key, value, cfg.clawGlassCount, 0, 20, i);
                            break;
                        case "clawKeyringCount":
                            cfg.clawKeyringCount = ParseClamp(cfg, key, value, cfg.clawKeyringCount, 0, 20, i);
                            break;
                        case "clawFillerCount":
                            cfg.clawFillerCount = ParseClamp(cfg, key, value, cfg.clawFillerCount, 0, 50, i);
                            break;
                        case "clawBombCount":
                            cfg.clawBombCount = ParseClamp(cfg, key, value, cfg.clawBombCount, 0, 8, i);
                            break;
                        case "clawWarningTimeSeconds":
                            cfg.clawWarningTimeSeconds = ParseClamp(cfg, key, value, cfg.clawWarningTimeSeconds, 0, 300, i);
                            break;
                        case "clawRewardText": // 10-28 보상 화면 상품 이름 — 빈 값이면 기본 유지
                            if (!string.IsNullOrWhiteSpace(value)) cfg.clawRewardText = value.Trim();
                            break;
                        case "clawRewardTextEn": // 11-10 영어 화면 보상 상품 이름 — 빈 값이면 기본 유지
                            if (!string.IsNullOrWhiteSpace(value)) cfg.clawRewardTextEn = value.Trim();
                            break;
                        case "clawLanguage": // 11-10 화면 언어 ko | en (실행 앱 인텐트가 있으면 그쪽 우선 — Loc)
                            {
                                string lang = Loc.Normalize(value);
                                if (lang != null) { cfg.clawLanguage = lang; cfg.clawLanguageSet = true; }
                                else cfg.warnings.Add($"{i + 1}행 {key} ko/en 아님 → {cfg.clawLanguage} 유지");
                            }
                            break;
                        case "clawViewTiltDeg": cfg.clawViewTiltDeg = ParseClampFloat(cfg, key, value, cfg.clawViewTiltDeg, 0f, 30f, i); break; // 11-10 시점 기울기
                        case "clawBombForce":
                            cfg.clawBombForce = ParseClampFloat(cfg, key, value, cfg.clawBombForce, 0.5f, 4f, i);
                            break;
                        case "clawBombLift":
                            cfg.clawBombLift = ParseClampFloat(cfg, key, value, cfg.clawBombLift, 0.1f, 0.9f, i);
                            break;
                        case "clawStickRelative":
                            if (bool.TryParse(value, out bool csr)) cfg.clawStickRelative = csr;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.clawStickRelative} 유지");
                            break;
                        case "clawPadRotation":
                            {
                                string v = value.ToLowerInvariant();
                                if (v == "left" || v == "right" || v == "none") cfg.clawPadRotation = v;
                                else cfg.warnings.Add($"{i + 1}행 {key} left/right/none 아님 → {cfg.clawPadRotation} 유지");
                            }
                            break;
                        case "clawCarriageSpeed":
                            cfg.clawCarriageSpeed = ParseClampFloat(cfg, key, value, cfg.clawCarriageSpeed, 0.1f, 1.0f, i);
                            break;
                        case "clawHeadSpeed":
                            cfg.clawHeadSpeed = ParseClampFloat(cfg, key, value, cfg.clawHeadSpeed, 0.2f, 2.0f, i);
                            break;
                        case "clawMaxItemSpeed":
                            cfg.clawMaxItemSpeed = ParseClampFloat(cfg, key, value, cfg.clawMaxItemSpeed, 1f, 8f, i);
                            break;
                        case "clawRim": cfg.clawRim = ParseClampFloat(cfg, key, value, cfg.clawRim, 0f, 3f, i); break;
                        case "clawReflection": cfg.clawReflection = ParseClampFloat(cfg, key, value, cfg.clawReflection, 0f, 2f, i); break;
                        case "perfHide": cfg.perfHide = value.Trim(); break; // 성능 진단 전용(기본 config에는 없음)
                        case "clawBooth":
                            cfg.clawBooth = ClawRenderTier.NormalizeBooth(value);
                            if (cfg.clawBooth != value.Trim().ToLowerInvariant()) cfg.warnings.Add($"{i + 1}행 clawBooth '{value}' → full|lite|off 아님, {cfg.clawBooth} 적용");
                            break;
                        case "clawMachine":
                            cfg.clawMachine = ClawRenderTier.NormalizeMachine(value);
                            if (cfg.clawMachine != value.Trim().ToLowerInvariant()) cfg.warnings.Add($"{i + 1}행 clawMachine '{value}' → full|lite 아님, {cfg.clawMachine} 적용");
                            break;
                        case "clawAniso": cfg.clawAniso = ParseClamp(cfg, key, value, cfg.clawAniso, 0, 16, i); break;
                        case "clawLighting":
                            cfg.clawLighting = ClawLighting.Normalize(value);
                            if (cfg.clawLighting != value.Trim().ToLowerInvariant()) cfg.warnings.Add($"{i + 1}행 clawLighting '{value}' → studio·daylight·lebombo·warehouse 아님, {cfg.clawLighting} 적용");
                            break;
                        case "clawBed":
                            if (bool.TryParse(value, out bool cb)) cfg.clawBed = cb;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.clawBed} 유지");
                            break;
                        case "clawRealTopLayer":
                            if (bool.TryParse(value, out bool crt)) cfg.clawRealTopLayer = crt;
                            else cfg.warnings.Add($"{i + 1}행 {key} true/false 아님 → {cfg.clawRealTopLayer} 유지");
                            break;
                        case "clawGuideColor":
                            if (ClawGuide.TryParseColor(value, out _)) cfg.clawGuideColor = value.Trim();
                            else cfg.warnings.Add($"{i + 1}행 clawGuideColor '{value}' → 색 이름(white·cyan·gold·pink·lime·white-cyan)·#RRGGBB 아님, {cfg.clawGuideColor} 유지");
                            break;
                        case "clawGuideSpotIntensity": cfg.clawGuideSpotIntensity = ParseClampFloat(cfg, key, value, cfg.clawGuideSpotIntensity, 0.5f, 3f, i); break; // 10-29
                        case "clawLinkLossSignature": cfg.clawLinkLossSignature = value.Trim(); break; // 10-29 비우면 감시 끔
                        case "xrViewportScale": cfg.xrViewportScale = ParseClampFloat(cfg, key, value, cfg.xrViewportScale, 0.5f, 1f, i); break; // 성능 조사 전용(기본 config에는 없음)
                        // ── 인형뽑기 시작 전 영상(8차, 09-14) ──
                        case "clawVideoEnabled":
                        case "clawVideoFile":
                        case "clawVideoSkipSeconds":
                            break; // 10-26 영상 삭제. 기존 기기 config의 영상 키는 무시.
                        case "revealEnabled": cfg.revealEnabled = ParseBool(cfg, key, value, cfg.revealEnabled, i); break;
                        // 10-28 기계 다가오기
                        case "clawApproachDistance": cfg.clawApproachDistance = ParseClampFloat(cfg, key, value, cfg.clawApproachDistance, 0f, 8f, i); break;
                        case "clawApproachSeconds": cfg.clawApproachSeconds = ParseClampFloat(cfg, key, value, cfg.clawApproachSeconds, 0.3f, 4f, i); break;
                        case "clawApproachPower": cfg.clawApproachPower = ParseClampFloat(cfg, key, value, cfg.clawApproachPower, 1f, 5f, i); break;

                        case "vipIntroEnabled": cfg.vipIntroEnabled = ParseBool(cfg, key, value, cfg.vipIntroEnabled, i); break;
                        case "vipCardStep": cfg.vipCardStep = ParseBool(cfg, key, value, cfg.vipCardStep, i); break;
                        case "vipStartButton": cfg.vipStartButton = ParseBool(cfg, key, value, cfg.vipStartButton, i); break;
                        case "vipPoseDetect": cfg.vipPoseDetect = ParseBool(cfg, key, value, cfg.vipPoseDetect, i); break;
                        case "vipPoseTiltDeg": cfg.vipPoseTiltDeg = ParseClampFloat(cfg, key, value, cfg.vipPoseTiltDeg, 5f, 60f, i); break;
                        case "vipPoseHoldSeconds": cfg.vipPoseHoldSeconds = ParseClampFloat(cfg, key, value, cfg.vipPoseHoldSeconds, 0.2f, 3f, i); break;
                        case "vipGazeSeconds": cfg.vipGazeSeconds = ParseClampFloat(cfg, key, value, cfg.vipGazeSeconds, 0.5f, 5f, i); break;
                        case "vipGazeAngleDeg": cfg.vipGazeAngleDeg = ParseClampFloat(cfg, key, value, cfg.vipGazeAngleDeg, 2f, 15f, i); break;
                        case "clawWearDetect": cfg.clawWearDetect = ParseBool(cfg, key, value, cfg.clawWearDetect, i); break;
                        case "clawWearResetSeconds": cfg.clawWearResetSeconds = ParseClampFloat(cfg, key, value, cfg.clawWearResetSeconds, 0f, 600f, i); break;
                        case "clawResumeResetSeconds": cfg.clawResumeResetSeconds = ParseClampFloat(cfg, key, value, cfg.clawResumeResetSeconds, 0f, 600f, i); break;
                        // ── 경품 연동(10-06) ──
                        case "prizeEnabled": cfg.prizeEnabled = ParseBool(cfg, key, value, cfg.prizeEnabled, i); break;
                        case "prizeServer":
                            if (value.StartsWith("https://") && value.Length > 8) cfg.prizeServer = value.TrimEnd('/');
                            else cfg.warnings.Add($"{i + 1}행 {key}는 https://로 시작해야 함 → {cfg.prizeServer} 유지");
                            break;
                        case "prizeToken": cfg.prizeToken = value; break;
                        case "prizeNftIdCeramic": cfg.prizeNftIdCeramic = ParseClamp(cfg, key, value, cfg.prizeNftIdCeramic, 0, int.MaxValue, i); break;
                        case "prizeNftIdGlass": cfg.prizeNftIdGlass = ParseClamp(cfg, key, value, cfg.prizeNftIdGlass, 0, int.MaxValue, i); break;
                        case "prizeNftIdKeyring": cfg.prizeNftIdKeyring = ParseClamp(cfg, key, value, cfg.prizeNftIdKeyring, 0, int.MaxValue, i); break;
                        case "prizeTimeoutSeconds": cfg.prizeTimeoutSeconds = ParseClamp(cfg, key, value, cfg.prizeTimeoutSeconds, 3, 30, i); break;
                        default:
                            // 10-15: 지운 모드1·2 키는 기기 config에 남아 있어도 조용히 무시(모르는 키마다 경고가 뜨지 않게)
                            if (RetiredKeys.Contains(key) || key.StartsWith("gate")) retiredKeys++;
                            else cfg.warnings.Add($"{i + 1}행(길이 {line.Length}) 알 수 없는 키 무시"); // 10-29 감사: 키 이름은 찍지 않음(토큰 조각 노출 방지)
                            break;
                    }
                }
                catch (Exception e)
                {
                    cfg.warnings.Add($"{i + 1}행 처리 실패(예외 {e.GetType().Name})");
                }
            }

            if (cfg.warningTimeSeconds > cfg.timeLimitSeconds)
            {
                cfg.warnings.Add($"warningTimeSeconds({cfg.warningTimeSeconds}) > 제한시간 → {cfg.timeLimitSeconds}로 조정");
                cfg.warningTimeSeconds = cfg.timeLimitSeconds;
            }
            ValidateClaw(cfg);
            ValidatePrize(cfg);
            if (retiredKeys > 0) Debug.Log($"[ConfigLoader] 지운 모드1·2 키 {retiredKeys}개 무시(10-15 미션형 복원)");

            // 보상 줄이 하나라도 유효하면 기본 3종을 통째로 대체
            if (rewardMap.Count > 0)
            {
                cfg.rewards.Clear();
                foreach (var kv in rewardMap) cfg.rewards.Add(kv.Value);
            }

            return Finish(cfg);
        }

        /// <summary>인형뽑기 미션형 교차 검사(10-15) — 고쳐서 적용하고 경고만 남긴다(개수·풀 조정은 ClawGameManager.ApplyGameConfig)</summary>
        static void ValidateClaw(GameConfig cfg)
        {
            if (cfg.clawWarningTimeSeconds > cfg.clawTimeLimitSeconds)
            {
                cfg.warnings.Add($"clawWarningTimeSeconds({cfg.clawWarningTimeSeconds}) > 제한 시간 → {cfg.clawTimeLimitSeconds}로 조정");
                cfg.clawWarningTimeSeconds = cfg.clawTimeLimitSeconds;
            }
            // 10-28 점수 = 키링 큐브만 — 목표는 큐브 수까지
            if (cfg.clawTargetCount > cfg.clawKeyringCount)
                cfg.warnings.Add($"clawTargetCount {cfg.clawTargetCount} > 키링 큐브 수 {cfg.clawKeyringCount} — 큐브 수로 맞춰 적용");
        }

        /// <summary>경품 연동(10-06) — 켰는데 토큰·경품 번호가 비어 있으면 현장에서 바로 보이게 경고</summary>
        static void ValidatePrize(GameConfig cfg)
        {
            if (!cfg.prizeEnabled) return;
            if (string.IsNullOrWhiteSpace(cfg.prizeToken))
                cfg.warnings.Add("prizeEnabled=true인데 prizeToken 비어 있음 — 경품 요청 안 됨(실물 안내만)");
            // 10-28 경품 = 키링 큐브만 — 세라믹·유리 번호는 파싱만(검사 안 함)
            if (cfg.prizeNftIdKeyring <= 0)
                cfg.warnings.Add("prizeEnabled=true인데 prizeNftIdKeyring 0 — 키링 경품 요청 안 됨");
            // 10-29 감사 I6: 주소·토큰 형식(공백·제어 문자) — 값은 찍지 않는다. PrizeLink.Configure가 같은 검사로 비활성
            string problem = PrizeLink.ConfigProblem(cfg);
            if (problem != null) cfg.warnings.Add(problem + " — 경품 요청 안 됨(실물 안내만)");
        }

        /// <summary>로그·운영자 패널용 — 토큰은 끝 4자만</summary>
        public static string MaskToken(string token)
            => string.IsNullOrEmpty(token) ? "(없음)" : token.Length <= 8 ? "****" : "…" + token.Substring(token.Length - 4);

        static bool ParseBool(GameConfig cfg, string key, string value, bool fallback, int lineIdx)
        {
            if (bool.TryParse(value, out bool b)) return b;
            cfg.warnings.Add($"{lineIdx + 1}행 {key} true/false 아님 → {fallback} 유지");
            return fallback;
        }

        /// <summary>파일 이름만(기기 폴더·StreamingAssets 기준) — 경로 문자는 거부</summary>
        static string ParseFileName(GameConfig cfg, string key, string value, string fallback, int lineIdx)
        {
            if (value.Length > 0 && value.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && !value.Contains("..")) return value;
            cfg.warnings.Add($"{lineIdx + 1}행 {key}는 파일 이름만(경로 불가) → {fallback} 유지");
            return fallback;
        }

        static int ParseClamp(GameConfig cfg, string key, string value, int fallback, int min, int max, int lineIdx)
        {
            if (!int.TryParse(value, out int v))
            {
                cfg.warnings.Add($"{lineIdx + 1}행 {key} 숫자 아님 → 기본 {fallback}");
                return fallback;
            }
            int clamped = Mathf.Clamp(v, min, max);
            if (clamped != v)
                cfg.warnings.Add($"{key}={v} 범위({min}~{max}) 초과 → {clamped} 적용");
            return clamped;
        }

        static float ParseClampFloat(GameConfig cfg, string key, string value, float fallback, float min, float max, int lineIdx)
        {
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float v))
            {
                cfg.warnings.Add($"{lineIdx + 1}행 {key} 숫자 아님 → {fallback:0.00} 유지");
                return fallback;
            }
            float clamped = Mathf.Clamp(v, min, max);
            if (clamped != v)
                cfg.warnings.Add($"{key}={v} 범위({min:0.0#}~{max:0.0#}) 초과 → {clamped:0.00} 적용");
            return clamped;
        }

        // 최종 적용값을 한 번에 로그 — 현장에서 "먹었는지" 확인용 (§4-2 결과 노출)
        static GameConfig Finish(GameConfig cfg)
        {
            var sb = new StringBuilder("[ConfigLoader] 적용된 설정:\n");
            sb.AppendLine($"  timeLimitSeconds={cfg.timeLimitSeconds}, targetCount={cfg.targetCount}, countdownSeconds={cfg.countdownSeconds}");
            sb.AppendLine($"  warningTimeSeconds={cfg.warningTimeSeconds}, useFixedLayout={cfg.useFixedLayout}, envOcclusionMode={cfg.envOcclusionMode}");
            sb.AppendLine($"  bloom={cfg.bloom}, renderScale={cfg.renderScale:0.00}, hdr={cfg.hdr}, audio={cfg.audio}, showFps={cfg.showFps}, rewardOnPartial={cfg.rewardOnPartial}, showStructures={cfg.showStructures}");
            for (int i = 0; i < cfg.rewards.Count; i++)
                sb.AppendLine($"  보상{i + 1}: \"{cfg.rewards[i].text}\" (meshIndex={cfg.rewards[i].meshIndex})");
            // 10-29 감사: linkSig는 원문 대신 길이만 — 서명 문자열이 자기 로그에 찍혀 링크 감시(XrRecoveryWatch)가 오탐하지 않게
            sb.AppendLine($"  [claw] 미션 time={cfg.clawTimeLimitSeconds}s target={cfg.clawTargetCount} ceramic={cfg.clawCeramicCount} glass={cfg.clawGlassCount} keyring={cfg.clawKeyringCount} filler={cfg.clawFillerCount} bomb={cfg.clawBombCount} " +
                          $"warn={cfg.clawWarningTimeSeconds} bombForce={cfg.clawBombForce:0.00} bombLift={cfg.clawBombLift:0.00} stickRelative={cfg.clawStickRelative} padRotation={cfg.clawPadRotation} " +
                          $"carriage={cfg.clawCarriageSpeed:0.00} head={cfg.clawHeadSpeed:0.00} maxSpeed={cfg.clawMaxItemSpeed:0.00} rim={cfg.clawRim:0.00} refl={cfg.clawReflection:0.00} " +
                          $"booth={cfg.clawBooth} machine={cfg.clawMachine} aniso={cfg.clawAniso} guideColor={cfg.clawGuideColor} guideSpot={cfg.clawGuideSpotIntensity:0.0#} linkSig={(string.IsNullOrEmpty(cfg.clawLinkLossSignature) ? "꺼짐" : "설정됨(" + cfg.clawLinkLossSignature.Length + "자)")} lighting={cfg.clawLighting} bed={cfg.clawBed} realTop={cfg.clawRealTopLayer} xrViewport={cfg.xrViewportScale:0.00} perfHide={cfg.perfHide} rewardText={cfg.clawRewardText} " +
                          $"approach={cfg.clawApproachDistance:0.0}m/{cfg.clawApproachSeconds:0.0}s/p{cfg.clawApproachPower:0.#} " +
                          $"lang={cfg.clawLanguage}{(cfg.clawLanguageSet ? "" : "(기본)")} rewardTextEn={cfg.clawRewardTextEn} viewTilt={cfg.clawViewTiltDeg:0.0#}°");
            sb.AppendLine($"  [prize] enabled={cfg.prizeEnabled} server={cfg.prizeServer} token={MaskToken(cfg.prizeToken)} " +
                          $"nftId ceramic={cfg.prizeNftIdCeramic} glass={cfg.prizeNftIdGlass} keyring={cfg.prizeNftIdKeyring} timeout={cfg.prizeTimeoutSeconds}s");
            if (cfg.warnings.Count > 0)
            {
                sb.AppendLine($"  경고 {cfg.warnings.Count}건:");
                foreach (var w in cfg.warnings) sb.AppendLine($"    - {w}");
                Debug.LogWarning(sb.ToString());
            }
            else
            {
                Debug.Log(sb.ToString());
            }
            Loc.ApplyConfig(cfg); // 11-10 화면 언어(인텐트가 없을 때만 config가 정함)
            return cfg;
        }
    }
}
