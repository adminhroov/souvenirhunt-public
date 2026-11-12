using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 미션형(10-15 복원 — git 8d3d1ed 규칙, 사용자 결정). 공통 흐름(Ready·Video·Reveal·Guide·Countdown·Result·Reward, 재정렬,
    /// 종료 다이얼로그, 일시정지)은 ClawSessionBase, 여기는 규칙만.
    ///   기계 안 = 세라믹 그릇·유리 그릇(목표물) + 캡슐(무득점) + 폭탄. 제한 시간 clawTimeLimitSeconds(45) 안에 그릇 clawTargetCount(3)개를
    ///   배출구에 넣으면 즉시 성공(종류 무관). 잡기 1회 = 1개(가이드가 가리킨 것). 캡슐 투입 = 무득점(시간만 낭비).
    ///   폭탄 = 집게가 닿는 순간 폭발 — 기계 안 물체가 크게 흩어지고 기계가 흔들린다(감점 없음, 폭탄은 다시 나오지 않음).
    ///   경품 = 넣은 그릇 중 많은 종류(동점이면 마지막에 넣은 종류)의 nftId(prizeNftIdCeramic / prizeNftIdGlass) — 오스리움은 참가자당 1회라 게임당 1개.
    ///   10-22 키링(KoreaCubeKeyring)도 목표물(사용자: "그릇처럼 목표물에 포함"). 10-22 규칙 변경(사용자: "3개를 다 채운 제품, 대신 시간을 1분 30초로"):
    ///   **같은 종류 N개(clawTargetCount)를 먼저 채우면 성공**, 경품 = 그 종류(prizeNftIdCeramic·Glass·Keyring). 시간 초과(부분 보상)면 많은 종류(동점이면 마지막).
    ///   10-28 규칙 변경(사용자 "55초에 큐브 5개 성공, 미션 뒤 상품 소개는 큐브만", 결정: 그릇은 방해물): **키링 큐브 N개를 넣으면 성공**,
    ///   그릇은 넣어도 점수·진행 없음(카운트만). 경품 = 키링(prizeNftIdKeyring). 집게는 한 번에 한 개만 잡힌다(ClawHead).
    /// </summary>
    public class ClawGameManager : ClawSessionBase
    {
        public static new ClawGameManager Instance => ClawSessionBase.Instance as ClawGameManager;

        /// <summary>Playing 안 단계 — 조준(잡기 가능) / 집게 동작 중</summary>
        public enum PlayPhase { None, Aim, Busy }
        public PlayPhase Phase { get; private set; }

        [Header("미션형 연출")]
        [SerializeField] ClawExplosionVfx[] explosions;   // 2개 번갈아(연속 폭발 대비)
        [Header("비주얼 2차(10-15) 연출")]
        [SerializeField] ClawMachineFx machineFx;          // 기계 네온·바닥 그리드 — 획득·폭발·성공 번쩍임, 경고 맥동
        [SerializeField] ClawExplosionVfx[] collectFx;     // 그릇 획득 빛 파동·반짝이(2개 번갈아)
        static readonly Color FxCyan = new Color(0.35f, 1f, 1f), FxRed = new Color(1f, 0.25f, 0.12f), FxGold = new Color(1f, 0.82f, 0.3f);

        // 규칙 — config.txt가 소스오브트루스(여기는 로드 전 기본값)
        int targetCount = 3, ceramicCount = 3, glassCount = 3, keyringCount = 3, fillerCount = 40, bombCount = 4;
        float timeLimitSec = 45f;
        bool rewardOnPartial;
        const float DeliverGraceSec = 0.5f;   // 시간이 다 됐을 때 배출구로 떨어지는 중인 그릇은 이만큼 기다려 인정

        // ─ 미션형 이벤트 ─
        public event Action<int, int> OnProgress;                 // (획득 큐브, 목표) — 10-28 큐브만
        public event Action<ClawItem.Kind> OnGrabbed;             // 배출구 투입·폭발 시 종류(스코어보드 배너)
        public event Action<bool, int, int, float> OnGameResult;  // (성공, 획득 큐브, 목표, 경과초)
        public event Action<Vector3> OnBombExploded;

        // ─ 표시 정보 ─
        public int TargetCount => targetCount;
        public int CeramicCount => ceramicCount;
        public int GlassCount => glassCount;
        public int KeyringCount => keyringCount;
        /// <summary>목표물(그릇 2종 + 키링) 배치 수</summary>
        public int TargetItemCount => ceramicCount + glassCount + keyringCount;
        public int FillerCount => fillerCount;
        public int BombCount => bombCount;
        public int Collected => delivered.Count;
        public IReadOnlyList<ClawItem.Kind> DeliveredKinds => delivered;
        public int CeramicGot { get; private set; }
        public int GlassGot { get; private set; }
        /// <summary>10-28 넣은 키링 큐브 수 — 이게 목표(큐브 N개)에 닿으면 성공(그릇은 점수 없음)</summary>
        public int KeyringGot { get; private set; }
        public float TimeLimit => timeLimitSec;
        public float TimeRemaining => Current == State.Playing ? Mathf.Max(remain, 0f) : timeLimitSec;
        public bool LastSuccess { get; private set; }
        public int Grabs => grabs;
        public int BombsHit => bombsHit;
        /// <summary>경품 대상 종류 — 10-28 키링 큐브를 하나라도 넣었으면 키링, 아니면 null(그릇은 점수 없음)</summary>
        public ClawItem.Kind? PrizeKind => KeyringGot > 0 ? ClawItem.Kind.Keyring : (ClawItem.Kind?)null;
        public override string StateName => Current == State.Playing ? $"Playing/{Phase}" : Current.ToString();

        // ─ 오버레이 라벨: 조준 "잡기" · 집게 동작 중 회색 ─
        public override bool PlayingGrabEnabled => Phase == PlayPhase.Aim;
        public override string PlayingGrabLabel => Loc.T("잡기", "Grab");
        public override string PlayingStickLabel => Loc.T("조이스틱", "Joystick");

        readonly List<ClawItem.Kind> delivered = new(8);
        int grabs, bombsHit, nextExplosion, nextCollect;
        float remain, graceUntil = -1f;
        int lastWholeSec = int.MinValue;

        protected override void Awake()
        {
            base.Awake();
            if (ClawSessionBase.Instance != this) return;
            ClawVisual.InvalidateAniso(); // 이방성 필터는 config(clawAniso)대로 첫 리셋에서 적용
            if (head != null)
            {
                head.OnDelivered += HandleDelivered;
                head.OnBombTouched += HandleBombTouched;
                head.OnCycleEnded += HandleCycleEnded;
            }
        }

        protected override void OnDestroy()
        {
            if (head != null)
            {
                head.OnDelivered -= HandleDelivered;
                head.OnBombTouched -= HandleBombTouched;
                head.OnCycleEnded -= HandleCycleEnded;
            }
            base.OnDestroy();
        }

#if UNITY_EDITOR
        /// <summary>셀프테스트 전용 — 남은 시간 강제(시간 초과 경로 검증)</summary>
        public void EditorSetTimeRemaining(float sec) => remain = sec;
#endif

        /// <summary>
        /// 목표물 종류별 경품 — 많은 종류, 동점이면 동점 종류 중 마지막에 넣은 것. 없으면 null(요청 안 함). 순수 함수(셀프테스트).
        /// 10-22 세라믹·유리·키링 셋으로 일반화(그릇 둘만일 때와 결과 같음)
        /// </summary>
        public static ClawItem.Kind? PickPrizeKind(IReadOnlyList<ClawItem.Kind> kinds)
        {
            if (kinds == null) return null;
            int ce = 0, gl = 0, ke = 0;
            foreach (var k in kinds)
            {
                if (k == ClawItem.Kind.Ceramic) ce++;
                else if (k == ClawItem.Kind.Glass) gl++;
                else if (k == ClawItem.Kind.Keyring) ke++;
            }
            int max = Mathf.Max(ce, Mathf.Max(gl, ke));
            if (max == 0) return null;
            for (int i = kinds.Count - 1; i >= 0; i--)
            {
                var k = kinds[i];
                int n = k == ClawItem.Kind.Ceramic ? ce : k == ClawItem.Kind.Glass ? gl : k == ClawItem.Kind.Keyring ? ke : -1;
                if (n == max) return k;
            }
            return null;
        }

        /// <summary>
        /// 10-22 규칙: 넣은 순서대로 세어 같은 종류 target개를 먼저 채운 종류. 아무것도 못 채웠으면(시간 초과·부분 보상) 많은 종류(동점이면 마지막). 순수 함수(셀프테스트)
        /// 10-28 미사용(경품 = 키링 큐브만 — PrizeKind). 되돌리기·셀프테스트용으로 남김
        /// </summary>
        public static ClawItem.Kind? PickPrizeKind(IReadOnlyList<ClawItem.Kind> kinds, int target)
        {
            if (kinds == null) return null;
            int ce = 0, gl = 0, ke = 0;
            foreach (var k in kinds)
            {
                int n = k == ClawItem.Kind.Ceramic ? ++ce : k == ClawItem.Kind.Glass ? ++gl : k == ClawItem.Kind.Keyring ? ++ke : 0;
                if (target > 0 && n >= target) return k;
            }
            return PickPrizeKind(kinds);
        }

        public static string KindLabel(ClawItem.Kind k) => k switch
        {
            ClawItem.Kind.Ceramic => "세라믹 그릇",
            ClawItem.Kind.Glass => "유리 그릇",
            ClawItem.Kind.Capsule => "캡슐",
            ClawItem.Kind.Keyring => "키링",
            _ => "폭탄",
        };

        /// <summary>11-10 영어 화면용 종류 이름(보상 화면 "Prize: …")</summary>
        public static string KindLabelEn(ClawItem.Kind k) => k switch
        {
            ClawItem.Kind.Ceramic => "Ceramic bowl",
            ClawItem.Kind.Glass => "Glass bowl",
            ClawItem.Kind.Capsule => "Capsule",
            ClawItem.Kind.Keyring => "Keyring",
            _ => "Bomb",
        };

        protected override void PlaceItems()
        {
            if (pool == null || basket == null) return;
            pool.ReturnAll();
            basket.ResetPile(); // 10-20 받침 더미 조각 전부 복원(지난 판 승격·배출분)
            basket.SpawnRealTopLayer(); // 10-22 받침 맨 윗층 = 진짜 캡슐
            scratch.Clear();
            void Add(ClawItem.Kind k, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    var it = pool.Get();
                    if (it == null) return;
                    it.SetKind(k, UnityEngine.Random.Range(0, 64));
                    scratch.Add(it);
                }
            }
            Add(ClawItem.Kind.Ceramic, ceramicCount);
            Add(ClawItem.Kind.Glass, glassCount);
            Add(ClawItem.Kind.Keyring, keyringCount);
            Add(ClawItem.Kind.Bomb, bombCount);
            Add(ClawItem.Kind.Capsule, fillerCount);
            basket.PlaceAndSettle(scratch);
            Debug.Log($"[Claw] 배치 세라믹 {ceramicCount}·유리 {glassCount}·키링 {keyringCount}·캡슐 {fillerCount}·폭탄 {bombCount} = {scratch.Count}개 + 진짜 맨 윗층 캡슐 {basket.RealTopCount}, 정착 {basket.LastSettleMs:F0}ms (셀 {basket.CellCount}, 0층 {basket.LowerCellCount}, 목표물 안전 구역 칸 {basket.LastSafeCells})");
        }

        protected override void OnPlayingStarted()
        {
            remain = timeLimitSec;
            lastWholeSec = int.MinValue;
            graceUntil = -1f;
            SetPhase(PlayPhase.Aim);
            TickTimer(0f);
            Debug.Log($"[Cycle] START claw 미션 큐브 {targetCount}개 · {timeLimitSec:F0}s · 아이템 {pool?.ActiveCount} settle={LastSettleMs:F0}ms");
        }

        void SetPhase(PlayPhase p)
        {
            Phase = p;
            if (head != null) head.GuideEnabled = p == PlayPhase.Aim;
            RaisePadLabelsChanged();
        }

        protected override void TickPlaying()
        {
            bool grab = ClawPadInput.TryConsumeGrab(); // 누름 1회 = 동작 1회. 진행 중 누름은 소비만(버퍼링 없음)
            TickTimer(Time.deltaTime);
            if (remain <= 0f)
            {
                // 큐브가 배출구로 떨어지는 중이면 잠깐 기다려 인정(10-28 그릇은 점수 없음 — 기다리지 않음)
                bool dropping = head != null && ((HoldsTarget(head) &&
                                (head.Current == ClawHead.Phase.DropLower || head.Current == ClawHead.Phase.Drop)) ||
                                head.FallingTargetPending); // 10-22 놓은 뒤 떨어지는 중(바닥 닿으면 획득)
                if (dropping)
                {
                    if (graceUntil < 0f) graceUntil = Time.time + DeliverGraceSec;
                    if (Time.time < graceUntil) return;
                }
                EndGame(false);
                return;
            }
            if (Phase == PlayPhase.Aim && grab && head != null && !head.Busy)
            {
                grabs++;
                SetPhase(PlayPhase.Busy);
                head.BeginGrab();
            }
        }

        /// <summary>쥔 것 중 점수 상품(10-28 키링 큐브)이 있음</summary>
        static bool HoldsTarget(ClawHead h)
        {
            var list = h.HeldItems;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].IsScoring) return true;
            return false;
        }

        void TickTimer(float dt)
        {
            remain -= dt;
            float shown = Mathf.Max(remain, 0f);
            int whole = Mathf.CeilToInt(shown);
            if (machineFx != null) machineFx.SetWarn(Current == State.Playing && shown > 0f && shown <= WarnThresholdSec);
            if (whole == lastWholeSec) return;
            lastWholeSec = whole;
            RaiseTimeTick(shown);
        }

        void HandleCycleEnded()
        {
            if (Current != State.Playing) return;
            SetPhase(PlayPhase.Aim);
        }

        void HandleDelivered(ClawItem item)
        {
            if (Current != State.Playing || item == null) return;
            var chute = basket != null ? basket.ChuteWorld : item.transform.position;
            EventUtil.SafeInvoke(OnGrabbed, item.ItemKind);
            if (item.IsTarget && !item.IsScoring)
            {
                // 10-28 그릇은 방해물 — 카운트만(점수·진행·햅틱 없음), 회색 연출
                delivered.Add(item.ItemKind);
                if (item.ItemKind == ClawItem.Kind.Ceramic) CeramicGot++;
                else GlassGot++;
                RaiseCountsChanged();
                CollectVfxPool.Instance?.Play(chute + Vector3.up * 0.05f, new Color(0.6f, 0.6f, 0.6f), showLabel: false);
                if (machineFx != null) machineFx.Ripple(chute, new Color(0.45f, 0.45f, 0.5f));
                Debug.Log($"[Claw] 그릇 — 점수 없음 ({KindLabel(item.ItemKind)}, 큐브 {KeyringGot}/{targetCount} · 그릇 {CeramicGot + GlassGot})");
            }
            else if (item.IsScoring)
            {
                delivered.Add(item.ItemKind);
                KeyringGot++;
                EventUtil.SafeInvoke(OnProgress, KeyringGot, targetCount);
                RaiseCountsChanged();
                ClawHaptics.Pulse(0.4f, 0.1f);
                CollectVfxPool.Instance?.Play(chute + Vector3.up * 0.05f, new Color(0f, 2.5f, 2.5f));
                if (collectFx != null && collectFx.Length > 0)
                {
                    var fx = collectFx[nextCollect++ % collectFx.Length];
                    if (fx != null) fx.Play(chute + Vector3.up * 0.06f, Config != null && Config.audio);
                }
                if (machineFx != null) { machineFx.Flash(FxCyan, 0.6f); machineFx.Ripple(chute, FxCyan); }
                Debug.Log($"[Claw] {KindLabel(item.ItemKind)} 획득 — 큐브 {KeyringGot}/{targetCount} · 그릇 {CeramicGot + GlassGot}(점수 없음)");
                if (KeyringGot >= targetCount) EndGame(true); // 10-28 큐브 N개
            }
            else
            {
                CollectVfxPool.Instance?.Play(chute + Vector3.up * 0.05f, new Color(0.6f, 0.6f, 0.6f), showLabel: false); // 무득점 — "+1" 없음(7차)
                if (machineFx != null) machineFx.Ripple(chute, new Color(0.45f, 0.45f, 0.5f));
                Debug.Log("[Claw] 캡슐 — 무득점");
            }
        }

        void HandleBombTouched(ClawItem bomb, Vector3 pos)
        {
            if (Current != State.Playing) return;
            bombsHit++;
            int moved = basket != null ? basket.Explode(pos, bomb) : 0;
            if (explosions != null && explosions.Length > 0)
            {
                var fx = explosions[nextExplosion++ % explosions.Length];
                if (fx != null) fx.Play(pos, Config != null && Config.audio);
            }
            else CollectVfxPool.Instance?.Play(pos, new Color(2.5f, 1.2f, 0.2f), showLabel: false);
            if (shake != null) shake.Shake(0.012f * ClawLayout.MachineScale, 0.8f);
            if (machineFx != null) { machineFx.Flash(FxRed, 0.7f); machineFx.Ripple(pos, new Color(1f, 0.45f, 0.15f)); }
            ClawHaptics.Pulse(1f, 0.35f);
            EventUtil.SafeInvoke(OnGrabbed, ClawItem.Kind.Bomb);
            EventUtil.SafeInvoke(OnBombExploded, pos);
            Debug.Log($"[Claw] 폭탄 폭발 #{bombsHit} — {moved}개 흩어짐");
        }

        void EndGame(bool success)
        {
            if (finishing) return;
            finishing = true;
            StopPlayForResult();
            LastSuccess = success;
            RewardEligible = success || rewardOnPartial;
            SetPhase(PlayPhase.None);
            if (machineFx != null)
            {
                machineFx.SetWarn(false);
                if (success) { machineFx.Flash(FxGold, 1.6f); machineFx.Ripple(basket != null ? basket.ChuteWorld : transform.position, FxGold); }
            }
            SetState(State.Result);
            EventUtil.SafeInvoke(OnGameResult, success, KeyringGot, targetCount, lastElapsed);
            float min60 = PerfMonitor.Instance != null ? PerfMonitor.Instance.Min60 : -1f;
            var prize = PrizeKind;
            Debug.Log($"[Cycle] END {(success ? "성공" : "시간초과")} claw 큐브 {KeyringGot}/{targetCount} · 그릇 {CeramicGot + GlassGot}(점수 없음 — 세라믹 {CeramicGot}·유리 {GlassGot}) 잡기={grabs} 폭탄={bombsHit} " +
                      $"경과={lastElapsed:F1}s min60={min60:F0} reward={RewardEligible} 경품={(prize.HasValue ? KindLabel(prize.Value) : "없음")} " +
                      $"escapes={(basket != null ? basket.EscapeCount : 0)} chute→pile={(basket != null ? basket.ChuteIntrusionCount : 0)}");
            finishing = false;
        }

        protected override int RewardScore => KeyringGot; // 10-29 감사: 그릇은 점수 없음 — 큐브 수만(로그·Mock 보상에만 영향)

        /// <summary>10-28 경품 = 키링 큐브 상품(prizeNftIdKeyring)만. 큐브를 못 넣었거나 번호가 0이면 요청 안 함</summary>
        protected override int PrizeNftId
        {
            get
            {
                var k = PrizeKind;
                if (Config == null || !k.HasValue || k.Value != ClawItem.Kind.Keyring) return 0;
                return Config.prizeNftIdKeyring;
            }
        }

        protected override void OnResetGame()
        {
            grabs = 0; bombsHit = 0;
            delivered.Clear();
            CeramicGot = 0; GlassGot = 0; KeyringGot = 0;
            LastSuccess = false;
            remain = timeLimitSec;
            lastWholeSec = int.MinValue;
            graceUntil = -1f;
            if (explosions != null) foreach (var fx in explosions) if (fx != null) fx.StopAll();
            if (collectFx != null) foreach (var fx in collectFx) if (fx != null) fx.StopAll();
            if (machineFx != null) machineFx.ResetFx();
            SetPhase(PlayPhase.None);
        }

        protected override string ResetSummary => $"claw 미션, 큐브 {targetCount}개·{timeLimitSec:F0}초";

        protected override void AppendGameSummary(StringBuilder sb)
        {
            var cfg = Config;
            if (cfg == null) return;
            sb.AppendLine("모드: 인형뽑기 미션형 — 키링 큐브를 배출구에(그릇은 점수 없음)");
            sb.AppendLine($"제한시간 {timeLimitSec:F0}초 · 목표 큐브 {targetCount}개 · 경고 {WarnThresholdSec:F0}초");
            sb.AppendLine($"큐브 {keyringCount} · 세라믹 {ceramicCount} · 유리 {glassCount} · 캡슐 {fillerCount} · 폭탄 {bombCount} (풀 {PoolCapacity})" +
                          (keyringCount != cfg.clawKeyringCount || ceramicCount != cfg.clawCeramicCount || glassCount != cfg.clawGlassCount || fillerCount != cfg.clawFillerCount || bombCount != cfg.clawBombCount
                              ? $"  (config {cfg.clawKeyringCount}/{cfg.clawCeramicCount}/{cfg.clawGlassCount}/{cfg.clawFillerCount}/{cfg.clawBombCount} → clamp)" : ""));
            sb.AppendLine($"조이스틱 {(cfg.clawStickRelative ? "상대" : "절대")} · 회전 {cfg.clawPadRotation} · 캐리지 {cfg.clawCarriageSpeed:0.00}m/s · 폭발 {cfg.clawBombForce:0.0}/{cfg.clawBombLift:0.00} · 림 {cfg.clawRim:0.0} · 광택 {cfg.clawReflection:0.0}");
            sb.AppendLine($"집게: 배출 뒤 가운데 복귀(배출구 이동과 같은 {(head != null ? head.TravelSpeed : 0f):0.00}m/s, 빈손·폭발은 제자리) — 10-29");
            sb.AppendLine($"렌더 등급: {(ClawRenderTier.Instance != null ? ClawRenderTier.Instance.Summary : "없음")} · 이방성 {cfg.clawAniso}" +
                          (string.IsNullOrEmpty(cfg.perfHide) ? "" : $" · ⚠ perfHide={cfg.perfHide}(진단용 — 운영에선 비움)"));

            sb.AppendLine("동영상 없음 · 인증 → 검정 전환 → 인형뽑기 공간");
            sb.AppendLine($"진행 큐브 {KeyringGot}/{targetCount} · 그릇 {CeramicGot + GlassGot}(점수 없음) · 잡기 {grabs}회 · 폭탄 {bombsHit}회" +
                          (basket != null ? $" · 정착 {basket.LastSettleMs:F0}ms · 이탈복구 {basket.EscapeCount}" : ""));
            sb.AppendLine($"경품 nftId 키링 {cfg.prizeNftIdKeyring} · 보상 이름 {cfg.clawRewardText}");
            sb.AppendLine("보상:");
            for (int i = 0; i < cfg.rewards.Count; i++)
                sb.AppendLine($" · {cfg.rewards[i].text} (mesh {cfg.rewards[i].meshIndex})");
        }

        protected override void ApplyGameConfig(GameConfig cfg)
        {
            timeLimitSec = cfg.clawTimeLimitSeconds;
            WarnThresholdSec = Mathf.Min(cfg.clawWarningTimeSeconds, cfg.clawTimeLimitSeconds);
            rewardOnPartial = cfg.rewardOnPartial;

            // 10-20 받침 더미 on/off — 개수 상한(그 위 한 층)·셀 바닥 높이가 달라지므로 개수보다 먼저. 덮이는 바닥 그리드·바닥 네온은 끈다
            if (basket != null && basket.Bed != null) basket.Bed.SetEnabled(cfg.clawBed);
            if (basket != null) basket.RealTopLayer = cfg.clawRealTopLayer;
            if (machineFx != null) machineFx.SetFloorCovered(basket != null && basket.BedActive);

            // 개수: 풀 안으로(받침 더미면 그 위 한 층 셀 수 안으로) — 10-28 큐브(점수 상품, 1개 이상 보장) → 세라믹 → 유리 → 폭탄 → 캡슐 순으로 줄여 적용
            int cap = PlaceCapacity;
            keyringCount = Mathf.Clamp(cfg.clawKeyringCount, 1, Mathf.Max(1, cap));
            ceramicCount = Mathf.Clamp(cfg.clawCeramicCount, 0, Mathf.Max(0, cap - keyringCount));
            glassCount = Mathf.Clamp(cfg.clawGlassCount, 0, Mathf.Max(0, cap - keyringCount - ceramicCount));
            int targets = ceramicCount + glassCount + keyringCount;
            targetCount = Mathf.Clamp(cfg.clawTargetCount, 1, keyringCount); // 10-28 큐브 N개 — 큐브 수 안으로
            bombCount = Mathf.Clamp(cfg.clawBombCount, 0, Mathf.Max(0, cap - targets));
            fillerCount = Mathf.Clamp(cfg.clawFillerCount, 0, Mathf.Max(0, cap - targets - bombCount));
            if (ceramicCount != cfg.clawCeramicCount || glassCount != cfg.clawGlassCount || keyringCount != cfg.clawKeyringCount || bombCount != cfg.clawBombCount || fillerCount != cfg.clawFillerCount)
                cfg.warnings.Add($"인형뽑기 개수(큐브 {cfg.clawKeyringCount}·세라믹 {cfg.clawCeramicCount}·유리 {cfg.clawGlassCount}·캡슐 {cfg.clawFillerCount}·폭탄 {cfg.clawBombCount}) > {(basket != null && basket.BedActive ? "받침 위 한 층" : "풀")}({cap}) 또는 큐브 0 → 큐브 {keyringCount}·세라믹 {ceramicCount}·유리 {glassCount}·캡슐 {fillerCount}·폭탄 {bombCount} 적용");
            if (targetCount != cfg.clawTargetCount)
                cfg.warnings.Add($"clawTargetCount {cfg.clawTargetCount} > 키링 큐브 수({keyringCount}) → {targetCount} 적용");

            if (carriage != null) carriage.MaxSpeed = cfg.clawCarriageSpeed;
            if (head != null)
            {
                head.DescendSpeed = cfg.clawHeadSpeed;
                head.AscendSpeed = cfg.clawHeadSpeed * 1.33f;
                head.TravelSpeed = cfg.clawHeadSpeed * 1.17f;
            }
            ClawVisual.ApplyGlobals(cfg.clawRim, cfg.clawReflection);
            // 성능(10-15): 부스·기계 렌더 등급과 텍스처 이방성 필터 — 리셋마다 config대로(같으면 아무 것도 안 함)
            if (ClawRenderTier.Instance != null) ClawRenderTier.Instance.Apply(cfg.clawBooth, cfg.clawMachine);
            ClawVisual.ApplyAnisotropic(cfg.clawAniso);
            // 10-19 조준 표시·선·대상 발광 색
            if (ClawGuide.Instance != null && ClawGuide.TryParseColor(cfg.clawGuideColor, out var gc)) ClawGuide.Instance.GuideColor = gc;
            if (ClawGuide.Instance != null) ClawGuide.Instance.SpotIntensity = cfg.clawGuideSpotIntensity; // 10-29 스포트라이트 밝기 배율
            if (ClawLighting.Instance != null) ClawLighting.Instance.Apply(cfg.clawLighting); // 10-20 조명 묶음
            if (basket != null)
            {
                basket.MaxItemSpeed = cfg.clawMaxItemSpeed;
                basket.ExplodeSpread = cfg.clawBombForce;
                basket.ExplodeLift = cfg.clawBombLift;
            }
        }
    }
}
