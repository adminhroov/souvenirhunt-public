using Unity.XR.CoreUtils;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 3DoF world-locked 정렬 (씬 구조 03장).
    /// 앱 시작 시(및 Home 버튼 리센터 시) BoothRoot를 카메라 전방 기준으로 정렬한다.
    /// 공간 인식(평면·앵커) 코드 금지 — 회전만 트래킹되는 3DoF 방식.
    /// 11-10 시점 기울기(ClawGame과 같은 시점 — config clawViewTiltDeg, 기본 13.2°): 부스가 아니라 **XR 리그(XROrigin)를 눈을 축으로 숙인다**
    ///   → 고개가 수평일 때 화면이 θ° 아래를 본다. 부스·중력·상품은 그대로(물리 영향 없음). 정렬(Align·TryGetAlignPose)은 기울기를 뺀 전방으로 계산해
    ///   기울어진 채로 불려도 같은 결과. 켜고 끄는 시점은 세션(ClawSessionBase)이 정한다.
    /// 11-10(2) QA "고개를 좌우로 돌리면 화면이 롤로 기울어 보임" — 켤 때 한 번만 그 순간 고개 방향 축으로 숙였기 때문 →
    ///   기울기가 켜진 동안(끄는 0.5초 포함) **매 프레임 Application.onBeforeRender에서 지금 고개 방향(기울기 뺀 평평한 전방) 축으로 다시 숙인다**.
    ///   카메라 = Yaw(고개)·Pitch(θ + 고개 피치)·Roll(고개 롤) — 기울기가 롤을 만들지 않는다. 리그 자세는 매번 기울기 없는 기준 자세(켤 때 저장)에서
    ///   새로 계산(누적 없음 — 넥 모델로 눈 위치가 조금 움직여도 리그가 흘러가지 않음). 0이면 아무것도 안 함.
    /// </summary>
    public class BoothAligner : MonoBehaviour
    {
        [Tooltip("정렬 대상. 미지정 시 이 오브젝트.")]
        [SerializeField] Transform boothRoot;
        [Tooltip("미지정 시 Camera.main.")]
        [SerializeField] Transform cam;
        [Tooltip("카메라 앞 몇 m 지점을 기준점으로 둘지.")]
        [SerializeField] float forwardOffset = 0f;

        /// <summary>정렬 대상 루트(재정렬 전후 델타 계산용 — ClawGameManager.Recenter)</summary>
        public Transform Root => boothRoot != null ? boothRoot : transform;

        // ─ 11-10 시점 기울기(XR 리그 피치) ─
        Transform rig;
        Quaternion rigBaseRot = Quaternion.identity;   // 기울기 없는 리그 회전(기울기 0 → 켤 때 저장 — yaw만일 것)
        Vector3 rigBasePos;                            // 기울기 없는 리그 위치(같은 때 저장)
        Vector3 tiltPivot;                             // 숙이는 축이 지나는 점 = 켤 때의 눈 위치(월드)
        bool tiltApplied;     // 리그가 기준 자세에서 벗어나 있음(숙인 상태)
        float tiltDeg;        // 지금 각(양수 = 아래를 봄) — onBeforeRender가 매 프레임 이 각으로 리그를 놓는다
        float tiltTarget;     // 목표 각
        float tiltRate;       // 목표로 가는 속도(도/초) — 0이면 즉시
        /// <summary>지금 리그에 적용된 시점 기울기(도)</summary>
        public float ViewTiltDeg => tiltDeg;
        /// <summary>시점 기울기 목표(도) — 0이면 끔(또는 끄는 중)</summary>
        public float ViewTiltTarget => tiltTarget;

        void Awake()
        {
            if (boothRoot == null) boothRoot = transform;
        }

        void OnEnable() => Application.onBeforeRender += OnBeforeRenderTilt;

        void OnDisable() => Application.onBeforeRender -= OnBeforeRenderTilt;

        // 트래킹(실기기 3DoF/에디터 시뮬레이터)이 카메라 포즈를 잡을 시간을 준 뒤 정렬 —
        // Start 즉시 정렬하면 시뮬레이터의 눈높이(약 1.36m) 반영 전 값으로 어긋난다.
        System.Collections.IEnumerator Start()
        {
            yield return null;
            yield return null;
            Align();
            // 세션(Main: GameManager.Start가 정렬 전 위치에 이미 스폰 → 재스폰 / Claw: 필요 시 재배치)에 통지
            GameSession.Current?.OnBoothAligned();
        }

        /// <summary>Home 리센터 등에서 호출 (예: 운영자 '리센터' 버튼, SDK 리센터 콜백).</summary>
        public void Align()
        {
            if (!TryGetAlignPose(out var pos, out var rot)) return;
            Root.position = pos;
            Root.rotation = rot;
            // 11-10(2) 기울기 축은 매 프레임 onBeforeRender가 지금 고개 방향으로 갱신 — 여기서 따로 다시 숙일 필요 없음
        }

        /// <summary>10-28 정렬 자세만 계산(적용 안 함) — 기계 다가오기(BoothApproach)가 제자리로 쓴다. 카메라가 없으면 false</summary>
        public bool TryGetAlignPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam == null) return false;

            Vector3 flatForward = UntiltedCamForward(); // 11-10 기울기를 뺀 전방(기울어진 채로 불려도 같은 결과)
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
            flatForward.Normalize();

            Vector3 basePos = cam.position + flatForward * forwardOffset;
            // 부스 원점 = 체험자 눈높이. 콘텐츠(패널·스폰 포인트)는 전부 눈높이 기준으로 저작됨 —
            // 실기기(3DoF, 카메라 y≈0)든 에디터 시뮬레이터(y≈1.36)든 자동으로 맞는다.
            // (리그는 눈을 축으로 숙이므로 기울기와 무관하게 눈 위치는 그대로)
            basePos.y = cam.position.y;
            pos = basePos;
            rot = Quaternion.LookRotation(flatForward, Vector3.up);
            return true;
        }

        // ─ 11-10 시점 기울기 ───────────────────────────────────────

        /// <summary>
        /// 시점 기울기 목표 — deg(0~30, 0 = 끔)로 seconds 동안(0 = 즉시). 켜기는 검정 화면(기계가 드러나기 직전)에서 즉시,
        /// 끄기는 처음 화면으로 갈 때 0.5초 안에 부드럽게(세션이 부름). 로그 [ViewTilt] on θ° / off
        /// </summary>
        public void SetViewTilt(float deg, float seconds = 0f)
        {
            deg = Mathf.Clamp(deg, 0f, 30f);
            if (Mathf.Approximately(deg, tiltTarget) && (seconds > 0f || Mathf.Approximately(deg, tiltDeg))) return;
            bool wasOn = tiltTarget > 0f;
            tiltTarget = deg;
            if (deg > 0f)
            {
                if (cam == null && Camera.main != null) cam = Camera.main.transform;
                var c = cam != null ? cam.GetComponent<Camera>() : null;
                if (c != null && c.farClipPlane < 15f)
                {
                    Debug.Log($"[ViewTilt] 카메라 far {c.farClipPlane:0.#}m → 15m(기계·부스가 멀어짐)");
                    c.farClipPlane = 15f;
                }
                Debug.Log($"[ViewTilt] on {deg:0.0#}°{(seconds > 0f ? $" ({seconds:0.0#}s)" : "")} (far {(c != null ? c.farClipPlane : 0f):0.#}m, 매 프레임 고개 방향 축)");
            }
            else if (wasOn) Debug.Log($"[ViewTilt] off{(seconds > 0f ? $" ({seconds:0.0#}s)" : "")}");
            if (seconds <= 0f) { tiltRate = 0f; tiltDeg = deg; ApplyTilt(deg); } // 즉시 — 이번 프레임부터(이후는 onBeforeRender)
            else tiltRate = Mathf.Abs(deg - tiltDeg) / seconds;
        }

        void Update()
        {
            // 각만 목표로 옮긴다 — 리그는 onBeforeRender가 이 각·지금 고개 방향으로 놓는다
            if (tiltRate <= 0f || Mathf.Approximately(tiltDeg, tiltTarget)) return;
            tiltDeg = Mathf.MoveTowards(tiltDeg, tiltTarget, tiltRate * Time.unscaledDeltaTime);
            if (Mathf.Approximately(tiltDeg, tiltTarget)) { tiltDeg = tiltTarget; tiltRate = 0f; }
        }

        /// <summary>
        /// 11-10(2) 렌더 직전(트래킹 BeforeRender 갱신 뒤) 매 프레임 — 지금 고개 방향 축으로 tiltDeg만큼 다시 숙인다.
        /// 카메라 TrackedPoseDriver(Input System, UpdateAndBeforeRender)는 InputSystem.onAfterUpdate의 BeforeRender 입력 갱신에서 카메라 로컬 자세를 쓰고,
        /// 그 입력 갱신은 Application.onBeforeRender 대리자들보다 먼저 돈다(XRI도 같은 가정으로 onBeforeRender에서 컨트롤러 자세를 읽음) →
        /// 여기서는 이번 프레임 최신 고개 자세를 본다. 순서 1000 = XRI(95~101)·옛 SpatialTracking 드라이버(−30000) 뒤.
        /// 0이고 이미 기준 자세면 아무것도 안 함(할당·Find 없음)
        /// </summary>
        [BeforeRenderOrder(1000)]
        void OnBeforeRenderTilt()
        {
            if (tiltDeg == 0f && !tiltApplied) return;
            ApplyTilt(tiltDeg);
        }

        bool EnsureRig()
        {
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam == null) return false;
            if (rig == null)
            {
                var origin = cam.GetComponentInParent<XROrigin>();
                rig = origin != null ? origin.transform : cam.parent != null ? (cam.parent.parent != null ? cam.parent.parent : cam.parent) : null;
                tiltApplied = false; // 새 리그 = 기울기 없는 상태
            }
            return rig != null;
        }

        /// <summary>기울기를 뺀 카메라 전방 — 리그를 기울기 없는 기준 회전으로 되돌렸을 때의 전방</summary>
        Vector3 UntiltedCamForward()
        {
            if (cam == null) return Vector3.forward;
            if (!tiltApplied || rig == null) return cam.forward;
            return rigBaseRot * (Quaternion.Inverse(rig.rotation) * cam.forward);
        }

        /// <summary>
        /// 리그를 기준 자세에서 deg만큼 숙인 자세로 놓는다(누적 없음): ① 숙이지 않은 상태면 기준 자세(rigBaseRot·rigBasePos)와 축이 지나는 점(눈)을 저장
        /// ② 기울기를 뺀 눈의 평평한 전방 f → 오른쪽 r = up × f ③ Q = AngleAxis(deg, r) · rigBaseRot(양수 = 아래를 봄)
        /// ④ rig.rotation = Q, rig.position = 눈 + (Q · 기준 회전의 역)·(기준 위치 − 눈). deg 0이면 기준 자세 그대로 되돌림
        /// </summary>
        void ApplyTilt(float deg)
        {
            if (!EnsureRig()) return;
            if (!tiltApplied)
            {
                if (deg == 0f) return;                 // 이미 기준 자세
                rigBaseRot = rig.rotation;
                rigBasePos = rig.position;
                tiltPivot = cam.position;
            }
            Vector3 f = UntiltedCamForward();
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = rigBaseRot * Vector3.forward; // 수직으로 내려다보는 순간 — 리그 정면 기준
            f.Normalize();
            Quaternion q = deg != 0f ? Quaternion.AngleAxis(deg, Vector3.Cross(Vector3.up, f)) * rigBaseRot : rigBaseRot;
            rig.SetPositionAndRotation(tiltPivot + (q * Quaternion.Inverse(rigBaseRot)) * (rigBasePos - tiltPivot), q);
            tiltApplied = deg != 0f;
        }

        void OnDestroy()
        {
            // 씬을 떠남(처음 화면으로) — 이 씬 리그는 SDK가 남겨 둘 수 있어 기울기를 풀어 둔다(다음 씬 리그는 기울기 없음)
            if (tiltApplied && rig != null)
            {
                ApplyTilt(0f);
                Debug.Log("[ViewTilt] off (씬 종료 — 리그 기울기 해제)");
            }
        }
    }
}
