using UnityEngine;
using UnityEngine.InputSystem;

namespace SouvenirHunt
{
    /// <summary>
    /// 리센터(Home) 감지 → 부스 재정렬 (Phase 5-1, 인수인계 §6 "시야 어긋나면 Home 버튼 리센터").
    /// XREAL 리센터는 InputAction(XREALSessionManager.m_RecenterAction)으로 들어온다. 같은 액션을 구독해
    /// BoothAligner.Align()으로 부스를 다시 정면에 놓고, 이동된 스폰 포인트로 수집품만 재배치한다.
    /// 게임 상태·타이머·수집 수는 유지(리셋 아님). 액션이 없으면 경고만 남기고 무동작 — 앱은 정상.
    /// </summary>
    public class RecenterWatcher : MonoBehaviour
    {
        [Tooltip("XREAL 리센터 InputAction (XR Interaction Setup의 m_RecenterAction과 동일 에셋).")]
        [SerializeField] InputActionReference recenterAction;
        [SerializeField] BoothAligner booth;

        InputAction action;

        void Awake()
        {
            if (booth == null) booth = GetComponentInParent<BoothAligner>();
            if (booth == null) booth = FindAnyObjectByType<BoothAligner>();
        }

        void OnEnable()
        {
            action = recenterAction != null ? recenterAction.action : null;
            if (action == null)
            {
                Debug.LogWarning("[RecenterWatcher] recenterAction 미지정 — 리센터 재정렬 비활성(앱은 정상)");
                return;
            }
            action.performed += OnRecenter;
            action.Enable();
        }

        void OnDisable()
        {
            if (action != null) action.performed -= OnRecenter;
        }

        void OnRecenter(InputAction.CallbackContext _)
        {
            // 10-28 Claw 세션은 세션 재정렬로(상품을 같은 델타로 옮김 — 기계 다가오는 중이면 제자리만 갱신). 루트만 옮기면 상품이 제자리에 남는다
            if (GameSession.Current is ClawSessionBase g)
            {
                g.Recenter();
                Debug.Log("[Recenter] 부스 재정렬 (Claw 세션 경유, 상태 유지)");
                return;
            }
            if (booth != null) booth.Align();
            GameSession.Current?.OnRecentered(); // Main: 수집품 재배치
            Debug.Log("[Recenter] 부스 재정렬 (상태 유지)");
        }
    }
}
