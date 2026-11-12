using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 결과 패널(10-15 미션형: 성공 / 시간 초과). ResultPanelController와 같은 규칙 — Result에서만 표시,
    /// [보상 확인] 월드 버튼(콜라이더+XRSimpleInteractable)은 보상 대상일 때만 켬(숨김 시 콜라이더도 off).
    /// 같은 동작을 Beam Pro 잡기 버튼("보상 확인" 라벨)으로도 할 수 있다(ClawGameManager.Update).
    /// 10-26 보상 대상이 아니면(시간 초과) 같은 자리에 "[다시 시작] = 처음 화면" 막대(Beam Pro 잡기 = 다시 시작 — ClawSessionBase.Restart).
    /// </summary>
    public class ClawResultPanel : MonoBehaviour
    {
        [SerializeField] GameObject panelRoot;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text detailText;
        [SerializeField] TMP_Text hintText;
        [SerializeField] XRSimpleInteractable rewardButton;
        [SerializeField] GameObject rewardButtonVisual;
        [SerializeField] GameObject restartVisual;   // 10-26 메뉴 30 — 없으면(예전 씬) 문구만

        /// <summary>셀프테스트 — [다시 시작] 막대가 보이는 중</summary>
        public bool RestartShown => restartVisual != null && restartVisual.activeInHierarchy;

        ClawGameManager gm;
        bool bound;
        Collider rewardCollider;

        void Awake()
        {
            if (rewardButton != null)
            {
                rewardCollider = rewardButton.GetComponent<Collider>();
                rewardButton.selectEntered.AddListener(_ => OnRewardClicked());
            }
        }

        void OnEnable() => TryBind();
        void Start() => TryBind();

        void TryBind()
        {
            if (bound) return;
            gm = ClawGameManager.Instance;
            if (gm == null) { SetVisible(false, false); return; }
            gm.OnGameResult += HandleResult;
            gm.OnGameReset += HandleReset;
            gm.OnStateChanged += HandleState;
            bound = true;
            SetVisible(gm.Current == ClawGameManager.State.Result, gm.RewardEligible);
        }

        void OnDisable()
        {
            if (!bound) return;
            gm.OnGameResult -= HandleResult;
            gm.OnGameReset -= HandleReset;
            gm.OnStateChanged -= HandleState;
            bound = false;
        }

        /// <summary>10-15 미션형(8d3d1ed 문구): 성공! "그릇 n/N 획득 · 12.3초" / 시간 초과 "그릇 n/N 획득\n아쉽지만 시간이 종료되었습니다". 10-28 큐브 문구</summary>
        void HandleResult(bool success, int got, int target, float elapsed)
        {
            bool eligible = gm != null && gm.RewardEligible;
            // 11-10(사용자) 실패(시간 초과 등) 큰 문구 = "즐겁게 해주셔서 감사합니다" — 한 줄에 들어가게 글자 자동 축소(최대는 원래 크기)
            if (titleText)
            {
                FitTitleOneLine();
                titleText.text = success ? Loc.T("성공!", "Success!") : Loc.T(FailTitle, "Thank you for playing!");
            }
            // 10-28 점수 = 키링 큐브만(got = 넣은 큐브 수) — 넣은 그릇은 작게 "점수 없음"
            int bowlsIn = gm != null ? gm.CeramicGot + gm.GlassGot : 0;
            string bowlNote = bowlsIn > 0 ? Loc.T($"\n<size=75%>그릇 {bowlsIn}개는 점수 없음</size>", $"\n<size=75%>{bowlsIn} bowl(s) — no score</size>") : "";
            Loc.Fit(detailText);
            Loc.Fit(hintText);
            if (detailText)
                detailText.text = success
                    ? Loc.T($"키링 {target}개 완성 · {elapsed:0.0}초{bowlNote}", $"{target} keyrings collected · {elapsed:0.0}s{bowlNote}")
                    : Loc.T($"<size=75%>시간 종료 · 키링 큐브 {got}/{target} 획득</size>", $"<size=75%>Time's up · Keyring cubes {got}/{target}</size>"); // 11-10 보조 정보는 작은 줄로
            if (hintText)
                hintText.text = eligible
                    ? Loc.T("Beam Pro [보상 확인]을 눌러 보상을 확인해 주세요", "Press [View Reward] on Beam Pro")
                    : Loc.T("<size=80%>Beam Pro [다시 시작]을 누르면 처음 화면으로 돌아갑니다</size>", "<size=80%>Press [Restart] on Beam Pro to go back to the start</size>");
            SetVisible(true, eligible);
        }

        /// <summary>11-10 실패 결과 제목(셀프테스트도 이 문구를 본다)</summary>
        public const string FailTitle = "즐겁게 해주셔서 감사합니다";

        bool titleFitted;

        /// <summary>제목 상자(860×130)에 긴 문구가 두 줄로 넘치지 않게 — 줄바꿈 없이 자동 축소(최대 = 씬의 원래 크기)</summary>
        void FitTitleOneLine()
        {
            if (titleFitted || titleText == null) return;
            titleFitted = true;
            float max = titleText.fontSize;
            titleText.textWrappingMode = TextWrappingModes.NoWrap;
            titleText.enableAutoSizing = true;
            titleText.fontSizeMax = max;
            titleText.fontSizeMin = Mathf.Min(max, 40f);
        }

        void HandleState(ClawGameManager.State s)
        {
            if (s != ClawGameManager.State.Result) SetVisible(false, false);
        }

        void HandleReset() => SetVisible(false, false);

        void OnRewardClicked()
        {
            if (gm == null) gm = ClawGameManager.Instance;
            gm?.ConfirmReward();
        }

        void SetVisible(bool show, bool buttonOn)
        {
            if (panelRoot) panelRoot.SetActive(show);
            bool btn = show && buttonOn;
            if (rewardButtonVisual) rewardButtonVisual.SetActive(btn);
            if (restartVisual) restartVisual.SetActive(show && !buttonOn);
            if (rewardButton) rewardButton.enabled = btn;
            if (rewardCollider) rewardCollider.enabled = btn;
        }
    }
}
