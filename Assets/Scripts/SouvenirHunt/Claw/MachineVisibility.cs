using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 기계 시각 루트(몸체·리그·스코어보드)와 부스(Model1) 표시/숨김. Ready·Guide에선 숨김.
    /// 잡기(확인) → 정면 재정렬 뒤 순차 등장(6차, 09-14 사용자 결정): 부스가 바닥 아래에서 먼저 솟고(boothDistance, boothDuration),
    /// machineDelay 뒤 기계가 그 앞에서 솟는다(slideDistance, slideDuration). 기계는 자기 차례에 켜진다 — 그 전엔 안 보임.
    /// 솟는 동안 기계 하부는 부스 바닥 평면에 가려 "바닥에서 올라오는" 모습. 페이드 없음(투명 셰이더 비용 회피).
    /// 아이템은 전체 등장(SlideDuration)이 끝난 뒤 배치되므로(ClawGameManager) 이미 정착한 모습으로 나타난다.
    /// </summary>
    public class MachineVisibility : MonoBehaviour
    {
        [Header("기계(몸체·리그·스코어보드)")]
        [SerializeField] GameObject[] roots;
        [SerializeField] float slideDistance = 0.6f;
        [SerializeField] float slideDuration = 0.8f;
        [Tooltip("부스가 솟기 시작한 뒤 기계가 솟기 시작하기까지(초)")]
        [SerializeField] float machineDelay = 0.6f;

        [Header("부스(Model1) — 먼저 등장")]
        [SerializeField] GameObject[] boothRoots;
        [SerializeField] float boothDistance = 1.2f;
        [SerializeField] float boothDuration = 0.8f;

        bool HasBooth => boothRoots != null && boothRoots.Length > 0;
        float MachineStart => HasBooth ? machineDelay : 0f;

        /// <summary>전체 등장 시간(부스 + 지연 + 기계) — GM이 이만큼 기다린 뒤 아이템 배치</summary>
        public float SlideDuration => Mathf.Max(HasBooth ? boothDuration : 0f, MachineStart + slideDuration);
        public bool IsShown { get; private set; }

        Vector3[] basePos, boothBasePos;
        float t = -1f;
        bool machineActivated;

        void Awake() => CaptureBase();

        static Vector3[] Capture(GameObject[] list, Vector3[] cache)
        {
            if (list == null) return null;
            if (cache != null && cache.Length == list.Length) return cache;
            var r = new Vector3[list.Length];
            for (int i = 0; i < list.Length; i++) r[i] = list[i] != null ? list[i].transform.localPosition : Vector3.zero;
            return r;
        }

        void CaptureBase()
        {
            basePos = Capture(roots, basePos);
            boothBasePos = Capture(boothRoots, boothBasePos);
        }

        public void Hide()
        {
            CaptureBase();
            t = -1f;
            SetGroup(roots, basePos, false, 0f, 0f);
            SetGroup(boothRoots, boothBasePos, false, 0f, 0f);
            machineActivated = false;
            IsShown = false;
        }

        public void Show(bool animate)
        {
            CaptureBase();
            IsShown = true;
            if (animate)
            {
                t = 0f;
                SetGroup(boothRoots, boothBasePos, true, boothDistance, 1f);
                machineActivated = MachineStart <= 0f;
                SetGroup(roots, basePos, machineActivated, slideDistance, 1f);
            }
            else
            {
                t = -1f;
                SetGroup(boothRoots, boothBasePos, true, 0f, 0f);
                SetGroup(roots, basePos, true, 0f, 0f);
                machineActivated = true;
            }
        }

        void Update()
        {
            if (t < 0f) return;
            t += Time.deltaTime;

            float kb = Mathf.Clamp01(t / Mathf.Max(0.01f, boothDuration));
            Offset(boothRoots, boothBasePos, boothDistance, Ease(kb));

            float km = Mathf.Clamp01((t - MachineStart) / Mathf.Max(0.01f, slideDuration));
            if (!machineActivated && t >= MachineStart)
            {
                machineActivated = true;
                SetGroup(roots, basePos, true, slideDistance, 1f);
            }
            if (machineActivated) Offset(roots, basePos, slideDistance, Ease(km));

            if (t >= SlideDuration) t = -1f;
        }

        static float Ease(float k) => 1f - Mathf.Pow(1f - k, 3f); // easeOutCubic

        /// <summary>활성 상태 + 아래로 distance×remain 만큼 내린 위치</summary>
        static void SetGroup(GameObject[] list, Vector3[] bases, bool active, float distance, float remain)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == null) continue;
                list[i].transform.localPosition = bases[i] + Vector3.down * (distance * remain);
                if (list[i].activeSelf != active) list[i].SetActive(active);
            }
        }

        static void Offset(GameObject[] list, Vector3[] bases, float distance, float e)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null) list[i].transform.localPosition = bases[i] + Vector3.down * (distance * (1f - e));
        }
    }
}
