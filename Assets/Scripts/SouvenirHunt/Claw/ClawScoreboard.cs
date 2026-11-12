using TMPro;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 기계 뒷벽 스코어보드(월드 고정 — head-lock 아님). 10-15 미션형(8d3d1ed 문구 기준):
    /// 왼쪽 "그릇 n / N" + 종류별(세라믹·유리), 오른쪽 남은 시간 m:ss(경고 구간 적색 점멸), 아래 배너("획득!"/"폭탄!"/"캡슐 — 무득점"/"정면 재정렬"),
    /// 카운트다운 3·2·1. 가운데는 유휴 집게가 가리므로 비움. 숫자는 이벤트 때만, 타이머는 1Hz 이벤트로만 갱신(SetText 무할당).
    /// 기계와 함께 표시/숨김되므로(MachineVisibility) OnEnable마다 재바인드해 현재 값을 다시 그린다.
    /// 비주얼 2차(10-15): 그릇을 넣으면 왼쪽 숫자가 톡 커졌다(1.35배) 돌아오며 흰색 → 청록으로 번쩍.
    /// </summary>
    public class ClawScoreboard : MonoBehaviour
    {
        [SerializeField] TMP_Text leftText;      // 그릇 n / N + 종류별
        [SerializeField] TMP_Text rightText;     // 아이템 구성(캡슐·폭탄)
        [SerializeField] TMP_Text timeText;
        [SerializeField] GameObject countdownRoot;
        [SerializeField] TMP_Text countdownText;
        [SerializeField] TMP_Text bannerText;
        [Tooltip("10-20 StartUpPark: 가운데를 감겨 올라간 집게가 가려 배너·카운트다운을 오른쪽 열에도 같이(없으면 하나만)")]
        [SerializeField] TMP_Text countdownText2;
        [SerializeField] TMP_Text bannerText2;
        [SerializeField] Color normalColor = new Color(0.4f, 0.9f, 1f);
        [SerializeField] Color warnColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] float blinkPeriod = 0.6f;
        [SerializeField] float bannerSeconds = 1.0f;
        [SerializeField] float punchSeconds = 0.45f;

        ClawGameManager gm;
        bool bound, playing, paused, blinkOn, bannerSticky;
        float lastRemain = float.MaxValue;
        float bannerUntil;
        float punchStart = -100f;
        Vector3 leftBaseScale = Vector3.one;
        bool leftScaleKnown;

        void OnEnable() => TryBind();
        void Start() => TryBind();

        void OnDisable()
        {
            if (!bound) return;
            gm.OnCountsChanged -= RefreshCounts;
            gm.OnTimeTick -= HandleTimeTick;
            gm.OnCountdown -= HandleCountdown;
            gm.OnStateChanged -= HandleState;
            gm.OnGameReset -= HandleReset;
            gm.OnPauseChanged -= HandlePause;
            gm.OnGrabbed -= HandleGrabbed;
            gm.OnRealigned -= HandleRealigned;
            bound = false;
        }

        void TryBind()
        {
            if (bound) return;
            gm = ClawGameManager.Instance;
            if (gm == null) return;
            gm.OnCountsChanged += RefreshCounts;
            gm.OnTimeTick += HandleTimeTick;
            gm.OnCountdown += HandleCountdown;
            gm.OnStateChanged += HandleState;
            gm.OnGameReset += HandleReset;
            gm.OnPauseChanged += HandlePause;
            gm.OnGrabbed += HandleGrabbed;
            gm.OnRealigned += HandleRealigned;
            bound = true;
            RefreshAll();
        }

        void RefreshAll()
        {
            playing = gm.Current == ClawSessionBase.State.Playing;
            RefreshCounts();
            HandleTimeTick(gm.TimeRemaining);
            if (countdownRoot) countdownRoot.SetActive(false);
            SetBannerActive(false);
            bannerSticky = false;
        }

        void SetBannerActive(bool on)
        {
            if (bannerText) bannerText.gameObject.SetActive(on);
            if (bannerText2) bannerText2.gameObject.SetActive(on);
        }

        void Update()
        {
            TickPunch();
            if (bannerText != null && bannerText.gameObject.activeSelf && !bannerSticky && Time.time > bannerUntil)
                SetBannerActive(false);

            if (timeText == null || !playing || paused || gm == null || lastRemain > gm.WarnThresholdSec) return;
            bool on = (Time.time % blinkPeriod) < blinkPeriod * 0.5f;
            if (on == blinkOn) return;
            blinkOn = on;
            timeText.color = on ? warnColor : normalColor;
        }

        void TickPunch()
        {
            if (leftText == null) return;
            if (!leftScaleKnown) { leftBaseScale = leftText.rectTransform.localScale; leftScaleKnown = true; }
            float k = (Time.time - punchStart) / punchSeconds;
            if (k < 0f || k > 1.05f) return;
            k = Mathf.Clamp01(k);
            float s = 1f + 0.35f * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f); // 톡 커졌다 돌아옴
            leftText.rectTransform.localScale = leftBaseScale * s;
            leftText.color = Color.Lerp(Color.white, normalColor, k);
            if (k >= 1f) { leftText.rectTransform.localScale = leftBaseScale; leftText.color = normalColor; punchStart = -100f; }
        }

        void RefreshCounts()
        {
            if (gm == null) return;
            // 10-28 점수 = 키링 큐브만 — "큐브 n / N" + 그릇은 점수 없음 안내
            if (leftText)
                leftText.SetText(Loc.T("큐브 {0} / {1}\n<size=70%>그릇은 점수 없음</size>", "Cubes {0} / {1}\n<size=70%>Bowls don't score</size>"), gm.KeyringGot, gm.TargetCount); // 11-10 Loc
            if (rightText)
            {
                // 10-26 상품만(캡슐·폭탄 0)이면 남은 잡을 거리 대신 규칙 한 줄
                if (gm.FillerCount == 0 && gm.BombCount == 0) rightText.SetText(Loc.T("<size=70%>큐브 {0}개 모으기</size>", "<size=70%>Collect {0} cubes</size>"), gm.TargetCount);
                // 10-29 버전 B 폭탄 복귀(캡슐 없음) — 규칙 한 줄 + 남은 폭탄
                else if (gm.FillerCount == 0) rightText.SetText(Loc.T("<size=70%>큐브 {0}개 모으기\n폭탄 {1}</size>", "<size=70%>Collect {0} cubes\nBombs {1}</size>"), gm.TargetCount, Mathf.Max(0, gm.BombCount - gm.BombsHit));
                else rightText.SetText(Loc.T("<size=70%>캡슐 {0} · 폭탄 {1}</size>", "<size=70%>Capsules {0} · Bombs {1}</size>"), gm.FillerCount, Mathf.Max(0, gm.BombCount - gm.BombsHit));
            }
        }

        void HandleTimeTick(float remain)
        {
            lastRemain = remain;
            if (timeText == null) return;
            int mm = Mathf.FloorToInt(remain / 60f);
            int ss = Mathf.CeilToInt(remain - mm * 60f);
            if (ss >= 60) { mm++; ss -= 60; }
            timeText.SetText("{0}:{1:00}", mm, ss);
            if (gm != null && remain > gm.WarnThresholdSec && blinkOn)
            {
                blinkOn = false;
                timeText.color = normalColor;
            }
        }

        void HandleCountdown(int n)
        {
            if (countdownRoot) countdownRoot.SetActive(n > 0);
            if (countdownText && n > 0) countdownText.SetText("{0}", n);
            if (countdownText2 && n > 0) countdownText2.SetText("{0}", n);
        }

        void HandleState(ClawSessionBase.State s)
        {
            playing = s == ClawSessionBase.State.Playing;
            if (!playing && timeText != null && blinkOn) { blinkOn = false; timeText.color = normalColor; }
            if (playing) ShowBanner(Loc.T("키링 큐브를 모으세요", "Collect keyring cubes"), normalColor); // 10-28
            if (s == ClawSessionBase.State.Result && gm != null)
                ShowBanner(gm.LastSuccess ? Loc.T("성공!", "Success!") : Loc.T("시간 초과", "Time's up"), gm.LastSuccess ? normalColor : warnColor, sticky: true);
            RefreshCounts();
        }

        void HandlePause(bool p) => paused = p;

        void HandleReset()
        {
            playing = false;
            paused = false;
            lastRemain = float.MaxValue;
            blinkOn = false;
            if (timeText) timeText.color = normalColor;
            if (gm != null) RefreshAll();
        }

        void HandleGrabbed(ClawItem.Kind kind)
        {
            switch (kind)
            {
                case ClawItem.Kind.Keyring: ShowBanner(Loc.T("키링 획득!", "Keyring!"), new Color(0.4f, 1f, 1f)); punchStart = Time.time; break;
                case ClawItem.Kind.Ceramic:
                case ClawItem.Kind.Glass: ShowBanner(Loc.T("그릇 — 점수 없음", "Bowl — no score"), new Color(0.8f, 0.8f, 0.8f)); break; // 10-28 방해물 — 회색, 숫자 튐 없음
                case ClawItem.Kind.Bomb: ShowBanner(Loc.T("폭탄!", "Bomb!"), new Color(1f, 0.55f, 0.2f)); break;
                default: ShowBanner(Loc.T("캡슐 — 무득점", "Capsule — no score"), new Color(0.8f, 0.8f, 0.8f)); break;
            }
            RefreshCounts();
        }

        void HandleRealigned() => ShowBanner(Loc.T("정면 재정렬", "Recentered"), new Color(0.4f, 1f, 1f));

        void ShowBanner(string text, Color color, bool sticky = false)
        {
            if (bannerText == null) return;
            bannerText.text = text;
            bannerText.color = color;
            if (bannerText2) { bannerText2.text = text; bannerText2.color = color; }
            SetBannerActive(true);
            bannerSticky = sticky;
            bannerUntil = Time.time + bannerSeconds;
        }
    }
}
