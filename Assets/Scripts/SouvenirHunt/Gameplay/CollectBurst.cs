using TMPro;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 수집 피드백 1회분 (Phase 4 VFX): 스파크 파티클 버스트 + "+1" 월드 텍스트 상승·페이드.
    /// CollectVfxPool이 사전 생성해 재사용 — Collectible 루트는 팝 후 즉시 비활성되므로 VFX는 풀 소속이어야 한다.
    /// 코루틴 대신 Update 타이머(GC 0). 끝나면 스스로 SetActive(false).
    /// </summary>
    public class CollectBurst : MonoBehaviour
    {
        [SerializeField] ParticleSystem spark;
        [SerializeField] TMP_Text label;
        [SerializeField] float duration = 0.6f;
        [Tooltip("+1 텍스트가 떠오르는 높이(m)")]
        [SerializeField] float rise = 0.25f;
        [SerializeField] Color labelColor = new Color(0.6f, 1f, 1f, 1f);

        public bool IsPlaying { get; private set; }
        public float StartedAt { get; private set; }
        /// <summary>"+1" 라벨 표시 중(셀프테스트용)</summary>
        public bool LabelVisible => IsPlaying && label != null && label.gameObject.activeSelf;

        float t;
        Vector3 startPos;

        /// <param name="showLabel">false면 스파크만(인형뽑기 캡슐·폭탄 — 점수 없는 이벤트에 "+1"을 띄우지 않는다, 7차)</param>
        public void Play(Vector3 pos, Color color, bool showLabel = true)
        {
            transform.position = pos;
            startPos = pos;
            t = 0f;
            IsPlaying = true;
            StartedAt = Time.unscaledTime;
            gameObject.SetActive(true);

            if (spark != null)
            {
                var main = spark.main;
                main.startColor = color;
                spark.Clear(true);
                spark.Play(true);
            }
            if (label != null)
            {
                label.color = labelColor;
                label.transform.position = pos;
                label.gameObject.SetActive(showLabel);
            }
        }

        void Update()
        {
            if (!IsPlaying) return;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);

            if (label != null)
            {
                float ease = 1f - (1f - k) * (1f - k); // ease-out
                label.transform.position = startPos + Vector3.up * (rise * ease);
                var c = labelColor;
                c.a = 1f - k * k;
                label.color = c;
            }

            if (t >= duration && (spark == null || !spark.IsAlive(true)))
            {
                IsPlaying = false;
                if (label != null) label.gameObject.SetActive(false);
                gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (!IsPlaying || label == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - cam.transform.position);
        }
    }
}
