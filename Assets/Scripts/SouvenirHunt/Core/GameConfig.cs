using System.Collections.Generic;

namespace SouvenirHunt
{
    /// <summary>
    /// 운영 설정 값 묶음 (Phase 0). 기본값 = 인수인계 §4-2 샘플과 동일.
    /// ConfigLoader가 config.txt를 파싱해 채우고, GameManager.ApplyConfig가 소비한다.
    /// </summary>
    public class RewardEntry
    {
        public string text = "";
        public int meshIndex;
    }

    public class GameConfig
    {
        public int timeLimitSeconds = 180;
        public int targetCount = 10;
        public int countdownSeconds = 3;
        public int warningTimeSeconds = 30;
        public bool useFixedLayout = false;
        public bool envOcclusionMode = false; // 실제 부스 설치 모드: 환경 메시를 안 보이는 오클루전 마스크로
        public bool showFps = false;          // Phase 4 프로파일링: HUD 상단 FPS 오버레이 (운영 시 false)
        // 08-31: OST 글래스는 FOV가 좁아 벽·바닥 같은 큰 평면의 잘린 경계가 그대로 드러난다 →
        // 구조물(바닥·벽·창문·등기구)은 기본 숨김. 가구는 계속 보인다(은폐 기능 담당). StructureVisibility가 소비.
        public bool showStructures = false;

        // Phase 5-0 렌더 품질 A/B — 빌드 없이 60FPS 조합 탐색 (RenderQualityApplier가 부팅·리셋마다 적용)
        public bool bloom = false;            // 08-26 A/B: Bloom이 매초 33ms 히치 유발 → 기본 OFF(성능 우선 §4-7). 글로우 이미션은 유지
        public float renderScale = 0.8f;      // 0.5~1.0 — 10-15 실측: XREAL은 눈 텍스처를 1968×1134로 고정(이 값 무시)
        public bool hdr = false;              // 빌드 고정값(URP 에셋) 표시용. 10-15 실측: HDR OFF가 더 빠르고 화면 동일(bloom off) → 에셋 OFF

        public bool audio = true;             // 10-12부터 켬(VIP 인증·보상 효과음, 사용자 결정). 클립 슬롯이 비어 있으면 그 자리는 무음

        // 시간초과(부분 수집) 시에도 [보상 확인]→보상 안내를 열어줄지 — 발주처 미확정(인수인계 §7), 기본 미지급
        public bool rewardOnPartial = false;

