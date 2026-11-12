using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// HUD (기획서 SCR-03). 수집 카운트 · 진행률 바 · 남은 시간 · 카운트다운 표시.
    /// GameManager 이벤트만 구독. 남은 시간 warningTimeSeconds 이하 → 적색 점멸.
    /// 큰 폰트·고대비(다크+시안) — 글래스 가독성 유지.
    /// 타이머 텍스트는 초당 1회(OnTimeTick)만 갱신, SetText로 문자열 할당 없음 (60FPS 수호).
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Header("수집")]
        [SerializeField] TMP_Text countText;      // "3 / 10"
        [SerializeField] Image progressBar;       // fillAmount 0~1

        [Header("타이머")]
        [SerializeField] TMP_Text timeText;       // "mm:ss"
        [SerializeField] Color normalColor = new Color(0.4f, 0.9f, 1f);
        [SerializeField] Color warnColor = new Color(1f, 0.3f, 0.3f);
        [Tooltip("config.txt의 warningTimeSeconds가 리셋마다 덮어씀 — 여기는 로드 전 기본값.")]
        [SerializeField] float warnThresholdSec = 30f;
        [Tooltip("경고 구간 점멸 주기(초) — 켜짐/꺼짐 한 사이클")]
        [SerializeField] float blinkPeriod = 0.6f;

        [Header("카운트다운")]
        [SerializeField] TMP_Text countdownText;  // "3" "2" "1" — 시작 전 큰 숫자
        [SerializeField] GameObject countdownRoot;

        [Header("표시 제어")]
        [SerializeField] GameObject hudRoot;      // Playing 중에만 표시
        [SerializeField] GameObject pausedRoot;   // Phase 5-1: 백그라운드 일시정지 안내

        GameManager gm;
        bool bound;
        bool playing, paused;
        float lastRemain = float.MaxValue;
        bool blinkOn;   // 마지막으로 적용한 색이 경고색인지 — 바뀔 때만 대입

        // OnEnable 시점엔 GameManager.Awake가 아직일 수 있음(오브젝트별 순서 비보장)
        // → 모든 Awake 이후가 보장되는 Start에서 재시도
        void OnEnable() => TryBind();
        void Start() => TryBind();

        void TryBind()
        {
            if (bound) return;
            gm = GameManager.Instance;
            if (gm == null) return;
            gm.OnCollected += HandleCollected;
            gm.OnTimeTick += HandleTimeTick;
            gm.OnCountdown += HandleCountdown;
            gm.OnStateChanged += HandleState;
            gm.OnGameReset += HandleReset;
            gm.OnPauseChanged += HandlePause;
            bound = true;
            HandleReset();
        }

        void OnDisable()
        {
            if (!bound) return;
            gm.OnCollected -= HandleCollected;
            gm.OnTimeTick -= HandleTimeTick;
            gm.OnCountdown -= HandleCountdown;
            gm.OnStateChanged -= HandleState;
            gm.OnGameReset -= HandleReset;
            gm.OnPauseChanged -= HandlePause;
            bound = false;
        }

        void Update()
        {
            // 경고 구간 적색 점멸 (SCR-03). Playing·미일시정지·경고 이하일 때만, 색이 바뀔 때만 대입.
            if (timeText == null || !playing || paused || lastRemain > warnThresholdSec) return;
            bool on = (Time.time % blinkPeriod) < blinkPeriod * 0.5f;
            if (on == blinkOn) return;
            blinkOn = on;
            timeText.color = on ? warnColor : normalColor;
        }

        void HandleCollected(int cur, int total)
        {
            if (countText) countText.SetText("{0} / {1}", cur, total);
            if (progressBar) progressBar.fillAmount = total > 0 ? (float)cur / total : 0f;
        }

        void HandleTimeTick(float remain)
        {
            lastRemain = remain;
            if (timeText == null) return;
            int mm = Mathf.FloorToInt(remain / 60f);
            int ss = Mathf.FloorToInt(remain % 60f);
            timeText.SetText("{0:00}:{1:00}", mm, ss); // 할당 없는 TMP 포맷
            if (remain > warnThresholdSec && blinkOn)
            {
                blinkOn = false;
                timeText.color = normalColor;
            }
        }

        void HandleCountdown(int n)
        {
            if (countdownRoot) countdownRoot.SetActive(n > 0);
            if (countdownText && n > 0) countdownText.SetText("{0}", n);
        }

        void HandleState(GameManager.State s)
        {
            playing = s == GameManager.State.Playing;
            if (hudRoot) hudRoot.SetActive(playing);
            if (!playing && pausedRoot) pausedRoot.SetActive(false);
        }

        void HandlePause(bool p)
        {
            paused = p;
            if (pausedRoot) pausedRoot.SetActive(p);
        }

        void HandleReset()
        {
            if (gm != null) warnThresholdSec = gm.WarnThresholdSec; // config 재로드 반영
            playing = false;
            paused = false;
            lastRemain = float.MaxValue;
            blinkOn = false;
            if (countText) countText.SetText("0 / {0}", gm != null ? gm.TargetCount : 10);
            if (progressBar) progressBar.fillAmount = 0f;
            if (timeText) { timeText.text = "--:--"; timeText.color = normalColor; }
            if (countdownRoot) countdownRoot.SetActive(false);
            if (hudRoot) hudRoot.SetActive(false);
            if (pausedRoot) pausedRoot.SetActive(false);
        }
    }
}
