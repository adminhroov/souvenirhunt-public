using System;
using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 집게 헤드. 캐리지에 케이블로 매달려 절차적 감쇠 진자(ζ 0.5, 최대 6°)로 흔들린다 — PhysX 조인트 대신(결정적·솔버 비용 0·리셋 용이).
    /// 10-15 미션형 복원(git 8d3d1ed의 1개 잡기 상태기계 + 이후 추가된 케이블 메시·Model3 오프셋·재정렬 델타):
    ///   Descend(아래 최상단 아이템 윗면까지) → Dwell → Attach(대상 1개를 집게 안으로, 도넛 레이저) → Ascend → Travel(배출구 위로 자동)
    ///   → DropLower → Drop(낙하 연출·OnDelivered·풀 반환) → Return. 빈손이면 Dwell → Ascend → Return.
    /// 잡을 대상 = ClawBasket.PickUnder(헤드 중심에 가장 가까운 것) — 조준 중 가이드도 같은 함수로 고른다(가리킨 것 = 잡히는 것).
    /// 폭탄: 하강 중 매 프레임 헤드 아래 얇은 판(OverlapBox)에 폭탄이 닿으면 즉시 OnBombTouched → 폭탄 제거 → Recoil(튕기듯 상승, 잡지 않음).
    /// 하강이 끝날 때 고른 대상이 폭탄이어도 같다(사용자 10-15: 집게가 닿으면 바로 폭발). 조준 중 가이드는 같은 접촉 판으로 미리 계산해
    /// 내려가면 폭탄에 닿는 자리면 그 폭탄을 가리킨다(그릇 옆 키 큰 폭탄에 먼저 닿는 경우 — 가리킨 것 = 일어날 일).
    /// </summary>
    public class ClawHead : MonoBehaviour
    {
        /// <summary>10-29 Home = 배출 뒤 쉬는 높이로 돌아와 캐리지가 가운데로 자동 이동하는 중(조이스틱·잡기 막힘)</summary>
        public enum Phase { Idle, Descend, Dwell, Attach, Ascend, Travel, DropLower, Drop, Recoil, Return, Home }

        [SerializeField] ClawCarriage carriage;
        [SerializeField] ClawBasket basket;
        public ClawBasket Basket => basket;
        [SerializeField] ClawGuide guide;
        [SerializeField] Transform attachPoint;
        [SerializeField] Transform cableAnchor;
        [SerializeField] LineRenderer cable;           // 구형 선 케이블(선택) — 5차부터는 cableMesh
        [SerializeField] ClawCableMesh cableMesh;      // 케이블(앵커→헤드 상단으로 늘어남)
        [Tooltip("아이템 규격 — 잡기 반경·집게 안 높이. 미배선이면 바스켓의 spec")]
        [SerializeField] ClawItemSpec spec;

        [Tooltip("config clawHeadSpeed — 10-29부터 하강 시간 = 실제 내려갈 거리 ÷ 이 속도(더미 모드만 10-20 방식: 기준 거리 DescendRefDistance의 시간으로 일정)")]
        public float DescendSpeed = 0.6f;
        /// <summary>
        /// 10-20 사용자 "어느 높이에 있든 내려가서 잡기까지 걸리는 시간이 일정하게, 층을 쌓기 전 정도로": 더미가 한 층이던 때
        /// 쉬는 발끝(바닥 +0.535)에서 캡슐 윗면(+0.15)까지 약 0.37m — 하강 시간 = 이 거리 ÷ DescendSpeed(기본 0.62초).
        /// 10-29 더미 모드(basket.BedActive)에서만 쓴다 — 더미가 없으면 바닥까지 약 0.65m라 거리 비례(DescendSecondsFor)
        /// </summary>
        public const float DescendRefDistance = 0.37f;
        public float DescendSeconds => DescendRefDistance / Mathf.Max(0.05f, DescendSpeed);
        /// <summary>10-29 거리 비례 하강의 최소 시간(바로 아래 상품 위 등 짧은 하강도 부드럽게)</summary>
        public const float DescendMinSec = 0.3f;
        /// <summary>10-29 마지막 잡기의 하강 시간(셀프테스트 — 거리 ÷ 속도)</summary>
        public float LastDescendSeconds { get; private set; }
        /// <summary>10-29 이 길이(케이블, m)만큼 더 내려가는 하강 시간 — 더미 모드는 10-20 일정 시간, 아니면 거리 ÷ DescendSpeed</summary>
        public float DescendSecondsFor(float distance) => basket != null && basket.BedActive ? DescendSeconds
            : Mathf.Max(DescendMinSec, distance / Mathf.Max(0.05f, DescendSpeed));
        float hangFrom, moveDur;      // 10-22 이번 오르내림(smoothstep) 시작 길이·시간
        float bounceT = -1f, hangBounce; // 바닥 출렁임
        ClawItem grabTarget;            // 잡기 시작 때 가이드가 가리킨 것(Dwell에서 우선)
        float settleT = -1f;            // 잡기(Attach)에서 발이 다 멈춘 시각
        bool homeAfterReturn;           // 10-29 이번 사이클에서 배출구에 놓았다 — 쉬는 높이로 돌아오면 가운데로(Home)
        ClawItem aimedBomb;             // 폭탄을 겨눠 잡으러 내려감(바닥에 닿으면 폭발)
        ClawItem candidateGrab;         // 조준 중 잡으려는 상품(가이드가 그 길에 닿을 폭탄을 가리켜도 집게는 이걸 잡으러 내려간다 → 폭탄에 닿아 폭발)

        /// <summary>배출구로 떨어지는 중인 상품 하나(헤드는 이미 돌아감) — 10-22 여러 개 잡기라 목록</summary>
        struct Faller
        {
            public ClawItem item;
            public Vector3 vel, angVel, target; // target = 떨어지며 모이는 배출구 안 자리(월드 — 배출구 가운데 + 집게 안 자리 간격)
            public float floorY, landT;
            public bool landed;
        }
        readonly List<Faller> fallers = new(4);
        /// <summary>배출구로 떨어지는 중인 상품 수(바닥에 닿으면 OnDelivered, 사라지면 빠짐)</summary>
        public int FallingCount => fallers.Count;
        /// <summary>떨어지는 중 아직 바닥에 안 닿은 점수 상품(10-28 키링 큐브만)이 있음 — 시간 종료 유예</summary>
        public bool FallingTargetPending
        {
            get
            {
                foreach (var f in fallers) if (!f.landed && f.item != null && f.item.IsScoring) return true;
                return false;
            }
        }

        // 10-22 물리 집게(사용자 "3개 제한이 아니라 진짜 집게의 물리력으로"): 발(ClawFingers 물리 막대)이 오므려 닿은 채 올라가면
        // 갈고리가 받친 것·그 위에 얹힌 것만 물리로 딸려 온다(몇 개든). 다 올라가 집게 안에 남은 것을 그때 고정해 배출구로 옮긴다
        // (운반 중엔 물리 대신 헤드에 고정 — 배출구 보이지 않는 기둥에 부딪혀 떨어지지 않게, 칸막이 위 여유 유지)
        readonly List<ClawItem> held = new(8);
        readonly List<Vector3> heldLocalPos = new(8);       // 고정 순간 헤드 로컬 자세
        readonly List<Quaternion> heldLocalRots = new(8);
        readonly List<float> heldLift = new(8);             // 운반 높이 여유가 모자라면 고정 직후 살짝 끌어올림(m)
        float lockT;
        /// <summary>지금 고정해 운반 중인 상품 전부</summary>
        public IReadOnlyList<ClawItem> HeldItems => held;
        public int HeldCount => held.Count;
        /// <summary>이번 잡기에서 다 올라갔을 때 집게 안에 남은 수(셀프테스트·로그)</summary>
        public int LastCarried { get; private set; }
        ClawFingers fingers;
        public float AscendSpeed = 0.8f;
        public float TravelSpeed = 0.7f;

        const float DwellSec = 0.12f, AttachSec = 0.45f, RecoverSec = 0.2f, RecoilHoldSec = 0.6f;
        /// <summary>물리 집게: 오므리기 최대 시간(발이 다 멈추면 더 일찍) · 다 멈춘 뒤 잠깐 쥔 채 · 오므린 발끝이 상품 밑면보다 이만큼 아래</summary>
        const float GripMaxSec = 0.9f, GripSettleSec = 0.08f, GripClear = 0.012f;
        // 10-22 사용자 "상품을 실제로 쥐고 올리기 · 움직임 타이밍·흔들림 · 배출구에서 떨어뜨리기"
        const float AscendMinSec = 0.5f, ReturnMinSec = 0.35f, DropLowerSec = 0.2f;   // 부드럽게(smoothstep) 오르내리는 최소 시간
        const float ReleaseDelaySec = 0.12f;           // 배출구에서 발이 벌어진 뒤 상품을 놓기까지
        const float BounceAmp = 0.010f, BounceHz = 3.5f, BounceTau = 0.12f; // 바닥에서 스프링 출렁임(아래로 1cm)
        const float LiftKick = 0.35f;                  // 들어 올릴 때 무게로 흔들림(rad/s, 질량 0.05kg 기준)
        const float FallBounce = 0.28f, FallRestSec = 0.35f, FallVanishSec = 0.18f; // 배출구 바닥에서 한 번 튀고 쉬었다 사라짐
        /// <summary>하강 정지 여유(집게 하단 ~ 최상단 윗면) / 폭탄 접촉 판 두께(정지 여유보다 두꺼워 정지 높이를 정한 폭탄은 반드시 닿음)</summary>
        const float StopGap = 0.01f, TouchSkin = 0.025f;
        const float Zeta = 0.5f;
        const float GrabSettleTau = 0.06f;   // 잡기 하강 중 흔들림 감쇠 시간 상수(0.2초면 약 4%로)
        static readonly float ThetaMax = 6f * Mathf.Deg2Rad;
        public ClawItemSpec Spec => spec != null ? spec : basket != null ? basket.Spec : ClawItemSpec.Default;

        public Phase Current { get; private set; } = Phase.Idle;
        public bool Busy => Current != Phase.Idle;
        /// <summary>Playing·조준 중에만 true — 유휴 시 가이드(원뿔·링·발광) 표시</summary>
        public bool GuideEnabled { get; set; }
        /// <summary>쥔 상품(여러 개면 가리킨 것) — 없으면 null</summary>
        public ClawItem Holding => held.Count > 0 ? held[0] : null;
        /// <summary>조준 중 지금 잡기를 누르면 잡힐 아이템(가이드 대상과 같음)</summary>
        public ClawItem Candidate { get; private set; }

        /// <summary>하강 끝 — 잡은 아이템(null = 빈손). 폭탄이면 불리지 않는다(OnBombTouched)</summary>
        public event Action<ClawItem> OnCaptured;
        /// <summary>배출구 투입 완료 — 풀 반환 직전(종류 읽기 가능). 구독자는 아이템을 보관하지 말 것</summary>
        public event Action<ClawItem> OnDelivered;
        /// <summary>집게가 폭탄에 닿음(폭탄, 닿은 위치) — 이벤트 뒤 폭탄은 풀로 반환된다</summary>
        public event Action<ClawItem, Vector3> OnBombTouched;
        public event Action OnCycleEnded;

        Vector2 theta, omega;     // 진자 각(rad): x=측면(BoothRoot x), y=전후(z)
        float hang;               // 현재 케이블 길이
        float hangTarget;
        float phaseT;
        float recoverT;
        bool emptyDescent;
        int itemMask, topMask;
        readonly Collider[] touchBuf = new Collider[16];

        void Awake()
        {
            hang = ClawLayout.CableLength;
            itemMask = LayerMask.GetMask("ClawItem");
            if (itemMask == 0) itemMask = ~0; // 레이어 미생성 시 전체(아이템만 콜라이더가 있으므로 실질 동일)
            topMask = itemMask | LayerMask.GetMask("ClawBed"); // 10-20 하강 정지 = 아이템 또는 받침 더미 윗면(폭탄 접촉은 아이템만)
            if (cable != null) cable.positionCount = 2;
            fingers = GetComponent<ClawFingers>();
        }

        Transform Space => carriage != null && carriage.transform.parent != null ? carriage.transform.parent : transform.parent;

        Vector3 HeadBottomWorld => transform.position + Vector3.down * ClawLayout.HeadHalfHeight;

        /// <summary>캐리지 바로 아래(집게 원반 반경) 최상단 아이템 윗면 월드 높이. 없으면 false</summary>
        bool TopUnder(out float topY, out ClawItem topItem)
        {
            topY = float.MinValue;
            topItem = null;
            if (carriage == null) return false;
            if (!Physics.SphereCast(carriage.transform.position, ClawLayout.StopProbeRadius, Vector3.down, out var hit,
                                    ClawLayout.ProbeDistance, topMask, QueryTriggerInteraction.Ignore))
                return false;
            topItem = hit.collider.GetComponentInParent<ClawItem>();
            topY = topItem != null ? Mathf.Max(hit.collider.bounds.max.y, topItem.TopWorldY) : hit.collider.bounds.max.y;
            return true;
        }

        /// <summary>
        /// 지금 이 자리에서 잡기를 누르면 일어날 일의 대상 — 내려가다 폭탄에 닿으면 그 폭탄(가이드가 폭탄을 가리켜 미리 보이게),
        /// 아니면 잡힐 아이템(최상단 윗면에서 GrabReach 안쪽, 수평으로 가장 가까운 것).
        /// </summary>
        public ClawItem PickCandidate(out float topY)
        {
            topY = float.MinValue;
            candidateGrab = null;
            if (basket == null || carriage == null || !TopUnder(out topY, out _)) return null;
            var c = carriage.transform.position;
            float natural = topY + StopGap;
            float tip = Mathf.Max(natural, GuardTipY());   // 멈출 발끝 높이 — BeginGrab과 같은 식
            var s = Spec;
            if (fingers != null && fingers.HasRods)
            {
                // 10-22 물리 집게: 잡을 것 → 그 쥐는 깊이까지 펼친 채 내려가 끝까지 오므릴 때 발 막대가 지나가는 폭탄이 있으면 그 폭탄(닿는 순간 폭발)
                var pick = basket.PickUnder(PickCenter(), s.GrabRadius, tip - StopGap - s.GrabReach - GuardExtraReach(tip, natural));
                if (pick == null || pick.ItemKind == ClawItem.Kind.Bomb) return pick;
                candidateGrab = pick;
                float stop = Mathf.Max(Mathf.Min(natural, GripTip(pick, natural)), GuardTipY());
                var bombHit = fingers.SweepBomb(new Vector3(c.x, stop + ClawLayout.HeadHalfHeight, c.z), transform.position.y, basket.PoolItems);
                return bombHit != null ? bombHit : pick;
            }
            var bomb = BombUnder(new Vector3(c.x, tip, c.z)); // 멈출 높이에서의 접촉 판
            if (bomb != null) return bomb;
            return basket.PickUnder(PickCenter(), s.GrabRadius, tip - StopGap - s.GrabReach - GuardExtraReach(tip, natural));
        }

        /// <summary>
        /// 잡기 판정 중심(부스 로컬 xz) = 캐리지 바로 아래. 10-22 한때 레일 끝에서 벽 쪽으로 밀었지만(벽 상품 잡기), 물리 집게는 가운데에 오지 않은
        /// 상품을 발 사이 틈으로 놓쳐 가이드가 못 잡을 것을 가리키게 됐다 — 사용자 결정 "벽 상품은 그대로(진짜 기계처럼)"로 되돌림
        /// </summary>
        public Vector2 PickCenter() => carriage != null ? carriage.LocalXZ : Vector2.zero;

        /// <summary>
        /// 칸막이 때문에 원래보다 높이 멈추면 그만큼(최대 GuardExtraReachMax) 더 아래까지 집는다 — 배출구 옆 낮은 그릇도 잡히게(10-20).
        /// 흡착 연출(Attach)이 그 거리만큼 더 끌어올린다
        /// </summary>
        static float GuardExtraReach(float tip, float natural) => Mathf.Clamp(tip - natural, 0f, GuardExtraReachMax);
        const float GuardExtraReachMax = 0.08f;
        float extraReach;   // 이번 하강의 GuardExtraReach(Dwell 잡기 판정이 쓴다)

        /// <summary>
        /// 집게(펼친 발 포함 = 캐리지 여유 반경)가 배출구 칸에 걸치면 발끝이 내려갈 수 있는 가장 낮은 월드 높이 = 아크릴 칸막이 위 1cm. 아니면 −∞.
        /// 집게에는 콜라이더가 없어 이 제한이 없으면 발이 칸막이를 뚫고 보인다(10-20 — 4차는 빈손 하강만·벽 0.22). 조준 예측과 하강이 같이 쓴다.
        /// 10-29 "집게가 안 내려가는 자리"의 원인: 칸막이 위 끝이 더미 기준 바닥 +0.59라 칸 옆(앞-왼쪽 약 1/4)에서 쉬는 높이부터 약 10cm만 내려갔다
        /// → 칸막이를 상자 벽 + 3cm(ClawLayout.ChuteGuardTopY, 바닥 +0.25)로 낮춰 발이 실제로 걸리는 높이까지만 막는다
        /// </summary>
        float GuardTipY()
        {
            if (carriage == null || !StopAtChuteGuard) return float.MinValue; // 10-29(2) 기본 끔 — 벽에 닿아도 바닥까지
            var cxz = carriage.LocalXZ;
            if (cxz.x >= ClawLayout.ChuteWallX + ClawLayout.CarriageMargin || cxz.y >= ClawLayout.ChuteWallZ + ClawLayout.CarriageMargin) return float.MinValue;
            var pts = fingers != null && fingers.HasRods ? fingers.OpenClawPoints : null;
            if (pts != null && pts.Count > 0 && basket != null)
            {
                // 10-22 물리 집게: 실제 발 막대·허브 점 중 배출구 칸(칸막이 + 여유) 위에 오는 것만 칸막이 위 1cm에 맞춘다 — 발 세 개 사이 틈이면 더 내려간다
                const float pad = 0.012f;
                float minHead = float.MinValue;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    if (cxz.x + p.x < ClawLayout.ChuteWallX + pad && cxz.y + p.z < ClawLayout.ChuteWallZ + pad)
                        minHead = Mathf.Max(minHead, ClawLayout.ChuteGuardTopY + 0.01f - p.y);
                }
                if (minHead == float.MinValue) return float.MinValue;
                return basket.transform.TransformPoint(new Vector3(0f, minHead - ClawLayout.HeadHalfHeight, 0f)).y;
            }
            float guardY = basket != null ? basket.transform.TransformPoint(new Vector3(0f, ClawLayout.ChuteGuardTopY, 0f)).y
                         : carriage.transform.position.y - (ClawLayout.RailY - ClawLayout.ChuteGuardTopY);
            return guardY + 0.01f;
        }

        [Tooltip("10-29(2) 사용자 \"집게가 배출구 벽에 닿으면 내려가지 않는데, 벽에 닿아도 물건 집으러 갈 수 있게\": false(기본) = 배출구 칸막이·상자 벽 위에서 멈추지 않고 어디서나 바닥(또는 아래 상품)까지 — 발이 벽을 뚫어 보이는 것은 허용(사용자 결정). 발 막대(ClawFinger 레이어)는 원래 상품하고만 충돌(ClawBasket 충돌 행렬). true = 예전처럼 칸막이 위 1cm에서 멈춤")]
        public bool StopAtChuteGuard = false;

        /// <summary>10-29 셀프테스트(격자 하강): 지금 캐리지 자리의 배출구 칸막이 하강 제한(발끝 월드 높이, 없으면 float.MinValue)</summary>
        public float ChuteGuardLimitY => GuardTipY();

        /// <summary>잡기 시작(Idle에서만). 아래에 아이템이 없으면 바닥 근처까지 빈손 하강.</summary>
        public bool BeginGrab()
        {
            if (Current != Phase.Idle || carriage == null) return false;
            carriage.ManualLocked = true;
            float railY = carriage.transform.position.y;
            float tip;
            if (TopUnder(out float topY, out _))
            {
                emptyDescent = false;
                tip = topY + StopGap;
            }
            else
            {
                emptyDescent = true;
                float floorY = basket != null ? basket.transform.TransformPoint(new Vector3(0f, ClawLayout.FloorY, 0f)).y : railY - (ClawLayout.RailY - ClawLayout.FloorY);
                tip = floorY + 0.03f;
            }
            float natural = tip;
            // 10-22 실제로 쥐기: 가리킨 상품(폭탄 아님) 밑면 조금 아래까지 내려가 발 갈고리가 밑으로 들어가게(물리로 받쳐 든다)
            var want = Candidate != null && Candidate.ItemKind == ClawItem.Kind.Bomb ? candidateGrab : Candidate;
            grabTarget = !emptyDescent && want != null && want.ItemKind != ClawItem.Kind.Bomb && want.gameObject.activeSelf && !want.Held ? want : null;
            // 10-22 물리 집게: 가이드가 폭탄 자체를 가리켰으면(가장 가까운 것) 그 폭탄을 쥐는 깊이까지 — 오므리는 발이 닿아 폭발(가리킨 것 = 일어날 일)
            var depthTarget = grabTarget != null ? grabTarget
                            : !emptyDescent && Candidate != null && Candidate.ItemKind == ClawItem.Kind.Bomb && candidateGrab == null && Candidate.gameObject.activeSelf ? Candidate : null;
            if (depthTarget != null) tip = Mathf.Min(tip, GripTip(depthTarget, natural));
            aimedBomb = depthTarget != null && depthTarget.ItemKind == ClawItem.Kind.Bomb ? depthTarget : null;
            tip = Mathf.Max(tip, GuardTipY());
            extraReach = emptyDescent ? 0f : GuardExtraReach(tip, natural);
            hangTarget = railY - tip - ClawLayout.HeadHalfHeight;
            hangTarget = Mathf.Max(hangTarget, ClawLayout.CableLength);
            // 10-29 하강 시간 = 실제 거리 ÷ DescendSpeed(더미 모드만 10-20처럼 높이와 관계없이 DescendSeconds) — 10-22 처음·끝을 부드럽게(smoothstep)
            float descSec = DescendSecondsFor(hangTarget - hang);
            LastDescendSeconds = descSec;
            BeginMove(hangTarget, descSec);
            if (basket != null) basket.HoldPromoted(Candidate, descSec + DwellSec + GripMaxSec + 0.5f); // 10-20 겨눈 받침 승격본을 잡을 때까지 유지
            Candidate = null;
            if (guide != null) guide.Hide();
            Set(Phase.Descend);
            return true;
        }

        /// <summary>게임 종료 등 — 들고 있던 것은 현 위치에서 놓아(바스켓 안으로 낙하) 헤드는 돌아온다. Recoil은 끝까지.</summary>
        public void Abort()
        {
            homeAfterReturn = false; // 10-29 게임 종료 등 — 가운데 복귀도 멈춤(캐리지는 그 자리에)
            for (int i = 0; i < held.Count; i++)
            {
                var item = held[i];
                if (item == null) continue;
                item.DropBack();
                // 10-20: 배출구 기둥 위(운반·내림 중)에서 놓치면 보이지 않는 기둥과 겹친다 — 정상 배달이 아니므로 더미 위로 되돌린다
                if (basket != null && basket.OverChuteColumn(item.transform.position, item.HalfExtent)) basket.RespawnOnPile(item);
            }
            ClearHeld();
            if (guide != null) { guide.EndLaser(); guide.Hide(); }
            if (carriage != null) carriage.StopAuto();
            if (Current != Phase.Idle && Current != Phase.Recoil) { recoverT = 0f; moveDur = 0f; Set(Phase.Return); }
        }

        /// <summary>재정렬(4차) — 흡착/낙하 연출용 월드 스냅샷 좌표를 BoothRoot 델타로 함께 이동(재정렬 중 미끄러짐 방지).</summary>
        public void ApplyWorldDelta(Matrix4x4 delta)
        {
            if (fingers != null) fingers.SnapBodies();
            float dy = delta.MultiplyPoint3x4(Vector3.zero).y; // 부스 높이 변화(보통 0)
            for (int i = 0; i < fallers.Count; i++)
            {
                var f = fallers[i];
                if (f.item != null)
                    f.item.transform.SetPositionAndRotation(delta.MultiplyPoint3x4(f.item.transform.position), delta.rotation * f.item.transform.rotation);
                f.vel = delta.rotation * f.vel;
                f.target = delta.MultiplyPoint3x4(f.target);
                f.floorY += dy;
                fallers[i] = f;
            }
        }

        /// <summary>리셋 — 즉시 유휴 상태로(들고 있던 것은 풀 반환).</summary>
        public void ResetHead()
        {
            foreach (var it in held) if (it != null) it.ReleaseToPool();
            ClearHeld();
            foreach (var f in fallers) if (f.item != null) f.item.ReleaseToPool();
            fallers.Clear();
            grabTarget = null;
            homeAfterReturn = false;
            bounceT = -1f; hangBounce = 0f;
            Candidate = null;
            hang = ClawLayout.CableLength;
            theta = Vector2.zero;
            omega = Vector2.zero;
            recoverT = 0f;
            if (carriage != null) { carriage.StopAuto(); carriage.ManualLocked = false; }
            if (guide != null) { guide.EndLaser(); guide.Hide(); }
            Current = Phase.Idle;
            ApplyPose();
            if (fingers != null) fingers.ResetFingers();
        }

        void Set(Phase p)
        {
            Current = p;
            phaseT = 0f;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f || carriage == null) return;

            // ── 진자(반암시적 오일러): 캐리지 가속도가 케이블을 흔든다 ──
            float L = ClawLayout.CableLength;
            float w0 = Mathf.Sqrt(9.81f / L);
            Vector2 a = carriage.LocalAcceleration;
            omega += (-w0 * w0 * theta - 2f * Zeta * w0 * omega - a / L) * dt;
            theta += omega * dt;
            float mag = theta.magnitude;
            if (mag > ThetaMax) { theta *= ThetaMax / mag; omega *= 0.5f; }
            // 10-19: 잡기 하강 중에는 흔들림을 빠르게 잦아들게 — 진자 각이 남은 채 케이블이 길어지면 집게가 옆으로 최대 약 10cm
            // (각 6° × 내려간 길이) 비껴, 조준 예측(캐리지 바로 아래)에 없던 옆 폭탄에 닿았다(StartUpPark 원본 집게에서 확인). 실제 기계처럼 곧게 내려간다
            if (Current == Phase.Descend || Current == Phase.Dwell || Current == Phase.Attach)
            {
                float settle = Mathf.Exp(-dt / GrabSettleTau);
                theta *= settle;
                omega *= settle;
            }

            phaseT += dt;
            switch (Current)
            {
                case Phase.Idle:
                    TickGuide();
                    break;

                case Phase.Descend:
                    {
                        float k = TickMove();
                        ApplyPose();
                        if (TryBombTouch()) break;
                        if (k >= 1f) { bounceT = 0f; Set(Phase.Dwell); } // 바닥에서 스프링 출렁임
                    }
                    break;

                case Phase.Dwell:
                    if (phaseT >= DwellSec)
                    {
                        ApplyPose();
                        if (TryBombTouch()) break;
                        // 가이드가 폭탄을 가리켰으면(집게 안 가장 가까운 것) 닿는 순간 — 그대로 폭발(가리킨 것 = 일어날 일)
                        if (!emptyDescent && basket != null && !StillGrabbable(grabTarget))
                        {
                            float minTop = HeadBottomWorld.y - StopGap - Spec.GrabReach - extraReach;
                            var pick = basket.PickUnder(PickCenter(), Spec.GrabRadius, minTop);
                            if (pick != null && pick.ItemKind == ClawItem.Kind.Bomb) { Detonate(pick); break; }
                        }
                        // 10-22 겨눈 폭탄(가이드가 가리킨 것)에 닿는 깊이까지 왔다 — 둘레 캡슐에 발이 먼저 막혀도 폭발(가리킨 것 = 일어날 일)
                        if (aimedBomb != null && aimedBomb.gameObject.activeSelf && !aimedBomb.Held)
                        {
                            var d = aimedBomb.transform.position - transform.position;
                            if (new Vector2(d.x, d.z).magnitude <= Spec.GrabRadius) { var b = aimedBomb; aimedBomb = null; Detonate(b); break; }
                        }
                        // 오므리는 발이 지나갈 폭탄(조준 가이드가 미리 보여 준 것) — 옆 상품에 먼저 막혀도 그대로 폭발
                        if (fingers != null && fingers.HasRods && basket != null)
                        {
                            var swept = fingers.SweepBomb(transform.position, transform.position.y, basket.PoolItems);
                            if (swept != null) { Detonate(swept); break; }
                        }
                        // 10-22 물리 잡기: 발마다 오므리다 닿으면 멈춤(ClawFingers) — 상품은 물리 그대로
                        if (fingers != null)
                            fingers.BeginGrip(grabTarget != null && grabTarget.gameObject.activeSelf && grabTarget.Visual != null ? grabTarget.Visual.bounds.min.y + 0.005f : float.NegativeInfinity,
                                              grabTarget != null && grabTarget.gameObject.activeSelf ? grabTarget : null);
                        Set(Phase.Attach);
                    }
                    break;

                case Phase.Attach:
                    {
                        if (fingers != null && fingers.TouchedBomb != null)
                        {
                            var bomb = fingers.TouchedBomb;
                            fingers.TouchedBomb = null;
                            if (bomb.gameObject.activeSelf && !bomb.Held) { Detonate(bomb); break; }
                        }
                        bool settled = fingers == null || fingers.BodyCount == 0 || fingers.GripSettled;
                        if (settled && settleT < 0f) settleT = phaseT;
                        if ((settled && phaseT - settleT >= GripSettleSec) || phaseT >= GripMaxSec)
                        {
                            settleT = -1f;
                            GripSqueezed();
                            StartAscend();
                        }
                    }
                    break;

                case Phase.Ascend:
                    if (TickMove() >= 1f)
                    {
                        ApplyPose();
                        LockCarried();
                        if (Current != Phase.Ascend) break; // 집게 안 폭탄 → 폭발(Recoil)
                        if (Holding != null)
                        {
                            carriage.StartAutoMove(new Vector2(ClawLayout.ChuteLocal.x, ClawLayout.ChuteLocal.z), TravelSpeed);
                            Set(Phase.Travel);
                        }
                        else { recoverT = RecoverSec; moveDur = 0f; Set(Phase.Return); }
                    }
                    break;

                case Phase.Travel:
                    if (carriage.AutoArrived)
                    {
                        carriage.StopAuto();
                        hangTarget = L + ClawLayout.DropLowerDepth; // 잡힌 상품 바닥이 아크릴 칸막이 위에 남게(10-20 0.05 → 0.03)
                        BeginMove(hangTarget, DropLowerSec);
                        Set(Phase.DropLower);
                    }
                    break;

                case Phase.DropLower:
                    if (TickMove() >= 1f)
                    {
                        ApplyPose();
                        FollowHeld();
                        Set(Phase.Drop);
                    }
                    break;

                case Phase.Drop:
                    // 10-22 발이 벌어지고(ClawFingers Drop = 펼침) 잠깐 뒤 상품을 놓는다 → 중력으로 떨어져 배출구 바닥에서 한 번 튀고 사라짐(TickFalling).
                    // 예전: 작아지며 빨려 들어감. 헤드는 놓자마자 돌아간다
                    FollowHeld();
                    if (phaseT >= ReleaseDelaySec)
                    {
                        for (int i = 0; i < held.Count; i++) if (held[i] != null) Release(held[i]);
                        homeAfterReturn = held.Count > 0; // 10-29 배출했으면 쉬는 높이로 돌아온 뒤 가운데로
                        ClearHeld();
                        recoverT = RecoverSec;
                        BeginMove(L, Mathf.Max(ReturnMinSec, (hang - L) / Mathf.Max(0.05f, AscendSpeed)));
                        Set(Phase.Return);
                    }
                    break;

                case Phase.Recoil:
                    hang = Mathf.MoveTowards(hang, L, AscendSpeed * 1.5f * dt);
                    if (Mathf.Approximately(hang, L) && phaseT >= RecoilHoldSec)
                    {
                        carriage.ManualLocked = false;
                        Set(Phase.Idle);
                        EventUtil.SafeInvoke(OnCycleEnded);
                    }
                    break;

                case Phase.Return:
                    {
                        if (moveDur <= 0f) BeginMove(L, Mathf.Max(ReturnMinSec, (hang - L) / Mathf.Max(0.05f, AscendSpeed)));
                        float k = TickMove();
                        if (recoverT > 0f) recoverT -= dt;
                        if (k < 1f || recoverT > 0f) break;
                        moveDur = 0f;
                        if (homeAfterReturn) { homeAfterReturn = false; StartHome(); break; } // 10-29 배출 뒤 → 가운데로(아직 Busy·잠금)
                        carriage.ManualLocked = false;
                        Set(Phase.Idle);
                        EventUtil.SafeInvoke(OnCycleEnded);
                    }
                    break;

                case Phase.Home:
                    // 10-29 가운데 도착 → 조준으로. 빈손·폭발 사이클은 여기 오지 않는다(제자리에서 조준)
                    if (carriage.AutoArrived)
                    {
                        carriage.StopAuto();
                        carriage.ManualLocked = false;
                        Set(Phase.Idle);
                        EventUtil.SafeInvoke(OnCycleEnded);
                    }
                    break;
            }

            TickBounce(dt);
            TickFalling(dt);
            ApplyPose();
            if (Current == Phase.Ascend || Current == Phase.Travel || Current == Phase.DropLower) FollowHeld();
            if (Current == Phase.Travel || Current == Phase.DropLower) lockT += dt;
        }

        /// <summary>10-29 배출 뒤 돌아갈 캐리지 자리(부스 로컬 xz) = 리셋 때 자리(기계 가운데)</summary>
        public static Vector2 HomeXZ => new Vector2(0f, ClawLayout.CenterZ);

        /// <summary>
        /// 10-29 사용자 "물건을 집어 배출구에 넣은 뒤 집게가 배출구 위에 멈춰 있지 말고 항상 가운데로 자동 이동": 쉬는 높이로 돌아온 뒤
        /// 배출구로 갈 때와 같은 자동 이동(TravelSpeed)으로 가운데까지 — 그동안 Busy(잡기 회색)·ManualLocked(조이스틱 무시)
        /// </summary>
        void StartHome()
        {
            var from = carriage.LocalXZ;
            carriage.StartAutoMove(HomeXZ, TravelSpeed);
            Debug.Log($"[Claw] 배출 뒤 가운데 복귀 ({from.x:F2}, {from.y:F2}) → ({HomeXZ.x:F2}, {HomeXZ.y:F2}) {Vector2.Distance(from, HomeXZ):F2}m · {TravelSpeed:F2}m/s");
            Set(Phase.Home);
        }

        void TickGuide()
        {
            if (guide == null) return;
            if (!GuideEnabled) { Candidate = null; guide.Hide(); return; }
            Candidate = PickCandidate(out float topY);
            // 10-22 S2 흰색(은은한 스포트라이트 + 옅은 원뿔): 집게 몸통에서 대상 허리(없으면 아래 표면)까지 — 잡기 범위를 비춘다
            if (Candidate != null) guide.ShowSpot(Candidate, transform.position, Candidate.transform.position.y);
            else if (topY > float.MinValue) guide.ShowSpot(null, transform.position, topY - 0.03f);
            else guide.Hide();
        }

        /// <summary>집게 하단 얇은 판 안에 폭탄이 있으면 폭발 처리(true). 하강 중 매 프레임 + 하강 끝</summary>
        bool TryBombTouch()
        {
            // 10-22 물리 집게: 발 막대에 닿은 폭탄(하강·바닥 대기) — 막대가 없으면 예전 헤드 밑 판
            var bomb = fingers != null && fingers.HasRods ? fingers.BombTouchingNow(basket != null ? basket.PoolItems : null) : BombUnder(HeadBottomWorld);
            if (bomb == null) return false;
            Detonate(bomb);
            return true;
        }

        /// <summary>집게 하단(headBottom) 아래 두께 TouchSkin 판(원반 반경 0.8)에 걸친 폭탄 — 조준 예측(PickCandidate)과 실제 접촉이 같은 판을 쓴다</summary>
        ClawItem BombUnder(Vector3 headBottom)
        {
            var rot = Space != null ? Space.rotation : Quaternion.identity;
            float r = ClawLayout.HeadRadius * 0.8f;
            var center = headBottom + Vector3.down * (TouchSkin * 0.5f - 0.005f);
            int n = Physics.OverlapBoxNonAlloc(center, new Vector3(r, TouchSkin * 0.5f + 0.005f, r), touchBuf, rot, itemMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var it = touchBuf[i] != null ? touchBuf[i].GetComponentInParent<ClawItem>() : null;
                if (it != null && !it.Held && it.gameObject.activeSelf && it.ItemKind == ClawItem.Kind.Bomb) return it;
            }
            return null;
        }

        void Detonate(ClawItem bomb)
        {
            var pos = bomb.transform.position;
            Debug.Log($"[Claw] 폭탄 접촉 — {Current}에서 폭발 ({bomb.name})");
            EventUtil.SafeInvoke(OnBombTouched, bomb, pos); // 위치·종류를 읽은 뒤 반환
            bomb.ReleaseToPool();
            if (guide != null) { guide.EndLaser(); guide.Hide(); }
            // 튕겨 오르는 느낌: 진자에 충격
            var kick = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)).normalized;
            omega += kick * 1.5f;
            Set(Phase.Recoil);
        }

        /// <summary>고정한 상품을 헤드에 붙여 따라가게 — 고정 직후 0.25초 동안 높이 여유(heldLift)만큼 부드럽게 끌어올림</summary>
        void FollowHeld()
        {
            float k = Mathf.Clamp01(lockT / 0.25f);
            float e = k * k * (3f - 2f * k);
            for (int i = 0; i < held.Count; i++)
            {
                if (held[i] == null) continue;
                var w = transform.TransformPoint(heldLocalPos[i]) + Vector3.up * (heldLift[i] * e);
                held[i].transform.SetPositionAndRotation(w, transform.rotation * heldLocalRots[i]);
            }
        }

        void ClearHeld()
        {
            held.Clear();
            heldLocalPos.Clear();
            heldLocalRots.Clear();
            heldLift.Clear();
        }

        /// <summary>
        /// 집게 안 우리(헤드 로컬): 축에서 HeadRadius + 3cm 안, 발끝 아래 CageBelow ~ 발끝 위 CageAbove — 다 올라간 높이에서 여기 있으면
        /// 물리로 딸려 온 것(더미 꼭대기는 쉬는 발끝보다 약 12cm 아래라 더미 위 상품은 안 들어온다)
        /// </summary>
        bool InCage(ClawItem it)
        {
            var lp = transform.InverseTransformPoint(it.transform.position);
            float tipY = -ClawLayout.HeadHalfHeight;
            return new Vector2(lp.x, lp.z).sqrMagnitude <= (ClawLayout.HeadRadius + 0.03f) * (ClawLayout.HeadRadius + 0.03f)
                && lp.y >= tipY - CageBelow && lp.y <= tipY + CageAbove;
        }
        const float CageBelow = 0.10f, CageAbove = 0.22f;

        /// <summary>
        /// 다 올라갔을 때 집게 안에 남은 실물(물리로 딸려 온 것 — 개수 제한 없음)을 지금 자세 그대로 헤드에 고정한다(운반·배출구는 연출).
        /// 폭탄이 같이 올라왔으면 그 자리에서 폭발. 상품 밑면이 운반 높이(TravelBottomY)보다 낮으면 그만큼 살짝 끌어올린다(칸막이 위 여유)
        /// </summary>
        /// <summary>쥔 목록에 지금 자세 그대로 붙인다(헤드 로컬) — 순간 이동 없음</summary>
        void AddHeld(ClawItem it)
        {
            held.Add(it);
            heldLocalPos.Add(transform.InverseTransformPoint(it.transform.position));
            heldLocalRots.Add(Quaternion.Inverse(transform.rotation) * it.transform.rotation);
            heldLift.Add(0f);
            it.BeginHeld();
        }

        readonly List<ClawItem> gripCand = new(12);
        readonly List<ClawItem> toHold = new(8);
        static float HorizSq(ClawItem it, Vector3 ax) { var d = it.transform.position - ax; return d.x * d.x + d.z * d.z; }

        /// <summary>toHold를 쥔다 — 그 전에(충돌체가 켜져 있을 때) 안 닿은 발이 마저 오므릴 각도를 구한다(ClawFingers.SnugTo)</summary>
        void HoldAll()
        {
            if (fingers != null) fingers.SnugTo(toHold);
            foreach (var it in toHold) AddHeld(it);
            toHold.Clear();
        }
        [Tooltip("10-22 쥐는 힘 보조(사용자 '약하게'): 발 하나만 닿은 덩어리도, 닿은 상품이 집게 안쪽(축에서 HeadRadius × 이 값 안)이면 쥔다. 0 = 순수 물리(두 발 이상만)")]
        public float GripAssistInner = 0.6f;
        [Tooltip("10-22 같이 딸려 오는 범위(같은 높이 이상, 쥔 덩어리에 맞닿은 것): 축에서 HeadRadius × 이 값 안. 사용자 '많으면 4~5개' — 클수록 많이")]
        public float GripTogetherReach = 1.0f;
        [Tooltip("10-22 아래층(잡을 것을 받친 캡슐)도 갈고리가 떠올리는 범위 — 축에서 HeadRadius × 이 값 안, 발끝 아래 GripScoopBelow(m)까지")]
        public float GripScoopReach = 0.75f;
        [Tooltip("10-22 뼈 집게(갈고리가 상품 아래쪽 옆면을 감쌈 — GripSideAim)에 맞춤: 0.035 평균 1.9·최대 3개 → 0.06 평균 2.5·최대 6개(에디터 더미 잡기 각 10~12번)")]
        public float GripScoopBelow = 0.06f;
        [Tooltip("10-22 뼈 집게: 오므리는 갈고리가 상품 가장자리에 올 때 겨눌 높이(밑면에서 상품 높이의 비율) — 아래쪽 옆면을 감싸 쥔다")]
        public float GripSideAim = 0.25f;
        readonly List<int> gripGroup = new(12);

        /// <summary>맞닿음 판정 반 크기 — 큐브 키링은 돌아가 있으면 대각선까지</summary>
        static float ReachR(ClawItem it) => it.HalfExtent * (it.ItemKind == ClawItem.Kind.Keyring ? 1.3f : 1f);

        int FindGroup(int x)
        {
            while (gripGroup[x] != x) { gripGroup[x] = gripGroup[gripGroup[x]]; x = gripGroup[x]; }
            return x;
        }

        /// <summary>
        /// 10-22 오므리기가 끝난 순간: 발이 닿아 멈춘 상품 + 집게 안에서 그것과 맞닿은 상품을 덩어리로 묶어, **두 발 이상이 끼운 덩어리**를 그 자리 그대로 쥔다
        /// (한 발만 닿은 것은 미끄러져 남는다 — 진짜 집게처럼 몇 개든). 쥔 것은 헤드에 고정(물리 끔), 그 위에 얹힌 것 등은 물리 그대로 올라가다 떨어질 수 있다.
        /// 잡을 것 밑(받친 캡슐)과 폭탄은 뺀다. 10-28 쥘 수 있는 것이 여럿이면 그중 하나만(가리킨 것 → 축에 가까운 것)
        /// </summary>
        void GripSqueezed()
        {
            ClearHeld();
            toHold.Clear();
            lockT = 0f;
            if (fingers == null || !fingers.HasRods || basket == null) return;
            var all = basket.PoolItems;
            gripCand.Clear();
            for (int f = 0; f < fingers.FingerCount; f++)
            {
                var it = fingers.StopItem(f);
                if (it != null && it.gameObject.activeSelf && !it.Held && !gripCand.Contains(it)) gripCand.Add(it);
            }
            float floorBelow = grabTarget != null && grabTarget.Visual != null ? grabTarget.Visual.bounds.min.y : float.NegativeInfinity;
            if (gripCand.Count == 0) { CradleAssist(all, floorBelow); return; }
            // 10-22 진짜 맨 윗층(빽빽한 캡슐)에서 "맞닿음 덩어리"가 둘레 캡슐 17개까지 이어졌다 — 같이 쥘 것은 닿은 상품들 밑면보다 낮지 않고,
            // 지금 발끝보다 위(받쳐짐)이며, 그 높이의 발 고리 안쪽인 것만
            float level = float.MaxValue;
            foreach (var si in gripCand) level = Mathf.Min(level, si.Visual != null ? si.Visual.bounds.min.y : si.transform.position.y - si.HalfHeight);
            float tipsY = fingers.TipsWorldY;
            // 발이 거의 안 오므려졌으면(옆에 막혀 평균 −10°보다 벌어짐) 감싸인 것이 없다 — 발에 끼인 것만
            bool wrapped = fingers.Angle <= -10f;
            if (all != null && wrapped)
                for (int i = 0; i < all.Count; i++)
                {
                    var it = all[i];
                    if (!it.gameObject.activeSelf || it.Held || it.ItemKind == ClawItem.Kind.Bomb || it.Body == null || it.Body.isKinematic) continue;
                    if (gripCand.Contains(it) || !InCage(it) || (grabTarget != null && grabTarget.IsUnder(it))) continue;
                    float cy = it.transform.position.y;
                    var dd = it.transform.position - transform.position;
                    float dh = new Vector2(dd.x, dd.z).magnitude;
                    bool lower = cy < level + 0.01f || (grabTarget != null && grabTarget.IsLowerLevel(it));
                    // 발끝보다 아래면 안 받쳐진다 — 단 아래층(받친 캡슐)은 갈고리가 윗부분을 걸어 떠올릴 수 있게 3.5cm 아래까지(사용자 "많으면 4~5개")
                    if (cy < tipsY + (lower ? -GripScoopBelow : 0.01f)) continue;
                    // 10-22 사용자 "많아야 2~3개는 적다 — 많으면 4~5개":
                    //   같은 높이 이상 = 집게 몸통 반지름 안(쥔 덩어리에 맞닿아 있으면 발 사이에 끼여 함께) — 발 고리 안쪽만으로는 많아야 3개
                    //   아래층(받친 캡슐) = 축에서 HeadRadius × GripScoopReach 안·발끝 3.5cm 아래까지만(넓게 이어 붙이면 17개까지 갔다)
                    if (lower ? dh > ClawLayout.HeadRadius * GripScoopReach : dh > ClawLayout.HeadRadius * GripTogetherReach) continue;
                    gripCand.Add(it);
                }
            // 맞닿음으로 덩어리 나누기(중심 거리 < 반 크기 합 × 1.15)
            int n = gripCand.Count;
            gripGroup.Clear();
            for (int i = 0; i < n; i++) gripGroup.Add(i);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float lim = (ReachR(gripCand[i]) + ReachR(gripCand[j])) * 1.15f;
                    if ((gripCand[i].transform.position - gripCand[j].transform.position).sqrMagnitude <= lim * lim)
                        gripGroup[FindGroup(i)] = FindGroup(j);
                }
            // 덩어리마다 닿은 발 수 — 두 발 이상이면 그 덩어리 전부. 보조: 한 발이라도 닿은 상품이 집게 안쪽이면
            float inner = ClawLayout.HeadRadius * GripAssistInner;
            var axis = transform.position;
            for (int i = 0; i < n; i++)
            {
                int root = FindGroup(i), fingersOn = 0;
                bool innerTouch = false;
                for (int f = 0; f < fingers.FingerCount; f++)
                {
                    var si = fingers.StopItem(f);
                    if (si == null) continue;
                    int k = gripCand.IndexOf(si);
                    if (k < 0 || FindGroup(k) != root) continue;
                    fingersOn++;
                    var d = si.transform.position - axis;
                    if (new Vector2(d.x, d.z).sqrMagnitude <= inner * inner) innerTouch = true;
                }
                if (fingersOn >= 2 || (fingersOn == 1 && innerTouch && GripAssistInner > 0f)) toHold.Add(gripCand[i]);
            }
            // 10-28 한 번에 한 개만(사용자 "한 번에 한 개씩만 잡힘"): 가리킨 것이 쥘 수 있으면 그것, 아니면 축에서 수평으로 가장 가까운 것 하나.
            // 안 고른 것은 물리 그대로 남아 떨어진다
            int canHold = toHold.Count;
            if (canHold > 1)
            {
                ClawItem pick = grabTarget != null && toHold.Contains(grabTarget) ? grabTarget : null;
                if (pick == null)
                {
                    var ax = transform.position;
                    float best = float.MaxValue;
                    foreach (var it in toHold) { float d = HorizSq(it, ax); if (d < best) { best = d; pick = it; } }
                }
                toHold.Clear();
                toHold.Add(pick);
            }
            HoldAll();
            if (held.Count > 0) Debug.Log($"[Claw] 하나만 쥠 ({KindsText()}, 쥘 수 있던 {canHold}개) · 발 닿음 {fingers.TouchCount}");
            else
            {
                var sb = new System.Text.StringBuilder("[Claw] 쥔 것 없음 — 발별 닿은 것:");
                for (int f = 0; f < fingers.FingerCount; f++) { var si = fingers.StopItem(f); sb.Append(si != null ? $" {si.name}/{si.ItemKind}" : " -"); }
                sb.Append($" · 후보 {gripCand.Count} · 잡을 것 {(grabTarget != null ? grabTarget.name : "없음")}");
                Debug.Log(sb.ToString());
            }
        }

        /// <summary>
        /// 보조(약하게): 발이 끝까지 오므렸는데 아무것도 안 닿았을 때 — 발끝이 밑으로 들어간 상품(집게 안쪽, 오므린 발끝 바로 위에 밑면)을 받쳐 든다.
        /// 공 모양 캡슐은 발끝이 밑으로 다 지나가 옆에 안 닿는다(10-22 셀프테스트). 가장 가까운 하나만
        /// </summary>
        void CradleAssist(IReadOnlyList<ClawItem> all, float floorBelow)
        {
            if (GripAssistInner <= 0f || all == null || fingers.TouchCount > 0) return;
            float tipsY = HeadBottomWorld.y + fingers.TipLiftForRadius(0f);
            float inner = ClawLayout.HeadRadius * GripAssistInner;
            var axis = transform.position;
            ClawItem best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (!it.gameObject.activeSelf || it.Held || it.ItemKind == ClawItem.Kind.Bomb || it.Body == null) continue;
                if ((grabTarget != null && grabTarget.IsUnder(it)) || !InCage(it)) continue;
                var d = it.transform.position - axis;
                float dh = new Vector2(d.x, d.z).sqrMagnitude;
                if (dh > inner * inner) continue;
                float bottom = it.Visual != null ? it.Visual.bounds.min.y : it.transform.position.y - it.HalfHeight;
                if (bottom < tipsY - 0.02f || bottom > tipsY + 0.06f) continue;
                if (dh < bestD) { bestD = dh; best = it; }
            }
            if (best == null) return;
            toHold.Clear();
            toHold.Add(best);
            HoldAll();
            Debug.Log($"[Claw] 오므린 발끝 위에 받쳐 쥠(보조) 1개 ({best.ItemKind})");
        }

        void LockCarried()
        {
            lockT = 0f;
            LastCarried = 0;
            if (basket == null) return;
            var all = basket.PoolItems;
            if (all == null) return;
            float travelBottom = basket.transform.TransformPoint(new Vector3(0f, ClawLayout.TravelBottomY, 0f)).y;
            int gripped = held.Count;
            // 쥔 것 위에 얹혀 물리로 함께 올라온 것 — 10-28 한 개만: 아무것도 안 쥐었을 때만 집게 안 상품 하나(축에 가까운 것), 이미 쥐었으면 더하지 않음
            ClawItem extra = null;
            float extraD = float.MaxValue;
            var ax = transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (!it.gameObject.activeSelf || it.Held || it.Body == null || it.Body.isKinematic || !InCage(it)) continue;
                if (it.ItemKind == ClawItem.Kind.Bomb) { foreach (var h in held) h.DropBack(); ClearHeld(); Detonate(it); return; }
                if (gripped > 0) continue;
                float d = HorizSq(it, ax);
                if (d < extraD) { extraD = d; extra = it; }
            }
            if (extra != null) AddHeld(extra);
            for (int i = 0; i < held.Count; i++)
            {
                var it = held[i];
                float bottom = it.Visual != null ? it.Visual.bounds.min.y : it.transform.position.y - it.HalfHeight;
                heldLift[i] = Mathf.Max(0f, travelBottom + 0.005f - bottom);
                heldLocalPos[i] = transform.InverseTransformPoint(it.transform.position);
                EventUtil.SafeInvoke(OnCaptured, it);
            }
            if (held.Count > gripped) Debug.Log($"[Claw] 쥔 것 위에 얹혀 함께 올라옴 {held.Count - gripped}개");
            LastCarried = held.Count;
            if (held.Count == 0) EventUtil.SafeInvoke(OnCaptured, (ClawItem)null);
            else Debug.Log($"[Claw] 물리로 {held.Count}개 들어 올림 ({KindsText()}) · 발 닿음 {(fingers != null ? fingers.TouchCount : 0)}");
        }

        string KindsText()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < held.Count; i++) { if (i > 0) sb.Append('·'); sb.Append(held[i].ItemKind); }
            return sb.ToString();
        }

        // ─ 10-22 연출 도우미 ─────────────────────────────────────────
        /// <summary>지금 길이에서 to까지 seconds 동안 smoothstep으로 오르내리기 시작</summary>
        void BeginMove(float to, float seconds)
        {
            hangFrom = hang;
            hangTarget = to;
            moveDur = Mathf.Max(0.05f, seconds);
        }

        /// <summary>오르내림 진행(Set이 phaseT를 0으로 한 단계 안에서). 반환 = 진행률 0~1</summary>
        float TickMove()
        {
            float k = Mathf.Clamp01(phaseT / Mathf.Max(0.05f, moveDur));
            float e = k * k * (3f - 2f * k);
            hang = Mathf.Lerp(hangFrom, hangTarget, e);
            return k;
        }

        /// <summary>들어 올리기 시작 — 부드럽게(최소 0.5초) + 집게 안(발 사이) 상품 무게만큼 흔들림</summary>
        void StartAscend()
        {
            BeginMove(ClawLayout.CableLength, Mathf.Max(AscendMinSec, (hang - ClawLayout.CableLength) / Mathf.Max(0.05f, AscendSpeed)));
            float mass = 0f;
            var pool = basket != null ? basket.PoolItems : null;
            if (pool != null)
                for (int i = 0; i < pool.Count; i++)
                {
                    var it = pool[i];
                    if (it.gameObject.activeSelf && !it.Held && it.Body != null && !it.Body.isKinematic && InCage(it)) mass += it.Body.mass;
                }
            if (mass > 0f)
            {
                var kick = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)).normalized;
                omega += kick * (LiftKick * Mathf.Clamp(mass / 0.05f, 0.5f, 2.5f)); // 여러 개면 무게 합만큼(상한 2.5배)
            }
            Set(Phase.Ascend);
        }

        /// <summary>바닥에 닿은 뒤 스프링 출렁임(케이블이 조금 더 늘었다 돌아옴)</summary>
        void TickBounce(float dt)
        {
            if (bounceT < 0f) { hangBounce = 0f; return; }
            bounceT += dt;
            hangBounce = BounceAmp * Mathf.Sin(bounceT * BounceHz * 2f * Mathf.PI) * Mathf.Exp(-bounceT / BounceTau);
            if (bounceT > 0.6f) { bounceT = -1f; hangBounce = 0f; }
        }

        /// <summary>
        /// 쥘 헤드 밑면 높이(월드) — 10-22 물리 집게: 오므리는 발끝은 안으로 들어가며 올라가므로(ClawFingers.TipLiftForRadius), 발끝이 상품 폭 30% 반지름까지
        /// 들어왔을 때 상품 밑면보다 GripClear 아래가 되는 깊이 = 갈고리가 밑으로 들어가 받친다(받침 더미는 발과 충돌 없음 — 사이로 파고든다).
        /// 내려가는 기둥(캐리지 축·폭탄 판 반경) 안 폭탄은 윗면 위에서 멈춰 닿지 않는다(폭탄 위 그릇은 그릇만 — 10-15 규칙)
        /// </summary>
        float GripTip(ClawItem target, float natural)
        {
            float top = target.TopWorldY;
            float bottom = target.Visual != null ? target.Visual.bounds.min.y : top - target.Spec.Height(target.ItemKind);
            float floorY = basket != null ? basket.transform.TransformPoint(new Vector3(0f, ClawLayout.FloorY, 0f)).y + 0.01f : float.MinValue;
            // 10-22 뼈 집게: 오므릴수록 갈고리가 거의 수평으로 모이며 조금 내려간다 — 밑면 아래를 겨누면 세 갈고리가 상품 밑을 그냥 지나쳐
            // 아무 발도 안 닿았다(셀프테스트 받침 캡슐 0/3). 발끝이 상품 가장자리(폭/2 + 1cm)에 올 때 밑면에서 높이의 GripSideAim 지점 = 아래쪽 옆면을 감싸 쥔다
            bool rigged = fingers != null && fingers.JointCount > 1;
            float lift = fingers != null ? fingers.TipLiftForRadius(rigged ? target.Width * 0.5f + 0.01f : Mathf.Max(0.02f, target.Width * 0.3f)) : 0.05f;
            float aim = rigged ? bottom + GripSideAim * Mathf.Max(0.01f, top - bottom) : bottom - GripClear;
            float hub = fingers != null ? fingers.HubClearance : ClawLayout.HeadAttachLift;
            // 갈고리가 밑으로 들어갈 깊이 — 단 가운데 몸통(허브)이 상품 윗면을 뚫지 않는 데까지(키 큰 것은 옆을 끼워 쥔다)
            float tip = Mathf.Max(Mathf.Max(aim - lift, top - hub + 0.005f), floorY);
            if (tip >= natural) return natural;
            // 상품 바로 밑(발자국 안) 폭탄 위에서는 멈춘다(폭탄 위 그릇 = 그릇만 — 10-15 규칙). 옆 폭탄은 발 막대 닿음(ClawFingers.SweepBomb)이 정한다
            var all = basket != null ? basket.PoolItems : null;
            if (all != null)
            {
                var tc = target.transform.position;
                for (int i = 0; i < all.Count; i++)
                {
                    var it = all[i];
                    if (it == target || it.ItemKind != ClawItem.Kind.Bomb || it.Held || !it.gameObject.activeSelf) continue;
                    var bc = it.transform.position;
                    float foot = (target.Width + it.Width) * 0.5f * 0.75f;
                    if (new Vector2(bc.x - tc.x, bc.z - tc.z).sqrMagnitude > foot * foot || it.TopWorldY > bottom + 0.03f) continue; // 밑(윗면이 상품 밑면 근처 아래)만
                    // 오므리는 발끝이 폭탄 반지름 안으로 들어올 때 폭탄 윗면보다 8mm 위 — 발이 폭탄에 안 닿는 가장 낮은 곳(예전 +3cm는 납작한 그릇 테두리 위로 발이 지나감)
                    float bl = fingers != null && fingers.HasRods ? fingers.TipLiftForRadius(it.Width * 0.5f + 0.012f) : -TouchSkin;
                    tip = Mathf.Max(tip, it.TopWorldY + 0.008f - bl);
                }
            }
            return Mathf.Min(natural, tip);
        }

        /// <summary>Dwell에서 가리켰던 것을 그대로 잡을 수 있는가(활성·안 들림·잡기 반경 안)</summary>
        bool StillGrabbable(ClawItem it)
        {
            if (it == null || !it.gameObject.activeSelf || it.Held || basket == null) return false;
            var lp = basket.transform.InverseTransformPoint(it.transform.position);
            var c = PickCenter();
            return new Vector2(lp.x - c.x, lp.z - c.y).sqrMagnitude <= Spec.GrabRadius * Spec.GrabRadius * 1.2f;
        }

        /// <summary>배출구에서 놓기 — 물리 없이 직접 떨어뜨린다(배출구 칸은 보이지 않는 기둥 콜라이더라 물리로는 못 들어감)</summary>
        void Release(ClawItem item)
        {
            var chute = basket != null ? basket.ChuteWorld : item.transform.position + Vector3.down * 0.4f;
            // 떨어지며 모이는 자리 = 배출구 가운데 + 집게 안 고리 간격(여러 개가 한 점에 겹쳐 떨어지지 않게)
            var off = item.transform.position - transform.position;
            off.y = 0f;
            fallers.Add(new Faller
            {
                item = item,
                vel = Vector3.zero,
                angVel = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(2f, 4f),
                target = chute + Vector3.ClampMagnitude(off, 0.12f),
                floorY = chute.y + item.HalfHeight,
                landT = 0f,
                landed = false,
            });
        }

        /// <summary>떨어지는 상품: 중력 → 배출구 바닥에서 한 번 튐(닿는 순간 OnDelivered) → 잠깐 쉬었다 작아지며 사라짐 → 풀 반환</summary>
        void TickFalling(float dt)
        {
            for (int i = fallers.Count - 1; i >= 0; i--)
            {
                if (i >= fallers.Count) continue; // OnDelivered → 게임 종료 → ResetHead가 목록을 비웠을 수 있다
                var f = fallers[i];
                if (f.item == null || !f.item.gameObject.activeSelf) { fallers.RemoveAt(i); continue; }
                var t = f.item.transform;
                var pos = t.position;
                bool deliver = false;
                if (!f.landed || Mathf.Abs(f.vel.y) > 0.02f || pos.y > f.floorY + 0.001f)
                {
                    f.vel += Physics.gravity * dt;
                    pos += f.vel * dt;
                    // 배출구 안 자리로 조금씩(벽에 붙어 떨어지지 않게)
                    float pull = 1f - Mathf.Exp(-dt * 3f);
                    pos.x = Mathf.Lerp(pos.x, f.target.x, pull);
                    pos.z = Mathf.Lerp(pos.z, f.target.z, pull);
                    if (pos.y <= f.floorY)
                    {
                        pos.y = f.floorY;
                        if (!f.landed)
                        {
                            f.landed = true;
                            f.vel = new Vector3(0f, -f.vel.y * FallBounce, 0f);
                            deliver = true;
                        }
                        else f.vel = Vector3.zero;
                    }
                    t.rotation = Quaternion.AngleAxis(f.angVel.magnitude * Mathf.Rad2Deg * dt * (f.landed ? 0.3f : 1f), f.angVel.normalized) * t.rotation;
                    t.position = pos;
                }
                if (f.landed)
                {
                    f.landT += dt;
                    if (f.landT >= FallRestSec)
                    {
                        float k = Mathf.Clamp01((f.landT - FallRestSec) / FallVanishSec);
                        f.item.SetShrink(1f - k);
                        if (k >= 1f)
                        {
                            fallers.RemoveAt(i);
                            f.item.ReleaseToPool();
                            continue;
                        }
                    }
                }
                fallers[i] = f;
                if (deliver) EventUtil.SafeInvoke(OnDelivered, f.item); // 바닥에 닿는 순간 획득(목록을 먼저 갱신 — 구독자가 리셋할 수 있다)
            }
        }

        void ApplyPose()
        {
            if (carriage == null) return;
            var space = Space;
            float sx = Mathf.Sin(theta.x), sz = Mathf.Sin(theta.y);
            float c = Mathf.Cos(theta.magnitude);
            float h = hang + hangBounce; // 10-22 바닥 출렁임
            var local = new Vector3(h * sx, -h * c, h * sz);
            var rot = space != null ? space.rotation : Quaternion.identity;
            transform.position = carriage.transform.position + rot * local;
            transform.rotation = rot * Quaternion.Euler(theta.y * Mathf.Rad2Deg, 0f, -theta.x * Mathf.Rad2Deg);
        }

        /// <summary>헤드 상단(케이블 연결점) 월드 좌표</summary>
        public Vector3 HeadTopWorld => transform.position + transform.up * ClawLayout.HeadHalfHeight;

        void LateUpdate()
        {
            if (cableAnchor == null) return;
            var top = HeadTopWorld;
            if (cable != null)
            {
                cable.SetPosition(0, cableAnchor.position);
                cable.SetPosition(1, top);
            }
            if (cableMesh != null)
            {
                var space = Space;
                cableMesh.SetSpan(cableAnchor.position, top, space != null ? space.rotation : Quaternion.identity);
            }
        }
    }
}