        // ── 인형뽑기 미션형 (Claw 씬) — ClawGameManager.ApplyGameConfig가 소비 ──
        // 10-15: 모드1·2(2게임 체제)를 지우고 09-03~09-15 미션형으로 복원(git 8d3d1ed 규칙). 키 이름도 그때 것을 다시 쓴다.
        // 기계 안 = 세라믹 그릇·유리 그릇(목표물) + 캡슐(무득점) + 폭탄(집게가 닿으면 폭발). 제한 시간 안에 그릇 clawTargetCount개 → 성공
        // 10-28(사용자 "더미 대신 상품 20개(큐브 10·세라믹 5·유리 5), 한 번에 한 개씩, 55초에 큐브 5개"): 점수 = 키링 큐브만, 그릇은 방해물(넣어도 점수·진행 없음)
        public int clawTimeLimitSeconds = 60;      // 10~300 제한 시간 — 10-22 45 → 90(같은 종류 3개), 10-26 90 → 120(같은 종류 10개), 10-28 120 → 55(큐브 5개), 11-10 55 → 60(사용자 "플레이 시간 60초")
        // 10-26(사용자 "모든 상품을 큐브/그릇1/그릇2로 1:1:1, 성공은 10개 모으기", 결정: 같은 종류 10개·캡슐 더미까지·폭탄 없음): 아래 받침 더미도 상품 3종(SouvenirHuntClawBed)
        public int clawTargetCount = 3;            // 1~20 성공 = 키링 큐브 이만큼(10-26 같은 종류 10개 → 10-28 큐브 5개 → 10-29 버전 B 큐브 3개) — 큐브 수보다 많으면 그 수로
        public int clawCeramicCount = 5;           // 0~20 세라믹 그릇 개수 — 10-28 방해물(넣어도 점수 없음). 운영자 스테퍼 '그릇(종류별)'은 세라믹·유리를 같은 수로 기록
        public int clawGlassCount = 5;             // 0~20 유리 그릇 개수 — 10-28 방해물
        public int clawKeyringCount = 10;          // 0~20 키링 큐브(10-22, KoreaCubeKeyring) 개수 — 10-28 유일한 득점 상품(0이면 1개로)
        public int clawFillerCount = 0;            // 0~50 캡슐(무득점) 개수 — 10-26부터 0(상품만)
        public int clawBombCount = 0;              // 0~8 폭탄 개수 — 10-26부터 0(사용자 "폭탄 없애기") → 10-29 버전 B 3개(사용자 "폭탄 3개 복귀", 상품 20 + 폭탄 3) → 10-29 3 → 0(사용자: 폭탄 제거; 코드·폭발은 그대로라 config로 되살릴 수 있음). 합계는 배치 상한(받침 더미면 그 위 한 층 셀 수, 아니면 풀) 안으로 자동 조정
        public int clawWarningTimeSeconds = 10;    // 남은 n초부터 적색 점멸(제한 시간 이하로)
        public string clawRewardText = "광화문 루빅큐브 키링"; // 10-28 미션 성공 보상 화면의 상품 이름(키링 큐브 3D 회전과 함께) — 빈 값이면 이 기본값
        public string clawRewardTextEn = "Gwanghwamun Rubik's Cube Keyring"; // 11-10 영어 화면(Loc.En)일 때 보상 상품 이름 — 빈 값이면 이 기본값
        // 11-10 화면 언어 ko | en(기본 ko). 실행 앱에서 고른 언어(인텐트 extra "lang")가 있으면 그것이 우선 — Loc
        public string clawLanguage = "ko";
        public bool clawLanguageSet;               // config에 clawLanguage 줄이 있었는가(Loc 로그 source=config|default 구분)
        // 11-10 시점 기울기(ClawGame과 같은 시점): 기계가 드러나기 직전(검정)부터 XR 리그를 눈을 축으로 이만큼 아래로 숙임 — 0~30°, 0 = 끔(BoothAligner)
        public float clawViewTiltDeg = 13.2f;
        public float clawBombForce = 1.6f;         // 0.5~4 폭발 옆 퍼짐 속도 m/s(중심 가까운 아이템 기준)
        public float clawBombLift = 0.65f;         // 0.1~0.9 폭발 튀는 높이 비율(바닥~캐리지 아래 대비)
        public bool clawStickRelative = false;     // false=패드 중심 기준 절대 / true=첫 터치점 기준 상대
        public string clawPadRotation = "left";    // left(왼쪽 90° 회전, 기본) | right | none — 실기기에서 축이 반대면 빌드 없이 교정
        public float clawCarriageSpeed = 0.42f;    // m/s, 0.1~1.0 — 10-28 0.35 → 0.42(사용자 "집게 조이스틱 속도 1.2배")
        public float clawHeadSpeed = 0.6f;         // 하강 m/s, 0.2~2.0 (상승·배출구 이동은 이 값의 1.33·1.17배)
        public float clawMaxItemSpeed = 3.5f;      // 아이템 속도 상한 m/s, 1~8 (폭발 직후 0.5초는 4.5)
        // 비주얼 1차(10-15): 아이템 셰이더(SouvenirHunt/ClawItemLit) 전역 세기 — 빌드 없이 실기기에서 맞춤
        public float clawRim = 1f;                 // 0~3 테두리 빛(림) 배율 (0 = 끔)
        public float clawReflection = 1f;          // 0~2 환경 반사(광택) 배율 (0 = 끔)
        // 성능(10-15): 부스·기계 렌더 등급 — full(원본 PBR) | lite(가벼운 셰이더) | off(부스만: 안 그림). 텍스처 이방성 필터 0~16
        public string clawBooth = "lite";         // 10-15 실측: 원본 19.7 → 부스·기계 lite 30 FPS(HDR OFF 33), 외형 거의 동일
        public string clawMachine = "lite";
        public int clawAniso = 4;
        // 10-19 조준 표시(대상 밑 스포트라이트 + 집게→대상 빛 방울 선)·대상 발광 공통 색 — 사용자 선택 gold. cyan·pink·lime·white-cyan 또는 #RRGGBB
        public string clawGuideColor = "white";   // 10-22 조준 = 은은한 스포트라이트 + 옅은 원뿔(사용자 선택 S2 흰색)
        public float clawGuideSpotIntensity = 2.2f; // 10-29 조준 스포트라이트 밝기 배율 0.5~3(1 = 10-22 은은함, 사용자 "더 밝게" 1.6 → "차이가 안 보인다" 2.2) — 상품 표면 + 바닥 빛 웅덩이
        // 10-29 안경 화면 링크 끊김 서명(자기 프로세스 logcat에서 5초에 3줄 이상이면 Beam Pro에 "처음부터 다시 실행" 안내 — 자동 재시작 없음). 비우면 감시 끔
        public string clawLinkLossSignature = XrLinkLoss.DefaultSignature;
        // 10-20 자연광 1단계: 조명 묶음 studio(지금) | daylight(코드 생성 낮 하늘) | lebombo(실내 자연 채광 HDRI) | warehouse(형광등+창 HDRI)
        public string clawLighting = "daylight";   // 10-20 사용자 선택(글래스 실화면 비교: daylight 밝고 선명, warehouse는 역광으로 어두움)
        // 10-20 받침 더미(아래층 고정 캡슐 — 실물은 그 위 한 층). false면 바닥에 실물만(예전 배치, 성능 대비용)
        // 10-28 기본 false(사용자 "더미 대신 상품 20개") — 더미 코드·메뉴는 남김(true로 되돌리기)
        public bool clawBed = false;
        // 10-22 받침 맨 윗층을 진짜 캡슐로(사용자 "더미가 집을 수 있는 아이템에 비해 너무 크다") — 끄면 받침 전부 고정 + 실물 한 층(성능 비교용)
        public bool clawRealTopLayer = true;
        // 성능 조사(10-15): XR 화면(뷰포트) 배율 0.5~1 — XREAL이 URP renderScale을 무시하는지 확인용. 1이면 손대지 않음
        public float xrViewportScale = 1f;
        // 성능 진단(10-15): 기계가 보일 때 숨길 부분 목록(쉼표) — booth,machine,items,rig,fx,scoreboard,ui,physics,all + list(남은 렌더러 로그). 비우면 끔(운영에선 비움)
        public string perfHide = "";
        // 인증 뒤 디지털 디졸브 전환(검정 → 그리드 → 픽셀 분해 → 기계·부스 등장) → [START]
        // 10-28 기계 다가오기(사용자 "트랜지션 뒤 조금 멀리서 시작해 줌인하듯 점점 가속하며 지금 위치까지"): 디졸브가 끝나면 기계·부스가
        // clawApproachDistance m 멀리서 clawApproachSeconds초 동안 k^clawApproachPower로 가속하며 제자리로(카메라는 못 움직이므로 기계를 옮김)
        public float clawApproachDistance = 3f;    // 0~8 m, 0 = 끔(예전처럼 제자리에서 등장)
        public float clawApproachSeconds = 1.5f;   // 0.3~4 초
        public float clawApproachPower = 3f;       // 1~5 가속 곡선 지수(1 = 등속, 클수록 끝에 몰아서 빠름)

