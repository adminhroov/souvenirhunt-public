using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SouvenirHunt
{
    /// <summary>
    /// 시작 패널 (T4). 카메라 전방 2m 월드 UI. 포인터 겨냥 → 터치패드 클릭 → RequestStart().
    /// 자동 시작 금지 — 반드시 명시적 버튼. 2차 DID 인증 통과 후에만 활성화되는 구조.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class StartPanelController : MonoBehaviour
    {
        [SerializeField] GameObject visualRoot; // 패널 그래픽(Ready 상태에서만 표시)

        XRSimpleInteractable interactable;
        GameManager gm;

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(_ => OnClicked());
        }

        bool bound;

        // OnEnable 시점엔 GameManager.Awake가 아직일 수 있음 → Start에서 재시도
        void OnEnable() => TryBind();
        void Start() => TryBind();

        void TryBind()
        {
            if (bound) return;
            gm = GameManager.Instance;
            RefreshVisibility(gm != null ? gm.Current : GameManager.State.Ready);
            if (gm == null) return;
            gm.OnStateChanged += HandleState;
            bound = true;
        }

        void OnDisable()
        {
            if (!bound) return;
            gm.OnStateChanged -= HandleState;
            bound = false;
        }

        void OnClicked()
        {
            if (gm == null) gm = GameManager.Instance;
            if (gm != null) gm.RequestStart(); // Ready + 인증 통과 시에만 카운트다운 진입
        }

        void HandleState(GameManager.State s) => RefreshVisibility(s);

        void RefreshVisibility(GameManager.State s)
        {
            bool ready = s == GameManager.State.Ready;
            bool authorized = gm == null || gm.Auth.IsAuthorized();
            bool show = ready && authorized;
            if (visualRoot) visualRoot.SetActive(show);
            if (interactable) interactable.enabled = show;
            // 콜라이더도 함께 꺼야 함 — 남겨두면 Playing 중 레이저가 (숨겨진) 패널에 막혀
            // 정면 2m 뒤쪽 기념품이 조준 불가가 된다
            if (TryGetComponent<Collider>(out var col)) col.enabled = show;
        }
    }
}
