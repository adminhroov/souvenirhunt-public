using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SouvenirHunt
{
    /// <summary>
    /// 에디터 전용 자동 검증(AutoRun 토큰 @selftest → 마커 파일 + Claw 씬 재생). 실기기 없이 전체 플로우를 돈다.
    /// 10-15 미션형 복원(세라믹·유리 그릇·캡슐·폭탄, 그릇 n개 넣기):
    ///   Ready → Video → Reveal → Guide → Playing → 아이템 종류·외형·충돌체·크기·바로 섬·배출구·캐리지·다이얼로그·재정렬
    ///   → 가이드 = 잡힐 것(발광) → 1개 잡기(그릇 +1, 캡슐 무득점) → 폭탄 아래 그릇은 그릇을 잡음 → 폭탄 접촉 폭발(흩어짐·이탈 0)
    ///   → 폭발 중 재정렬 → 울타리 걸림 해소 → 시간 초과 → 리셋 → 그릇 3개 성공 → 경품(그릇 종류별 nftId, 가짜 서버)
    ///   → config 경로·영상 자동 진행·운영자 패널 → 폭발 20회 스트레스.
    /// 10-28 규칙(큐브 10·세라믹 5·유리 5, 더미 끔, 한 번에 한 개만, 점수 = 키링 큐브만·그릇은 방해물, 55초/큐브 5개):
    ///   전 구간 한 번에 쥔 수 ≤ 1 · 큐브 잡기 = 하나만 · 그릇 투입 = 점수 그대로·"그릇 — 점수 없음" · 큐브 3개 모아 잡아도 하나만 → 하나씩 목표까지
    ///   · 보상 화면 = 상품 이름 + 큐브 모델 · 운영자 스테퍼 큐브/그릇.
    ///   기계 다가오기(검정 뒤 D m 멀리 → 디졸브 끝 → 가속하며 제자리 → Guide): 이동 중 상품 고정·도착 자세·상품 편차·이동 중 리셋·바로 도착·끔 변형.
    /// 결과는 "[ClawSelfTest] RESULT PASS/FAIL" 로그 후 재생 종료. 마커가 없으면 아무 것도 하지 않는다(빌드엔 코드가 들어가지 않음).
    /// </summary>
    public class ClawSelfTest : MonoBehaviour
    {
#if UNITY_EDITOR
        public static readonly string MarkerPath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "claw_selftest.txt"));

        StringBuilder log;
        int fails;
        ClawGameManager gm;
        ClawItemPool pool;
        ClawBasket basket;
        ClawCarriage carriage;
        ClawHead head;
        readonly List<ClawItem> captured = new();
        readonly HashSet<ClawHead.Phase> phasesSeen = new();
        float fingerOpenMax, fingerHoldMin; // 10-19 집게발 여닫기: 하강 중 최대 펼침 / 들고 있는 동안 최소(오므림)
        float descendOffsetMax;              // 10-19 하강 중(유휴 높이보다 25cm 아래) 집게가 캐리지 바로 아래에서 벗어난 최대 수평 거리
        float guardClearMin;                 // 10-20 잡힌 상품이 칸막이에 걸쳐 지나가는 동안 상품 바닥 − 칸막이 위 끝(최소)
        float descendStart, descendDur;      // 10-20 하강 시간(높이 무관 일정)
        int heldMax;                         // 10-22 한 번에 쥔 최대 수
        int heldMaxAll;                      // 10-28 테스트 전 구간에서 한 번에 쥔 최대 수(한 개만 잡기 — ≤ 1)
        string heldMaxAllAt = "";

        IEnumerator Start()
        {
            if (!File.Exists(MarkerPath)) yield break;
            try { File.Delete(MarkerPath); } catch { }
            yield return StartCoroutine(Run());
        }

        void LateUpdate()
        {
            if (log == null || head == null) return;
            if (head.HeldCount > heldMaxAll) { heldMaxAll = head.HeldCount; heldMaxAllAt = $"{head.Current}"; }
        }

        static Transform FindBoothRoot()
        {
            var br = GameObject.Find("BoothRoot");
            return br != null ? br.transform.Find("Booth") : null;
        }

        /// <summary>t 아래 메시 렌더러의 재질 셰이더 집계: URP Lit 재질 수, LiteLit 재질 수, 그런 재질을 가진 렌더러 중 켜진 수</summary>
        static (int lit, int lite, int on) TierShaders(Transform t)
        {
            int lit = 0, lite = 0, on = 0;
            if (t == null) return (0, 0, 0);
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool any = false;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    if (m.shader.name == "Universal Render Pipeline/Lit") { lit++; any = true; }
                    else if (m.shader.name == "SouvenirHunt/LiteLit") { lite++; any = true; }
                }
                if (any && r.enabled) on++;
            }
            return (lit, lite, on);
        }

        void CheckRenderTier()
        {
            var tier = ClawRenderTier.Instance;
            var root = FindAnyObjectByType<MachineVisibility>();
            Check(tier != null && root != null, "렌더 등급(ClawRenderTier)·BoothRoot 존재");
            if (tier == null || root == null) return;
            Transform booth = root.transform.Find("Booth"), env = root.transform.Find("Environment"), rig = root.transform.Find("ClawRig");
            var cfg = gm.Config;
            var b0 = TierShaders(booth);
            tier.Apply("lite", "lite");
            var bl = TierShaders(booth); var el = TierShaders(env); var rl = TierShaders(rig);
            Check(tier.Summary == "부스 lite · 기계 lite" && (booth == null || (bl.lit == 0 && bl.lite > 0 && bl.on == b0.on)) && el.lit == 0 && el.lite > 0 && rl.lit == 0,
                  $"lite: 부스·기계·리그 URP Lit → LiteLit (부스 Lit {bl.lit}/Lite {bl.lite}/켜짐 {bl.on}, 기계 {el.lit}/{el.lite}, 리그 {rl.lit}/{rl.lite})");
            tier.Apply("off", "full");
            var bo = TierShaders(booth); var eo = TierShaders(env);
            Check((booth == null || (bo.on == 0 && bo.lite == 0)) && eo.lit > 0 && eo.lite == 0,
                  $"off: 부스 렌더러 전부 꺼짐(켜짐 {bo.on}) · 기계 원본 재질 복귀(Lit {eo.lit}/Lite {eo.lite})");
            tier.Apply(cfg.clawBooth, cfg.clawMachine);
            var br = TierShaders(booth);
            Check(cfg.clawBooth != "full" || (br.on == b0.on && br.lit == b0.lit && br.lite == 0),
                  $"config({cfg.clawBooth}/{cfg.clawMachine})로 복귀 — 부스 켜짐 {br.on}/{b0.on}, Lit {br.lit}/{b0.lit}");
        }

        /// <summary>10-20 조명 묶음: studio ↔ daylight·lebombo·warehouse 전환(주변광·반사가 실제로 바뀌고 studio로 정확히 돌아옴)</summary>
        void CheckLighting()
        {
            var lt = ClawLighting.Instance;
            Check(lt != null, "조명 전환(ClawLighting) 존재");
            if (lt == null) return;
            var res = new Color[1];
            Color Up()
            {
                var dirs = new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward };
                var r = new Color[dirs.Length];
                RenderSettings.ambientProbe.Evaluate(dirs, r);
                return new Color(r[0].r + r[1].r * 0.5f + r[2].r * 0.25f + r[3].r * 0.125f, r[0].g + r[1].g * 0.5f + r[2].g * 0.25f + r[3].g * 0.125f, r[0].b + r[1].b * 0.5f + r[2].b * 0.25f + r[3].b * 0.125f);
            }
            float Diff(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
            lt.Apply("studio");
            var s0 = Up(); var refl0 = RenderSettings.customReflectionTexture;
            var sb = new StringBuilder();
            int changed = 0;
            foreach (var id in new[] { "daylight", "lebombo", "warehouse" })
            {
                lt.Apply(id);
                var u = Up();
                bool ok = lt.Current == id && RenderSettings.customReflectionTexture != refl0 && Diff(u, s0) > 0.01f;
                if (ok) changed++;
                sb.Append($" {id}{(ok ? "" : "✗")}(위 주변광 {u.r:F2},{u.g:F2},{u.b:F2})");
            }
            lt.Apply("studio");
            bool back = Diff(Up(), s0) < 1e-4f && RenderSettings.customReflectionTexture == refl0;
            lt.Apply(gm.Config.clawLighting);
            Check(changed == 3 && back, $"조명 묶음 전환 3/3 = {changed} · studio 원복 {back} (studio 주변광 지문 {s0.r:F2},{s0.g:F2},{s0.b:F2}){sb}");
        }

        void Check(bool ok, string what)
        {
            log.AppendLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) fails++;
        }

        IEnumerator WaitState(ClawGameManager.State s, float timeout)
        {
            float t0 = Time.time;
            while (gm.Current != s && Time.time - t0 < timeout) yield return null;
        }

        IEnumerator WaitAim(float timeout)
        {
            float t0 = Time.time;
            while (!(gm.Current == ClawGameManager.State.Playing && gm.Phase == ClawGameManager.PlayPhase.Aim && !head.Busy)
                   && gm.Current != ClawGameManager.State.Result && Time.time - t0 < timeout)
                yield return null;
        }

        static IEnumerator WaitUntil(System.Func<bool> cond, float timeout)
        {
            float t0 = Time.time;
            while (!cond() && Time.time - t0 < timeout) yield return null;
        }

        static void Tap()
        {
            ClawPadInput.PressGrab();
            ClawPadInput.ReleaseGrab();
        }

        /// <summary>Ready에서 영상·디졸브 건너뛰고 Playing(조준)까지. 제한 시간은 넉넉히</summary>
        IEnumerator StartGame()
        {
            if (gm.Current != ClawGameManager.State.Ready) { gm.ResetGame(); yield return null; }
            gm.RequestStart(); yield return null;
            gm.EditorSkipReveal(); yield return null;
            gm.ConfirmGuide();
            yield return WaitAim(12f);
            gm.EditorSetTimeRemaining(180f);
        }

        Vector3 Local(ClawItem it) => basket.transform.InverseTransformPoint(it.transform.position);
        /// <summary>배치한 실물 수 — 10-20 승격본(받침 → 진짜 캡슐, 조준 중 생겼다 되돌려짐)은 뺀다</summary>
        int RealActive() => pool.ActiveCount - basket.PromotedActive;

        static Vector2 ClampToCarriage(Vector2 xz) => new Vector2(
            Mathf.Clamp(xz.x, ClawLayout.CarriageMinX, ClawLayout.CarriageMaxX),
            Mathf.Clamp(xz.y, ClawLayout.CarriageMinZ, ClawLayout.CarriageMaxZ));

        /// <summary>
        /// kind 종류 중 캐리지를 바로 위에 놓았을 때 가이드 후보(= 잡힐 것)가 되는 아이템을 찾아 캐리지를 거기 둔다. 없으면 null.
        /// 벽 가까운 것은 캐리지가 못 가므로 가운데 가까운 것부터 시도.
        /// </summary>
        IEnumerator AimAt(System.Predicate<ClawItem> want, System.Action<ClawItem> found)
        {
            var list = new List<ClawItem>();
            foreach (var it in pool.All)
                if (it.gameObject.activeSelf && !it.Held && want(it)) list.Add(it);
            list.Sort((a, b) =>
            {
                var la = Local(a); var lb = Local(b);
                float da = (new Vector2(la.x, la.z) - ClampToCarriage(new Vector2(la.x, la.z))).sqrMagnitude;
                float db = (new Vector2(lb.x, lb.z) - ClampToCarriage(new Vector2(lb.x, lb.z))).sqrMagnitude;
                return da.CompareTo(db);
            });
            for (int i = 0; i < list.Count && i < 8; i++)
            {
                var l = Local(list[i]);
                var xz = new Vector2(l.x, l.z);
                if ((xz - ClampToCarriage(xz)).magnitude > pool.Spec.GrabRadius * 0.5f) break;
                carriage.SetLocalXZ(ClampToCarriage(xz));
                yield return null; yield return null; yield return null;
                if (head.Candidate == list[i]) { found(list[i]); yield break; }
            }
            found(null);
        }

        /// <summary>
        /// 10-20 승격 검사용: 캐리지가 닿고 배출구 칸막이 높이 제한 밖인 받침 윗면 조각 하나(avoid에서 0.6m 넘게)를 골라
        /// 그 둘레 0.32m 실물을 다른 데(0.45m 밖 더미 위)로 치운다. 없으면 −1
        /// </summary>
        /// <summary>10-26 배치한 실물 하나(캡슐이 있으면 캡슐, 없으면 상품 — 받침 승격본·진짜 맨 윗층·폭탄 제외). 상품만인 설정에서 캡슐 쓰던 검사용</summary>
        ClawItem LooseItem(ClawItem except = null)
        {
            ClawItem any = null;
            foreach (var it in pool.All)
            {
                if (!it.gameObject.activeSelf || it.Held || it == except || it.BedIndex >= 0 || it.TopLayer || it.ItemKind == ClawItem.Kind.Bomb) continue;
                if (it.ItemKind == ClawItem.Kind.Capsule) return it;
                any ??= it;
            }
            return any;
        }

        IEnumerator ClearBedSpot(int avoid, System.Action<int> found)
        {
            var bed = basket.Bed;
            if (bed == null || !basket.BedActive || bed.Count == 0) { found(-1); yield break; } // 10-28 더미 꺼짐(기본) — 고를 조각 없음
            int pick = -1; float bestD = float.MaxValue;
            float dia = bed.PieceRadius * 2f;
            for (int i = 0; i < bed.Count; i++)
            {
                if (bed.IsHidden(i)) continue;
                var p = bed.LocalPosition(i);
                if (p.x < ClawLayout.CarriageMinX + 0.05f || p.x > ClawLayout.CarriageMaxX - 0.05f || p.z < ClawLayout.CarriageMinZ + 0.05f || p.z > ClawLayout.CarriageMaxZ - 0.05f) continue;
                if (p.x < ClawLayout.ChuteWallX + ClawLayout.CarriageMargin + 0.05f && p.z < ClawLayout.ChuteWallZ + ClawLayout.CarriageMargin + 0.05f) continue;
                if (bed.LocalTop(i) < bed.LocalTopNear(p.x, p.z, Mathf.Max(dia, ClawLayout.StopProbeRadius + bed.PieceRadius)) - 0.01f) continue; // 윗면 조각만(집게 하강 판정 반경 안에서 가장 높은 것 — 10-22 맨 윗층이 숨으면 군데군데 낮은 층이 드러난다)
                if (avoid >= 0) { var q = bed.LocalPosition(avoid); if (new Vector2(p.x - q.x, p.z - q.z).magnitude < 0.45f) continue; } // 10-26 상품 더미는 조각이 적어 0.6 → 0.45
                float dd = new Vector2(p.x - 0.1f, p.z - ClawLayout.CenterZ).sqrMagnitude;
                if (dd < bestD) { bestD = dd; pick = i; }
            }
            if (pick >= 0)
            {
                var pp = bed.LocalPosition(pick);
                foreach (var it in pool.All)
                {
                    if (!it.gameObject.activeSelf || it.Held) continue;
                    var l = Local(it);
                    if (new Vector2(l.x - pp.x, l.z - pp.z).magnitude > 0.5f) continue; // 10-22 진짜 맨 윗층 캡슐이 빈자리로 굴러들어 오지 않게 넓게
                    // 옮길 자리는 0.65m 밖 — 너무 멀게 잡으면(0.75) 기계가 좁아 16번 안에 못 찾고 그 자리에 남았다
                    for (int k = 0; k < 40; k++)
                    {
                        var w = basket.RespawnPoint(it);
                        var wl = basket.transform.InverseTransformPoint(w);
                        if (new Vector2(wl.x - pp.x, wl.z - pp.z).magnitude > 0.65f) { it.Teleport(w, Quaternion.identity); it.Body.WakeUp(); break; }
                    }
                }
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.1f);
            }
            found(pick);
        }

        /// <summary>부스 로컬 (x, z) 둘레 radius 안 실물(keep 제외)을 다른 데(keepOut m 밖 더미 위)로 치운다 — 10-22 벽·여러 개 잡기 검사 자리 만들기</summary>
        void ClearAround(Vector2 xz, float radius, float keepOut, ICollection<ClawItem> keep, bool keepTopLayer = true)
        {
            foreach (var it in pool.All)
            {
                if (!it.gameObject.activeSelf || it.Held || (keep != null && keep.Contains(it))) continue;
                if (keepTopLayer && it.TopLayer) continue; // 10-22 진짜 맨 윗층은 두고(구멍이 생기면 둘레가 굴러든다) 그 위만 치운다
                var l = Local(it);
                if (new Vector2(l.x - xz.x, l.z - xz.y).magnitude > radius) continue;
                for (int k = 0; k < 24; k++)
                {
                    var w = basket.RespawnPoint(it);
                    var wl = basket.transform.InverseTransformPoint(w);
                    if (new Vector2(wl.x - xz.x, wl.z - xz.y).magnitude > keepOut) { it.Teleport(w, Quaternion.identity); it.Body.WakeUp(); break; }
                }
            }
            Physics.SyncTransforms();
        }

        /// <summary>상품을 부스 로컬 (x, z) 더미 표면 위에 바로 세워 놓는다(잠깐 뒤 정착)</summary>
        void PlaceOnPile(ClawItem it, Vector2 xz)
        {
            float r = it.Width * 0.5f;
            float sc = basket.SurfaceCenterY(xz.x, xz.y, r, it);
            var w = basket.transform.TransformPoint(new Vector3(xz.x, sc - r + it.HalfHeight + 0.01f, xz.y));
            it.Teleport(w, basket.transform.rotation);
            it.Body.WakeUp();
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 10-22 물리 집게 검사용: 대상 둘레 0.45m 실물(keep 제외)을 다른 데로 치우고 대상을 레일 끝 5cm 안쪽 자리에 다시 놓아 정착 → 캐리지를 바로 위로.
        /// 물리 집게는 펼친 발(지름 약 60cm)이 둘레 상품을 밀고, 발 사이 틈·이웃에 따라 놓칠 수 있다 — 검사는 "잘 겨눈 하나"의 동작을 본다
        /// </summary>
        static readonly Vector2[] IsolateSpots = { new Vector2(0.12f, 0.05f), new Vector2(-0.05f, 0.25f), new Vector2(0.3f, -0.15f), new Vector2(-0.1f, -0.25f) };

        IEnumerator Isolate(ClawItem target, ICollection<ClawItem> keep = null, int attempt = 0)
        {
            var l = Local(target);
            var spot = new Vector2(Mathf.Clamp(l.x, ClawLayout.CarriageMinX + 0.05f, ClawLayout.CarriageMaxX - 0.05f),
                                   Mathf.Clamp(l.z, ClawLayout.CarriageMinZ + 0.05f, ClawLayout.CarriageMaxZ - 0.05f));
            // 다시 할 때는 기계 가운데 쪽 다른 자리(10-22 진짜 맨 윗층에 앞선 잡기·폭발로 구멍이 생기면 거기 빠진 것은 둘레에 막혀 계속 놓친다)
            if (attempt > 0) { var o = IsolateSpots[(attempt - 1) % IsolateSpots.Length]; spot = new Vector2(o.x, ClawLayout.CenterZ + o.y); }
            // 배출구 칸막이 쪽이면 기계 가운데 쪽으로(칸막이 높이 제한에 걸리지 않게)
            if (spot.x < ClawLayout.ChuteWallX + ClawLayout.CarriageMargin + 0.06f && spot.y < ClawLayout.ChuteWallZ + ClawLayout.CarriageMargin + 0.06f)
                spot = new Vector2(Mathf.Max(spot.x, ClawLayout.ChuteWallX + ClawLayout.CarriageMargin + 0.06f), spot.y);
            var keepAll = new List<ClawItem> { target };
            if (keep != null) keepAll.AddRange(keep);
            ClearAround(spot, 0.45f, 0.6f, keepAll);
            PlaceOnPile(target, spot);
            yield return new WaitForSeconds(0.8f);
            var l2 = Local(target);
            carriage.SetLocalXZ(ClampToCarriage(new Vector2(l2.x, l2.z)));
            yield return null; yield return null; yield return null;
        }

        /// <summary>대상을 따로 놓고 잡기 — 물리로 놓치면 tries번까지 다시(놓친 횟수는 missed에)</summary>
        IEnumerator GrabTarget(ClawItem target, int tries, System.Action<int> missed = null)
        {
            int miss = 0;
            for (int k = 0; k < tries; k++)
            {
                gm.EditorSetTimeRemaining(180f);
                yield return Isolate(target, null, k);
                yield return GrabCycle();
                if (captured.Contains(target) || !target.gameObject.activeSelf || gm.Current != ClawGameManager.State.Playing) break;
                miss++;
                yield return WaitAim(6f);
            }
            missed?.Invoke(miss);
        }

        /// <summary>잡기 1회(조준 → 잡기 → 집게 동작 끝 → 조준 복귀 또는 결과). 헤드 단계 기록</summary>
        IEnumerator GrabCycle()
        {
            captured.Clear();
            phasesSeen.Clear();
            fingerOpenMax = 0f; fingerHoldMin = 0f; descendOffsetMax = 0f; guardClearMin = float.MaxValue;
            descendStart = -1f; descendDur = -1f;
            heldMax = 0;
            var fingers = head.GetComponent<ClawFingers>();
            Tap();
            float t0 = Time.time;
            yield return null;
            while (Time.time - t0 < 15f)
            {
                phasesSeen.Add(head.Current);
                heldMax = Mathf.Max(heldMax, head.HeldCount);
                if (head.Current == ClawHead.Phase.Descend && descendStart < 0f) descendStart = Time.time;
                if (descendStart >= 0f && descendDur < 0f && head.Current != ClawHead.Phase.Descend) descendDur = Time.time - descendStart;
                // 10-20 운반 여유: 잡힌 상품이 칸막이(배출구 기둥 가장자리)에 걸쳐 지나가는 동안 상품 바닥 − 칸막이 위 끝
                var held = head.Holding;
                if (held != null && (head.Current == ClawHead.Phase.Ascend || head.Current == ClawHead.Phase.Travel || head.Current == ClawHead.Phase.DropLower))
                {
                    var hp = Local(held);
                    float he = held.HalfExtent, gt = ClawLayout.ChuteGuardThickness;
                    bool over = basket.OverChuteColumn(held.transform.position, he);
                    bool inside = hp.x + he < ClawLayout.ChuteWallX - gt && hp.z + he < ClawLayout.ChuteWallZ - gt;
                    if (over && !inside) guardClearMin = Mathf.Min(guardClearMin, hp.y - held.HalfHeight - ClawLayout.ChuteGuardTopY);
                }
                if (head.Current == ClawHead.Phase.Descend || head.Current == ClawHead.Phase.Dwell)
                {
                    // 하강 25cm 아래 또는 하강이 끝난 자리(Dwell = 실제 접촉) — 10-20 더미가 높아 하강이 약 9cm라 Dwell에서도 잰다
                    var hl = basket.transform.InverseTransformPoint(head.transform.position);
                    var cl = basket.transform.InverseTransformPoint(carriage.transform.position);
                    if (head.Current == ClawHead.Phase.Dwell || hl.y < ClawLayout.RailY - ClawLayout.CableLength - 0.25f)
                        descendOffsetMax = Mathf.Max(descendOffsetMax, new Vector2(hl.x - cl.x, hl.z - cl.z).magnitude);
                }
                if (fingers != null)
                {
                    if (head.Current == ClawHead.Phase.Descend || head.Current == ClawHead.Phase.Dwell) fingerOpenMax = Mathf.Max(fingerOpenMax, fingers.Angle);
                    if (head.Holding != null && (head.Current == ClawHead.Phase.Ascend || head.Current == ClawHead.Phase.Travel)) fingerHoldMin = Mathf.Min(fingerHoldMin, fingers.Angle);
                }
                if (gm.Current != ClawGameManager.State.Playing) break;
                if (Time.time - t0 > 0.1f && gm.Phase == ClawGameManager.PlayPhase.Aim && !head.Busy && head.FallingCount == 0) break; // 10-22 배출구로 떨어지는 것까지
                yield return null;
            }
        }

        IEnumerator Run()
        {
            log = new StringBuilder("[ClawSelfTest] 시작\n");
            fails = 0;

            gm = ClawGameManager.Instance;
            Check(gm != null, "ClawGameManager 존재");
            if (gm == null) { Finish(); yield break; }
            // 10-26 시간 초과 결과·보상 화면의 잡기 = [다시 시작](인증 씬으로 이동) — 이 테스트는 게임 씬에 머물러야 하므로 기록만(실제 이동은 @introtest)
            System.Action restartStub = () => log.AppendLine("  INFO [다시 시작] 호출 — 셀프테스트라 씬 이동 없음");
            gm.RestartHandler = restartStub;

            pool = FindAnyObjectByType<ClawItemPool>();
            basket = FindAnyObjectByType<ClawBasket>();
            carriage = FindAnyObjectByType<ClawCarriage>();
            head = FindAnyObjectByType<ClawHead>();
            var guide = FindAnyObjectByType<ClawGuide>();
            Check(pool != null && basket != null && carriage != null && head != null && guide != null, "핵심 컴포넌트(풀·바스켓·캐리지·헤드·가이드) 존재");
            if (pool == null || basket == null || carriage == null || head == null || guide == null) { Finish(); yield break; }
            head.OnCaptured += it => { if (it != null) captured.Add(it); };

            // 1) 부팅 → Ready (BoothAligner 2프레임 정렬 + Reset)
            yield return WaitState(ClawGameManager.State.Ready, 3f);
            yield return new WaitForSeconds(0.3f);
            Check(gm.Current == ClawGameManager.State.Ready, $"초기 상태 Ready (실제 {gm.Current})");
            Check(pool.ActiveCount == 0, $"Ready에서 활성 아이템 0 (실제 {pool.ActiveCount})");
            var spec = pool.Spec;
            Check(pool.Capacity >= spec.poolSize, $"풀 사전 생성 {pool.Capacity} ≥ {spec.poolSize}");
            Check(spec.name == "ClawSpec_Mission" && basket.Spec == spec && head.Spec == spec,
                  $"아이템 규격 = ClawSpec_Mission 풀·바스켓·헤드 공통 (pool={spec.name}, basket={basket.Spec.name}, head={head.Spec.name})");
            Check(gm.TargetCount >= 1 && gm.TargetCount <= gm.KeyringCount && gm.TimeLimit >= 10f,
                  $"목표 큐브 {gm.TargetCount} ≤ 키링 큐브 {gm.KeyringCount} (그릇 {gm.CeramicCount}+{gm.GlassCount}은 점수 없음), 제한 {gm.TimeLimit:F0}s");
            {
                // 10-28 시작 패널 규칙 문구(메뉴 30이 구움) — 큐브만 점수
                var sp = FindAnyObjectByType<ClawStartPanel>(FindObjectsInactive.Include);
                var rule = sp != null ? sp.transform.Find("StartCanvas/Rule")?.GetComponent<TMPro.TMP_Text>() : null;
                Check(rule != null && rule.text.Contains("키링 큐브를 모아") && rule.text.Contains("그릇은 점수가 없어요"),
                      $"시작 패널 규칙 = 큐브만 점수 ('{(rule != null ? rule.text.Replace('\n', ' ') : "null")}') — 다르면 메뉴 30 재실행");
            }
            var boothT = FindBoothRoot();
            Check(boothT != null && !boothT.gameObject.activeSelf, $"Ready에서 부스 숨김 (root={(boothT != null)}, active={(boothT != null && boothT.gameObject.activeSelf)})");
            if (basket.Bed != null && basket.Bed.Render != null)
                Check(!basket.Bed.Render.gameObject.activeInHierarchy, "Ready에서 받침 더미 숨김(기계와 함께 등장)");
            Check(LayerMask.NameToLayer("ClawItem") >= 0 && LayerMask.NameToLayer("ClawMachine") >= 0 && LayerMask.NameToLayer("ClawHeld") >= 0, "Claw 레이어 3종 존재");
            {
                // 10-22 물리 집게: 발 3개 = kinematic 막대 바디(ClawFinger 레이어, 아이템하고만 충돌 — 뼈 집게는 발마다 윗마디·갈고리 2개), 허브 높이·발끝 곡선 측정
                var fg = head.GetComponent<ClawFingers>();
                int fl = LayerMask.NameToLayer("ClawFinger"), il = LayerMask.NameToLayer("ClawItem"), ml = LayerMask.NameToLayer("ClawMachine"), bl = LayerMask.NameToLayer("ClawBed");
                bool matrix = fl >= 0 && !Physics.GetIgnoreLayerCollision(fl, il) && Physics.GetIgnoreLayerCollision(fl, ml) && (bl < 0 || Physics.GetIgnoreLayerCollision(fl, bl)) && Physics.GetIgnoreLayerCollision(fl, fl);
                float lift = fg != null ? fg.TipLiftForRadius(0.05f) : 0f;
                bool rigged = fg != null && fg.JointCount > 1;
                Check(fg != null && fg.FingerCount == 3 && fg.BodyCount == 3 * Mathf.Max(1, fg.JointCount - 1) && matrix && fg.HubClearance > 0.05f
                      && (rigged ? Mathf.Abs(lift) < ClawLayout.HeadHalfHeight : lift > 0.01f),
                      $"물리 집게발({(rigged ? $"뼈 집게 — 발마다 관절 {fg.JointCount}" : "발 메시 회전")}): 바디 {(fg != null ? fg.BodyCount : 0)}개 · 충돌 = 아이템만 {matrix} · " +
                      $"허브 발끝 위 {(fg != null ? fg.HubClearance * 100f : 0f):F1}cm · 오므린 발끝(반지름 5cm) 높이 {lift * 100f:+0.0;-0.0}cm(헤드 밑면 기준)");
                if (ClawLayout.SupClaw)
                    Check(rigged && head.GetComponentInChildren<SkinnedMeshRenderer>(true) != null,
                          $"10-22 뼈 집게: StartUpPark_set.fbx 스킨 메시·뼈 사용 (관절 {(fg != null ? fg.JointCount : 0)}, 스킨 {(head.GetComponentInChildren<SkinnedMeshRenderer>(true) != null ? "O" : "X")}) — X면 메뉴 30 재실행");
            }
            Check(ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Ceramic, ClawItem.Kind.Ceramic, ClawItem.Kind.Glass }) == ClawItem.Kind.Ceramic &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Glass, ClawItem.Kind.Capsule, ClawItem.Kind.Glass, ClawItem.Kind.Ceramic }) == ClawItem.Kind.Glass &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Ceramic, ClawItem.Kind.Glass }) == ClawItem.Kind.Glass &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Glass, ClawItem.Kind.Ceramic }) == ClawItem.Kind.Ceramic &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Capsule }) == null && ClawGameManager.PickPrizeKind(new ClawItem.Kind[0]) == null &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Keyring, ClawItem.Kind.Keyring, ClawItem.Kind.Ceramic }) == ClawItem.Kind.Keyring &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Ceramic, ClawItem.Kind.Keyring }) == ClawItem.Kind.Keyring &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Keyring, ClawItem.Kind.Glass, ClawItem.Kind.Glass }) == ClawItem.Kind.Glass &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Keyring, ClawItem.Kind.Glass, ClawItem.Kind.Ceramic }) == ClawItem.Kind.Ceramic,
                  "경품 종류 = 넣은 그릇 중 많은 종류 · 동점이면 마지막 그릇 · 그릇 없으면 없음(캡슐 제외)");
            // 10-22 규칙(사용자 "3개를 다 채운 제품"): 넣은 순서대로 같은 종류 3개를 먼저 채운 것, 못 채웠으면 많은 종류
            Check(ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Ceramic, ClawItem.Kind.Glass, ClawItem.Kind.Ceramic, ClawItem.Kind.Keyring, ClawItem.Kind.Ceramic }, 3) == ClawItem.Kind.Ceramic &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Keyring, ClawItem.Kind.Keyring, ClawItem.Kind.Glass, ClawItem.Kind.Glass, ClawItem.Kind.Glass, ClawItem.Kind.Keyring }, 3) == ClawItem.Kind.Glass &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Keyring, ClawItem.Kind.Keyring, ClawItem.Kind.Keyring }, 3) == ClawItem.Kind.Keyring &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Keyring, ClawItem.Kind.Glass }, 3) == ClawItem.Kind.Glass &&
                  ClawGameManager.PickPrizeKind(new[] { ClawItem.Kind.Capsule, ClawItem.Kind.Capsule, ClawItem.Kind.Capsule }, 3) == null,
                  "경품 종류(같은 종류 3개) = 먼저 3개 채운 종류 · 못 채우면 많은 종류(동점이면 마지막) · 캡슐만이면 없음");
            CheckRenderTier();
            CheckLighting();

            // 2) 시작 = 잡기(빔 미사용) → Video → Guide, 축 변환 검사
            Check(ClawPadInput.Root == null || ClawPadInput.RootActive, "Ready에서 오버레이 활성(빔 미사용)");
            var sup = FindAnyObjectByType<InteractorSuppressor>();
            Check(sup == null || sup.Suppressed, "레이 억제(Ready부터)");
            Tap();
            yield return null;
            Check(gm.RevealEnabled && gm.Current == ClawSessionBase.State.Reveal && !gm.MachineShown,
                  "동영상 없이 기존 검정 전환부터 시작");
            Check(FindObjectsByType<UnityEngine.Video.VideoPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0,
                  "게임 씬에 VideoPlayer 없음");
            Check(GameObject.Find("DigitalReveal") != null && GameObject.Find("VideoScreen") == null,
                  "동영상 스크린 삭제·검정 전환 보존");
            {
                var reveal = gm.RevealPanel;
                var dome = reveal.transform.Find("Dome");
                Check(reveal.Running && dome.gameObject.activeSelf && !ClawPadInput.GrabEnabled,
                      "검정 전환 실행·START 입력 잠금");
                ClawPadInput.PressHome(); yield return null;
                Check(!gm.ExitDialogOpen, "전환 중 홈은 종료창을 열지 않음");
                yield return WaitUntil(() => reveal.BlackReached, 3f);
                Check(gm.MachineShown && pool.ActiveCount > 0, "검정 뒤 공간·상품 배치");
                // 10-28 기계 다가오기: 검정 뒤 루트가 정면으로 D m 멀리(대기) → 디졸브 끝 → 가속하며 제자리 → Guide
                var ap = gm.Approach;
                var aligner = FindAnyObjectByType<BoothAligner>();
                var camA = Camera.main != null ? Camera.main.transform : null;
                var snap = new Dictionary<ClawItem, (Vector3 p, Quaternion r)>();
                int esc0A = basket.EscapeCount;
                if (ap != null && gm.ApproachEnabled && camA != null)
                {
                    var rootT = ap.transform;
                    var fin = ap.FinalPosition;
                    Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);
                    float dRoot = (Flat(rootT.position) - Flat(camA.position)).magnitude, dFin = (Flat(fin) - Flat(camA.position)).magnitude;
                    var camFwd = Vector3.ProjectOnPlane(camA.forward, Vector3.up).normalized;
                    Check(ap.Active && !ap.Running && Mathf.Abs(dRoot - dFin - ap.Distance) < 0.01f && Vector3.Dot(rootT.position - fin, camFwd) > 0f && Mathf.Abs(rootT.position.y - fin.y) < 0.001f,
                          $"[다가오기] 검정 뒤 기계가 정면으로 {ap.Distance:F1}m 멀리 대기 (대기 {ap.Active}·이동 {ap.Running}, 카메라→루트 {dRoot:F3}m − 제자리 {dFin:F3}m, 높이 차 {(rootT.position.y - fin.y) * 1000f:F1}mm)");
                    foreach (var it in pool.All)
                        if (it.gameObject.activeSelf && !it.Held) snap[it] = (Local(it), Quaternion.Inverse(basket.transform.rotation) * it.transform.rotation);
                }
                else Check(false, $"[다가오기] BoothApproach 없음 또는 꺼짐 (approach {(ap != null)}, enabled {gm.ApproachEnabled}, config {gm.Config.clawApproachDistance})");
                bool sawRunning = false, guideWhileRunning = false;
                int notFrozenFrames = 0, runFrames = 0;
                float devMove = 0f;
                {
                    float tA = Time.time;
                    while (gm.Current != ClawSessionBase.State.Guide && Time.time - tA < 10f)
                    {
                        if (ap != null && ap.Running)
                        {
                            sawRunning = true;
                            runFrames++;
                            bool allKin = true;
                            foreach (var kv in snap)
                            {
                                var it = kv.Key;
                                if (!it.gameObject.activeSelf || it.Held) continue;
                                if (!it.Body.isKinematic) allKin = false;
                                devMove = Mathf.Max(devMove, (Local(it) - kv.Value.p).magnitude);
                            }
                            if (!allKin) notFrozenFrames++;
                        }
                        yield return null;
                    }
                    if (ap != null && gm.Current == ClawSessionBase.State.Guide && ap.Running) guideWhileRunning = true;
                }
                if (ap != null && gm.ApproachEnabled && aligner != null)
                {
                    bool posed = aligner.TryGetAlignPose(out var alignPos, out var alignRot);
                    var rootT = ap.transform;
                    float posErr = (rootT.position - alignPos).magnitude;
                    float yawErr = Mathf.Abs(Mathf.DeltaAngle(rootT.eulerAngles.y, alignRot.eulerAngles.y));
                    Check(sawRunning && !guideWhileRunning && notFrozenFrames == 0 && !ap.Active && gm.Current == ClawSessionBase.State.Guide && posed && posErr < 0.001f && yawErr < 0.1f,
                          $"[다가오기] 이동({runFrames}프레임) 중 상품 전부 고정·Guide는 도착 뒤 → 제자리 = 정렬 자세 (오차 {posErr * 1000f:F2}mm·yaw {yawErr:F3}°, 고정 안 된 프레임 {notFrozenFrames}, 대기 {ap.Active})");
                    float devArrive = 0f, rotArrive = 0f;
                    int inside = 0, total = 0, settled = 0;
                    foreach (var kv in snap)
                    {
                        var it = kv.Key;
                        if (!it.gameObject.activeSelf || it.Held) continue;
                        total++;
                        devArrive = Mathf.Max(devArrive, (Local(it) - kv.Value.p).magnitude);
                        rotArrive = Mathf.Max(rotArrive, Quaternion.Angle(Quaternion.Inverse(basket.transform.rotation) * it.transform.rotation, kv.Value.r));
                        if (basket.IsInside(it.transform.position, 0.02f)) inside++;
                        if (!it.Body.isKinematic && it.Body.interpolation == RigidbodyInterpolation.Interpolate && it.Body.IsSleeping()) settled++;
                    }
                    Check(total > 0 && devArrive < 0.005f && inside == total && basket.EscapeCount == esc0A && settled == total && !basket.TransitFrozen,
                          $"[다가오기] 도착 뒤 상품이 기계 안 제자리 그대로 (로컬 편차 최대 {devArrive * 1000f:F2}mm·회전 {rotArrive:F2}°, 안 {inside}/{total}, 이탈 {esc0A}→{basket.EscapeCount}, 물리 복귀·잠듦 {settled}/{total})");
                    log.AppendLine($"  INFO [다가오기] 이동 중 상품 로컬 편차 최대 {devMove * 1000f:F2}mm · 도착 뒤 {devArrive * 1000f:F2}mm · max frame {ap.MaxFrameMs:F1} ms ({ap.Frames}프레임)");
                }
                Check(!reveal.Running && !dome.gameObject.activeSelf && gm.MachineShown && ClawPadInput.GrabEnabled,
                      "전환 종료·돔 숨김·공간과 START 표시");
            }
            ClawPadInput.Rotation = ClawPadInput.PadRotation.Left90;
            ClawPadInput.SetStick(new Vector2(1f, 0f), true);
            var ms = ClawPadInput.MachineStick;
            Check(ms.y > 0.99f && Mathf.Abs(ms.x) < 0.01f, $"축 변환 Left90: 세로 +X(멀어짐) → 기계 앞 {ms}");
            ClawPadInput.SetStick(new Vector2(0f, 1f), true);
            ms = ClawPadInput.MachineStick;
            Check(ms.x < -0.99f, $"축 변환 Left90: 세로 +Y(사용자 왼쪽) → 기계 왼쪽 {ms}");
            ClawPadInput.SetStick(Vector2.zero, false);

            // 2b) 7차: Guide 틀·문구가 시선 yaw를 따라감 → 아무 방향에서나 시작 가능
            {
                var guidePanel = FindAnyObjectByType<ClawGuidePanel>(FindObjectsInactive.Include);
                var camT = Camera.main != null ? Camera.main.transform : null;
                var camParent = camT != null ? camT.parent : null;
                if (guidePanel != null && camParent != null)
                {
                    yield return null;
                    Check(guidePanel.Showing, "Guide에서 안내 틀 표시");
                    var parentRot = camParent.localRotation;
                    camParent.localRotation = parentRot * Quaternion.Euler(0f, 40f, 0f);
                    yield return new WaitForSeconds(1.2f);
                    float camYaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized, Vector3.up).eulerAngles.y;
                    Check(Mathf.Abs(Mathf.DeltaAngle(guidePanel.Yaw, camYaw)) < 3f, $"시선 40° 돌림 → 안내 틀이 따라옴 (틀 {guidePanel.Yaw:F1}° vs 시선 {camYaw:F1}°)");
                    camParent.localRotation = parentRot;
                    yield return new WaitForSeconds(1.2f);
                    camYaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized, Vector3.up).eulerAngles.y;
                    Check(Mathf.Abs(Mathf.DeltaAngle(guidePanel.Yaw, camYaw)) < 3f, $"시선 원복 → 안내 틀 복귀 (틀 {guidePanel.Yaw:F1}° vs 시선 {camYaw:F1}°)");
                }
                else log.AppendLine("  SKIP GuidePanel/카메라 부모 없음 — 안내 틀 추종 검사 생략");
            }

            // 3) 잡기 버튼 = 확인([START]) → Countdown → Playing(조준)
            {
                var br = GameObject.Find("BoothRoot");
                var envT = br != null ? br.transform.Find("Environment") : null;
                var boothRT = FindBoothRoot();
                var envBefore = envT != null ? envT.localPosition : Vector3.zero;
                var boothBefore = boothRT != null ? boothRT.localPosition : Vector3.zero;
                Tap();
                if (envT != null && boothRT != null && gm.MachineShown)
                {
                    yield return new WaitForSeconds(0.3f);
                    Check(gm.Current == ClawGameManager.State.Countdown && envT.gameObject.activeSelf && boothRT.gameObject.activeSelf &&
                          (envT.localPosition - envBefore).sqrMagnitude < 1e-8f && (boothRT.localPosition - boothBefore).sqrMagnitude < 1e-8f,
                          $"디졸브로 이미 등장 → [START] 뒤 솟아오름 없이 3·2·1 ({gm.Current}, 기계 y {envT.localPosition.y:F3})");
                }
            }
            yield return WaitAim(12f);
            Check(gm.Current == ClawGameManager.State.Playing && gm.Phase == ClawGameManager.PlayPhase.Aim,
                  $"확인 → Playing 조준 (실제 {gm.StateName})");
            Check(Mathf.Abs(gm.TimeRemaining - gm.TimeLimit) < 1f, $"시작 남은 시간 {gm.TimeRemaining:F1} ≈ 제한 {gm.TimeLimit:F0}s");
            gm.EditorSetTimeRemaining(180f); // 아래 검사들이 시간을 쓰므로 넉넉히

            // 3a) 아이템: 종류별 개수·외형(메시·재질)·충돌체 하나·크기·바스켓 안·정착·그릇 바로 섬·덮이지 않음
            {
                int expect = gm.CeramicCount + gm.GlassCount + gm.KeyringCount + gm.FillerCount + gm.BombCount;
                Check(RealActive() - basket.RealTopCount == expect, $"배치 {RealActive() - basket.RealTopCount}/{expect} (세라믹 {gm.CeramicCount}·유리 {gm.GlassCount}·키링 {gm.KeyringCount}·캡슐 {gm.FillerCount}·폭탄 {gm.BombCount}) + 진짜 맨 윗층 {basket.RealTopCount}");
                // 10-26 목표물(그릇 2종·키링)은 집게가 닿는 안전 구역에 놓이고 다시 놓인다(사용자 "양 끝에 스폰되면 잡을 수 없어") — 정착하며 몇 cm 구름: 6cm 여유
                {
                    int tN = 0, tIn = 0;
                    ClawItem anyTarget = null;
                    var outs = new System.Text.StringBuilder();
                    foreach (var it in pool.All)
                    {
                        if (!it.gameObject.activeSelf || it.Held || !it.IsTarget || it.TopLayer || it.BedIndex >= 0) continue; // 배치한 실물만(받침 맨 윗층·승격본 = 더미 자리 그대로)
                        tN++;
                        anyTarget ??= it;
                        var lp = basket.transform.InverseTransformPoint(it.transform.position);
                        if (ClawLayout.InTargetSafeZone(lp.x, lp.z, basket.BedActive ? 0.10f : 0.06f)) tIn++; // 붓기는 굴러 몇 cm 더
                        else outs.Append($" {it.ItemKind}@({lp.x:F2},{lp.z:F2})");
                    }
                    Check(tN == gm.CeramicCount + gm.GlassCount + gm.KeyringCount && tN > 0 && tIn == tN && basket.LastSafeCells >= tN,
                          $"[안전 구역] 목표물 {tIn}/{tN}개가 집게가 닿는 구역 안 (구역 칸 {basket.LastSafeCells}, 키링 {gm.KeyringCount}){outs}");
                    int rIn = 0;
                    if (anyTarget != null)
                        for (int k = 0; k < 30; k++)
                        {
                            var l = basket.transform.InverseTransformPoint(basket.RespawnPoint(anyTarget));
                            if (ClawLayout.InTargetSafeZone(l.x, l.z)) rIn++;
                        }
                    Check(rIn == 30, $"[안전 구역] 목표물 다시 놓는 자리 {rIn}/30 구역 안");
                }
                // 10-22 받침 맨 윗층 = 진짜 캡슐(같은 자리·색, 조각은 숨김, 잠든 채 시작, 기계 안)
                if (basket.BedActive && gm.Config.clawRealTopLayer)
                {
                    int topN = 0, topHidden = 0, topInside = 0, topAsleep = 0;
                    foreach (var it in pool.All)
                    {
                        if (!it.gameObject.activeSelf || !it.TopLayer) continue;
                        topN++;
                        if (it.IsTarget && basket.IsInside(it.transform.position, 0.02f)) topInside++; // 10-26 받침 = 상품 3종
                        if (it.Body.IsSleeping() || it.Body.linearVelocity.sqrMagnitude < 0.0004f) topAsleep++;
                    }
                    foreach (int i in basket.Bed.TopLayer) if (basket.Bed.IsHidden(i)) topHidden++;
                    Check(topN == basket.Bed.TopLayer.Length && topN == basket.RealTopCount && topHidden == topN && topInside == topN && topAsleep >= topN * 0.8f,
                          $"받침 맨 윗층 → 진짜 상품 {topN}개(조각 {basket.Bed.TopLayer.Length}·숨김 {topHidden}·기계 안 {topInside}·정착 {topAsleep})");
                }
                var count = new Dictionary<ClawItem.Kind, int>();
                var meshName = new Dictionary<ClawItem.Kind, string>
                {
                    { ClawItem.Kind.Ceramic, "Claw_CeramicBowl" }, { ClawItem.Kind.Glass, "Claw_GlassBowl" },
                    { ClawItem.Kind.Capsule, "Claw_CapsuleMesh" }, { ClawItem.Kind.Bomb, "Claw_BombMesh" },
                    { ClawItem.Kind.Keyring, "Claw_KeyringMesh" },
                };
                int looks = 0, oneCol = 0, sized = 0, inside = 0, asleep = 0, upright = 0, bowls = 0, covered = 0, total = 0;
                var capsuleMats = new HashSet<Material>();
                var bowlList = new List<ClawItem>();
                foreach (var it in pool.All)
                {
                    if (!it.gameObject.activeSelf || it.BedIndex >= 0 || it.TopLayer) continue; // 승격본·진짜 맨 윗층 제외(배치한 실물만)
                    total++;
                    count[it.ItemKind] = (count.TryGetValue(it.ItemKind, out int c) ? c : 0) + 1;
                    var mesh = it.CurrentMesh;
                    var mat = it.Visual != null ? it.Visual.sharedMaterial : null;
                    if (mesh != null && mesh.name == meshName[it.ItemKind] && mat != null) looks++;
                    if (it.ItemKind == ClawItem.Kind.Capsule && mat != null) capsuleMats.Add(mat);
                    int enabledCols = 0;
                    foreach (var col in it.GetComponentsInChildren<Collider>(true)) if (col.enabled) enabledCols++;
                    if (enabledCols == 1 && it.ActiveCollider != null && it.ActiveCollider.enabled) oneCol++;
                    if (it.Visual != null && Mathf.Abs(it.Visual.transform.lossyScale.x - it.Width) < 0.002f) sized++;
                    if (basket.IsInside(it.transform.position, 0.02f)) inside++;
                    if (it.Body.IsSleeping()) asleep++;
                    if (it.IsBowl)
                    {
                        bowls++;
                        bowlList.Add(it);
                        if (Vector3.Dot(it.transform.up, basket.transform.up) > Mathf.Cos(30f * Mathf.Deg2Rad)) upright++;
                    }
                }
                foreach (var b in bowlList)
                {
                    var lb = Local(b);
                    foreach (var it in pool.All)
                    {
                        if (it == b || !it.gameObject.activeSelf) continue;
                        var li = Local(it);
                        if (new Vector2(li.x - lb.x, li.z - lb.z).magnitude < b.Width * 0.4f && li.y > lb.y + b.HalfHeight) { covered++; break; }
                    }
                }
                Check(count.GetValueOrDefault(ClawItem.Kind.Ceramic) == gm.CeramicCount && count.GetValueOrDefault(ClawItem.Kind.Glass) == gm.GlassCount &&
                      count.GetValueOrDefault(ClawItem.Kind.Capsule) == gm.FillerCount && count.GetValueOrDefault(ClawItem.Kind.Bomb) == gm.BombCount &&
                      count.GetValueOrDefault(ClawItem.Kind.Keyring) == gm.KeyringCount,
                      "종류별 개수 = 설정");
                Check(looks == total, $"외형 = 종류별 메시(그릇 FBX 2종·캡슐·폭탄)·재질 ({looks}/{total})");
                Check(oneCol == total, $"충돌체 = 종류별 하나만 켜짐(그릇 볼록 헐·캡슐/폭탄 구) ({oneCol}/{total})");
                Check(sized == total, $"외형 크기 = 규격 폭 ({sized}/{total})");
                if (gm.FillerCount > 0) Check(capsuleMats.Count >= 2, $"캡슐 색 다양 ({capsuleMats.Count}색)");
                Check(inside == total, $"전부 바스켓 안 {inside}/{total}");
                Check(asleep >= Mathf.CeilToInt(total * 0.8f), $"배치 직후 정착(sleep) {asleep}/{total}");
                var tilts = bowlList.Select(b => Vector3.Angle(b.transform.up, basket.transform.up).ToString("F0")).ToArray();
                if (basket.BedActive)
                {
                    // 10-26(2) 받침 위는 아무 방향으로 붓는다(사용자 "자연스럽게") — 바로 섬·덮임은 검사 대신 기록
                    log.AppendLine($"  INFO 붓기 배치: 그릇 바로 선 것 {upright}/{bowls} · 덮인 그릇 {covered} · 기울기 {string.Join("·", tilts)}°");
                    Check(bowls > 0 && upright < bowls, $"받침 위 붓기 = 그릇이 다 바로 서 있지 않음(자연스럽게) {upright}/{bowls}");
                }
                else
                {
                    Check(bowls > 0 && upright == bowls, $"그릇 바로 섬(30° 안) {upright}/{bowls} (기울기 {string.Join("·", tilts)}°)");
                    Check(covered == 0, $"그릇 위를 덮은 아이템 없음(보이는 데 있음) — 덮인 그릇 {covered}");
                }
                Check(basket.CellCount >= total, $"배치 셀 {basket.CellCount} ≥ 아이템 {total}");
                log.AppendLine($"  INFO 폭 m: 세라믹 {spec.Width(ClawItem.Kind.Ceramic):F3}(높이 {spec.Height(ClawItem.Kind.Ceramic):F3}) · 유리 {spec.Width(ClawItem.Kind.Glass):F3}(높이 {spec.Height(ClawItem.Kind.Glass):F3}) · " +
                               $"캡슐 {spec.Width(ClawItem.Kind.Capsule):F3} · 폭탄 {spec.Width(ClawItem.Kind.Bomb):F3} · 헤드 지름 {ClawLayout.HeadRadius * 2f:F3}");
                Check(spec.MaxItemWidth < ClawLayout.HeadRadius * 2f, $"가장 큰 아이템 폭 {spec.MaxItemWidth:F3} < 헤드 지름 {ClawLayout.HeadRadius * 2f:F3}");
                log.AppendLine($"  INFO 정착 예열 {basket.LastSettleMs:F0}ms (에디터 — 기기 목표 ≤ 500)");
                float fy = ClawLayout.FloorY;
                log.AppendLine($"  INFO 높이 예산(바닥 기준 m, 집게 ×{ClawLayout.SupClawScale:F2}): 쉬는 발끝 +{ClawLayout.IdleTipY - fy:F3} · 칸막이 위 +{ClawLayout.ChuteGuardTopY - fy:F3} · " +
                               $"더미 꼭대기 +{ClawLayout.PileTopY - fy:F3} · 받침 윗면 +{ClawLayout.BedTopY(spec.MaxItemHeight) - fy:F3} · 뚜껑 +{ClawLayout.LidY - fy:F3}");

                // 3a-2) 10-20 받침 더미(아래층 고정): 메뉴 30 규격과 같음 · 윗면 ≤ 예산 · 배출구 기둥 안 0 · 실물 꼭대기 ≤ 더미 꼭대기 · 쉬는 발끝 여유 · 기계와 함께 보임
                var bed = basket.Bed;
                if (gm.Config.clawBed)
                {
                    Check(bed != null && basket.BedActive, $"받침 더미 켜짐 (조각 {(bed != null ? bed.Count : 0)}개)");
                    if (bed != null && bed.Count > 0)
                    {
                        // 10-26 받침 = 상품 3종 1:1:1(사용자 "캡슐 더미까지 전부 큐브·그릇")
                        var kindN = new Dictionary<ClawItem.Kind, int>();
                        for (int i = 0; i < bed.Count; i++) { var k = bed.PieceKind(i); kindN[k] = (kindN.TryGetValue(k, out int kn) ? kn : 0) + 1; }
                        int nC = kindN.GetValueOrDefault(ClawItem.Kind.Ceramic), nG = kindN.GetValueOrDefault(ClawItem.Kind.Glass), nK = kindN.GetValueOrDefault(ClawItem.Kind.Keyring);
                        int minK = Mathf.Min(nC, Mathf.Min(nG, nK)), maxK = Mathf.Max(nC, Mathf.Max(nG, nK));
                        Check(bed.BakedHash == ClawBed.ComputeHash(spec) && nC + nG + nK == bed.Count && minK > 0 && maxK <= minK * 1.35f + 3,
                              $"받침 조각 = 상품 3종 비슷하게(세라믹 {nC}·유리 {nG}·키링 {nK}, 캡슐 0) · 규격 지문 일치 (다르면 메뉴 30 재실행)");
                        int inCol = 0; float bedTop = float.MinValue;
                        for (int i = 0; i < bed.Count; i++)
                        {
                            var p = bed.LocalPosition(i);
                            if (p.x < ClawLayout.ChuteWallX && p.z < ClawLayout.ChuteWallZ) inCol++;
                            bedTop = Mathf.Max(bedTop, bed.LocalTop(i));
                        }
                        float bedBudget = ClawLayout.BedTopY(spec.MaxItemHeight);
                        Check(bedTop <= bedBudget + 0.003f && bedTop >= bedBudget - bed.PieceRadius && inCol == 0,
                              $"받침 윗면 +{bedTop - fy:F3} ≤ 예산 +{bedBudget - fy:F3} · 배출구 기둥 안 조각 {inCol}");
                        float itemTop = float.MinValue;
                        foreach (var it in pool.All)
                        {
                            if (!it.gameObject.activeSelf) continue;
                            var w = it.transform.position;
                            itemTop = Mathf.Max(itemTop, basket.transform.InverseTransformPoint(new Vector3(w.x, it.TopWorldY, w.z)).y);
                        }
                        // 10-26(2) 아무 방향 붓기는 엎어진 것이 몇 cm 더 솟을 수 있다(붓기 손보기가 +2cm 넘는 것을 다시 떨어뜨림) — 지키는 것은 쉬는 발끝 여유 6cm
                        float topSlack = basket.BedActive ? 0.05f : 0.02f;
                        Check(itemTop <= ClawLayout.PileTopY + topSlack && ClawLayout.IdleTipY - itemTop >= 0.06f,
                              $"실물 꼭대기 +{itemTop - fy:F3} ≤ 더미 꼭대기 +{ClawLayout.PileTopY - fy:F3}(+{topSlack * 100f:F0}cm) · 쉬는 발끝까지 {(ClawLayout.IdleTipY - itemTop) * 100f:F1}cm ≥ 6cm");
                        Check(bed.Render != null && bed.Render.enabled && bed.Render.gameObject.activeInHierarchy, "Playing에서 받침 더미 보임");
                        log.AppendLine($"  INFO 받침 더미 조각 {bed.Count}(맨 윗층 {bed.TopLayer.Length} = 진짜 상품 {basket.RealTopCount}) · 실물 한 층 상한 {gm.PlaceCapacity}(셀 {basket.SingleLayerCellCount}, 풀 {gm.PoolCapacity} − 승격 몫 {ClawBasket.PromotionReserve} − 맨 윗층 {basket.RealTopReserve})");
                    }
                }
                else
                {
                    // 10-28 더미 꺼짐(clawBed=false 기본) — 받침 안 보임·진짜 맨 윗층 없음·상품은 바닥(0층 칸)에
                    bool bedHidden = bed == null || bed.Render == null || !bed.Render.enabled || !bed.Render.gameObject.activeInHierarchy;
                    int onFloor = 0;
                    foreach (var it in pool.All)
                    {
                        if (!it.gameObject.activeSelf || it.Held) continue;
                        if (Local(it).y - it.HalfHeight < ClawLayout.FloorY + 0.02f) onFloor++;
                    }
                    Check(!basket.BedActive && bedHidden && basket.RealTopCount == 0 && total <= basket.LowerCellCount && onFloor >= Mathf.CeilToInt(total * 0.8f),
                          $"받침 더미 꺼짐(clawBed=false) · 바닥 배치 (더미 보임 {!bedHidden}, 맨 윗층 {basket.RealTopCount}, 상품 {total} ≤ 0층 칸 {basket.LowerCellCount}, 바닥에 닿음 {onFloor}/{total})");
                }
            }

            // 3c) 스코어보드: 좌·우 열이 뒤 기둥 창 안(눈 기준 수평각) + 그릇 0 / N
            {
                var sb = FindAnyObjectByType<ClawScoreboard>();
                if (sb != null)
                {
                    var pillar = ClawLayout.BackPillarInnerXZ;
                    float windowDeg = Mathf.Atan2(pillar.x, pillar.y) * Mathf.Rad2Deg;
                    float worst = 0f;
                    var corners = new Vector3[4];
                    foreach (var name in new[] { "Left", "Right", "Time" })
                    {
                        var rt = sb.transform.Find("ScoreCanvas/" + name) as RectTransform;
                        if (rt == null) { worst = 999f; continue; }
                        rt.GetWorldCorners(corners);
                        foreach (var c in corners)
                        {
                            var lp = basket.transform.InverseTransformPoint(c);
                            worst = Mathf.Max(worst, Mathf.Abs(Mathf.Atan2(lp.x, lp.z) * Mathf.Rad2Deg));
                        }
                    }
                    Check(worst < windowDeg, $"스코어보드 글자가 뒤 기둥 창 안 (최대 {worst:F1}° < 창 {windowDeg:F1}°)");
                    var left = sb.transform.Find("ScoreCanvas/Left")?.GetComponent<TMPro.TMP_Text>();
                    yield return null;
                    Check(left != null && left.text.Contains($"큐브 0 / {gm.TargetCount}"), $"스코어보드 큐브 0 / {gm.TargetCount} ({(left != null ? left.text.Replace('\n', '/') : "null")})");
                }
                else Check(false, "ClawScoreboard 없음");
            }

            var machineRoot = GameObject.Find("BoothRoot/Environment/Machine");
            int leftover = 0;
            if (machineRoot != null)
                foreach (var t in machineRoot.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Model_2.006" || t.name == "Model_2.004" || t.name == "Model_2.005" || (t.name.StartsWith("Model_2.0") && int.TryParse(t.name.Substring(8), out int pn) && pn >= 19 && pn <= 32)
                        || t.name.StartsWith("tripo_part")) leftover++;
            Check(machineRoot != null && leftover == 0, $"기계 = Model{ClawLayout.MachineModel}, 떼어낸·원본 파트 잔존 0 (root={(machineRoot != null)}, 잔존 {leftover})");
            if (ClawLayout.IsModel3)
            {
                // 10-13 Model3: 몸체 한 메시가 콜라이더 내부(HalfX × FrontZ~BackZ)를 감싸고 좌우 중심이 맞는다
                var bodyT = machineRoot != null ? machineRoot.transform.Find("Body") : null;
                var bodyMf = bodyT != null ? bodyT.GetComponent<MeshFilter>() : null;
                if (bodyMf != null && bodyMf.sharedMesh != null)
                {
                    var mb = bodyMf.sharedMesh.bounds;
                    var lb = new Bounds(basket.transform.InverseTransformPoint(bodyT.TransformPoint(mb.center)), Vector3.zero);
                    for (int cx = -1; cx <= 1; cx += 2) for (int cy = -1; cy <= 1; cy += 2) for (int cz = -1; cz <= 1; cz += 2)
                        lb.Encapsulate(basket.transform.InverseTransformPoint(bodyT.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3(cx, cy, cz)))));
                    Check(lb.min.x <= -ClawLayout.HalfX && lb.max.x >= ClawLayout.HalfX && lb.min.z <= ClawLayout.FrontZ && lb.max.z >= ClawLayout.BackZ
                          && lb.min.y <= ClawLayout.FloorY && lb.max.y >= ClawLayout.LidY && Mathf.Abs(lb.center.x) < 0.02f,
                          $"{(ClawLayout.CabinetSup ? "StartUpPark" : "Model3")} 몸체 {lb.min:F3}~{lb.max:F3}가 내부 ±{ClawLayout.HalfX:F3} × z {ClawLayout.FrontZ:F3}~{ClawLayout.BackZ:F3} × y {ClawLayout.FloorY:F3}~{ClawLayout.LidY:F3}를 감쌈");
                }
                else Check(false, "기계 몸체(Machine/Body) 메시 없음");
            }
            // 11차: 가로 1.2배 — 유리 몸체 폭이 콜라이더 내부 폭(HalfX)을 감싸고(유리 두께만큼 바깥) 깊이는 그대로
            else
            {
                var glassT = machineRoot != null ? machineRoot.transform.Find("Model_2") : null;
                var glassMf = glassT != null ? glassT.GetComponent<MeshFilter>() : null;
                if (glassMf != null && glassMf.sharedMesh != null)
                {
                    // 부스(바스켓) 로컬 기준 바운즈 — 월드 AABB는 부스 yaw에 따라 가로·깊이가 섞인다
                    var mb = glassMf.sharedMesh.bounds;
                    var lb = new Bounds(basket.transform.InverseTransformPoint(glassT.TransformPoint(mb.center)), Vector3.zero);
                    for (int cx = -1; cx <= 1; cx += 2) for (int cy = -1; cy <= 1; cy += 2) for (int cz = -1; cz <= 1; cz += 2)
                        lb.Encapsulate(basket.transform.InverseTransformPoint(glassT.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3(cx, cy, cz)))));
                    var gc = lb.center;
                    float halfW = lb.extents.x, halfD = lb.extents.z;
                    Check(halfW >= ClawLayout.HalfX - 0.002f && halfW <= ClawLayout.HalfX + 0.04f && halfD >= ClawLayout.HalfZ - 0.002f && halfD <= ClawLayout.HalfZ + 0.04f && Mathf.Abs(gc.x) < 0.02f,
                          $"유리 몸체 반폭 {halfW:F3}·반깊이 {halfD:F3} ↔ 내부 {ClawLayout.HalfX:F3}×{ClawLayout.HalfZ:F3}, 중심 x {gc.x:F3} (가로 ×{ClawLayout.MachineWidthScale})");
                }
                else Check(false, "유리 파트(Model_2) 렌더러 없음");
            }
            if (boothT != null)
                Check(boothT.gameObject.activeSelf && Mathf.Abs(boothT.localPosition.y) < 0.001f, $"Playing에서 부스 표시·슬라이드 완료 (active={boothT.gameObject.activeSelf}, y={boothT.localPosition.y:F3})");
            var rigFingers = head.GetComponent<ClawFingers>();
            if (rigFingers != null && rigFingers.JointCount > 1)
            {
                // 10-22 뼈 집게: 몸통 메시가 따로 없다(스킨 한 메시) — 휴식 자세 발끝 반지름이 잡기 반경 기준(HeadRadius)과 맞는지
                float tr = rigFingers.RestTipRadius();
                Check(tr > 0f && Mathf.Abs(tr - ClawLayout.HeadRadius) < 0.03f, $"뼈 집게 휴식 발끝 반지름 {tr:F3} ≈ HeadRadius {ClawLayout.HeadRadius:F3}");
            }
            else
            {
                var ufoMf = head.GetComponentInChildren<MeshFilter>();
                float ufoR = ufoMf != null && ufoMf.sharedMesh != null ? Mathf.Max(ufoMf.sharedMesh.bounds.extents.x, ufoMf.sharedMesh.bounds.extents.z) : -1f;
                Check(ufoR > 0f && ufoR <= ClawLayout.HeadRadius + 0.005f, $"UFO 본체 메시 반지름 {ufoR:F3} ≤ HeadRadius {ClawLayout.HeadRadius:F3}");
            }

            // 3b) 배출구 유리 울타리: 배치 시 배출구 칸 비어 있음 · 침입 복구 · 굴러오는 아이템 차단
            int inChute = 0;
            foreach (var it in pool.All) if (it.gameObject.activeSelf && basket.IsInChuteCell(it.transform.position, it.HalfExtent)) inChute++;
            Check(inChute == 0, $"배치 직후 배출구 칸 안 아이템 0 (실제 {inChute})");
            ClawItem probeItem = null;
            probeItem = LooseItem(); // 10-26 캡슐이 없으면 상품으로
            if (probeItem != null)
            {
                var probeHome = probeItem.transform.position;
                int intr0 = basket.ChuteIntrusionCount;
                probeItem.Teleport(basket.transform.TransformPoint(new Vector3(ClawLayout.ChuteLocal.x, ClawLayout.FloorY + probeItem.HalfHeight + 0.005f, ClawLayout.ChuteLocal.z)), Quaternion.identity);
                probeItem.Body.WakeUp();
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.25f);
                Check(!basket.IsInChuteCell(probeItem.transform.position, probeItem.HalfExtent) && basket.ChuteIntrusionCount == intr0 + 1 && basket.IsInside(probeItem.transform.position, 0.02f)
                      && !basket.OverChuteColumn(probeItem.transform.position, probeItem.HalfExtent),
                      $"배출구 칸 침입 복구 → 기둥 밖 더미 위 (intrusion {intr0}→{basket.ChuteIntrusionCount}, 위치 {Local(probeItem)})");
                yield return new WaitForSeconds(0.75f);
                int intr1 = basket.ChuteIntrusionCount;
                var near = new Vector3(ClawLayout.ChuteWallX + spec.CellClear, ClawLayout.FloorY + 0.05f, ClawLayout.ChuteWallZ - 0.08f);
                ClawItem roller = null; float best = float.MaxValue;
                foreach (var it in pool.All)
                {
                    if (!it.gameObject.activeSelf || it.Held || it.BedIndex >= 0 || it.TopLayer || it.ItemKind == ClawItem.Kind.Bomb) continue;
                    float dd = (Local(it) - near).sqrMagnitude;
                    if (dd < best) { best = dd; roller = it; }
                }
                if (roller != null)
                {
                    roller.Body.WakeUp();
                    roller.Body.linearVelocity = basket.transform.TransformDirection(new Vector3(-1.0f, 0f, -0.2f));
                    yield return new WaitForSeconds(1.0f);
                    Check(!basket.IsInChuteCell(roller.transform.position, roller.HalfExtent) && basket.ChuteIntrusionCount == intr1,
                          $"유리 벽이 굴러오는 {roller.ItemKind} 차단 (침입 {intr1}→{basket.ChuteIntrusionCount}, 위치 {Local(roller)})");
                }
            }

            // 3c) 10-20 보이지 않는 기둥(배출구 칸 전체, 바닥~뚜껑): 칸막이 위·뚜껑 아래 어느 높이로 던져도 못 넘어감 · 안쪽 면에 박힌 것은 파일 쪽으로 밀려남
            {
                gm.EditorSetTimeRemaining(180f); // 던지기 검사(약 4초)가 게임 제한 시간을 먹지 않게
                ClawItem thrower = null;
                thrower = LooseItem(probeItem);
                if (thrower != null)
                {
                    var home = thrower.transform.position; var homeRot = thrower.transform.rotation;
                    int intr0 = basket.ChuteIntrusionCount, blocked = 0, tries = 0;
                    float he = thrower.HalfExtent;
                    float[] heights = { ClawLayout.ChuteGuardTopY + he + 0.02f, (ClawLayout.ChuteGuardTopY + ClawLayout.LidY) * 0.5f, ClawLayout.LidY - he - 0.03f };
                    float speed = Mathf.Max(basket.MaxItemSpeed, 3f);
                    float midZ = (ClawLayout.FrontZ + ClawLayout.ChuteWallZ) * 0.5f, midX = (-ClawLayout.HalfX + ClawLayout.ChuteWallX) * 0.5f;
                    foreach (float h in heights)
                        for (int side = 0; side < 2; side++)
                        {
                            // side 0: X 칸막이 바깥(파일 쪽)에서 −x로 / side 1: Z 칸막이 바깥에서 −z로 — 살짝 위로 던짐
                            var from = side == 0 ? new Vector3(ClawLayout.ChuteWallX + he + 0.06f, h, midZ) : new Vector3(midX, h, ClawLayout.ChuteWallZ + he + 0.06f);
                            var dir = side == 0 ? new Vector3(-1f, 0.25f, -0.1f) : new Vector3(-0.1f, 0.25f, -1f);
                            thrower.Teleport(basket.transform.TransformPoint(from), Quaternion.identity);
                            thrower.Body.WakeUp();
                            thrower.Body.linearVelocity = basket.transform.TransformDirection(dir.normalized * speed);
                            Physics.SyncTransforms();
                            tries++;
                            bool entered = false;
                            float tEnd = Time.time + 0.5f;
                            while (Time.time < tEnd)
                            {
                                if (basket.IsInChuteCell(thrower.transform.position, he)) entered = true;
                                yield return new WaitForFixedUpdate();
                            }
                            if (!entered) blocked++;
                        }
                    Check(blocked == tries && basket.ChuteIntrusionCount == intr0,
                          $"보이지 않는 기둥: 칸막이 위~뚜껑 아래 {heights.Length}높이×2면 {blocked}/{tries} 막음 ({speed:F1}m/s, 침입 {intr0}→{basket.ChuteIntrusionCount})");
                    // 안쪽 면(X)에 반지름 60% 박힌 채 놓기 → 0.5초 안에 파일 쪽(+x)으로 밀려남
                    var stuck = new Vector3(ClawLayout.ChuteWallX + he * 0.4f, ClawLayout.ChuteGuardTopY + he + 0.05f, midZ);
                    thrower.Teleport(basket.transform.TransformPoint(stuck), Quaternion.identity);
                    thrower.Body.WakeUp();
                    Physics.SyncTransforms();
                    yield return new WaitForSeconds(0.5f);
                    var sl = Local(thrower);
                    // 10-26 상품(그릇·큐브)은 HalfExtent가 실제 옆 폭보다 넉넉해(그릇 테두리·큐브 대각) 반 크기의 60%로 본다
                    float pushMin = ClawLayout.ChuteWallX + (thrower.ItemKind == ClawItem.Kind.Capsule ? he - 0.01f : he * 0.6f);
                    Check(sl.x >= pushMin && !basket.IsInChuteCell(thrower.transform.position, he) && basket.ChuteIntrusionCount == intr0,
                          $"기둥 안쪽 면에 박힌 {thrower.ItemKind} → 파일 쪽으로 밀려남 (x {sl.x:F3} ≥ {pushMin:F3}, 침입 {intr0}→{basket.ChuteIntrusionCount})");
                    thrower.Teleport(home, homeRot);
                    thrower.Body.Sleep();
                    Physics.SyncTransforms();
                }
                else Check(false, "기둥 검사용 실물 없음");
            }

            // 4) 조이스틱 → 캐리지 이동(앞), 경계 클램프
            var start = carriage.LocalXZ;
            var console = FindAnyObjectByType<ClawConsoleVisual>();
            ClawPadInput.SetMachineStick(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.6f);
            Vector3 stickUp = console != null && console.StickPivot != null ? console.StickPivot.localRotation * Vector3.up : Vector3.up;
            ClawPadInput.SetMachineStick(Vector2.zero);
            var moved = carriage.LocalXZ;
            Check(moved.y > start.y + 0.05f && moved.y <= ClawLayout.CarriageMaxZ + 0.001f, $"스틱 앞 → 캐리지 z {start.y:F2}→{moved.y:F2} (상한 {ClawLayout.CarriageMaxZ:F2})");
            Check(console != null && stickUp.z > 0.2f && Mathf.Abs(stickUp.x) < 0.05f, $"조이스틱 앞으로 기울어짐 (up {stickUp})");
            foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, 1f) })
            {
                carriage.SetLocalXZ(new Vector2(corner.x * 9f, ClawLayout.CenterZ + corner.y * 9f));
                yield return null;
                var hl = basket.transform.InverseTransformPoint(head.transform.position);
                Check(Mathf.Abs(hl.x) + ClawLayout.HeadRadius <= ClawLayout.HalfX + 0.001f
                      && hl.z - ClawLayout.HeadRadius >= ClawLayout.FrontZ - 0.001f && hl.z + ClawLayout.HeadRadius <= ClawLayout.BackZ + 0.001f,
                      $"UFO 원반 유리 안 (코너 {corner}, 헤드 로컬 {hl})");
            }
            carriage.SetLocalXZ(start);
            ClawPadInput.SetMachineStick(new Vector2(1f, 0f));
            yield return new WaitForSeconds(0.25f);
            stickUp = console != null && console.StickPivot != null ? console.StickPivot.localRotation * Vector3.up : Vector3.up;
            ClawPadInput.SetMachineStick(Vector2.zero);
            Check(console != null && stickUp.x > 0.2f, $"조이스틱 오른쪽으로 기울어짐 (up {stickUp})");

            // 4b) 홈 → 종료/취소 다이얼로그: 조이스틱 좌우 선택·잡기 확정·타이머 정지·캐리지 차단 (종료는 핸들러 교체로 검증)
            var dlg = FindAnyObjectByType<ClawExitDialog>(FindObjectsInactive.Include);
            if (dlg != null)
            {
                bool quitCalled = false;
                var prevQuit = gm.QuitHandler;
                gm.QuitHandler = () => quitCalled = true;

                yield return new WaitForSeconds(0.5f);
                ClawPadInput.PressHome(); yield return null;
                Check(gm.ExitDialogOpen && dlg.Selected == 1, $"홈 → 다이얼로그 열림, 기본 선택=취소 (open={gm.ExitDialogOpen}, sel={dlg.Selected})");
                var cxz = carriage.LocalXZ;
                ClawPadInput.SetMachineStick(new Vector2(-1f, 0f)); yield return null; yield return null;
                Check(dlg.Selected == 0, $"스틱 왼쪽 → 종료 선택 (sel={dlg.Selected})");
                yield return new WaitForSeconds(0.3f);
                Check((carriage.LocalXZ - cxz).magnitude < 0.001f, "다이얼로그 중 캐리지 입력 차단");
                ClawPadInput.SetMachineStick(new Vector2(0.3f, 0f)); yield return null;
                Check(dlg.Selected == 0, "데드밴드(0.3) 선택 유지");
                ClawPadInput.SetMachineStick(new Vector2(1f, 0f)); yield return null;
                Check(dlg.Selected == 1, $"스틱 오른쪽 → 취소 선택 (sel={dlg.Selected})");
                ClawPadInput.SetMachineStick(Vector2.zero);
                float remain0 = gm.TimeRemaining;
                yield return new WaitForSeconds(0.5f);
                Check(Mathf.Abs(gm.TimeRemaining - remain0) < 0.05f, $"다이얼로그 중 제한 시간 정지 ({remain0:F2}→{gm.TimeRemaining:F2})");
                Tap(); yield return null; yield return null;
                Check(!gm.ExitDialogOpen && gm.Phase == ClawGameManager.PlayPhase.Aim && !quitCalled, "취소 확정 → 닫힘·조준 유지·종료 안 함");

                ClawPadInput.PressHome(); yield return null;
                ClawPadInput.SetMachineStick(new Vector2(-1f, 0f)); yield return null; yield return null;
                ClawPadInput.SetMachineStick(Vector2.zero);
                Tap(); yield return null; yield return null;
                Check(quitCalled && !gm.ExitDialogOpen, "종료 확정 → QuitHandler 호출·닫힘");
                gm.QuitHandler = prevQuit;

                dlg.AutoCloseSeconds = 0.3f;
                ClawPadInput.PressHome(); yield return new WaitForSeconds(0.6f);
                Check(!gm.ExitDialogOpen && gm.Current == ClawGameManager.State.Playing, "무조작 자동 취소");
                dlg.AutoCloseSeconds = 10f;
            }
            else log.AppendLine("  SKIP ClawExitDialog 없음(메뉴 30 미실행)");

            // 4c) 홈 길게 누르기 → 재정렬(4차): 파일 형태·상태 유지
            {
                var camT = Camera.main != null ? Camera.main.transform : null;
                var camParent = camT != null ? camT.parent : null;
                // 10-20: 승격본(받침 → 진짜 캡슐)은 중간에 되돌려질 수 있어 실물만 비교
                var localBefore = new Dictionary<ClawItem, Vector3>();
                foreach (var it in pool.All) if (it.gameObject.activeSelf && !it.Held && it.BedIndex < 0) localBefore[it] = basket.transform.InverseTransformPoint(it.transform.position);
                int activeA = RealActive(), escA = basket.EscapeCount, rc0 = gm.RecenterCount;
                Quaternion parentRot = camParent != null ? camParent.localRotation : Quaternion.identity;
                bool yawed = false;
                if (camParent != null) { camParent.localRotation = parentRot * Quaternion.Euler(0f, 25f, 0f); yawed = true; yield return null; }
                ClawPadInput.PressRecenter(); yield return null; yield return null;
                Check(gm.RecenterCount == rc0 + 1 && gm.Current == ClawGameManager.State.Playing, $"재정렬 실행 (count {rc0}→{gm.RecenterCount}, 상태 {gm.StateName})");
                if (yawed && camT != null)
                {
                    var fwd = camT.forward; fwd.y = 0f;
                    float camYaw = Quaternion.LookRotation(fwd.normalized, Vector3.up).eulerAngles.y;
                    float boothYaw = basket.transform.rotation.eulerAngles.y;
                    Check(Mathf.Abs(Mathf.DeltaAngle(camYaw, boothYaw)) < 1f, $"부스 yaw = 카메라 yaw ({boothYaw:F1} vs {camYaw:F1})");
                }
                float maxDev = 0f; int inside2 = 0, total2 = 0;
                foreach (var it in pool.All)
                {
                    if (!it.gameObject.activeSelf || it.Held) continue;
                    var l = basket.transform.InverseTransformPoint(it.transform.position);
                    if (localBefore.TryGetValue(it, out var lb0)) maxDev = Mathf.Max(maxDev, (l - lb0).magnitude);
                    total2++;
                    if (basket.IsInside(it.transform.position, 0.02f)) inside2++;
                }
                Check(maxDev < 0.005f && inside2 == total2 && RealActive() == activeA && basket.EscapeCount == escA,
                      $"재정렬 후 파일 유지 (편차 {maxDev * 1000f:F1}mm, 안 {inside2}/{total2}, 이탈 {escA}→{basket.EscapeCount})");
                if (yawed) { camParent.localRotation = parentRot; yield return null; ClawPadInput.PressRecenter(); yield return null; yield return null; }
            }

            // 4d) 홈 버튼 롱프레스 vs 클릭 (프리팹·EventSystem 있을 때)
            var homeBtn = FindAnyObjectByType<ClawHomeButton>(FindObjectsInactive.Include);
            if (homeBtn != null && EventSystem.current != null && homeBtn.gameObject.activeInHierarchy)
            {
                int rc = gm.RecenterCount;
                var ev = new PointerEventData(EventSystem.current) { pointerId = 0 };
                ExecuteEvents.Execute(homeBtn.gameObject, ev, ExecuteEvents.pointerDownHandler);
                yield return new WaitForSecondsRealtime(1.15f);
                ExecuteEvents.Execute(homeBtn.gameObject, ev, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(homeBtn.gameObject, ev, ExecuteEvents.pointerClickHandler);
                yield return null; yield return null;
                Check(!gm.ExitDialogOpen && gm.RecenterCount == rc + 1, $"홈 1.15초 누름 → 재정렬(count {rc}→{gm.RecenterCount}), 다이얼로그 안 열림(open={gm.ExitDialogOpen})");
                ExecuteEvents.Execute(homeBtn.gameObject, ev, ExecuteEvents.pointerDownHandler);
                yield return new WaitForSecondsRealtime(0.2f);
                ExecuteEvents.Execute(homeBtn.gameObject, ev, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(homeBtn.gameObject, ev, ExecuteEvents.pointerClickHandler);
                yield return null; yield return null;
                Check(gm.ExitDialogOpen, "홈 짧게 → 다이얼로그 열림");
                ClawPadInput.PressHome(); yield return null; yield return null;
                Check(!gm.ExitDialogOpen, "홈 재누름 → 다이얼로그 닫힘");
            }
            else log.AppendLine("  SKIP 홈 버튼 프리팹/EventSystem 없음 — 롱프레스 검사 생략");

            // 5) 가이드 = 잡힐 것: 캐리지를 그릇 위에 → 후보·가이드 대상·발광이 그 그릇 하나, 다른 데로 옮기면 발광도 옮겨 감
            ClawItem aimed = null;
            yield return AimAt(it => it.IsBowl, x => aimed = x);
            Check(aimed != null, $"캐리지를 그릇 위에 두면 그 그릇이 잡기 후보 (후보 {(head.Candidate != null ? head.Candidate.ItemKind.ToString() : "없음")})");
            if (aimed != null)
            {
                int glowing = 0;
                foreach (var it in pool.All) if (it.gameObject.activeSelf && it.Glowing) glowing++;
                yield return new WaitForSeconds(0.3f); // 스포트라이트가 서서히 켜짐
                var spotPos = Shader.GetGlobalVector("_ClawSpotPos");
                bool bombAim = aimed.ItemKind == ClawItem.Kind.Bomb;
                Check(guide.Showing && guide.Current == aimed && spotPos.w > 0.9f && glowing == (bombAim ? 1 : 0),
                      $"조준 표시(10-22 S2) = 은은한 스포트라이트 켜짐(세기 {spotPos.w:F2}) · 후보 {aimed.ItemKind} · 발광 {glowing}개(폭탄일 때만 1)");
                ClawItem other = null;
                yield return AimAt(it => it != aimed && !it.TopLayer && it.BedIndex < 0 && it.ItemKind != ClawItem.Kind.Bomb, x => other = x); // 10-26 캡슐 대신 다른 실물
                Check(other != null && !other.Glowing && guide.Current == other, "다른 아이템 위로 옮기면 조준 대상도 옮겨 감(발광 없음)");
            }

            // 6) 1개 잡기 — 10-28 키링 큐브: 가이드가 가리킨 그 큐브 하나만 → 배출구 → 큐브 1 / N
            ClawItem bowl = null; // (이름은 예전 그대로 — 10-28부터 겨누는 것은 큐브)
            yield return AimAt(it => it.ItemKind == ClawItem.Kind.Keyring, x => bowl = x);
            if (bowl != null)
            {
                var bowlKind = bowl.ItemKind;
                int active0 = RealActive(), grabs0 = gm.Grabs, kg6 = gm.KeyringGot;
                var mfx = FindAnyObjectByType<ClawMachineFx>();
                int flash0 = mfx != null ? mfx.FlashCount : 0, ripple0 = mfx != null ? mfx.RippleCount : 0;
                int miss6 = 0;
                yield return GrabTarget(bowl, 3, m => miss6 = m); // 10-22 물리 집게: 둘레를 비워 따로 놓고(놓치면 다시)
                if (miss6 > 0) log.AppendLine($"  INFO 그릇 물리 잡기 놓침 {miss6}번 뒤 성공");
                // 10-19 집게발: 내려갈 때 펼치고(+), 그릇을 든 채 올라가·이동할 때 오므림(−)
                var fingerComp = head.GetComponent<ClawFingers>();
                // 10-19 하강은 곧게: 캐리지를 옮긴 직후 잡아도(AimAt이 순간 이동) 집게가 캐리지 바로 아래로 — 조준 예측 = 실제 접촉
                Check(descendOffsetMax < 0.012f, $"잡기 하강이 곧게(흔들림 감쇠): 캐리지에서 벗어난 최대 {descendOffsetMax * 100f:F1}cm < 1.2cm");
                Check(descendDur > 0f && Mathf.Abs(descendDur - head.DescendSeconds) < 0.1f,
                      $"하강 시간 일정(높이 무관): {descendDur:F2}s ≈ {head.DescendSeconds:F2}s (기준 {ClawHead.DescendRefDistance:F2}m ÷ {head.DescendSpeed:F2}m/s)");
                Check(fingerComp != null && fingerComp.FingerCount == 3 && fingerOpenMax > 8f && fingerHoldMin < -6f,
                      $"집게발 3개 여닫기: 하강 펼침 최대 {fingerOpenMax:F1}° · 든 채 오므림 {fingerHoldMin:F1}° (발 {(fingerComp != null ? fingerComp.FingerCount : 0)}개)");
                // 비주얼 2차: 그릇 획득 → 네온 번쩍임·바닥 물결·획득 파티클
                bool collectPlaying = false;
                foreach (var fx in FindObjectsByType<ClawExplosionVfx>(FindObjectsSortMode.None))
                    if (fx.name.StartsWith("CollectFx") && fx.Playing) collectPlaying = true;
                Check(mfx != null && mfx.FlashCount > flash0 && mfx.RippleCount > ripple0 && collectPlaying,
                      $"큐브 획득 연출: 네온 번쩍임 {(mfx != null ? mfx.FlashCount - flash0 : -1)}·바닥 물결 {(mfx != null ? mfx.RippleCount - ripple0 : -1)}·획득 파티클 {collectPlaying}");
                Check(captured.Contains(bowl) && captured.Count == 1 && heldMax == 1,
                      $"잡기 → 가이드가 가리킨 큐브 하나만 쥠 (집은 수 {captured.Count}, 한 번에 쥔 최대 {heldMax})");
                Check(guardClearMin < float.MaxValue && guardClearMin >= 0.02f,
                      $"운반 중 큐브가 아크릴 칸막이 위로 여유 있게 지나감 (바닥 − 칸막이 위 최소 {(guardClearMin < float.MaxValue ? guardClearMin * 100f : -1f):F1}cm ≥ 2cm)");
                Check(phasesSeen.Contains(ClawHead.Phase.Attach) && phasesSeen.Contains(ClawHead.Phase.Travel) && phasesSeen.Contains(ClawHead.Phase.Drop) && !phasesSeen.Contains(ClawHead.Phase.Recoil),
                      "집게 단계: 하강 → 집기 → 배출구 이동 → 낙하(폭발 반동 없음)");
                // 10-28 점수 = 키링 큐브만
                Check(gm.KeyringGot == kg6 + 1 && gm.DeliveredKinds.Contains(bowlKind) && gm.Grabs == grabs0 + 1 + miss6 && RealActive() <= active0 - 1,
                      $"배출구 투입 → 큐브 {kg6}→{gm.KeyringGot} / 목표 {gm.TargetCount}, 아이템 {active0}→{RealActive()}");
                var left = FindAnyObjectByType<ClawScoreboard>()?.transform.Find("ScoreCanvas/Left")?.GetComponent<TMPro.TMP_Text>();
                Check(left != null && left.text.Contains($"큐브 {gm.KeyringGot} / {gm.TargetCount}") && left.text.Contains("그릇은 점수 없음"),
                      $"스코어보드 큐브 {gm.KeyringGot} / {gm.TargetCount} ({(left != null ? left.text.Replace('\n', '/') : "null")})");
            }
            else Check(false, "잡을 수 있는 큐브 없음");

            // 6b') 10-28 그릇 투입 → 점수 그대로(방해물) · 배너 "그릇 — 점수 없음"(회색, 번쩍임 없음)
            {
                ClawItem dish = null;
                gm.EditorSetTimeRemaining(180f);
                yield return AimAt(it => it.IsBowl && it.BedIndex < 0 && !it.TopLayer, x => dish = x);
                if (dish != null)
                {
                    int kg0 = gm.KeyringGot, bowls0 = gm.CeramicGot + gm.GlassGot;
                    var mfx = FindAnyObjectByType<ClawMachineFx>();
                    int flash0 = mfx != null ? mfx.FlashCount : 0;
                    var banner = FindAnyObjectByType<ClawScoreboard>()?.transform.Find("ScoreCanvas/Banner")?.GetComponent<TMPro.TMP_Text>();
                    int missB = 0;
                    yield return GrabTarget(dish, 3, m => missB = m);
                    bool bannerOk = banner == null || banner.text == "그릇 — 점수 없음";
                    if (banner == null) log.AppendLine("  INFO 스코어보드 배너(ScoreCanvas/Banner) 못 찾음 — 배너 문구 검사 생략");
                    Check(captured.Contains(dish) && captured.Count == 1 && gm.KeyringGot == kg0 && gm.CeramicGot + gm.GlassGot == bowls0 + 1 && bannerOk
                          && (mfx == null || mfx.FlashCount == flash0) && gm.Current == ClawGameManager.State.Playing,
                          $"그릇 투입 → 큐브 그대로 {kg0}→{gm.KeyringGot} · 그릇 {bowls0}→{gm.CeramicGot + gm.GlassGot} · 배너 '{(banner != null ? banner.text : "-")}' · 번쩍임 {(mfx != null ? mfx.FlashCount - flash0 : -1)} (놓침 {missB})");
                }
                else Check(false, "잡을 수 있는 그릇 없음");
            }

            // 6b) 캡슐: 넣어도 무득점
            ClawItem capsule = null;
            if (gm.FillerCount > 0) yield return AimAt(it => it.ItemKind == ClawItem.Kind.Capsule && it.BedIndex < 0 && !it.TopLayer, x => capsule = x);
            if (gm.FillerCount == 0) log.AppendLine("  INFO 캡슐 0개(10-26 상품만) — 캡슐 무득점 검사 생략");
            else if (capsule != null)
            {
                int got0 = gm.Collected, active0 = RealActive();
                yield return GrabTarget(capsule, 3);
                Check(captured.Contains(capsule) && gm.Collected == got0 && RealActive() <= active0 - 1,
                      $"캡슐 투입 → 무득점 (그릇 {got0}→{gm.Collected}, 아이템 {active0}→{RealActive()})");
            }
            else Check(false, "잡을 수 있는 캡슐 없음");


            // 6c) 폭탄 위에 그릇이 얹혀 있으면 그릇을 잡는다(폭발 없음) — 둘 다 잠시 고정해 쌓은 채로
            ClawItem stackBowl = null, stackBomb = null;
            gm.EditorSetTimeRemaining(180f);
            if (gm.BombCount == 0) log.AppendLine("  INFO 폭탄 0개(10-26 사용자 \"폭탄 없애기\") — 폭탄 위 그릇·폭탄 접촉 검사 생략(폭발 자체는 7b에서)");
            else {
            foreach (var it in pool.All) if (it.gameObject.activeSelf && !it.Held && it.IsBowl) { stackBowl = it; break; } // 10-22 겨누지 않고 고름(어차피 옮겨 쌓는다)
            foreach (var it in pool.All) if (it.gameObject.activeSelf && !it.Held && it.ItemKind == ClawItem.Kind.Bomb) { stackBomb = it; break; }
            if (stackBowl != null && stackBomb != null)
            {
                var bombHome = stackBomb.transform.position; var bombHomeRot = stackBomb.transform.rotation;
                var lb0 = Local(stackBowl);
                var lb = new Vector3(Mathf.Clamp(lb0.x, ClawLayout.CarriageMinX + 0.05f, ClawLayout.CarriageMaxX - 0.05f), lb0.y,
                                     Mathf.Clamp(lb0.z, ClawLayout.CarriageMinZ + 0.05f, ClawLayout.CarriageMaxZ - 0.05f));
                if (lb.x < ClawLayout.ChuteWallX + ClawLayout.CarriageMargin + 0.06f && lb.z < ClawLayout.ChuteWallZ + ClawLayout.CarriageMargin + 0.06f)
                    lb.x = ClawLayout.ChuteWallX + ClawLayout.CarriageMargin + 0.06f;
                ClearAround(new Vector2(lb.x, lb.z), 0.45f, 0.6f, new[] { stackBowl, stackBomb }); // 10-22 물리 집게: 둘레 비움
                float bombHalf = stackBomb.HalfHeight;
                // 10-20: 바닥 대신 그 자리 받침 표면(실물 아이템은 무시) 위에 쌓는다
                float baseY = basket.SurfaceCenterY(lb.x, lb.z, bombHalf, null, 0, new[] { stackBowl, stackBomb }) - bombHalf; // 10-22 진짜 맨 윗층 위
                stackBomb.Teleport(basket.transform.TransformPoint(new Vector3(lb.x, baseY + bombHalf + 0.001f, lb.z)), basket.transform.rotation);
                stackBomb.Body.isKinematic = true;
                stackBowl.Teleport(basket.transform.TransformPoint(new Vector3(lb.x, baseY + bombHalf * 2f + stackBowl.HalfHeight + 0.004f, lb.z)), basket.transform.rotation);
                stackBowl.Body.isKinematic = true;
                Physics.SyncTransforms();
                carriage.SetLocalXZ(new Vector2(lb.x, lb.z));
                yield return new WaitForSeconds(0.5f);
                int hits0 = gm.BombsHit, got0 = gm.Collected;
                Check(head.Candidate == stackBowl, $"폭탄 위 그릇 → 후보는 그릇 ({(head.Candidate != null ? head.Candidate.ItemKind.ToString() : "없음")})");
                yield return GrabCycle();
                Check(gm.BombsHit == hits0 && captured.Count == 1 && captured[0] == stackBowl && gm.Collected == got0 + 1 && stackBomb.gameObject.activeSelf,
                      $"폭탄 위 그릇 잡기 → 그릇만 집고 폭발 없음 (폭발 {hits0}→{gm.BombsHit}, 그릇 {got0}→{gm.Collected})");
                stackBomb.Body.isKinematic = false;
                stackBomb.Teleport(bombHome, bombHomeRot);
                stackBomb.Body.WakeUp();
                Physics.SyncTransforms();
                yield return new WaitForSeconds(0.5f);
            }
            else Check(false, "쌓기 검사용 그릇·폭탄 없음");
            }

            // 6d) 10-20 받침 캡슐 잡기(승격): 실물을 비운 받침 윗면 조각 위 → 후보 = 그 자리·같은 색 진짜 캡슐 → 잡아서 배출(무득점, 조각은 숨은 채)
            //     / 다른 조각을 겨눴다 떠나면 0.5초 뒤 받침으로 되돌림
            var bedT = basket.Bed;
            if (gm.Config.clawBed && bedT != null && bedT.Count > 0)
            {
                gm.EditorSetTimeRemaining(180f);
                int pieceA = -1;
                yield return ClearBedSpot(-1, x => pieceA = x);
                if (pieceA >= 0)
                {
                    var pa = bedT.LocalPosition(pieceA);
                    carriage.SetLocalXZ(new Vector2(pa.x, pa.z));
                    // 10-22 진짜 맨 윗층이 빈자리 가장자리에서 움직일 수 있어 몇 프레임 기다림
                    for (int f = 0; f < 15 && !(head.Candidate != null && head.Candidate.BedIndex == pieceA); f++) yield return null;
                    var proxy = head.Candidate;
                    bool same = proxy != null && proxy.BedIndex == pieceA && bedT.IsHidden(pieceA)
                                && (proxy.transform.position - bedT.WorldPosition(pieceA)).magnitude < 0.005f && proxy.ItemKind == bedT.PieceKind(pieceA);
                    Check(same, $"받침 조각 조준 → 같은 자리·같은 종류 진짜 상품으로 승격 (조각 {pieceA} {bedT.PieceKind(pieceA)}, 후보 {(proxy != null ? proxy.name + " " + proxy.ItemKind + " 조각 " + proxy.BedIndex : "없음")}, 거리 {(proxy != null ? (proxy.transform.position - bedT.WorldPosition(pieceA)).magnitude * 1000f : -1f):F1}mm)");
                    if (proxy != null)
                    {
                        int got0 = gm.Collected, prom0 = basket.PromotedTotal;
                        yield return GrabCycle();
                        if (!captured.Contains(proxy) && proxy.gameObject.activeSelf && proxy.BedIndex == pieceA) { yield return WaitAim(6f); yield return GrabTarget(proxy, 2); }
                        // 배출 뒤 풀 인스턴스는 곧바로 다른 승격에 다시 쓰일 수 있다 — "그 조각의 승격본으로 남아 있지 않음"으로 본다
                        int capT = 0;
                        foreach (var ci in captured) if (ci != null && ci.IsTarget) capT++;
                        bool scoreOk = proxy.IsTarget ? gm.Collected == got0 + capT : gm.Collected == got0; // 10-26 받침 = 상품 → 득점
                        Check(captured.Contains(proxy) && !(proxy.gameObject.activeSelf && proxy.BedIndex == pieceA) && bedT.IsHidden(pieceA) && scoreOk,
                              $"승격 상품 잡기 → 배출({(proxy.IsTarget ? "득점" : "무득점")})·받침 조각은 숨은 채 (집은 수 {captured.Count}, 집은 것 {(captured.Count > 0 ? captured[0].name : "-")}=후보 {(captured.Count > 0 && captured[0] == proxy)}, " +
                              $"활성 {proxy.gameObject.activeSelf}·조각 {proxy.BedIndex}, 숨김 {bedT.IsHidden(pieceA)}, 단계 {string.Join(">", phasesSeen)}, 그릇 {got0}→{gm.Collected})");
                    }
                }
                else Check(false, "승격 검사용 받침 조각 없음");

                // 10-26(2) 받침이 아무 방향으로 쌓여 옆에 기댄 조각이 많다 — 둘레 실물을 치우면 기댄 조각이 미끄러져 실물로 남는다(설계대로: 움직이면 되돌리지 않음).
                // 조각 3개까지 해 보고 하나라도 되돌려지면 통과, 셋 다 움직였으면 기록만
                int pieceB = -1, demotedOk = 0, movedAway = 0;
                string lastB = "";
                var triedB = new List<int> { pieceA };
                for (int tryB = 0; tryB < 3 && demotedOk == 0; tryB++)
                {
                    pieceB = -1;
                    int avoidB = triedB[triedB.Count - 1];
                    yield return ClearBedSpot(avoidB, x => pieceB = x);
                    if (pieceB < 0 || triedB.Contains(pieceB)) break;
                    triedB.Add(pieceB);
                    var pb = bedT.LocalPosition(pieceB);
                    carriage.SetLocalXZ(new Vector2(pb.x, pb.z));
                    yield return null; yield return null; yield return null;
                    var proxyB = head.Candidate;
                    bool promotedB = proxyB != null && proxyB.BedIndex == pieceB && bedT.IsHidden(pieceB);
                    int dem0 = basket.DemotedTotal;
                    // 멀리(반대쪽 뒤 모서리)로 옮기고 기다림 → 제자리에 잠든 채 안 겨눠졌으니 되돌림
                    carriage.SetLocalXZ(new Vector2(-Mathf.Sign(pb.x) * ClawLayout.CarriageMaxX * 0.9f, pb.z < ClawLayout.CenterZ ? ClawLayout.CarriageMaxZ : ClawLayout.CarriageMinZ + 0.3f));
                    yield return new WaitForSeconds(1.0f);
                    float movedB = proxyB != null ? (Local(proxyB) - pb).magnitude : -1f;
                    if (promotedB && !bedT.IsHidden(pieceB) && basket.DemotedTotal > dem0) demotedOk++;
                    else if (promotedB && movedB > 0.01f) movedAway++;
                    lastB = $"조각 {pieceB} 승격 {promotedB}·숨김 {bedT.IsHidden(pieceB)}·되돌림 {dem0}→{basket.DemotedTotal}·움직임 {movedB * 100f:F1}cm";
                }
                if (pieceB >= 0 || demotedOk > 0 || movedAway > 0)
                {
                    if (demotedOk == 0 && movedAway > 0) log.AppendLine($"  INFO 되돌림 검사 조각 {movedAway}개가 둘레를 치우자 미끄러져 실물로 남음(설계대로) — {lastB}");
                    Check(demotedOk > 0 || movedAway > 0, $"겨눴다 떠나면 받침으로 되돌림 (되돌림 {demotedOk} · 미끄러져 실물로 남음 {movedAway} — {lastB})");
                }
                else Check(false, "되돌림 검사용 받침 조각 없음");
                yield return WaitAim(6f);
            }

            // 7) 폭탄 접촉 → 즉시 폭발: 집지 않고 튕겨 오름 · 폭탄 사라짐 · 연출 · 기계 안 물체 대부분이 크게 움직임 · 밖으로 안 나감
            ClawItem bomb = null;
            gm.EditorSetTimeRemaining(180f);
            if (gm.BombCount > 0) {
            yield return AimAt(it => it.ItemKind == ClawItem.Kind.Bomb, x => bomb = x);
            if (bomb == null && bedT != null && basket.BedActive)
            {
                // 10-20 울퉁불퉁한 받침 위에서는 폭탄이 골에 들어가 옆 캡슐보다 집게 닿는 깊이 넘게 낮으면 안 닿는다(게임에서는 정상 — 안전한 폭탄).
                // 검사를 위해 받침 윗면 빈자리에 하나 올려놓고 다시 겨눈다
                ClawItem anyBomb = null;
                foreach (var it in pool.All) if (it.gameObject.activeSelf && !it.Held && it.ItemKind == ClawItem.Kind.Bomb) { anyBomb = it; break; }
                int spot = -1;
                if (anyBomb != null) yield return ClearBedSpot(-1, x => spot = x);
                if (spot >= 0)
                {
                    var sp = bedT.LocalPosition(spot) + Vector3.up * (bedT.PieceRadius + anyBomb.HalfHeight + 0.01f);
                    anyBomb.Teleport(basket.transform.TransformPoint(sp), basket.transform.rotation);
                    anyBomb.Body.WakeUp();
                    Physics.SyncTransforms();
                    yield return new WaitForSeconds(0.8f);
                    yield return AimAt(it => it == anyBomb, x => bomb = x);
                    log.AppendLine($"  INFO 겨눌 폭탄이 골에 있어 받침 윗면 조각 {spot} 위에 올려 검사 ({(bomb != null ? "성공" : "실패")})");
                }
            }
            if (bomb != null)
            {
                var before = new Dictionary<ClawItem, Vector3>();
                float startMaxY = ClawLayout.FloorY;
                foreach (var it in pool.All) if (it.gameObject.activeSelf && it != bomb) { before[it] = Local(it); startMaxY = Mathf.Max(startMaxY, before[it].y); }
                int hits0 = gm.BombsHit, got0 = gm.Collected, esc0 = basket.EscapeCount, active0 = RealActive(), intrB0 = basket.ChuteIntrusionCount, blast0 = basket.BlastPromotedTotal;
                captured.Clear(); phasesSeen.Clear();
                int bombs0 = 0;
                foreach (var it in pool.All) if (it.gameObject.activeSelf && it.ItemKind == ClawItem.Kind.Bomb) bombs0++;
                Tap();
                float t0 = Time.time;
                while (gm.BombsHit == hits0 && Time.time - t0 < 6f) { phasesSeen.Add(head.Current); yield return null; }
                int bombs1 = 0;
                foreach (var it in pool.All) if (it.gameObject.activeSelf && it.ItemKind == ClawItem.Kind.Bomb) bombs1++;
                // 10-22 물리 집게: 내려가며·오므리며 발에 닿거나, 다 올라간 집게 안에 폭탄이 딸려 왔으면 그때 폭발 — 아무것도 배출구로 안 감
                // (겨눈 폭탄 옆 다른 폭탄에 발이 먼저 닿을 수도 — 폭탄 하나가 사라짐)
                Check(gm.BombsHit == hits0 + 1 && bombs1 == bombs0 - 1 && head.Current == ClawHead.Phase.Recoil && captured.Count == 0 && !phasesSeen.Contains(ClawHead.Phase.Travel),
                      $"폭탄에 닿음 → 즉시 폭발·폭탄 사라짐·집지 않고 튕겨 오름 ({head.Current}, {Time.time - t0:F1}s)");
                bool fxPlaying = false;
                foreach (var fx in FindObjectsByType<ClawExplosionVfx>(FindObjectsSortMode.None)) if (fx.Playing) fxPlaying = true;
                Check(fxPlaying, "폭발 연출(파티클) 재생");
                if (basket.BedActive)
                    Check(basket.BlastPromotedTotal > blast0 && basket.BlastPromotedTotal - blast0 <= ClawBasket.BlastPromoteMax,
                          $"폭탄 옆 받침 윗면 캡슐도 같이 날아감(승격 {basket.BlastPromotedTotal - blast0}개 ≤ {ClawBasket.BlastPromoteMax})");
                yield return null; // 네온 세기는 다음 Update에 반영
                var mfxB = FindAnyObjectByType<ClawMachineFx>();
                Check(mfxB != null && mfxB.FlashLevel > 0.1f, $"폭발 → 기계 네온 붉게 번쩍임 (세기 {(mfxB != null ? mfxB.FlashLevel : -1f):F2})");
                float peak = ClawLayout.FloorY, t1 = Time.time;
                int aboveLid = 0;
                var maxMove = new Dictionary<ClawItem, float>();
                while (Time.time - t1 < 1.5f)
                {
                    foreach (var it in pool.All)
                    {
                        if (!it.gameObject.activeSelf || it.Held) continue;
                        var l = Local(it);
                        peak = Mathf.Max(peak, l.y);
                        if (l.y > ClawLayout.LidY + 0.01f) aboveLid++;
                        if (before.TryGetValue(it, out var b0)) maxMove[it] = Mathf.Max(maxMove.TryGetValue(it, out float m) ? m : 0f, (l - b0).magnitude);
                    }
                    yield return null;
                }
                int movedCount = 0, settledFar = 0;
                foreach (var kv in maxMove) if (kv.Value > 0.05f) movedCount++;
                foreach (var kv in before) if (kv.Key.gameObject.activeSelf && (Local(kv.Key) - kv.Value).magnitude > 0.05f) settledFar++;
                Check(movedCount >= Mathf.CeilToInt(before.Count * 0.8f), $"폭발 1.5초 동안 {movedCount}/{before.Count}개가 5cm 이상 움직임(80% 이상 — 기계 전체가 흔들림)");
                Check(settledFar >= Mathf.CeilToInt(before.Count * 0.5f), $"폭발 1.5초 뒤 {settledFar}/{before.Count}개가 원래 자리에서 5cm 이상 떨어짐(흩어짐)");
                Check(peak > startMaxY + 0.12f && aboveLid == 0, $"튀어 오른 최고 높이 바닥 +{peak - ClawLayout.FloorY:F2}m(폭발 전 최고 +{startMaxY - ClawLayout.FloorY:F2}보다 12cm 이상), 뚜껑 위 0 (프레임×개수 {aboveLid})");
                yield return WaitAim(6f);
                yield return new WaitForSeconds(1.5f);
                int insideN = 0, totalN = 0, chuteN = 0;
                foreach (var it in pool.All)
                {
                    if (!it.gameObject.activeSelf) continue;
                    totalN++;
                    if (basket.IsInside(it.transform.position, 0.02f)) insideN++;
                    if (basket.IsInChuteCell(it.transform.position, it.HalfExtent)) chuteN++;
                }
                Check(basket.EscapeCount == esc0 && insideN == totalN && chuteN == 0 && RealActive() == active0 - 1 && basket.ChuteIntrusionCount == intrB0,
                      $"폭발 뒤 이탈 0·전부 안 {insideN}/{totalN}·배출구 칸 0·기둥 침입 0 (침입 {intrB0}→{basket.ChuteIntrusionCount})");
                Check(gm.Current == ClawGameManager.State.Playing && gm.Phase == ClawGameManager.PlayPhase.Aim && gm.Collected == got0,
                      $"폭발은 감점 없음 → 조준 복귀 (그릇 {gm.Collected}, {gm.StateName})");
            }
            else Check(false, "잡을 수 있는 폭탄 없음");
            }

            // 7b) 폭발 직후 재정렬(홈 길게) — 날아다니는 중에도 이탈 0
            {
                int esc0 = basket.EscapeCount;
                basket.Explode(basket.transform.TransformPoint(new Vector3(0f, ClawLayout.FloorY + 0.05f, ClawLayout.CenterZ)), null);
                yield return new WaitForSeconds(0.1f);
                var camT = Camera.main != null ? Camera.main.transform : null;
                var camParent = camT != null ? camT.parent : null;
                Quaternion parentRot = camParent != null ? camParent.localRotation : Quaternion.identity;
                if (camParent != null) camParent.localRotation = parentRot * Quaternion.Euler(0f, 20f, 0f);
                yield return null;
                ClawPadInput.PressRecenter(); yield return null;
                yield return new WaitForSeconds(2f);
                int insideN = 0, totalN = 0;
                foreach (var it in pool.All) { if (!it.gameObject.activeSelf) continue; totalN++; if (basket.IsInside(it.transform.position, 0.02f)) insideN++; }
                Check(basket.EscapeCount == esc0 && insideN == totalN, $"폭발 중 재정렬 → 이탈 0·전부 안 {insideN}/{totalN}");
                if (camParent != null) { camParent.localRotation = parentRot; yield return null; ClawPadInput.PressRecenter(); yield return null; }
                yield return new WaitForSeconds(1.5f);
            }

            // 7c) 배출구 위에서 놓침(내림 중 Abort — 게임 종료 등) → 기둥과 겹치지 않게 더미 위로, 정상 배달 아님(10-20: 울타리 윗면 밀기 검사 대체 — 기둥에는 윗면이 없다)
            {
                ClawItem drop = null;
                gm.EditorSetTimeRemaining(180f);
                yield return AimAt(it => (gm.FillerCount > 0 ? it.ItemKind == ClawItem.Kind.Capsule : it.IsTarget) && it.BedIndex < 0 && !it.TopLayer, x => drop = x); // 10-26 캡슐이 없으면 상품
                if (drop == null)
                {
                    // 10-28 더미 없이 바닥에 20개 — 7b 폭발 뒤 상품이 벽 쪽(캐리지가 못 가는 곳)으로 흩어져 바로 겨눌 것이 없을 수 있다.
                    // 아래 Isolate가 어차피 가운데 쪽 자리에 다시 놓으므로 실물 하나를 골라 그대로 검사
                    drop = LooseItem();
                    if (drop != null) log.AppendLine($"  INFO 놓침 검사: 바로 겨눌 실물 없음(폭발 뒤 벽 쪽) → {drop.ItemKind} 하나를 가운데로 옮겨 검사");
                }
                if (drop != null)
                {
                    int intr0 = basket.ChuteIntrusionCount, got0 = gm.Collected;
                    bool reached = false;
                    for (int k = 0; k < 3 && !reached; k++)
                    {
                        if (k > 0) yield return WaitAim(6f);
                        gm.EditorSetTimeRemaining(180f);
                        yield return Isolate(drop, null, k); // 10-22 물리 집게: 따로 놓아 쥐게(놓치면 다른 자리에서 다시)
                        captured.Clear();
                        Tap();
                        float t0 = Time.time;
                        yield return null;
                        while (head.Current != ClawHead.Phase.DropLower && head.Busy && Time.time - t0 < 10f) yield return null;
                        reached = head.Current == ClawHead.Phase.DropLower && head.HeldItems.Contains(drop); // 10-22 물리 집게: 옆 캡슐이 함께 쥐일 수 있다
                    }
                    head.Abort();
                    yield return new WaitForSeconds(0.6f);
                    Check(reached && drop.gameObject.activeSelf && !drop.Held && basket.IsInside(drop.transform.position, 0.02f)
                          && !basket.OverChuteColumn(drop.transform.position, drop.HalfExtent - 0.01f) && basket.ChuteIntrusionCount == intr0 && gm.Collected == got0,
                          $"배출구 위에서 놓침(내림 중 Abort) → 기둥 밖 더미 위 (도달 {reached}, 위치 {Local(drop)}, 침입 {intr0}→{basket.ChuteIntrusionCount})");
                    yield return WaitAim(6f);
                }
                else Check(false, "놓침 검사용 실물 없음");
            }

            // 8) 시간 초과 → 결과(시간 초과)
            gm.EditorSetTimeRemaining(0.05f);
            yield return WaitState(ClawGameManager.State.Result, 3f);
            Check(gm.Current == ClawGameManager.State.Result && !gm.LastSuccess, $"제한 시간 0 → 시간 초과 결과 ({gm.Current}, 성공 {gm.LastSuccess})");
            Check(gm.RewardEligible == gm.Config.rewardOnPartial, $"시간 초과 보상 대상 = rewardOnPartial({gm.Config.rewardOnPartial})");
            {
                var rp = FindAnyObjectByType<ClawResultPanel>(FindObjectsInactive.Include);
                var title = rp != null ? rp.transform.Find("ResultCanvas/Title")?.GetComponent<TMPro.TMP_Text>() : null;
                var detail = rp != null ? rp.transform.Find("ResultCanvas/Detail")?.GetComponent<TMPro.TMP_Text>() : null;
                // 11-10 실패 제목 = 감사 문구(ClawResultPanel.FailTitle)
                Check(title != null && title.text.Contains(ClawResultPanel.FailTitle) && detail != null && detail.text.Contains($"키링 큐브 {gm.KeyringGot}/{gm.TargetCount}"),
                      $"결과 패널 '{(title != null ? title.text : "null")}' / '{(detail != null ? detail.text.Replace('\n', ' ') : "null")}'");
                // 10-26 [다시 시작] — 시간 초과(보상 대상 아님) 결과에서 잡기 = 다시 시작. 들어온 직후 1.5초는 회색(연타 막이). 실제 씬 이동은 @introtest
                if (!gm.RewardEligible)
                {
                    int calls = 0;
                    gm.RestartHandler = () => calls++;
                    Check(gm.RestartOffered && !gm.RestartAllowed && (ClawPadInput.Root == null || (!ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel == "다시 시작")) && rp != null && rp.RestartShown,
                          $"[다시 시작] 시간 초과 결과 → Beam Pro '다시 시작'(들어온 직후 회색 '{ClawPadInput.GrabLabel}') · 안경 결과 패널 [다시 시작] 막대 {(rp != null && rp.RestartShown)}");
                    Tap(); yield return null; yield return null;
                    Check(calls == 0 && gm.Current == ClawGameManager.State.Result, "[다시 시작] 결과가 뜨자마자 누른 잡기(연타)는 무시");
                    yield return WaitUntil(() => gm.RestartAllowed, ClawSessionBase.RestartGuardSec + 1f);
                    yield return null;
                    Check(gm.RestartAllowed && (ClawPadInput.Root == null || ClawPadInput.GrabEnabled), $"[다시 시작] {ClawSessionBase.RestartGuardSec}초 뒤 활성");
                    ClawPadInput.SetStick(new Vector2(0.6f, 0f), true); // 조이스틱을 누른 채로 다시 시작해도
                    Tap(); yield return null;
                    Check(calls == 1 && gm.Restarts >= 1 && !ClawPadInput.IsTouching && ClawPadInput.RawStick == Vector2.zero && !ClawPadInput.GrabHeld,
                          $"[다시 시작] 잡기 → 처음으로 1번 + 패드 입력 비움 (호출 {calls}, 터치 {ClawPadInput.IsTouching})");
                    Tap(); yield return null;
                    Check(calls == 1, "[다시 시작] 처음으로 가는 중 또 누름 → 한 번만");
                    gm.RestartHandler = restartStub;
                }
                else log.AppendLine("  INFO rewardOnPartial=true — 시간 초과도 보상 대상이라 결과 화면 [다시 시작] 검사 생략");
            }

            // 9) 운영자 리셋 → Ready, 아이템 0
            gm.ResetGame();
            yield return null;
            Check(gm.Current == ClawGameManager.State.Ready && pool.ActiveCount == 0 && gm.Collected == 0 && gm.Grabs == 0 && gm.BombsHit == 0,
                  $"리셋 → Ready, 활성 아이템 {pool.ActiveCount}, 그릇 {gm.Collected}");
            if (basket.Bed != null)
                Check(basket.Bed.HiddenCount == 0, $"리셋 → 받침 더미 조각 전부 복원 (숨은 조각 {basket.Bed.HiddenCount}, 누적 승격 {basket.PromotedTotal}·폭발 승격 {basket.BlastPromotedTotal}·되돌림 {basket.DemotedTotal})");
            Check(gm.LastResetMs < 1000f, $"리셋 {gm.LastResetMs:F0}ms ≤ 1000");

            // 10) 큐브 N개 → 즉시 성공 → [보상 확인] → 경품(키링 nftId, 가짜 서버)
            yield return StartGame();
            if (gm.KeyringCount >= gm.TargetCount)
            {
                // 10-22 사용자 "3개 제한이 아니라 진짜 집게의 물리력으로" + "같은 종류 3개 = 그 상품": 키링 3개를 한데 모아 잡으면 발이 덩어리를 끼워
                // 둘 이상이 함께 올라온다(물리 — 몇 개일지는 그때그때). 남은 것은 하나씩 → 같은 종류 3개 = 성공·키링 경품
                // 10-28 반전(사용자 "한 번에 한 개씩만 잡힘"): 큐브 3개를 모아 잡아도 **하나만** 쥔다 → 나머지는 하나씩 목표(큐브 N개)까지
                gm.EditorSetTimeRemaining(180f);
                var keys = new List<ClawItem>();
                foreach (var it in pool.All) if (it.gameObject.activeSelf && !it.Held && it.ItemKind == ClawItem.Kind.Keyring) keys.Add(it);
                if (keys.Count >= Mathf.Min(3, gm.TargetCount))
                {
                    int clusterN = Mathf.Min(3, gm.TargetCount); // 10-26 목표 10개 — 모아 잡기는 3개로 보고 나머지는 하나씩
                    keys.RemoveRange(clusterN, keys.Count - clusterN);
                    var c0 = new Vector2(0.15f, ClawLayout.CenterZ - 0.05f);
                    ClearAround(c0, 0.45f, 0.6f, keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        float a = (90f + i * 360f / keys.Count) * Mathf.Deg2Rad;
                        PlaceOnPile(keys[i], c0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.09f);
                    }
                    yield return new WaitForSeconds(1.0f);
                    var mid = Vector2.zero;
                    foreach (var k in keys) { var l = Local(k); mid += new Vector2(l.x, l.z); }
                    mid /= keys.Count;
                    carriage.SetLocalXZ(ClampToCarriage(mid));
                    yield return null; yield return null; yield return null;
                    int together = 0, heldBest = 0;
                    // 10-28 모아 둔 데서 하나라도 잡힐 때까지(최대 3번) — 잡히면 그 한 번에 몇 개였는지 본다
                    for (int attempt = 0; attempt < 3 && together < 1 && gm.Current == ClawGameManager.State.Playing && gm.KeyringGot < gm.TargetCount; attempt++)
                    {
                        if (attempt > 0)
                        {
                            // 남은 키링을 다시 모아(덩어리는 그때그때 물리라 한 발만 닿을 수도)
                            yield return WaitAim(6f);
                            gm.EditorSetTimeRemaining(180f);
                            var restK = new List<ClawItem>();
                            foreach (var k in keys) if (k.gameObject.activeSelf && !k.Held && k.ItemKind == ClawItem.Kind.Keyring) restK.Add(k);
                            if (restK.Count < 2) break;
                            ClearAround(c0, 0.45f, 0.6f, restK);
                            for (int i = 0; i < restK.Count; i++)
                            {
                                float a = (90f + i * 360f / restK.Count) * Mathf.Deg2Rad;
                                PlaceOnPile(restK[i], c0 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (restK.Count == 2 ? 0.075f : 0.09f));
                            }
                            yield return new WaitForSeconds(1.0f);
                            mid = Vector2.zero;
                            foreach (var k in restK) { var l = Local(k); mid += new Vector2(l.x, l.z); }
                            mid /= restK.Count;
                            carriage.SetLocalXZ(ClampToCarriage(mid));
                            yield return null; yield return null; yield return null;
                        }
                        int kg0 = gm.KeyringGot;
                        yield return GrabCycle();
                        together = Mathf.Max(together, gm.KeyringGot - kg0);
                        heldBest = Mathf.Max(heldBest, heldMax);
                    }
                    heldMax = heldBest;
                    var leftK = FindAnyObjectByType<ClawScoreboard>()?.transform.Find("ScoreCanvas/Left")?.GetComponent<TMPro.TMP_Text>();
                    Check(heldMax == 1 && together == 1,
                          $"[한 개만] 큐브 {keys.Count}개 모아 잡기 → 하나만 쥠(한 번에 쥔 최대 {heldMax}) · 한 번에 {together}개 획득");
                    Check(leftK != null && leftK.text.Contains($"{gm.KeyringGot} / {gm.TargetCount}"), $"스코어보드 큐브 {gm.KeyringGot} / {gm.TargetCount} ({(leftK != null ? leftK.text.Replace('\n', '/') : "null")})");
                    // 남은 키링 하나씩
                    for (int guard = 0; guard < gm.TargetCount + 8 && gm.Current == ClawGameManager.State.Playing && gm.KeyringGot < gm.TargetCount; guard++)
                    {
                        gm.EditorSetTimeRemaining(180f);
                        ClawItem rest = null;
                        foreach (var k in pool.All) // 10-26 배치한 키링 먼저, 없으면 진짜 맨 윗층 키링
                        {
                            if (!k.gameObject.activeSelf || k.Held || k.ItemKind != ClawItem.Kind.Keyring) continue;
                            if (k.BedIndex < 0 && !k.TopLayer) { rest = k; break; }
                            rest ??= k;
                        }
                        if (rest == null) break;
                        yield return GrabTarget(rest, 3);
                    }
                    yield return WaitState(ClawGameManager.State.Result, 3f);
                    Check(gm.PrizeKind == ClawItem.Kind.Keyring, $"큐브 {gm.TargetCount}개 → 경품 종류 키링 ({(gm.PrizeKind.HasValue ? ClawGameManager.KindLabel(gm.PrizeKind.Value) : "없음")})");
                }
                else Check(false, $"성공 시나리오 키링 {keys.Count}개 — 목표 {gm.TargetCount}보다 적음");
            }
            else
                Check(false, $"키링 큐브 {gm.KeyringCount}개 < 목표 {gm.TargetCount} — 10-28 목표는 큐브 수 안으로 맞춰져야 함(ApplyGameConfig)"); // 10-28 세라믹 대체 경로 삭제(그릇은 점수 없음)
            yield return WaitState(ClawGameManager.State.Result, 3f);
            Check(gm.Current == ClawGameManager.State.Result && gm.LastSuccess && gm.KeyringGot == gm.TargetCount && gm.RewardEligible,
                  $"큐브 {gm.KeyringGot}/{gm.TargetCount} → 즉시 성공·보상 대상 ({gm.Current}, 그릇 세라믹 {gm.CeramicGot}·유리 {gm.GlassGot}은 점수 없음)");
            {
                var rp = FindAnyObjectByType<ClawResultPanel>(FindObjectsInactive.Include);
                var title = rp != null ? rp.transform.Find("ResultCanvas/Title")?.GetComponent<TMPro.TMP_Text>() : null;
                var detail = rp != null ? rp.transform.Find("ResultCanvas/Detail")?.GetComponent<TMPro.TMP_Text>() : null;
                Check(title != null && title.text.Contains("성공") && detail != null && detail.text.Contains($"키링 {gm.TargetCount}개 완성"),
                      $"결과 패널 '{(title != null ? title.text : "null")}' / '{(detail != null ? detail.text.Replace('\n', ' ') : "null")}'");
            }
            if (gm.RewardEligible)
            {
                Check(gm.Prize != null && !gm.Prize.Active && gm.Prize.Claim == PrizeLink.ClaimState.None,
                      "경품 연동 기본 꺼짐(config prizeEnabled=false) — 시작 때 요청 없음");
                var cfgOn = PrizeSelfTest.TestConfig();
                cfgOn.prizeNftIdCeramic = 23; cfgOn.prizeNftIdGlass = 24; cfgOn.prizeNftIdKeyring = 25;
                gm.Config.prizeEnabled = true; gm.Config.prizeNftIdCeramic = 23; gm.Config.prizeNftIdGlass = 24; gm.Config.prizeNftIdKeyring = 25; // PrizeNftId는 세션 Config를 읽는다(리셋 때 파일로 되돌아감)
                var fake = PrizeSelfTest.Arm(gm.Prize, cfgOn).User(200, PrizeSelfTest.BodyDid).Reward(200, PrizeSelfTest.BodyPurchase8);
                fake.HoldRewards = true;
                var kind = gm.PrizeKind;
                int expectNft = kind == ClawItem.Kind.Ceramic ? 23 : kind == ClawItem.Kind.Glass ? 24 : 25;
                {
                    // 10-26 결과 화면에서 [보상 확인] 전에 안경을 벗고 오래 지나도 처음으로 가지 않는다(경품을 잃지 않게)
                    var ww = ClawWearWatch.Instance;
                    int resets0 = ww != null ? ww.AutoResets : -1;
                    ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
                    ClawWearWatch.SimulateOffAgo(120);
                    yield return null; yield return null; yield return null;
                    Check(ww != null && ww.AutoResets == resets0 && gm.Current == ClawSessionBase.State.Result && ww.Notice.Contains("보상 확인"),
                          $"[착용] 결과 화면 [보상 확인] 전 안경 벗고 2분 → 처음으로 안 감·Beam Pro 안내 ('{(ww != null ? ww.Notice.Replace('\n', ' ') : "")}')");
                    ClawWearWatch.SimulateWear(ClawWearWatch.WearState.On);
                    yield return null;
                }
                Tap();
                yield return null; yield return null;
                Check(gm.Current == ClawSessionBase.State.Reward, $"잡기(보상 확인) → Reward ({gm.Current})");
                if (gm.PrizeSending)
                    Check(!gm.RestartOffered && (ClawPadInput.Root == null || (!ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel.Contains("요청 중"))),
                          $"[다시 시작] 경품 요청 중에는 다시 시작 없음(Beam Pro '{ClawPadInput.GrabLabel.Replace('\n', ' ')}' 회색)");
                else log.AppendLine("  INFO 경품 응답이 한 프레임 안에 와 '요청 중' 화면 검사 생략");
                {
                    var watch = ClawWearWatch.Instance;
                    int resetsBefore = watch.AutoResets;
                    ClawWearWatch.SimulateWear(ClawWearWatch.WearState.Off);
                    ClawWearWatch.SimulateOffAgo(120);
                    yield return null; yield return null;
                    Check(gm.PrizeSending && watch.AutoResets == resetsBefore && gm.Current == ClawSessionBase.State.Reward,
                          "경품 응답 대기 중 탈착 120초 → 요청·보상 화면 보존");
                    ClawWearWatch.SimulateWear(ClawWearWatch.WearState.On);
                    ClawWearWatch.SimulateResume(10);
                    yield return null;
                    Check(gm.PrizeSending && watch.AutoResets == resetsBefore,
                          "경품 응답 대기 중 앱 복귀 → 자동 초기화 없음");
                    fake.HoldRewards = false;
                }
                yield return WaitUntil(() => gm.Prize.Claim != PrizeLink.ClaimState.Sending, 3f);
                Check(kind.HasValue && gm.Prize.Claim == PrizeLink.ClaimState.Accepted && fake.Count("POST") == 1 && fake.requests[fake.requests.Count - 1].Contains($"\"nftId\":{expectNft}"),
                      $"[경품] 넣은 그릇 [{string.Join(",", gm.DeliveredKinds)}] → {(kind.HasValue ? ClawGameManager.KindLabel(kind.Value) : "없음")} 상품 nftId={expectNft} → {gm.Prize.Claim} #{gm.Prize.PurchaseNo}");
                var rwp = FindAnyObjectByType<ClawRewardPanel>();
                bool shownNo = false, shownKind = false;
                if (rwp != null)
                    foreach (var t in rwp.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    {
                        if (!t.gameObject.activeInHierarchy) continue;
                        if (t.text.Contains("접수번호 8")) shownNo = true;
                        if (kind.HasValue && t.text.Contains("경품: " + ClawGameManager.KindLabel(kind.Value))) shownKind = true;
                    }
                Check(shownNo && shownKind, $"[경품] 보상 화면에 경품 종류·'경품 요청 완료 (접수번호 8)' 표시 (종류 {shownKind}, 접수 {shownNo})");
                {
                    // 10-28 상품 소개 = 큐브만: config 상품 이름 한 줄 + 키링 큐브 3D 모델이 돎(메뉴 30 PrizeCube)
                    string rewardName = gm.Config.clawRewardText;
                    bool nameShown = false;
                    if (rwp != null)
                        foreach (var t in rwp.GetComponentsInChildren<TMPro.TMP_Text>(true))
                            if (t.gameObject.activeInHierarchy && t.text.Contains(rewardName)) nameShown = true;
                    var cubeT = rwp != null ? rwp.transform.Find("PrizeCube") : null;
                    var rot0 = cubeT != null ? cubeT.localRotation : Quaternion.identity;
                    yield return new WaitForSeconds(0.2f);
                    float spun = cubeT != null ? Quaternion.Angle(rot0, cubeT.localRotation) : 0f;
                    var cubeMf = cubeT != null ? cubeT.GetComponent<MeshFilter>() : null;
                    Check(nameShown && cubeT != null && cubeT.gameObject.activeInHierarchy && spun > 2f && cubeMf != null && cubeMf.sharedMesh != null && cubeMf.sharedMesh.name == "Claw_KeyringMesh",
                          $"[보상] 상품 이름 '{rewardName}' {nameShown} · 키링 큐브 모델 표시 {(cubeT != null && cubeT.gameObject.activeInHierarchy)}·0.2초에 {spun:F1}° 돎 (없으면 메뉴 30 재실행)");
                }
                Check(rwp != null && rwp.HasCard && rwp.HasSound, "[보상] 보상 카드·효과음 배선");
                var sbOp = new StringBuilder();
                gm.AppendOperatorSummary(sbOp);
                Check(sbOp.ToString().Contains("경품 접수 #8") && sbOp.ToString().Contains("…ZDT6Lb"), "[경품] 운영자 패널에 참가자 DID 끝자리·접수번호");

                // 10-26 보상 화면(경품 접수 뒤) → Beam Pro '다시 시작' · 안경 보상 패널 맨 아래 안내 → 잡기 = 처음으로
                int rcalls = 0;
                gm.RestartHandler = () => rcalls++;
                yield return WaitUntil(() => gm.RestartAllowed, ClawSessionBase.RestartGuardSec + 1f);
                yield return null;
                bool tailShown = false;
                if (rwp != null)
                    foreach (var t in rwp.GetComponentsInChildren<TMPro.TMP_Text>(true))
                        if (t.gameObject.activeInHierarchy && t.text.Contains(ClawRewardPanel.RestartTail)) tailShown = true;
                Check(gm.RestartAllowed && tailShown && (ClawPadInput.Root == null || (ClawPadInput.GrabEnabled && ClawPadInput.GrabLabel == "다시 시작")),
                      $"[다시 시작] 보상 화면(경품 접수 뒤) → Beam Pro '{ClawPadInput.GrabLabel}' · 안경 안내 '{ClawRewardPanel.RestartTail}' {tailShown}");
                Tap(); yield return null;
                Check(rcalls == 1, $"[다시 시작] 보상 화면 잡기 → 처음으로 (호출 {rcalls})");
                gm.RestartHandler = restartStub;
            }

            // 10b) 경품 연동 단독 시나리오(가짜 서버 — 404·409·401·재시도·리셋 등)
            yield return PrizeSelfTest.Run(Check);

            // 11) 운영자 스테퍼 경로: config 쓰기 → 리셋 → 반영 → 원복
            gm.ResetGame(); yield return null;
            int origFiller = gm.FillerCount;
            int testFiller = origFiller == 10 ? 12 : 10;
            bool wrote = ConfigLoader.SetValues(new Dictionary<string, string> { { "clawFillerCount", testFiller.ToString() } }, out string wmsg);
            gm.ResetGame(); yield return null;
            Check(wrote && gm.FillerCount == testFiller, $"SetValues(clawFillerCount={testFiller}) → 리셋 후 FillerCount={gm.FillerCount} ({wmsg})");
            ConfigLoader.SetValues(new Dictionary<string, string> { { "clawFillerCount", origFiller.ToString() } }, out _);
            gm.ResetGame(); yield return null;
            Check(gm.FillerCount == origFiller, $"원복 clawFillerCount={origFiller}");

            gm.RequestStart(); yield return null;
            Check(gm.RevealPanel.Running && gm.Current == ClawSessionBase.State.Reveal, "재시작도 검정 전환 유지");
            gm.ResetGame(); yield return null;
            Check(!gm.RevealPanel.Running && !gm.RevealPanel.transform.Find("Dome").gameObject.activeSelf && !gm.MachineShown && pool.ActiveCount == 0,
                  "전환 중 리셋 시 검정 돔·공간 숨김·상품 반환");

            // 11b) 10-28 기계 다가오기 변형: 이동 중 리셋 / EditorSkipReveal(바로 도착) / clawApproachDistance=0(끔)
            {
                var ap = gm.Approach;
                var aligner = FindAnyObjectByType<BoothAligner>();
                if (ap != null && aligner != null && gm.ApproachEnabled)
                {
                    bool AtHome()
                    {
                        if (!aligner.TryGetAlignPose(out var p, out var r)) return false;
                        return (ap.transform.position - p).magnitude < 0.001f && Mathf.Abs(Mathf.DeltaAngle(ap.transform.eulerAngles.y, r.eulerAngles.y)) < 0.1f;
                    }
                    // (a) 다가오는 중 운영자 리셋 → 멈춤·상품 반환·고정 풀림
                    gm.RequestStart(); yield return null;
                    yield return WaitUntil(() => ap.Running, 8f);
                    bool wasRunning = ap.Running;
                    yield return null; yield return null;
                    gm.ResetGame(); yield return null;
                    int kin = 0;
                    foreach (var it in pool.All) if (it.Body != null && it.Body.isKinematic) kin++;
                    Check(wasRunning && !ap.Active && !ap.Running && pool.ActiveCount == 0 && !gm.MachineShown && kin == 0 && !basket.TransitFrozen && AtHome(),
                          $"[다가오기] 이동 중 리셋 → 멈춤·상품 반환·풀 상품 kinematic 0 (이동 중이었음 {wasRunning}, 대기 {ap.Active}, 활성 {pool.ActiveCount}, kinematic {kin}, 루트 제자리 {AtHome()})");
                    // (b) EditorSkipReveal → 같은 프레임에 Guide·제자리
                    gm.RequestStart(); yield return null;
                    gm.EditorSkipReveal();
                    bool skipOk = gm.Current == ClawSessionBase.State.Guide && !ap.Active && AtHome() && !basket.TransitFrozen;
                    Check(skipOk, $"[다가오기] EditorSkipReveal → 같은 프레임에 Guide·제자리 ({gm.Current}, 대기 {ap.Active}, 제자리 {AtHome()})");
                    gm.ResetGame(); yield return null;
                    // (c) clawApproachDistance=0 → 다가오기 없이 제자리에서 등장(예전)
                    float origD = gm.Config.clawApproachDistance;
                    bool wroteD = ConfigLoader.SetValues(new Dictionary<string, string> { { "clawApproachDistance", "0" } }, out string dmsg);
                    gm.ResetGame(); yield return null;
                    gm.RequestStart(); yield return null;
                    yield return WaitUntil(() => gm.RevealPanel.BlackReached, 3f);
                    bool offBlack = !gm.ApproachEnabled && !ap.Active && gm.MachineShown && AtHome();
                    yield return WaitState(ClawSessionBase.State.Guide, 10f);
                    Check(wroteD && offBlack && gm.Current == ClawSessionBase.State.Guide && !ap.Active && AtHome(),
                          $"[다가오기] clawApproachDistance=0 → 검정 뒤 바로 제자리·다가오기 없이 Guide (검정 때 {offBlack}, {gm.Current}, {dmsg})");
                    ConfigLoader.SetValues(new Dictionary<string, string> { { "clawApproachDistance", origD.ToString(System.Globalization.CultureInfo.InvariantCulture) } }, out _);
                    gm.ResetGame(); yield return null;
                    Check(gm.ApproachEnabled && Mathf.Approximately(gm.Config.clawApproachDistance, origD), $"[다가오기] 원복 clawApproachDistance={gm.Config.clawApproachDistance}");
                }
                else Check(false, $"[다가오기] 변형 검사 불가 (approach {(ap != null)}, aligner {(aligner != null)}, enabled {gm.ApproachEnabled})");
            }

            // 8c) 운영자 패널: Claw 세션이면 가로 뷰(회전)·탭 존 우하단 — 프리팹이 있을 때만(에디터에서도 SDK가 Instantiate)
            var opc = FindAnyObjectByType<OperatorPanelController>(FindObjectsInactive.Include);
            if (opc != null)
            {
                opc.Toggle(); yield return null;
                var v = opc.ActiveView;
                bool landscapeView = v != null && v.name == "OperatorPanelLandscape" && v.IsOpen;
                float rotZ = v != null && v.Content != null ? v.Content.localEulerAngles.z : -1f;
                Check(landscapeView, $"패널 Toggle → 가로 뷰 열림 (실제 {(v != null ? v.name : "null")} open={(v != null && v.IsOpen)})");
                Check(Mathf.Abs(Mathf.DeltaAngle(rotZ, -90f)) < 0.5f, $"가로 콘텐츠 회전 z={rotZ:F0} (Left90 기대 −90)");
                var zone = GameObject.Find("CornerTapZone");
                if (zone != null)
                {
                    var zrt = (RectTransform)zone.transform;
                    Check(Mathf.Approximately(zrt.anchorMin.x, 0.8f) && Mathf.Approximately(zrt.anchorMin.y, 0f) && Mathf.Approximately(zrt.anchorMax.y, 0.15f),
                          $"탭 존 세로 기준 우하단 anchors {zrt.anchorMin}~{zrt.anchorMax}");
                }
                else Check(false, "CornerTapZone 없음");
                var counts = FindAnyObjectByType<OperatorClawCounts>(FindObjectsInactive.Include);
                var cubeText = counts != null ? counts.transform.Find("CubesValue")?.GetComponent<TMPro.TMP_Text>() : null;
                var bowlText = counts != null ? counts.transform.Find("BowlsValue")?.GetComponent<TMPro.TMP_Text>() : null;
                int bowlN = Mathf.Max(gm.CeramicCount, gm.GlassCount);
                bool oldRows = counts != null && (counts.transform.Find("FillersValue") != null || counts.transform.Find("BombsValue") != null);
                Check(cubeText != null && cubeText.text == gm.KeyringCount.ToString() && bowlText != null && bowlText.text == bowlN.ToString() && !oldRows,
                      $"운영자 스테퍼 큐브 {(cubeText != null ? cubeText.text : "null")} = {gm.KeyringCount} · 그릇(종류별) {(bowlText != null ? bowlText.text : "null")} = {bowlN} · 캡슐·폭탄 행 없음 {!oldRows} (다르면 메뉴 14 재실행)");
                opc.Hide(); yield return null;
                Check(!opc.IsOpen, "패널 Hide");
            }
            else log.AppendLine("  SKIP 운영자 패널 프리팹 없음(에디터) — 뷰/탭 존 검사 생략");

            // 12) 스트레스: 다시 배치 후 폭발 20회
            yield return StartGame();
            Check(gm.Current == ClawGameManager.State.Playing && pool.ActiveCount > 0, $"스트레스 전 재시작 → Playing (실제 {gm.StateName}, 아이템 {pool.ActiveCount})");
            {
                int esc0 = basket.EscapeCount;
                basket.StartStressTest(20);
                yield return null;
                while (basket.StressRunning) { gm.EditorSetTimeRemaining(180f); yield return null; }
                Check(basket.EscapeCount == esc0 && basket.MaxSpeedSeen <= ClawBasket.ExplosionMaxSpeed + 0.01f && basket.LastStressReport.Contains("aboveLid=0"),
                      $"폭발 20회 이탈 0·속도 상한·뚜껑 위 0 — {basket.LastStressReport}");
            }
            var perf = PerfMonitor.Instance;
            if (perf != null) log.AppendLine($"  INFO 에디터 FPS {perf.Summary()}");
            gm.ResetGame();
            // 10-28 한 번에 한 개만 — 테스트 전 구간(모든 잡기·폭발·재정렬)에서 집게가 한 번에 쥔 최대 수
            Check(heldMaxAll <= 1, $"[한 개만] heldMaxAll = {heldMaxAll} ≤ 1 (최대일 때 단계 {(heldMaxAll > 0 ? heldMaxAllAt : "-")})");

            Finish();
        }

        void Finish()
        {
            string verdict = fails == 0 ? "PASS" : $"FAIL({fails})";
            log.AppendLine($"[ClawSelfTest] RESULT {verdict}");
            if (fails == 0) Debug.Log(log.ToString());
            else Debug.LogError(log.ToString());
            UnityEditor.EditorApplication.ExitPlaymode();
        }
#endif
    }
}
