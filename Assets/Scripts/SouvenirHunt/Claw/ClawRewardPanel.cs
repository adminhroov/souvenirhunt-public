using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// 보상 화면(SCR-06) — RewardPanelController와 동일 내용(config rewardN.text 나열 + 수령 안내). Reward에서만 표시.
    /// 버튼·콜라이더 없음. 다음 진행은 Beam Pro [다시 시작](10-26 — 맨 아래 작은 줄로 안내) 또는 운영자 리셋.
    /// 10-06: 경품 연동이 켜져 있으면 안내 문구 자리에 요청 상태(요청 중 → 접수 완료 #번호 / 실패 사유)와 참가자 DID 끝자리를 표시.
    /// 10-12(VIP 인증 연출 기획): 보상 카드 이미지(reward_card.png)가 Scale·Fade로 등장하고 보상 효과음(config audio=true).
    /// 카드 연출은 표시만 — 경품 지급 성공 여부는 PrizeLink 상태(실제 API 응답)로 따로 보여준다(기획서 주의 사항).
    /// 10-28(사용자 "미션 뒤 상품 소개는 큐브(키링)만", 결정: 이름 + 3D 큐브 회전): 상품 이름 = config clawRewardText, 패널 위에 키링 큐브가 돈다.
    /// </summary>
    public class ClawRewardPanel : MonoBehaviour
    {
        [SerializeField] GameObject panelRoot;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text rewardText;
        [SerializeField] TMP_Text guideText;
        [SerializeField] Image card;               // 보상 카드(없으면 연출 생략)
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip rewardClip;
        [SerializeField] float appearSeconds = 0.35f;
        [Tooltip("10-28 보상 화면의 키링 큐브 3D 모델(메뉴 30 — 패널 위·눈 쪽). 패널과 함께 켜지고 천천히 돈다")]
        [SerializeField] Transform prizeModel;
        const float PrizeSpinDegPerSec = 50f;

        ClawSessionBase gm;
        PrizeLink prize;
        bool bound, showing;
        float shownAt;
        Vector3 baseScale = Vector3.one;

        /// <summary>셀프테스트 — 보상 카드·효과음 배선(메뉴 30/35)</summary>
        public bool HasCard => card != null && card.sprite != null;
        public bool HasSound => audioSource != null && rewardClip != null;

        void Awake()
        {
            if (panelRoot != null) baseScale = panelRoot.transform.localScale;
        }

        void OnEnable() => TryBind();
        void Start() => TryBind();

        void TryBind()
        {
            if (bound) return;
            gm = ClawSessionBase.Instance;
            if (gm == null) { Show(false); return; }
            gm.OnStateChanged += HandleState;
            gm.OnGameReset += HandleReset;
            prize = gm.Prize;
            if (prize != null) prize.OnChanged += HandlePrize;
            bound = true;
            HandleState(gm.Current);
        }

        void OnDisable()
        {
            if (!bound) return;
            gm.OnStateChanged -= HandleState;
            gm.OnGameReset -= HandleReset;
            if (prize != null) prize.OnChanged -= HandlePrize;
            bound = false;
        }

        void HandleState(ClawSessionBase.State s)
        {
            bool show = s == ClawSessionBase.State.Reward;
            if (show) Fill();
            Show(show);
        }

        void HandleReset() => Show(false);

        void HandlePrize()
        {
            if (gm != null && gm.Current == ClawSessionBase.State.Reward) Fill();
        }

        void Fill()
        {
            if (titleText) titleText.text = Loc.T("보상 안내", "Your Reward");
            // 10-28 상품 소개 = 큐브(키링)만 — config clawRewardText 한 줄(보상 목록 rewardN 대신). 11-10 영어 화면 = clawRewardTextEn
            string prizeName = gm != null && gm.Config != null ? (Loc.En ? gm.Config.clawRewardTextEn : gm.Config.clawRewardText) : "";
            string items = string.IsNullOrWhiteSpace(prizeName) ? "" : $"<nobr>{prizeName}</nobr>";
            if (string.IsNullOrEmpty(items)) items = Loc.T("실물 기념품", "Souvenir gift");
            // 경품(NFT) 종류 한 줄 — 10-28부터 키링 큐브만(큐브를 하나라도 넣었으면 "경품: 키링")
            if (gm is ClawGameManager cg && cg.PrizeKind.HasValue)
                items = Loc.T($"<size=80%>경품: {ClawGameManager.KindLabel(cg.PrizeKind.Value)}</size>\n", $"<size=80%>Prize: {ClawGameManager.KindLabelEn(cg.PrizeKind.Value)}</size>\n") + items;
            Loc.Fit(rewardText); // 11-10 영어 상품 이름이 길어(<nobr>) 상자를 넘지 않게
            if (rewardText) rewardText.text = items;
            // 10-26 [다시 시작] 안내는 맨 아래 작은 줄(참가자 끝자리와 한 줄)
            string tail = gm != null && gm.RestartOffered ? RestartTailText : "";
            Loc.Fit(guideText);
            if (guideText) guideText.text = PrizeGuide(prize, tail) ?? Loc.T("출구에서 실물 기념품을 수령하세요\n진행요원에게 기기를 전달해 주세요", "Pick up your souvenir at the exit\nPlease hand the device to the staff") + (tail.Length > 0 ? $"\n<size=70%>{tail}</size>" : "");
        }

        public const string RestartTail = "Beam Pro [다시 시작] → 처음 화면";
        /// <summary>11-10 지금 언어의 [다시 시작] 꼬리 줄</summary>
        public static string RestartTailText => Loc.T(RestartTail, "Beam Pro [Restart] → back to start");

        /// <summary>경품 연동 안내 문구 — 연동이 꺼져 있거나 아직 요청 전이면 null(기존 문구). tail = 맨 아래 작은 줄에 덧붙일 안내(10-26 [다시 시작])</summary>
        public static string PrizeGuide(PrizeLink p, string tail = "")
        {
            if (p == null || !p.Active) return null;
            string who0 = Loc.T("참가자", "Participant");
            string small = string.IsNullOrEmpty(p.DidTail) ? (tail ?? "") : string.IsNullOrEmpty(tail) ? $"{who0} {p.DidTail}" : $"{who0} {p.DidTail} · {tail}";
            string who = small.Length > 0 ? $"\n<size=70%>{small}</size>" : "";
            return p.Claim switch
            {
                // 11-10 실패 사유(p.Reason)는 진행요원용 진단 문구라 번역하지 않는다
                PrizeLink.ClaimState.Sending => Loc.T("경품 요청 중…", "Requesting your prize…") + who,
                PrizeLink.ClaimState.Accepted => (p.PurchaseNo > 0 ? Loc.T($"경품 요청 완료 (접수번호 {p.PurchaseNo})", $"Prize requested (No. {p.PurchaseNo})") : Loc.T("경품 요청 완료", "Prize requested")) +
                                                 Loc.T("\n진행요원에게 기기를 전달해 주세요", "\nPlease hand the device to the staff") + who,
                PrizeLink.ClaimState.Rejected or PrizeLink.ClaimState.Failed or PrizeLink.ClaimState.Skipped =>
                    Loc.T($"경품 요청 실패: {p.Reason}\n", $"Prize request failed: {p.Reason}\n") +
                    (p.CanRetry ? Loc.T("진행요원 확인 후 [경품 다시 요청]을 눌러 주세요", "After staff check, press [Retry Prize]") : Loc.T("진행요원에게 기기를 전달해 주세요", "Please hand the device to the staff")) + who,
                _ => null,
            };
        }

        void Show(bool on)
        {
            if (on && !showing)
            {
                shownAt = Time.time;
                if (audioSource != null && rewardClip != null && gm != null && gm.Config != null && gm.Config.audio)
                    audioSource.PlayOneShot(rewardClip);
            }
            showing = on;
            if (panelRoot) panelRoot.SetActive(on);
            if (prizeModel) prizeModel.gameObject.SetActive(on); // 10-28 큐브 모델도 패널과 함께
            Animate();
        }

        void Update()
        {
            if (!showing) return;
            Animate();
            if (prizeModel) prizeModel.Rotate(0f, PrizeSpinDegPerSec * Time.deltaTime, 0f);
        }

        /// <summary>등장: 패널 0.85→1배, 카드 투명도 0→1(appearSeconds). 끝나면 그대로</summary>
        void Animate()
        {
            if (panelRoot == null) return;
            float k = showing ? Mathf.Clamp01((Time.time - shownAt) / Mathf.Max(0.01f, appearSeconds)) : 1f;
            float e = 1f - (1f - k) * (1f - k);
            panelRoot.transform.localScale = baseScale * Mathf.Lerp(0.85f, 1f, e);
            if (card != null)
            {
                var c = card.color;
                c.a = e;
                card.color = c;
            }
        }
    }
}
