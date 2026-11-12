using TMPro;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// FPS 오버레이 (Phase 4). config.txt의 showFps=true일 때만 HUD 캔버스 상단에 표시.
    /// 리셋마다 config가 재로드되므로 OnGameReset에서 표시 여부를 다시 적용한다.
    /// TMP_Text.SetText(포맷, float...)로 문자열 할당 없이 갱신.
    /// 세션은 GameSession.Current — Main/Claw 씬 공용(09-03).
    /// </summary>
    public class PerfOverlay : MonoBehaviour
    {
        [SerializeField] TMP_Text text;
        [SerializeField] float refreshSec = 0.5f;

        IGameSession gm;
        bool bound, shown;
        float timer;

        void OnEnable()
        {
            GameSession.Changed += OnSessionChanged;
            TryBind();
        }

        void Start() => TryBind();

        void OnDisable()
        {
            GameSession.Changed -= OnSessionChanged;
            Unbind();
        }

        void OnSessionChanged(IGameSession _)
        {
            Unbind();
            TryBind();
        }

        void Unbind()
        {
            if (bound && gm != null) gm.OnGameReset -= Apply;
            bound = false;
            gm = null;
        }

        void TryBind()
        {
            if (bound) return;
            gm = GameSession.Current;
            if (gm == null) return;
            gm.OnGameReset += Apply;
            bound = true;
            Apply();
        }

        void Apply()
        {
            shown = gm != null && gm.Config != null && gm.Config.showFps;
            if (text != null) text.gameObject.SetActive(shown);
        }

        void Update()
        {
            if (!shown || text == null) return;
            var pm = PerfMonitor.Instance;
            if (pm == null) return;
            timer += Time.unscaledDeltaTime;
            if (timer < refreshSec) return;
            timer = 0f;
            text.SetText("{0:0} fps  |  min60 {1:0}  |  {2:0.0} ms", pm.CurrentFps, pm.Min60, pm.WorstFrameMs);
        }
    }
}
