using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SouvenirHunt
{
    /// <summary>
    /// 수집 대상 오브젝트 (T2 + Phase 1 개정).
    /// 기본 = 터치패드 클릭 1회 수집 (기획서 §05). useHoldFallback = 프로토타입의 지연시간 홀드 방식
    /// (인수인계 §2-1 — 실기기 클릭 오선택률이 높으면 프리팹 토글 하나로 복귀).
    /// Hover 시 이미시브 글로우, 수집 시 스케일 팝(0.15s) 후 비활성 → 풀 반환.
    /// 비주얼은 자식 Visual에 격리 — MeshLibrary 항목으로 SetVisual 교체 (더미 → 실물 시임).
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class Collectible : MonoBehaviour
    {
        [Header("상호작용")]
        [Tooltip("실기기 클릭 오선택률이 높으면 홀드 방식으로 되돌리는 토글 (인수인계 §2-1)")]
        [SerializeField] bool useHoldFallback = false;
        [Tooltip("홀드 폴백 시 수집까지 걸리는 시간(초)")]
        [SerializeField] float holdDuration = 1f;

        [Header("비주얼")]
        [Tooltip("자식 Visual의 렌더러. 미지정 시 자식에서 자동 탐색.")]
        [SerializeField] Renderer visualRenderer;
        [Tooltip("비주얼 정규화 크기(m) — 최대 변 기준 (기획서 §08 약 0.2m)")]
        [SerializeField] float visualSize = 0.2f;
        /// <summary>에디터 가시성 검증(메뉴 13)이 실루엣 실치수를 계산하려면 이 값이 필요하다.</summary>
        public float VisualSize => visualSize;
        [SerializeField] Color glowColor = Color.cyan;
        [SerializeField] float popScale = 1.35f;
        [SerializeField] float popDuration = 0.15f;

        [Header("사운드 (1차 무음 — 2차에서 켬, 인수인계 §6 오디오 훅)")]
        [SerializeField] bool enableSfx = false;

        const float HoldDecaySpeed = 2f;   // 홀드를 놓았을 때 게이지가 빠지는 배속
        const float HoldRingDiameter = 0.5f;

        XRSimpleInteractable interactable;
        MaterialPropertyBlock mpb;
        Vector3 baseScale;
        bool consumed;

        // 홀드 폴백 상태
        float holdProgress;
        GameObject holdRing;
        Image holdFill;
        AudioSource holdAudio;

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            if (visualRenderer == null) visualRenderer = GetComponentInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            baseScale = transform.localScale;

            interactable.hoverEntered.AddListener(_ => SetGlow(true));
            interactable.hoverExited.AddListener(_ => SetGlow(false));
            interactable.selectEntered.AddListener(_ => OnSelected());

            // 홀드 폴백일 때만 게이지 링 생성 — 클릭 모드(기본)에서는 캔버스·텍스처 비용 0 (풀 12개 × 3 오브젝트 절약)
            if (useHoldFallback) BuildHoldRing();
        }

        void OnDestroy()
        {
            if (interactable == null) return;
            interactable.hoverEntered.RemoveAllListeners();
            interactable.hoverExited.RemoveAllListeners();
            interactable.selectEntered.RemoveAllListeners();
        }

        // ─ 클릭 수집 (기본 경로) ──────────────────────────────────

        void OnSelected()
        {
            if (useHoldFallback) return; // 홀드 모드에서는 Update 게이지가 담당
            TryCollect();
        }

        void TryCollect()
        {
            if (consumed || GameManager.Instance == null || GameManager.Instance.Current != GameManager.State.Playing)
                return;
            consumed = true;
            // 크레딧은 클릭 시점에 즉시 — 연출 대기 중 타임업으로 유실되지 않게
            GameManager.Instance.NotifyCollected(this); // 수집음은 GameManager.OnCollected → GameAudio가 담당(Phase 5-2)
            CollectVfxPool.Instance?.Play(transform.position, glowColor); // Phase 4 VFX(스파크·+1) — 풀 없으면 생략
            StartCoroutine(PopAndReturn());
        }

        IEnumerator PopAndReturn()
        {
            // 스케일 팝(0.15s): baseScale → popScale → 0
            float half = popDuration * 0.5f;
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                transform.localScale = Vector3.Lerp(baseScale, baseScale * popScale, t / half);
                yield return null;
            }
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                transform.localScale = Vector3.Lerp(baseScale * popScale, Vector3.zero, t / half);
                yield return null;
            }
            gameObject.SetActive(false); // 풀로 반환 (Destroy 금지)
        }

        // ─ 홀드 폴백 (프로토타입 PopTarget 이식) ──────────────────

        void Update()
        {
            if (!useHoldFallback || consumed) return;

            bool playing = GameManager.Instance != null && GameManager.Instance.Current == GameManager.State.Playing;
            if (playing && interactable.isSelected)
                holdProgress += Time.deltaTime / holdDuration;
            else
                holdProgress -= Time.deltaTime * HoldDecaySpeed / holdDuration;
            holdProgress = Mathf.Clamp01(holdProgress);

            // 게이지에 따라 살짝 커지는 긴장감
            transform.localScale = baseScale * (1f + 0.18f * holdProgress);

            UpdateHoldRing();
            UpdateHoldAudio(playing);

            if (holdProgress >= 1f)
            {
                holdProgress = 0f;
                TryCollect();
            }
        }

        void LateUpdate()
        {
            if (!useHoldFallback || holdRing == null || !holdRing.activeSelf) return;
            var cam = Camera.main;
            if (cam == null) return;
            holdRing.transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        void BuildHoldRing()
        {
            // 풀링 대응: Destroy 없이 자식으로 1회 생성, ResetState에서 비활성 (PopTarget과 다른 점)
            holdRing = new GameObject("HoldRing");
            holdRing.transform.SetParent(transform, false);
            var canvas = holdRing.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)holdRing.transform;
            rt.sizeDelta = new Vector2(128f, 128f);
            rt.localScale = Vector3.one * (HoldRingDiameter / 128f);

            var bg = CreateRingImage("RingBg", holdRing.transform);
            bg.color = new Color(1f, 1f, 1f, 0.25f);
            bg.fillAmount = 1f;

            holdFill = CreateRingImage("RingFill", holdRing.transform);
            holdFill.fillAmount = 0f;

            holdRing.SetActive(false);
        }

        static Image CreateRingImage(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = HoldRingSprites.Ring;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            return img;
        }

        void UpdateHoldRing()
        {
            if (holdRing == null) BuildHoldRing(); // 인스펙터에서 런타임에 켠 경우 대비
            bool show = holdProgress > 0.001f || interactable.isHovered;
            holdRing.SetActive(show);
            if (!show) return;
            holdFill.fillAmount = holdProgress;
            holdFill.color = Color.Lerp(new Color(1f, 1f, 1f, 0.9f), new Color(1f, 0.55f, 0.1f, 1f), holdProgress);
        }

        void UpdateHoldAudio(bool playing)
        {
            if (!enableSfx) return;
            if (holdAudio == null)
            {
                holdAudio = gameObject.AddComponent<AudioSource>();
                holdAudio.clip = HoldSfx.HoldLoop;
                holdAudio.loop = true;
                holdAudio.playOnAwake = false;
                holdAudio.spatialBlend = 1f;
                holdAudio.volume = 0f;
            }
            bool charging = playing && interactable.isSelected && holdProgress > 0f;
            if (charging)
            {
                if (!holdAudio.isPlaying) holdAudio.Play();
                holdAudio.pitch = 0.8f + holdProgress * 1.3f;
                holdAudio.volume = 0.12f + 0.3f * holdProgress;
            }
            else if (holdAudio.isPlaying)
            {
                holdAudio.Stop();
            }
        }

        // ─ 비주얼 ─────────────────────────────────────────────────

        void SetGlow(bool on)
        {
            if (consumed || visualRenderer == null) return;
            visualRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, on ? glowColor : Color.black);
            visualRenderer.SetPropertyBlock(mpb);
        }

        /// <summary>
        /// MeshLibrary 항목으로 비주얼 교체 (스폰 시 SpawnManager가 호출).
        /// 자식 Visual의 메시·머티리얼을 갈아끼우고 크기 정규화(visualSize) 후
        /// 콜라이더를 비주얼보다 20% 크게 재계산 (기획서 §08 선택 성공률 확보).
        /// </summary>
        public void SetVisual(MeshLibrary.Entry entry)
        {
            // 본문은 MeshFit로 분리(09-03) — 콜라이더 1.2배 = 시각 모델보다 20% 크게 (기획서 §08)
            MeshFit.Apply(visualRenderer, entry, visualSize, GetComponent<BoxCollider>(), 1.2f);
        }

        /// <summary>풀에서 재사용 시 초기 상태로 복원 (T3 스폰).</summary>
        public void ResetState()
        {
            consumed = false;
            holdProgress = 0f;
            StopAllCoroutines();
            transform.localScale = baseScale;
            if (holdRing != null) holdRing.SetActive(false);
            if (holdAudio != null && holdAudio.isPlaying) holdAudio.Stop();
            SetGlow(false);
        }
    }
}
