using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// Guide 상태 안내: 가슴 앞 Beam Pro 비율의 가로 프레임(LineRenderer)과 "가로로 눕혀 이 틀에 겹친 뒤 [잡기]" 안내문. 콜라이더 없음.
    /// 7차(플레이테스트): 루트가 **시선 yaw를 따라간다** — 월드 고정이던 틀은 부팅 방향을 봐야만 맞출 수 있어 정면 재정렬이 늘 시작 방향과 같았다.
    /// 이제 어느 방향을 보든 틀·문구가 앞에 오고, [잡기] 확인 시 BoothAligner.Align()이 그 시선 yaw로 부스·기계를 세운다.
    /// 표시되는 순간 스냅, 이후 지수 감쇠 추종(LazyFollow와 같은 1−e^(−k·dt), head-lock 금지). 수평 yaw만 — 고개를 숙여도 틀은 가슴 앞에 머문다.
    /// 프레임 알파를 천천히 맥동.
    /// 10-13 연출안 STEP 05: 디지털 디졸브로 기계가 이미 드러났으면(세션 MachineShown) 기계 안쪽에 겹치는 문구 대신
    ///   기계 앞(부스 고정, 결과 패널 자리)의 [START] 패널을 띄운다 — 육각 START가 맥동. 가슴 앞 틀은 그대로.
    /// </summary>
    public class ClawGuidePanel : MonoBehaviour
    {
        [SerializeField] GameObject visualRoot;   // 프레임(가슴 앞)
        [SerializeField] GameObject textRoot;     // 안내 문구(정면 시야 하단, 4차 분리)
        [SerializeField] LineRenderer frame;
        [SerializeField] Color frameColor = new Color(0.3f, 0.85f, 0.9f, 1f);
        [Tooltip("시선 추종 감쇠 — 클수록 빨리 따라옴")]
        [SerializeField] float followDamping = 5f;
        [Header("10-13 START(디졸브 뒤) — 부스 고정")]
        [SerializeField] GameObject startRoot;
        [SerializeField] RectTransform startHex;

        ClawSessionBase gm;
        Transform cam;
        bool bound, showing;

        /// <summary>현재 틀 루트의 yaw(도) — 셀프테스트용</summary>
        public float Yaw => transform.eulerAngles.y;
        public bool Showing => showing;
        /// <summary>[START] 패널 표시 중(디졸브 뒤 Guide)</summary>
        public bool StartShowing => startRoot != null && startRoot.activeSelf;
        /// <summary>기계 안쪽 자리의 안내 문구 표시 중(전환 끔 Guide)</summary>
        public bool TextShowing => textRoot != null && textRoot.activeSelf;

        void OnEnable() => TryBind();
        void Start() => TryBind();

        void TryBind()
        {
            if (bound) return;
            gm = ClawSessionBase.Instance;
            Refresh(gm != null ? gm.Current : ClawSessionBase.State.Ready);
            if (gm == null) return;
            gm.OnStateChanged += Refresh;
            bound = true;
        }

        void OnDisable()
        {
            if (!bound) return;
            gm.OnStateChanged -= Refresh;
            bound = false;
        }

        void Refresh(ClawSessionBase.State s)
        {
            bool show = s == ClawSessionBase.State.Guide;
            bool revealed = show && gm != null && gm.MachineShown;
            if (show && !showing) Follow(1f); // 나타나는 순간 시선 앞으로 스냅
            showing = show;
            if (visualRoot) visualRoot.SetActive(show);
            if (textRoot) textRoot.SetActive(show && !revealed);   // 문구 자리는 기계 앞 유리 뒤 부피 — 기계가 보이면 숨김
            if (startRoot) startRoot.SetActive(revealed);
        }

        void LateUpdate()
        {
            if (!showing) return;
            Follow(1f - Mathf.Exp(-followDamping * Time.deltaTime));
        }

        /// <summary>루트를 눈 위치·시선 수평 yaw 쪽으로 k만큼 보간(k=1 스냅). 카메라가 없으면 그대로(앱 생존).</summary>
        void Follow(float k)
        {
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam == null) return;
            var fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return; // 수직으로 내려다보는 순간은 yaw 불명 — 유지
            var targetRot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, cam.position, k), Quaternion.Slerp(transform.rotation, targetRot, k));
        }

        void Update()
        {
            if (startHex != null && startRoot != null && startRoot.activeInHierarchy)
                startHex.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(Time.time * 4f));
            if (frame == null || visualRoot == null || !visualRoot.activeInHierarchy) return;
            var c = frameColor;
            c.a = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.time * 3f));
            frame.startColor = c;
            frame.endColor = c;
        }
    }
}
