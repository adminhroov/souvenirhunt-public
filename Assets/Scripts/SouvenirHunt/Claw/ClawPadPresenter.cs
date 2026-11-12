using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// Beam Pro 화면의 가로 오버레이(조이스틱·잡기·홈)를 게임 상태에 맞춰 관리한다. 3차(빔 미사용)부터 **모든 상태에서 켜져 있고**
    /// 잡기 라벨만 바뀐다: Ready "영상 시청"(영상 단계 끔: "잡기(시작)") · Video "영상 시청 종료 (n초)"→"영상 시청 종료"(대기 중 회색) ·
    /// Reveal "잠시만요"(회색) · Guide "START"(디졸브로 기계 등장 뒤) / "잡기(확인)"(전환 끔) · Countdown "잡기" · Result "보상 확인"/"다시 시작" ·
    /// Reward "다시 시작"(경품 요청 중 "경품 요청 중" 회색, 실패해 다시 요청 가능 "경품 다시 요청"). 10-26 [다시 시작]은 화면에 들어온 뒤 1.5초 회색(연타 막이).
    /// Playing 중 라벨·활성은 게임이 정한다(ClawSessionBase.PlayingGrabLabel 등 — 미션형은 조준 "잡기"·집게 동작 중 회색).
    /// 종료/취소 다이얼로그가 열리면 잡기 "확인", 조이스틱 "← 종료 · 취소 →"로 안내(플레이어가 아직 파일을 보고 있을 때의 폰 쪽 피드백).
    /// 오버레이는 SDK가 씬 로드 전 만드는 프리팹 안(ClawPadRegistrar 등록)이라 늦게 나타날 수 있다 → 5초까지 재시도.
    /// </summary>
    public class ClawPadPresenter : MonoBehaviour
    {
        ClawSessionBase gm;
        bool bound, padBound, warned;
        float retryUntil;

        void OnEnable()
        {
            retryUntil = Time.unscaledTime + 5f;
            ClawPadInput.Resumed += OnPadResumed;
            Loc.Changed += OnPadResumed; // 11-10 언어가 바뀌면 라벨 다시
            TryBind();
        }

        void OnPadResumed()
        {
            if (gm == null || !padBound) return;
            if (gm.ExitDialogOpen) HandleDialog(true); else Apply(gm.Current);
        }

        void Start() => TryBind();

        void OnDisable()
        {
            ClawPadInput.Resumed -= OnPadResumed;
            Loc.Changed -= OnPadResumed;
            if (bound && gm != null)
            {
                gm.OnStateChanged -= Apply;
                gm.OnPadLabelsChanged -= HandleLabels;
                gm.OnExitDialogChanged -= HandleDialog;
            }
            bound = false;
            padBound = false;
            ClawPadInput.SetRootActive(false); // 프리팹은 씬보다 오래 살 수 있다
        }

        bool lastRestartAllowed;

        void Update()
        {
            if (!bound) TryBind();
            // 8차: Video 동안 종료 버튼 카운트다운·활성 표시(1초 단위)

            // 10-26 [다시 시작] 연타 막이가 풀리면 회색 → 활성
            if (gm != null && padBound && !gm.ExitDialogOpen && gm.RestartAllowed != lastRestartAllowed) Apply(gm.Current);
            if (padBound || gm == null) return;
            if (ClawPadInput.Root != null)
            {
                padBound = true;
                Apply(gm.Current);
            }
            else if (!warned && Time.unscaledTime > retryUntil)
            {
                warned = true;
                Debug.LogWarning("[ClawPad] Beam Pro 오버레이 없음 — 메뉴 XREAL/14 재실행 필요. 에디터는 키보드 폴백(WASD/Space/H)으로 계속");
            }
        }

        void TryBind()
        {
            if (bound) return;
            gm = ClawSessionBase.Instance;
            if (gm == null) return;
            gm.OnStateChanged += Apply;
            gm.OnPadLabelsChanged += HandleLabels;
            gm.OnExitDialogChanged += HandleDialog;
            bound = true;
            if (ClawPadInput.Root != null) { padBound = true; Apply(gm.Current); }
        }

        void Apply(ClawSessionBase.State s)
        {
            ClawPadInput.SetRootActive(true); // 빔 미사용 — 부팅부터 Reward까지 내내 오버레이
            if (gm != null && gm.ExitDialogOpen) { ClawPadInput.GrabEnabled = true; HandleDialog(true); return; }
            bool playing = s == ClawSessionBase.State.Playing && gm != null;
            bool sending = s == ClawSessionBase.State.Reward && gm != null && gm.PrizeSending;
            lastRestartAllowed = gm != null && gm.RestartAllowed;
            bool restartWait = gm != null && gm.RestartOffered && !lastRestartAllowed; // 막 들어온 끝 화면 — 잠깐 회색
            ClawPadInput.GrabEnabled = !sending && !restartWait && s != ClawSessionBase.State.Reveal && (!playing || gm.PlayingGrabEnabled);
            string label = s switch
            {
                ClawSessionBase.State.Ready => Loc.T("공간 열기", "Open Space"),
                ClawSessionBase.State.Reveal => Loc.T("잠시만요", "Please wait"),
                ClawSessionBase.State.Guide => gm != null && gm.MachineShown ? "START" : Loc.T("잡기\n(확인)", "Grab\n(OK)"),
                ClawSessionBase.State.Result => gm != null && gm.RewardEligible ? Loc.T("보상 확인", "View Reward") : Loc.T("다시 시작", "Restart"),
                ClawSessionBase.State.Reward => gm != null && gm.Prize != null && gm.Prize.CanRetry ? Loc.T("경품\n다시 요청", "Retry\nPrize") : sending ? Loc.T("경품\n요청 중", "Requesting\nPrize") : Loc.T("다시 시작", "Restart"),
                ClawSessionBase.State.Playing when playing => gm.PlayingGrabLabel,
                _ => Loc.T("잡기", "Grab"),
            };
            ClawPadInput.SetGrabLabel(label);
            bool end = s == ClawSessionBase.State.Result || s == ClawSessionBase.State.Reward;
            ClawPadInput.SetStickLabel(playing ? gm.PlayingStickLabel : end ? Loc.T("게임 종료", "Game over") : Loc.T("조이스틱", "Joystick"));
        }

        void HandleLabels()
        {
            if (gm != null && padBound) Apply(gm.Current);
        }

        void HandleDialog(bool open)
        {
            if (open)
            {
                ClawPadInput.SetGrabLabel(Loc.T("확인", "OK"));
                ClawPadInput.SetStickLabel(Loc.T("← 종료 · 취소 →", "← Quit · Cancel →"));
            }
            else if (gm != null) Apply(gm.Current);
        }
    }
}
