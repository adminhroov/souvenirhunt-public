using System;
using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 성능 진단 전용(10-15): config perfHide에 적은 부분을 기계가 보이는 순간(Guide·Playing) 한 번 숨겨, 어느 부분이 FPS를 잡아먹는지
    /// 실기기에서 빌드 없이 비교한다. 목록: booth, machine(몸체·콘솔·배출구), items, rig(캐리지·케이블·집게·가이드), fx(네온·그리드),
    /// scoreboard, ui(글래스에 그려지는 월드 캔버스), physics(물리 시뮬레이션 정지), all(physics 제외 전부),
    /// list(숨긴 뒤 남은 렌더러·캔버스·카메라를 로그로 — 숨기지는 않음). 비어 있으면 아무 것도 안 한다(운영 기본).
    /// 렌더러만 끄고 오브젝트·로직은 그대로라 게임 흐름은 바뀌지 않는다. 결과는 [Perf] 로그(cpu·render·gpu)로 비교.
    /// </summary>
    public class ClawPerfProbe : MonoBehaviour
    {
        bool applied;

        void Update()
        {
            var gm = ClawGameManager.Instance;
            if (gm == null || gm.Config == null) return;
            string hide = gm.Config.perfHide;
            if (string.IsNullOrEmpty(hide)) { applied = false; return; }
            if (applied || (gm.Current != ClawSessionBase.State.Guide && gm.Current != ClawSessionBase.State.Playing)) return;
            applied = true;
            try { Apply(hide); }
            catch (Exception e) { Debug.LogWarning("[PerfProbe] 적용 실패(무시): " + e.Message); }
        }

        static void Apply(string hide)
        {
            var set = new HashSet<string>();
            foreach (var s in hide.ToLowerInvariant().Split(new[] { ',', ' ', '+' },StringSplitOptions.RemoveEmptyEntries)) set.Add(s.Trim());
            bool all = set.Contains("all");
            int n = 0;
            void HideUnder(string path)
            {
                var go = GameObject.Find(path);
                if (go == null) return;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.enabled = false; n++; }
            }
            if (all || set.Contains("booth")) HideUnder("BoothRoot/Booth");
            if (all || set.Contains("machine"))
            {
                HideUnder("BoothRoot/Environment/Machine");
                HideUnder("BoothRoot/Environment/Console");
                HideUnder("BoothRoot/Environment/Chute");
            }
            if (all || set.Contains("fx")) HideUnder("BoothRoot/Environment/MachineFx");
            if (all || set.Contains("rig")) HideUnder("BoothRoot/ClawRig");
            if (all || set.Contains("scoreboard")) HideUnder("BoothRoot/Scoreboard");
            if (all || set.Contains("items"))
                foreach (var it in FindObjectsByType<ClawItem>(FindObjectsSortMode.None))
                    if (it.Visual != null) { it.Visual.enabled = false; n++; }
            int canvases = 0;
            if (all || set.Contains("ui")) // 글래스에 그려지는 월드 캔버스(패널·스코어보드 글자 등)
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.isRootCanvas && c.renderMode == RenderMode.WorldSpace && c.enabled) { c.enabled = false; canvases++; }
            bool physics = set.Contains("physics");
            if (physics) Physics.simulationMode = SimulationMode.Script;
            Debug.Log($"[PerfProbe] hide={hide} — 렌더러 {n}개 끔{(canvases > 0 ? $" · 월드 캔버스 {canvases}개 끔" : "")}{(physics ? " · 물리 정지" : "")}");
            if (set.Contains("list")) LogVisible();
        }

        /// <summary>지금 켜진(보이는) 렌더러·캔버스 목록 — 모두 숨겼는데도 남는 GPU 시간이 어디서 오는지 찾기용</summary>
        static void LogVisible()
        {
            var rows = new List<(float size, string line)>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var m = r.sharedMaterial;
                var e = r.bounds.size;
                rows.Add((e.x * e.y + e.y * e.z + e.x * e.z,
                    $"{Path(r.transform)} [{r.GetType().Name}] {(m != null ? m.shader.name : "-")} q{(m != null ? m.renderQueue : 0)} 크기 {e.x:0.00}×{e.y:0.00}×{e.z:0.00}"));
            }
            rows.Sort((a, b) => b.size.CompareTo(a.size));
            Debug.Log($"[PerfProbe] 보이는 렌더러 {rows.Count}개(큰 순, 최대 60):");
            for (int i = 0; i < rows.Count && i < 60; i++) Debug.Log("[PerfProbe]   " + rows[i].line);
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isRootCanvas && c.enabled && c.gameObject.activeInHierarchy)
                    Debug.Log($"[PerfProbe]   캔버스 {Path(c.transform)} {c.renderMode} 그래픽 {c.GetComponentsInChildren<UnityEngine.UI.Graphic>(false).Length}개");
            var cams = Camera.allCameras;
            foreach (var cam in cams)
                Debug.Log($"[PerfProbe]   카메라 {Path(cam.transform)} #{cam.GetInstanceID()} 씬 {cam.gameObject.scene.name} depth {cam.depth} 마스크 0x{cam.cullingMask:X} clear {cam.clearFlags} " +
                          $"눈 {cam.stereoTargetEye} 디스플레이 {cam.targetDisplay} hdr {cam.allowHDR} msaa {cam.allowMSAA} target {(cam.targetTexture != null ? cam.targetTexture.name : "화면")}");
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }
    }
}
