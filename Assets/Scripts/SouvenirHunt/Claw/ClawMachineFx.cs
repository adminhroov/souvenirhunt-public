using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 기계 연출(10-15 비주얼 2차): 안쪽 네온 선(SouvenirHunt/ClawNeon) + 바닥 디지털 그리드(SouvenirHunt/ClawFloorGrid)를 게임 이벤트에 맞춰 움직인다.
    ///   · Flash(색, 초): 네온 전체가 그 색으로 번쩍였다 돌아옴 — 그릇 획득(청록)·폭발(붉은색)·성공(금색)
    ///   · Ripple(월드 위치, 색): 바닥 그리드에 그 자리에서 퍼지는 물결
    ///   · SetWarn: 남은 시간 경고 구간 동안 네온이 붉게 맥동
    /// 네온·그리드는 Environment 자식(메뉴 30이 ClawLayout 치수로 생성)이라 기계 표시·흔들림·재정렬을 그대로 따른다 — 기계가 바뀌어도 치수만 맞으면 됨.
    /// 재질은 공유하고 값은 MPB로만 바꾼다(재질 에셋 오염 없음). 예외를 던지지 않는다.
    /// </summary>
    public class ClawMachineFx : MonoBehaviour
    {
        public static ClawMachineFx Instance { get; private set; }

        [SerializeField] Renderer[] neon;
        [SerializeField] Renderer floorGrid;
        [SerializeField] float warnFadeSec = 0.4f;

        public int FlashCount { get; private set; }
        public int RippleCount { get; private set; }
        public bool Warning => warnTarget > 0.5f;
        /// <summary>지금 네온 번쩍임 세기(0~1) — 셀프테스트</summary>
        public float FlashLevel { get; private set; }

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        static readonly int WarnId = Shader.PropertyToID("_Warn");
        static readonly int RippleOriginId = Shader.PropertyToID("_RippleOrigin");
        static readonly int RippleStartId = Shader.PropertyToID("_RippleStart");
        static readonly int RippleColorId = Shader.PropertyToID("_RippleColor");

        MaterialPropertyBlock mpb;
        Color flashColor = Color.white;
        float flashStart = -100f, flashDur = 0.5f;
        float warn, warnTarget;
        bool dirty = true;

        void Awake()
        {
            Instance = this;
            mpb = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Flash(Color color, float seconds)
        {
            flashColor = color;
            flashStart = Time.time;
            flashDur = Mathf.Max(0.05f, seconds);
            FlashCount++;
            dirty = true;
        }

        /// <summary>바닥 그리드 물결 — worldPos를 그리드 로컬 xz로 바꿔 넣는다(셰이더는 로컬 좌표)</summary>
        public void Ripple(Vector3 worldPos, Color color)
        {
            if (floorGrid == null) return;
            var lp = floorGrid.transform.InverseTransformPoint(worldPos);
            floorGrid.GetPropertyBlock(mpb);
            mpb.SetVector(RippleOriginId, new Vector4(lp.x, lp.z, 0f, 0f));
            mpb.SetFloat(RippleStartId, Time.time); // URP 셰이더 _Time.y = 재생 중 Time.time
            mpb.SetColor(RippleColorId, color);
            floorGrid.SetPropertyBlock(mpb);
            RippleCount++;
        }

        public void SetWarn(bool on)
        {
            float t = on ? 1f : 0f;
            if (Mathf.Approximately(t, warnTarget)) return;
            warnTarget = t;
            dirty = true;
        }

        /// <summary>10-20 받침 더미가 바닥을 덮으면 바닥 그리드·바닥 네온을 끈다(안 보이는데 그리는 비용). 물결 카운트는 그대로</summary>
        public void SetFloorCovered(bool covered)
        {
            if (floorGrid != null) floorGrid.enabled = !covered;
            if (neon != null)
                foreach (var r in neon)
                    if (r != null && r.name == "NeonFloor") r.enabled = !covered;
        }

        /// <summary>리셋 — 번쩍임·경고·물결을 즉시 끔</summary>
        public void ResetFx()
        {
            flashStart = -100f;
            warnTarget = 0f;
            warn = 0f;
            FlashLevel = 0f;
            dirty = true;
            if (floorGrid != null)
            {
                floorGrid.GetPropertyBlock(mpb);
                mpb.SetFloat(RippleStartId, -100f);
                floorGrid.SetPropertyBlock(mpb);
            }
        }

        void Update()
        {
            float age = Time.time - flashStart;
            float f = age < flashDur ? 1f - age / flashDur : 0f;
            f *= f; // 처음 번쩍 → 빠르게 빠짐
            warn = Mathf.MoveTowards(warn, warnTarget, Time.deltaTime / Mathf.Max(0.05f, warnFadeSec));
            if (!dirty && Mathf.Approximately(f, FlashLevel) && (Mathf.Approximately(warn, warnTarget))) return;
            bool settled = f <= 0f && Mathf.Approximately(warn, warnTarget);
            FlashLevel = f;
            if (neon != null)
                foreach (var r in neon)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat(FlashId, f);
                    mpb.SetColor(FlashColorId, flashColor);
                    mpb.SetFloat(WarnId, warn);
                    r.SetPropertyBlock(mpb);
                }
            dirty = !settled;
        }
    }
}
