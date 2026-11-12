using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 시작 패널 — 표시 전용(3차). 빔을 쓰지 않으므로 시작은 오버레이 [잡기](ClawSessionBase.Update Ready) 가 담당하고,
    /// 이 패널은 Ready에서 안내문만 보여준다. 콜라이더·인터랙터블 없음(레이 억제와 이중 안전).
    /// </summary>
    public class ClawStartPanel : MonoBehaviour
    {
        [SerializeField] GameObject visualRoot;

        ClawSessionBase gm;
        bool bound;

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
            if (visualRoot) visualRoot.SetActive(s == ClawSessionBase.State.Ready);
        }
    }
}