        // ── MR VIP 인증 연출(10-12, MR_VIP_인증연출_시각화_개발기획서.pptx) — VipIntro(ClawIntro 씬, 게임 전)가 소비 ──
        public bool revealEnabled = true;         // 인증 뒤 검정·디지털 전환 연출 유지
        public bool vipIntroEnabled = true;        // false면 연출 없이 바로 게임
        public bool vipCardStep = false;           // (10-26) STEP 01 카드키 인증 — false면 바로 홍채·시선(사용자 "카드 인식 제거, 안구 인식만 — 롤백할 수 있게 삭제는 말고"). true = 예전 흐름
        public bool vipStartButton = true;         // (10-26) 홍채·시선 전에 Beam Pro [시연 시작] 대기(작업 리스트 "글래스 착용 후 시연 시작 버튼을 눌러야 게임 시작"). false = 바로 홍채·시선. 카드키 단계를 켜면 카드키 [인증]이 그 역할
        public bool vipPoseDetect = true;          // [잡기] 외에 Beam Pro를 수평으로 들고 있으면 인증 시작(3DoF라 패드 위 위치는 모름)
        public float vipPoseTiltDeg = 25f;         // 5~60 수평에서 이 각도 안이면 '수평'(화면 위·아래 무관) — 실기기 튜닝
        public float vipPoseHoldSeconds = 0.8f;    // 0.2~3 수평·정지를 이만큼 유지하면 인증 시작
        public float vipGazeSeconds = 2f;          // 0.5~5 정면 타깃 연속 응시 시간(벗어나면 처음부터)
        public float vipGazeAngleDeg = 6f;         // 2~15 타깃 중심에서 이 각도 안이면 '보는 중'(나갈 땐 +2° 여유)
        // ── 10-26 안경 착용 감지·자동 처음으로 — ClawWearWatch(사용자 "착용하고 있을 때만 안구 인식, 벗고 30초 지나면 처음 시작 단계로, 카운트는 빔프로 화면에") ──
        public bool clawWearDetect = true;         // 안경 근접 센서(SDK 착용 이벤트): 벗은 동안 홍채 인증 멈춤 + 벗고 clawWearResetSeconds 지나면 처음으로. false = 착용 무시(예전)
        public float clawWearResetSeconds = 30f;   // 0~600 벗은 뒤 처음 화면(홍채 인증)으로 돌아가기까지 초 — Beam Pro 화면에 카운트다운. 0 = 처음으로 안 감
        public float clawResumeResetSeconds = 3f;  // 0~600 앱이 백그라운드에 이만큼 이상 있다 돌아오면(다시 실행) 처음으로. 0 = 끔
        // ── 경품 연동(10-06, 오스리움 이벤트 경품 연동 API v1.0) — PrizeLink가 소비 ──
        // 게임 시작 시 EVT-001(현재 참가자 DID) → [보상 확인] 시 EVT-002(경품 지급 요청, 게임당 1회). 실제 전송 결과는 진행자 화면 팝업
        public bool prizeEnabled = false;          // false면 지금처럼 실물 보상 안내만(서버 호출 없음)
        public string prizeServer = "https://dataspace.authrium.com";
        public string prizeToken = "";             // 파트너 Bearer 토큰(만료 없음) — 코드·git에 넣지 말고 기기 config.txt에만
        public int prizeNftIdCeramic = 25;         // 10-15: 넣은 그릇 중 세라믹이 많으면 이 nftId(마켓 상품 카드 맨 위 번호). 0 = 요청 안 함. 10-28 20 → 25(오스리움 호출 테스트용 번호 — 사용자 지시). 10-28 큐브 규칙부터 안 씀(파싱만 — 되돌리기용)
        public int prizeNftIdGlass = 25;           // 유리 그릇이 많으면 이 nftId(동점이면 마지막에 넣은 종류). 10-28 25(세라믹과 같은 번호). 10-28 큐브 규칙부터 안 씀(파싱만)
        public int prizeNftIdKeyring = 25;         // 10-22 키링이 많으면 이 nftId — 사용자: 종류별로 다르게(번호는 받는 대로). 10-28 25(호출 테스트용). 10-28 경품 = 키링 큐브만 → 이 번호만 씀
        public int prizeTimeoutSeconds = 8;        // 3~30 요청 1회 제한 시간(통신 실패 시 2회까지 재시도)

        public List<RewardEntry> rewards = new()
        {
            new RewardEntry { text = "ICIS 생수", meshIndex = 0 },
            new RewardEntry { text = "I LOVE KOREA 키링", meshIndex = 1 },
            new RewardEntry { text = "신라약과", meshIndex = 2 },
        };

        // 파싱·clamp 중 발생한 경고 누적 — 로그와 SCR-07 배지(Phase 3)가 함께 사용
        public readonly List<string> warnings = new();
    }
}
