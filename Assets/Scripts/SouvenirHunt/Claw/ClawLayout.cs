using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 기계 치수 — 에디터 씬 생성(메뉴 30)과 런타임(캐리지 범위·판정·배치)이 같은 숫자를 쓰도록 한 곳에 모음.
    /// 좌표는 전부 BoothRoot 로컬, 눈높이 y=0. 글래스 카메라 FOV 25°(수직)가 좁다.
    /// 5차(09-14): 기계 = 임포트 모델 Assets/Models/Model2(실물 크기 0.80×1.80×0.85m).
    /// 모델 실측(Model*, FBX 원본 스케일) × MachineScale → 배치(Machine*) → 나머지는 전부 파생. 기계 중심을 1.25m 앞에 세운다
    /// (7차 플레이테스트 "좀 더 가까웠으면" → 1.45에서 당김).
    /// 9차(09-15, 사용자 "조금 작은 느낌"): 기계 1.2배를 시도했다가 사용자 판단으로 기계는 1.0 유지, 대신 부스(BoothScale)를 키움.
    /// 배율 1.0에서는 천장 = 눈높이(부스 바닥 −1.51), 유휴 UFO 하단 −10.6°, 파일 뒷줄 상단 −22°, 앞 바닥 −40°.
    /// 실기기 착용 후 조정할 값도 여기서만 바꾼다.
    /// </summary>
    public static class ClawLayout
    {
        /// <summary>
        /// 기계 모델 선택(10-13). 2 = Model2(UFO 원반, 5차~) / 3 = Model3(Downloads "arcade claw machine" Tripo 생성 모델, 3발 집게 — 사용자 결정으로 기본).
        /// 아래 Model* 실측·배율이 모델마다 다르고 나머지는 전부 파생. 바꾸면 메뉴 30·35 재실행. Model2 경로는 되돌릴 수 있게 그대로 둔다.
        /// </summary>
        public const int MachineModel = 3;
        public const bool IsModel3 = MachineModel == 3;

        /// <summary>
        /// 10-19 기계 몸체 = StartUpPark 인형뽑기 기계(Downloads StartUPPark.zip의 Gacha_Machine.005~007, 사용자: "대체할 수 있는 부분만 대체,
        /// 집게·캐리지·케이블은 지금 것"). 몸체·유리 상자(4면)·배출구 상자·문·조이스틱·버튼 = StartUpPark, 집게·캐리지·케이블 = Model3(위 MachineModel).
        /// 게임 공간(바닥·뚜껑·벽·배출구)은 아래 Sup* 실측에서 파생. false면 Model3 몸체로 복귀(메뉴 30 재실행).
        /// </summary>
        public const bool CabinetSup = true;
        // StartUpPark 기계 실측(10-19, StartUpPark.prefab 루트 기준 m, 배율 1, 앞 = +z 콘솔) — 메뉴 30이 메시와 대조해 1cm 넘게 다르면 경고
        public const float SupBottomY = 0.015f;        // 캐비닛 밑면(카펫 위)
        public const float SupFloorY = 0.755f;         // 유리 상자 안 보이는 바닥(위를 보는 면 2.3㎡)
        public const float SupLidY = 1.805f;           // 유리 상단(1.796) 바로 위 프레임 립 하면 — 뚜껑 콜라이더
        public const float SupInnerHalf = 0.828f;      // 유리(±0.834) 안 = 모서리 기둥(±0.874, 폭 0.092) 안쪽면
        public const float SupPillarInset = 0.006f;
        public const float SupFrontFaceZ = 1.093f;     // 콘솔 앞면(캐비닛 최전면) — 유리 상자 중심 z 0 기준
        // 배출구 상자(앞-왼쪽, 모델 +x): 파일 쪽 벽 바깥면 x 0.3395 / z 0.1155, 벽 상단 0.9775 — 바닥에 구멍(0.565)이 있는 칸
        public const float SupChuteWallX = 0.3395f;
        public const float SupChuteWallZ = 0.1155f;
        public const float SupChuteTopY = 0.9775f;
        /// <summary>
        /// 10-19 집게·캐리지도 StartUpPark 원본(사용자: "원본 StartUpPark 집게 등 기존 메시 재활용했던 부분들을 새로운 메시로") — 몸체 메시 안의
        /// 자체 집게 성분(캐리지 상자·스프링 2단·허브·팔 3개·발 3개)을 쓴다. 늘어나는 케이블은 원본에 없어(스프링만) 우리 케이블 메시(스프링 색).
        /// 원본 집게는 Model3보다 약 1.5배 크다(발끝 반지름 0.195 vs 0.13) → 아이템·잡기 반지름도 HeadRadius 비례로 커진다. 줄이려면 SupClawScale.
        /// </summary>
        public const bool SupClaw = CabinetSup;
        public const float SupClawScale = 1f;            // 집게(스프링~발끝)만 배율 — 캐리지 상자는 천장에 맞춰 1 고정
        /// <summary>
        /// 10-20 아이템 크기 배율(원본 집게 ×1일 때 크기 기준) — 집게 배율과 따로. 집게만 줄이고 아이템은 덜 줄이면 더미를 더 높게·크게 쌓을 수 있다
        /// (사용자: "5층인데 캡슐과 상품 크기를 키워서 더 높게"). 집게 배율과 같으면 예전처럼 같이 줄어듦.
        /// 10-20 사용자 선택 A안: 집게 1.0(원본 크기) · 아이템 0.81(캡슐 0.120m) → 받침 4층 + 실물 1층 = 5층, 더미 꼭대기 바닥 +0.56
        /// (비교: B 집게 0.8·아이템 0.92 = +0.64, C 0.7·0.96 = +0.67, 둘 다 1.0 = 4층 +0.56). 바꾸면 메뉴 30 재실행(받침을 다시 굽는다)
        /// </summary>
        public const float SupItemScale = 0.81f;
        /// <summary>
        /// 10-20 쉬는 동안 집게 윗부분(맨 위 스프링 0.08 + 관절 고리 일부)이 캐리지 상자(높이 0.16) 안으로 들어가 있는 길이 — 실제 기계처럼 끝까지 감아 올린 자세.
        /// 발끝이 그만큼 높아져 더미를 더 쌓을 수 있다. 내려가면 스프링이 상자 밖으로 나온다
        /// </summary>
        public const float SupHeadRetract = 0.145f;   // 10-22 0.11 → 0.145: 상품을 발끝 가까이(쥔 모양) 들게 되어 내려온 만큼 더 감아 올려 높이 예산 유지(캐리지 상자 0.16 안)
        // 원본 집게 실측(10-19, 프리팹 루트 기준 m): 캐리지 상자 1.824~1.984 · 스프링 윗면 1.824 · 발끝 1.300(반지름 0.195) · 발 가장 넓은 곳 반지름 0.278
        public const float SupCarriageBottomY = 1.824f;  // 캐리지 상자 밑면 = 케이블 앵커
        public const float SupHeadTopY = 1.824f;         // 헤드(스프링 포함) 윗면
        public const float SupHeadBottomY = 1.300f;      // 발끝
        public const float SupHeadTipRadius = 0.195f;    // 발끝 반지름 = HeadRadius(Model3도 발끝 반지름 기준)
        public const float SupHeadMaxRadius = 0.278f;    // 발이 가장 넓은 곳 — 캐리지 여유(유리 관통 방지)
        public const float SupHeadAttachLift = 0.10f;    // 든 물건 윗면이 발끝 위 이만큼(10-22 0.16 → 0.10, 사용자 "실제로 쥐고 올리기" — 허브 끝까지 빨아올리지 않고 발이 감싼 자리)

        /// <summary>기계 균일 배율 — 몸체·헤드·조이스틱·콜라이더·슈트·부스 위치가 전부 따라감(메뉴 30 재실행 필요).
        /// 바닥(BoothFloorY) 고정으로 위로 커진다. 1이 아니면 ScoreboardPitchDeg도 다시 맞출 것(Model2 1.2에서는 −7°가 유휴 UFO와 파일 사이).
        /// Model3: 모델 원래 크기는 높이 1.0m·폭 0.77m. 10-13 사용자 "원래 크기에 비해 많이 작다, 이전 크기 정도로" → 1.8(높이 1.8m = Model2와 같음,
        /// 폭 1.39m·깊이 1.24m, 내부 1.28×0.85m, 유리 상단 = 눈높이). 헤드 반지름 0.13으로 이전 UFO(0.127)와 비슷해짐</summary>
        public const float MachineScale = IsModel3 ? 1.8f : 1f;
        const float S = MachineScale;
        /// <summary>11차(09-30, 사용자 "물건이 많아진 만큼 옆으로 넓혀 시야·공간 확보, 면적 20%"): 기계 가로(부스 x)만 추가 배율.
        /// 몸체(유리·간판·콘솔·기둥·프레임)는 x로 늘어나고, UFO·공·조이스틱·버튼 메시는 늘이지 않는다(위치만 따라감). 내부 바닥 0.67×0.59 → 0.80×0.59.
        /// Model3는 1(간판 글자가 늘어나지 않게)</summary>
        public const float MachineWidthScale = IsModel3 ? 1f : 1.2f;
        const float SX = S * MachineWidthScale;
        /// <summary>Model2 인스턴스 localScale(모델 로컬 = yaw 전) — 콘솔 부품 위치 변환도 같은 값</summary>
        public static Vector3 MachineLocalScale => new Vector3(SX, S, S);

        // ── 모델 실측(모델 로컬 FBX 원본 스케일, 원점 = 바닥 중앙, 조이스틱 콘솔이 +z) — 메뉴 30이 실제 메시와 대조해 1cm 넘게 다르면 경고 ──
        // Model3(10-13): 정점 실측(tripo_part 번호) — 받침 상자 27 윗면, 옆 창틀 30·31, 앞 창틀 55, 뒤 창틀 58, 집게 13+허브 15·17·20, 캐리지 상자 63
        // 바닥: 처음엔 바닥 타일 68·69·72·73 상면(0.360)으로 잡았으나 그 위를 받침 상자(#27)의 파란 윗면(0.391, 내부 전체를 덮는 사각 1장)이 덮어
        //   공(3cm·5cm)이 그 판 아래에 묻혀 집게로 들어 올리기 전까지 안 보였다(10-13 사용자 보고) → 보이는 바닥 = #27 윗면
        public const float ModelFloorY = IsModel3 ? 0.391f : 0.70f;          // 유리 바닥 = 콘솔 상판 / Model3 받침 상자(#27) 윗면 = 보이는 내부 바닥
        public const float ModelCeilingY = IsModel3 ? 0.848f : 1.51f;        // 유리 상단 = 상단 간판 하면
        public const float ModelFrameTopY = IsModel3 ? 0.830f : 1.484f;      // 상부 프레임 링 하면(뚜껑 콜라이더 기준) / Model3 레일 끝 캡 하면
        public const float ModelInnerHalfX = IsModel3 ? 0.355f : 0.334f;     // 유리(창틀) 안쪽면
        public const float ModelInnerHalfZ = IsModel3 ? 0.236f : 0.295f;
        /// <summary>내부 중심 z(모델 로컬). Model3는 앞 창틀 안쪽 +0.206 / 뒤 −0.266이라 중심이 뒤로 3cm — yaw 180이라 부스에서는 앞으로(CenterZ는 내부 중심)</summary>
        public const float ModelInnerCenterZ = IsModel3 ? -0.030f : 0f;
        public const float ModelPillarInset = IsModel3 ? 0.030f : 0.026f;    // 모서리 기둥이 유리 안쪽면에서 안으로 튀어나온 폭(Model2 x 0.308 / z 0.27)
        // 헤드: Model2 = UFO 본체, Model3 = 3발 집게(집게발 끝 0.566 ~ 허브 상단 0.766 — 위아래 대칭 가정이라 중심은 그 가운데)
        public const float ModelUfoBodyCenterY = IsModel3 ? 0.666f : 1.281f; // UFO 본체(1.179~1.383) 중심
        public const float ModelUfoBodyTopY = IsModel3 ? 0.766f : 1.383f;
        /// <summary>케이블 앵커. Model2 = 천장 마운트 판 하면 / Model3 = 허브가 캐리지 상자(하면 0.760)에 바로 붙어 있어 상단 위 1cm(케이블 원형 길이)</summary>
        public const float ModelUfoMountY = IsModel3 ? 0.776f : 1.499f;
        public const float ModelUfoRadius = IsModel3 ? 0.0725f : 0.181f;

        // ── 배치 ──
        public const float MachineYaw = 180f;             // 콘솔(+z)이 플레이어 쪽을 향하게
        // 11-10 ClawGame 시점(부스 오프셋을 상수에 합침): ClawGame(com.hroov.clawgame vc67)은 BoothFloorY −2.04·CenterZ 1.98에 BoothAligner가 부스 루트를
        //   눈 앞 1.58m·아래 0.18m에 두고 시점을 13.2° 숙인다 → 우리는 부스 루트 = 눈 그대로, 바닥 −2.04 − 0.18 = −2.22 · 중심 1.98 + 1.58 = 3.56,
        //   숙임은 XR 리그(BoothAligner.SetViewTilt, config clawViewTiltDeg 13.2). 기계·부스 모형·점수판·상품은 이 상수에서 파생(메뉴 30 재실행)
        public const float BoothFloorY = -2.22f;          // 부스 바닥 = 눈높이 아래 2.22m(11-10 ClawGame 시점 −1.51 → −2.22. 이전: 5차 원본 기계 천장 높이 1.51, 9차부터 기계·부스 배율과 무관하게 고정)
        public const float MachineBaseY = BoothFloorY;
        /// <summary>내부(바스켓) 중심 z. 기계 원점은 ModelInnerCenterZ만큼 어긋날 수 있다(MachineOriginLocal).
        /// Model3 ×1.8(10-13): 콘솔이 앞으로 더 나와(모델 +z 0.345 × 1.8) 1.25면 콘솔 앞면이 눈앞 0.58m — 이전 Model2 콘솔 앞면(약 0.83m)과
        /// 같은 거리에 서도록 1.50(앞 유리 안쪽 z 1.075, 내부 가로 ±31°로 이전 ±23°보다 넓음).
        /// StartUpPark(10-19): 유리 상자 중심 1.92 — 앞 유리까지 1.092m(Model3 1.075와 비슷), 콘솔 앞면은 눈앞 0.83m(Model2·3와 같게)</summary>
        public const float CenterZ = CabinetSup ? 3.56f : IsModel3 ? 1.50f : 1.25f;   // Model2: 기계 중심(앞 유리 안쪽 z 0.955) · 11-10 ClawGame 시점 StartUpPark 1.92 → 3.56(= 1.98 + 1.58, 위 BoothFloorY 주석)
        const float CenterZPre1110 = 1.92f; // 11-10 이전 StartUpPark 중심 — 점수판 크기 기준(ScoreboardScale)
        /// <summary>기계 원점(부스 로컬). StartUpPark: 프리팹 루트 — 밑면이 MachineBaseY, 유리 상자 중심(모델 xz 0)이 (0, CenterZ)</summary>
        public static Vector3 MachineOriginLocal => CabinetSup
            ? new Vector3(0f, MachineBaseY - SupBottomY, CenterZ)
            : new Vector3(0f, MachineBaseY, CenterZ + ModelInnerCenterZ * S); // yaw 180 → 모델 z는 부스 −z

        // ── 부스(6차, 09-14): 임포트 모델 Model1 "XR EXPERIENCE"(실물 크기 5.1×3.1×4.5m, 입구 아치가 +z) — 기계와 같은 바닥·yaw.
        // 가운데 구조물(지구본 받침)의 앞 가장자리가 기계 뒷면에서 BoothGap 뒤에 오도록 메뉴 30이 메시 실측으로 원점을 계산한다(시각 전용).
        // 9차(09-15): 부스 균일 배율 — 바닥 높이·가운데 구조물 앞 가장자리 위치는 유지하고 나머지(입구 쪽 포함)가 커진다
        public const float BoothYaw = 180f;
        public const float BoothGap = 0.05f;
        public const float BoothScale = 1.2f;

        // ── 바스켓 내부 ──
        public const float HalfX = CabinetSup ? SupInnerHalf : ModelInnerHalfX * SX;   // 0.401 (11차 가로 1.2배) / StartUpPark 0.828
        public const float HalfZ = CabinetSup ? SupInnerHalf : ModelInnerHalfZ * S;    // 0.295
        public const float InnerX = HalfX * 2f;
        public const float InnerZ = HalfZ * 2f;
        public const float FloorY = CabinetSup ? MachineBaseY + SupFloorY - SupBottomY : MachineBaseY + ModelFloorY * S;  // −0.81 / StartUpPark −0.77 내부 바닥면(아이템이 놓이는 면)
        public const float LidY = CabinetSup ? MachineBaseY + SupLidY - SupBottomY : MachineBaseY + ModelFrameTopY * S;   // −0.026 / StartUpPark +0.28 콜라이더 뚜껑 하면
        public const float FrontZ = CenterZ - HalfZ;                    // 0.955
        public const float BackZ = CenterZ + HalfZ;                     // 1.545
        public const float WallThickness = 0.20f;                       // 콜라이더 벽 두께 — 10-15 폭발 직후 속도 상한 4.5m/s × 0.02s = 0.09m/스텝의 2배 여유(바깥쪽으로만 두꺼워짐)
        public const float PillarInsetX = CabinetSup ? SupPillarInset : ModelPillarInset * SX;   // 코너 기둥 콜라이더(유리 안쪽 코너에서 안으로) — 기둥도 x로 늘어난다
        public const float PillarInsetZ = CabinetSup ? SupPillarInset : ModelPillarInset * S;

        // ── 레일 / 헤드(UFO) ──
        /// <summary>StartUpPark: Model3 캐리지(마운트 위로 뚜껑까지 (0.830−0.776)×S = 0.097)를 뚜껑 바로 아래에 단다</summary>
        public const float RailY = SupClaw ? MachineBaseY - SupBottomY + SupCarriageBottomY
            : CabinetSup ? LidY - (ModelFrameTopY - ModelUfoMountY) * S : MachineBaseY + ModelUfoMountY * S;   // −0.011 캐리지 = 마운트 판 하면 = 케이블 앵커
        // 7차(플레이테스트 "UFO가 커서 끝 공을 못 잡음", 사용자 선택): UFO 본체·마운트·케이블을 기계 대비 0.7배로 굽는다(메뉴 30, 실제 배율 UfoScale × MachineScale).
        // 모델 실측 상수는 그대로, 아래 헤드 치수만 스케일 파생 → 슈트가 코너 쪽으로 붙어 바닥도 넓어짐
        public const float UfoScale = IsModel3 ? 1f : 0.7f;   // Model3 집게는 원래 크기
        public const float UfoBakeScale = UfoScale * S;
        /// <summary>StartUpPark: 원본은 스프링이 캐리지에 바로 붙어 있어 유휴 케이블은 1cm만(케이블 메시 원형 길이)</summary>
        public const float CableRestLength = SupClaw ? 0.01f : (ModelUfoMountY - ModelUfoBodyTopY) * UfoBakeScale;     // 0.081 유휴 시 앵커→헤드 상단(케이블 메시 원형 길이)
        public const float HeadRadius = SupClaw ? SupHeadTipRadius * SupClawScale : ModelUfoRadius * UfoBakeScale;     // 0.127 / StartUpPark 0.195
        public const float HeadHalfHeight = SupClaw ? (SupHeadTopY - SupHeadBottomY) * 0.5f * SupClawScale
            : (ModelUfoBodyTopY - ModelUfoBodyCenterY) * UfoBakeScale; // 0.071 헤드 중심 → 상단/하단 / StartUpPark 0.262(스프링 포함)
        /// <summary>쉬는 동안 헤드 윗면이 앵커(캐리지 밑면)보다 위로 들어가 있는 길이(StartUpPark만, 10-20)</summary>
        public const float HeadRetract = SupClaw ? SupHeadRetract : 0f;
        public const float CableLength = CableRestLength + HeadHalfHeight - HeadRetract;               // 0.153 유휴 시 앵커→헤드 중심 / StartUpPark 0.162(감아 올림)
        /// <summary>아이템 크기 = 헤드 지름 × 종류 비율 × 이 값(10-20) — 집게와 아이템 배율을 따로 둔다</summary>
        public const float ItemSizeFactor = CabinetSup ? SupItemScale / SupClawScale : 1f;
        /// <summary>StartUpPark: 발 가장 넓은 곳(0.278)까지 다 비우면 벽 쪽 물건을 못 잡아(잡기 반지름 0.156) 0.25 — 끝에서 발 옆구리가 유리에 최대 3cm 걸침</summary>
        public const float CarriageMargin = SupClaw ? 0.25f * SupClawScale : HeadRadius + 0.01f;          // 내부 벽 → 캐리지 중심(UFO 원반이 유리에 닿지 않게)
        public const float ProbeRadius = HeadRadius;    // 수직 판정 SphereCast 반지름 = 원반(7차: 원반 아래 어디든 최상단 공 — 끝·코너 공 포함)
        public const float ProbeDistance = CabinetSup ? RailY - FloorY + 0.1f * S : IsModel3 ? (ModelUfoMountY - ModelFloorY + 0.1f) * S : 0.90f * S;   // 레일에서 바닥 아래까지(Model2 레일−바닥 0.80)
        /// <summary>흡착한 공 덩어리를 헤드 하단에서 이만큼 위로 — Model3 집게는 집게발 안쪽에 공이 들어가 보이게(Model2 UFO는 원반 아래라 0)</summary>
        public const float HeadAttachLift = SupClaw ? SupHeadAttachLift * SupClawScale : IsModel3 ? 0.05f * S : 0f;
        public const float StopProbeRadius = HeadRadius * 0.9f;          // 하강 정지 높이 판정(원반 아래 최상단 아이템)

        public static float CarriageMinX => -(HalfX - CarriageMargin);   // −0.264 (11차, 이전 −0.197)
        public static float CarriageMaxX => HalfX - CarriageMargin;      // +0.264
        public static float CarriageMinZ => FrontZ + CarriageMargin;     // 1.092
        public static float CarriageMaxZ => BackZ - CarriageMargin;      // 1.408

        /// <summary>
        /// 10-26 목표물(그릇 2종·키링) 배치·리스폰 안전 구역 — 사용자 "양 끝에 스폰되면 잡을 수 없어, 집을 수 있는 안전 존에서 리스폰, 그릇 2종도 같은 규칙".
        /// 캐리지가 바로 위에 설 수 있는 범위(벽에서 CarriageMargin 안쪽, 그보다 TargetSafeInset 더 안)이면서, 배출구 칸에서 TargetChuteGap(펼친 발끝 반지름)
        /// 이상 떨어진 곳 — 칸 옆은 펼친 발이 아크릴 칸막이 위에 걸려 덜 내려간다. 폭발·밀림으로 굴러간 것은 그대로(벽·모서리는 원래 어렵게 — 10-22 결정)
        /// </summary>
        public const float TargetSafeInset = 0.03f;
        public const float TargetChuteGap = HeadRadius * 1.4f;   // ≈ 0.27 — 뼈 집게 하강 때 펼친 발끝 반지름(약 0.26) + 여유
        /// <summary>부스 로컬 (x, z)가 목표물 안전 구역인가. slack만큼 넓혀 판정(셀프테스트 — 정착하며 몇 cm 구름)</summary>
        public static bool InTargetSafeZone(float x, float z, float slack = 0f)
        {
            float edge = TargetSafeInset - slack;
            if (Mathf.Abs(x) > CarriageMaxX - edge || z < CarriageMinZ + edge || z > CarriageMaxZ - edge) return false;
            float dx = Mathf.Max(0f, x - ChuteWallX), dz = Mathf.Max(0f, z - ChuteWallZ); // 배출구 칸(x < ChuteWallX, z < ChuteWallZ)까지 거리
            float gap = Mathf.Max(0f, TargetChuteGap - slack);
            return dx * dx + dz * dz >= gap * gap;
        }

        // ── 아이템(공) ──
        // 10-15 미션형: 아이템(그릇·캡슐·폭탄) 치수·개수·물리는 ClawItemSpec 에셋 — 크기는 헤드 지름 대비 비율이라 기계가 바뀌면 따라간다.
        // 여기에는 기계 치수만 둔다. 포획 반지름·흡착 오프셋·셀 여유도 spec 파생(ClawItemSpec.GrabRadius 등).

        // ── 슈트: 앞-왼쪽(캐리지 도달 한계 지점). 아이템은 여기서 사라진다 ──
        // StartUpPark(10-19): 기계 자체 배출구 상자(앞-왼쪽, 벽·바닥 구멍·앞 문)를 그대로 쓰고 칸·벽·낙하 지점을 그 상자에 맞춘다(우리 구멍·유리 울타리 시각은 만들지 않음)
        public static readonly Vector3 ChuteLocal = CabinetSup
            ? new Vector3(Mathf.Max(-(SupChuteWallX + SupInnerHalf) * 0.5f, -(HalfX - CarriageMargin)), FloorY, Mathf.Max((CenterZ - SupInnerHalf + CenterZ - SupChuteWallZ) * 0.5f, CenterZ - HalfZ + CarriageMargin))
            : new Vector3(CarriageMinX, FloorY, CarriageMinZ);
        public const float ChuteHoleSize = (IsModel3 ? 0.16f : 0.20f) * S;   // 0.20 구멍 한 변(공 + 여유) / Model3 0.16 — 집게 지름 0.145, 0.20이면 안쪽 벽 밖으로 1.75cm 나감(StartUpPark는 시각 안 씀)
        // 슈트 유리 울타리: 공이 슈트 셀로 굴러 들어오지 않게. 안쪽(파일 쪽) 벽면 좌표 — 셀 = x<ChuteWallX && z<ChuteWallZ
        public static float ChuteWallX => CabinetSup ? -SupChuteWallX : ChuteLocal.x + ChuteHoleSize * 0.5f + 0.012f;   // −0.085 / StartUpPark −0.34
        public static float ChuteWallZ => CabinetSup ? CenterZ - SupChuteWallZ : ChuteLocal.z + ChuteHoleSize * 0.5f + 0.012f;   // 1.204 / StartUpPark 1.80
        /// <summary>모델 배출구 상자 벽 높이(StartUpPark 0.22) — 그 위에 아크릴 칸막이(ChuteGuard)를 세운다. Model3 경로는 우리 유리 울타리 0.12</summary>
        public const float ChuteWallHeight = CabinetSup ? SupChuteTopY - SupFloorY : 0.12f * S;
        /// <summary>보이는 칸막이 판 두께(아크릴)</summary>
        public const float ChuteGuardThickness = 0.006f;

        // ── 더미 높이 예산(10-20, 사용자 "4~5층 + 배출구 벽을 그 높이까지 + 뚜껑까지 투명 벽") ──
        // 집게는 쉬는 높이 그대로 옆으로 운반한다(위로 더 못 올라감) → 잡힌 상품 바닥 ≈ 쉬는 발끝. 그 아래로 칸막이 → 더미 순서로 여유를 둔다.
        // 배출구 칸 전체는 바닥~뚜껑 보이지 않는 기둥 콜라이더(ChuteBarrier) — 집게(콜라이더 없음)와 잡힌 상품(ClawHeld)만 지나간다.
        /// <summary>쉬는 집게 발끝 높이(StartUpPark ×1 = 바닥 +0.535, 감아 올리면 +0.645)</summary>
        public const float IdleTipY = RailY - CableLength - HeadHalfHeight;
        /// <summary>가장 키 큰 아이템(유리 그릇) 높이 ÷ 헤드 지름 — 규격 에셋 유리 widthToHead 0.46 × 높이/폭 0.91(바뀌면 같이; 셀프테스트가 실제 운반 여유를 잰다)</summary>
        const float TallestItemToHeadDiameter = 0.42f;
        const float HeldHangRaw = 2f * HeadRadius * ItemSizeFactor * TallestItemToHeadDiameter - HeadAttachLift;
        /// <summary>잡힌 상품이 발끝 아래로 내려오는 최대 길이(아이템이 집게보다 크면 커짐)</summary>
        public const float HeldHangBelowTip = HeldHangRaw > 0f ? HeldHangRaw : 0f;
        /// <summary>운반 중(쉬는 높이로 옆 이동) 잡힌 상품 바닥 높이</summary>
        public const float TravelBottomY = IdleTipY - HeldHangBelowTip;
        /// <summary>배출구 위에서 놓기 전에 더 내리는 길이(기존 0.05) — 잡힌 상품 바닥이 칸막이 위 2cm 넘게 남도록</summary>
        public const float DropLowerDepth = CabinetSup ? 0.03f : 0.05f;
        /// <summary>보이는 칸막이(아크릴) 위 끝 — 운반 중 상품 바닥 5.5cm 아래. Model3 경로는 기존 유리 울타리 높이</summary>
        public const float ChuteGuardTopY = CabinetSup ? TravelBottomY - 0.055f : FloorY + ChuteWallHeight;
        public const float ChuteGuardHeight = ChuteGuardTopY - FloorY;
        /// <summary>더미 꼭대기 목표(칸막이 3cm 아래) — 받침 더미 + 맨 위 실물 한 층의 윗면</summary>
        public const float PileTopY = ChuteGuardTopY - 0.03f;
        /// <summary>받침 더미 윗면 — 그 위에 가장 큰 아이템 한 층이 PileTopY 안에 들어가게</summary>
        public static float BedTopY(float maxItemHeight) => PileTopY - maxItemHeight - 0.01f;

        // ── 가이드 프레임(Beam Pro를 겹쳐 놓는 가상 틀) — 기계와 무관, 가슴 앞 ──
        // 7차: 아래 두 오프셋은 GuidePanel 루트(= 시선 yaw를 따라가는 눈 원점, ClawGuidePanel) 기준 — 아무 방향을 보고 시작해도 틀·문구가 정면에 온다.
        // 가이드 프레임(4차): 팔꿈치 굽힌 가슴 앞 0.30m(−54.5°). 크기는 Beam Pro 실측 ×1.1(0.52m에서 0.85° 여유)
        public static readonly Vector3 GuideFrameLocal = new Vector3(0f, -0.42f, 0.30f);
        public const float GuideFrameW = 0.1815f;       // Beam Pro 가로 비율 2340:1080 ≈ 2.17
        public const float GuideFrameH = 0.0836f;
        // 가이드 문구(4차): 정면 시야(±12.5°) 하단에 윗부분(~42%)이 걸리도록(중심 −13.5°, 상단 −8.3°) → 아래를 보게 유도.
        // 7차: 기계 앞 유리(z 0.955) 뒤 부피 안이므로 Guide 중에는 기계를 숨긴 채 둔다(MachineVisibility.Hide).
        public static readonly Vector3 GuideTextLocal = new Vector3(0f, -0.24f, 1.0f);

        // ── UI 위치 ──
        // 스코어보드(7차): 뒷유리 바로 바깥(부스 가운데 구조물 앞 가장자리 z≈1.65보다 앞 — 더 뒤로 보내면 지구본 부피와 겹침),
        // 시선 −15°(유휴 UFO 하단 −10.6°와 파일 뒷줄 상단 약 −22° 사이). 폭 760 캔버스를 0.75배로 줄여 좌우 글자 끝(|x|≤0.28)이
        // 뒤 기둥 안쪽 모서리가 만드는 창(≈ ±11.5°) 안에 들어오게 — 5차는 기둥에 가렸음(플레이테스트). 거리 여유·크기는 기계 배율 파생
        // StartUpPark(10-19): 뒤 유리가 2.75m로 멀고 레일이 눈높이 위라 유휴 집게 하단 약 −5.6°, 파일 뒷줄 상단 약 −13.6° → −9.5°.
        // 크기는 Model3 때(스코어보드 z 1.988)와 같은 각크기가 되도록 거리 비례
        // 10-20 더미 5층(꼭대기 뒷줄 약 −4.5°)·집게 감아 올림(발끝 −3.7°~캐리지 +11°)으로 −9.5°는 더미에 완전히 가림 →
        // 사용자 선택: 뒤 유리 밖 더미 위·천장(뒤 +5.8°) 아래 띠 0.5°, 가운데는 집게(가운데일 때 ±8.2°)가 가리므로 좌·우 열을 벌린다 —
        // 열 폭 230(글자 자동 크기), 열 가운데 ±318 → 눈 기준 7.8°~16.3°(뒤 기둥 창 16.7° 안). 캔버스 폭 890, 배경은 열 뒤에만
        // 11-10 ClawGame 시점(바닥 −1.51 → −2.22·기계 중심 1.92 → 3.56): 눈 기준 0.5°로 두면 점수판이 눈높이(바닥 위 약 2.26m)에 남아 기계 위·뒤로 뜨거나
        //   상단 간판 틀에 가림 → StartUpPark는 높이를 **기계(부스) 바닥 기준**으로(바닥 위 이전 높이 1.5345 = 뒤 z 2.811에서 +0.5°).
        //   새 시점에서 1.5345면 점수판 윗부분 2/3가 기계 앞 윗판(간판 틀, 유리 상단 1.796 위)에 가림
        //   (Sup_Body 메시 광선 검사: 1.5345 → 글자 칸 12/36만 보임, 1.30 → 전부, 1.32 → 맨 윗줄 가림) → 1.27(여유 3cm). 눈 기준 약 −12°(숙인 시점 중심 −13.2° 바로 위)
        const float ScoreboardPitchDeg = -15f;              // Model2·3 몸체(CabinetSup=false) 경로만 — 눈 기준 기울기
        public const float ScoreboardAboveFloor = 1.27f;    // StartUpPark — 부스 바닥 위 점수판 중심 높이(m). 11-10 (이전: 눈 기준 0.5° ≈ 바닥 위 1.5345)
        public static float ScoreboardZ => BackZ + 0.035f * S;
        public static Vector3 ScoreboardLocal => new Vector3(0f,
            CabinetSup ? BoothFloorY + ScoreboardAboveFloor : ScoreboardZ * Mathf.Tan(ScoreboardPitchDeg * Mathf.Deg2Rad), ScoreboardZ);
        public const float ScoreboardWidth = CabinetSup ? 890f : 760f;
        public const float ScoreboardColumnWidth = CabinetSup ? 230f : 280f;
        /// <summary>좌·우 열을 벌려 가운데(집게)를 비우고 배경도 열 뒤에만(StartUpPark)</summary>
        public const bool ScoreboardSplit = CabinetSup;
        /// <summary>
        /// 11-10 기계가 멀어지면(중심 1.92 → 3.56) 뒤 기둥 창이 좁아진다(16.7° → 10.6°) — 거리 비례만 하면 각크기가 그대로라 글자 끝이 기둥에 가림.
        /// 각크기를 창 너비(tan)에 비례해 줄인다: 1.92이면 예전과 같은 값(글자 바깥 끝 16.3°), 3.56이면 약 10.4°(창 10.6° 안)
        /// </summary>
        static float BackWindowTan(float centerZ) => (HalfX - PillarInsetX) / (centerZ + HalfZ - PillarInsetZ);
        public static float ScoreboardScale => 0.75f * S * (CabinetSup ? ScoreboardZ / 1.988f * BackWindowTan(CenterZ) / BackWindowTan(CenterZPre1110) : 1f);
        /// <summary>뒤 기둥 안쪽 모서리(부스 로컬 x, z) — 스코어보드 가림 검사용</summary>
        public static Vector2 BackPillarInnerXZ => new Vector2(HalfX - PillarInsetX, BackZ - PillarInsetZ);
        // 모달 패널(5차): 상단 간판(y 0~0.29, z 0.95~1.65)이 기계 위·뒤를 가리므로 기계 앞 시선 방향(−20°, 0.85m)에 둔다.
        // 기존 거리에서 쓰던 캔버스를 각크기 그대로 보이도록 루트 스케일 = 새 거리 / 기존 거리
        const float PanelPitchDeg = -20f;
        const float PanelDistance = 0.85f;
        public static Vector3 PanelLocal => Quaternion.Euler(-PanelPitchDeg, 0f, 0f) * Vector3.forward * PanelDistance;
        public static readonly Vector3 StartPanelLocal = PanelLocal;
        public static readonly Vector3 ResultPanelLocal = PanelLocal;
        public static readonly Vector3 ExitDialogLocal = PanelLocal + new Vector3(0f, 0f, -0.05f); // 결과창 앞
        public static float StartPanelScale => PanelDistance / 2.0f;    // 기존 (0,0,2.0)
        public static float ResultPanelScale => PanelDistance / 2.33f;  // 기존 (0,0.48,2.3)
        public static float ExitDialogScale => (PanelDistance - 0.05f) / 1.53f; // 기존 (0,0.30,1.5)
    }
}
