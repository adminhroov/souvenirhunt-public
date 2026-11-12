using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SouvenirHunt
{
    /// <summary>
    /// SCR-07 숨김 해제 (Phase 3, 인수인계 §4-3). Beam Pro 화면 구석의 투명 탭 존에 부착.
    /// 존 위치는 OperatorPanelController.ApplyLayout이 세션별로 옮긴다(09-03): Main = 우상단 (0.7,0.85)~(1,1) /
    /// Claw(눕혀 사용) = 세로 기준 우하단 (0.8,0)~(1,0.15) — 눕히면 가로 화면 우상단. 이 스크립트는 위치를 모른다.
    /// 연타 방식은 실기기 테스트(08-26) 후 **B MaxInterval(탭 간격 1초 이내 연속)로 확정** — §7 항목 종결.
    /// (A 고정 시간창 방식은 제거. mode 필드는 프리팹 직렬화 호환을 위해 남겨두되 동작은 항상 B.)
    /// 3회째부터 남은 횟수를 counterText에 표시 (피드백이 없으면 운영자가 계속 두드리게 됨).
    /// 탭 존은 anchor (0.7,0.85)~(1,1) — SDK 기본 프리팹의 Trigger(y≤~1549)·App·Home(y≤~1816)·
    /// Logo(x450~630)와 겹치지 않음(프리팹 실측) → 연타가 수집 클릭으로 새지 않는 것이 구조적으로 보장됨.
    /// SDK 업데이트로 레이아웃이 겹치게 되면: 이 존이 최상위라 버튼 입력을 가로채므로, 폴백으로
    /// OnPointerDown에서 ExecuteEvents.Execute(해당 버튼 GO, eventData, pointerDownHandler)로
    /// 재전달하는 패스스루를 검토할 것 (현재는 불필요하여 미구현).
    /// 예외를 던지지 않는다 — 널 가드만 (앱은 절대 죽지 않게).
    /// </summary>
    public class CornerTapReveal : MonoBehaviour, IPointerDownHandler
    {
        public enum TapDetectMode { WindowCount, MaxInterval } // WindowCount는 폐기(08-26) — 직렬화 호환용으로만 유지

        [Header("감지 방식 — B(MaxInterval)로 확정, 값은 직렬화 호환용")]
        [SerializeField] TapDetectMode mode = TapDetectMode.MaxInterval;
        [SerializeField] int tapsRequired = 10;
        [Tooltip("탭 간격이 이 값 이내면 연속으로 인정")]
        [SerializeField] float maxIntervalSeconds = 1f;

        [Header("피드백")]
        [Tooltip("남은 횟수 표시를 시작하는 탭 수 (§4-3: 3회째부터)")]
        [SerializeField] int counterShowFrom = 3;
        [Tooltip("마지막 탭 후 이 시간이 지나면 카운터 숨김")]
        [SerializeField] float counterHideSeconds = 1.5f;
        [SerializeField] TMP_Text counterText;

        [Header("대상")]
        [SerializeField] OperatorPanelController panel;
        [Tooltip("패널 토글 직후 연타 관성으로 즉시 재토글되는 것 방지")]
        [SerializeField] float cooldownSeconds = 1f;

        int chain;
        float lastTap = float.NegativeInfinity;
        float cooldownUntil = float.NegativeInfinity;

        void Awake()
        {
            if (mode != TapDetectMode.MaxInterval) mode = TapDetectMode.MaxInterval; // 구 프리팹 값 정규화
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            float now = Time.unscaledTime; // 게임 일시정지와 무관하게 동작
            if (now < cooldownUntil) return;

            chain = (now - lastTap <= maxIntervalSeconds) ? chain + 1 : 1;
            lastTap = now;

            if (chain >= tapsRequired) { Fire(now); return; }
            UpdateCounter(chain);
        }

        void Update()
        {
            // 연타가 끊기면 카운터만 숨긴다. 체인은 다음 탭의 간격 검사가 리셋한다.
            if (counterText != null && counterText.gameObject.activeSelf &&
                Time.unscaledTime - lastTap > counterHideSeconds)
                counterText.gameObject.SetActive(false);
        }

        void Fire(float now)
        {
            chain = 0;
            lastTap = float.NegativeInfinity;
            cooldownUntil = now + cooldownSeconds;
            if (counterText != null) counterText.gameObject.SetActive(false);
            if (panel != null) panel.Toggle();
        }

        void UpdateCounter(int n)
        {
            if (counterText == null || n < counterShowFrom) return;
            counterText.SetText("{0}", tapsRequired - n);
            if (!counterText.gameObject.activeSelf) counterText.gameObject.SetActive(true);
        }
    }
}
