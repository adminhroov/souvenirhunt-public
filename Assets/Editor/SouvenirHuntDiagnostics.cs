using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SouvenirHunt;

// 재생 중 씬 상태를 콘솔에 덤프하는 진단 도구.
// 사용: 에디터 재생 상태에서 메뉴 XREAL/12 실행 → Console(Editor.log)에서 결과 확인.
public static class SouvenirHuntDiagnostics
{
    [MenuItem("XREAL/12. Dump Runtime Diagnostics")]
    public static void Dump()
    {
        var sb = new StringBuilder("[Diag] ===== 런타임 진단 =====\n");
        sb.AppendLine($"playing={Application.isPlaying}");

        // 카메라 전수
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            sb.AppendLine($"Camera '{cam.name}' enabled={cam.isActiveAndEnabled} pos={cam.transform.position} " +
                          $"euler={cam.transform.eulerAngles} fov={cam.fieldOfView:F0} near={cam.nearClipPlane} far={cam.farClipPlane} " +
                          $"mask={cam.cullingMask:X} display={cam.targetDisplay} depth={cam.depth} MainTag={cam.CompareTag("MainCamera")}");
        }

        // 캔버스 전수
        foreach (var cv in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var rt = (RectTransform)cv.transform;
            sb.AppendLine($"Canvas '{FullPath(cv.transform)}' activeInHierarchy={cv.gameObject.activeInHierarchy} " +
                          $"enabled={cv.enabled} mode={cv.renderMode} worldPos={cv.transform.position} " +
                          $"lossyScale={cv.transform.lossyScale.x:F4} size={rt.sizeDelta} layer={cv.gameObject.layer}");
        }

        // 그래픽 몇 개 샘플 (Image/TMP 렌더 상태)
        foreach (var g in Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.transform.parent != null && g.transform.parent.name == "StartCanvas")
                sb.AppendLine($"Graphic '{FullPath(g.transform)}' active={g.gameObject.activeInHierarchy} enabled={g.enabled} " +
                              $"color={g.color} material={(g.materialForRendering ? g.materialForRendering.shader.name : "null")}");
        }
        foreach (var t in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == "Button" || t.name == "Title")
                sb.AppendLine($"TMP '{FullPath(t.transform)}' text='{t.text}' font={(t.font ? t.font.name : "NULL")} " +
                              $"active={t.gameObject.activeInHierarchy}");
        }

        // 게임 오브젝트 상태
        var gm = GameManager.Instance;
        sb.AppendLine($"GameManager state={(gm != null ? gm.Current.ToString() : "NULL")} target={(gm != null ? gm.TargetCount : -1)}");

        // 씬 배선 건강 검진 (에디터 비재생 상태에서도 동작) — 08-27 정리 항목
        var gmComp = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (gmComp != null)
        {
            var lib = new SerializedObject(gmComp).FindProperty("meshLibrary")?.objectReferenceValue;
            sb.AppendLine(lib != null ? $"meshLibrary OK ({lib.name})" : "⚠ meshLibrary 미배선 — 기념품 전부 기본 캡슐로 보임. 메뉴 XREAL/21 실행");
        }
        else sb.AppendLine("⚠ GameManager 컴포넌트 없음 — 메뉴 XREAL/10");
        sb.AppendLine(GameObject.Find("DevResetButton") != null ? "⚠ DevResetButton 잔존(Phase 1 임시) — 메뉴 XREAL/21 실행" : "DevResetButton 없음 OK");
        sb.AppendLine(Object.FindAnyObjectByType<RewardPanelController>(FindObjectsInactive.Include) != null ? "RewardPanel OK" : "⚠ RewardPanel 없음 — 메뉴 XREAL/21 실행");
        sb.AppendLine(Object.FindAnyObjectByType<RenderQualityApplier>(FindObjectsInactive.Include) != null ? "Phase5 배선 OK" : "⚠ Phase 5 배선 없음 — 메뉴 XREAL/20 실행");

        var booth = GameObject.Find("BoothRoot");
        if (booth != null) sb.AppendLine($"BoothRoot pos={booth.transform.position} euler={booth.transform.eulerAngles}");

        // 인형뽑기(Claw 씬) 블록 — 09-03
        var claw = Object.FindAnyObjectByType<ClawGameManager>(FindObjectsInactive.Include);
        if (claw != null)
        {
            sb.AppendLine($"[Claw] state={claw.StateName} 큐브 {claw.KeyringGot}/{claw.TargetCount} · 그릇 {claw.CeramicGot + claw.GlassGot}(점수 없음) time={claw.TimeRemaining:F1}/{claw.TimeLimit:F0}s grabs={claw.Grabs} bombs={claw.BombsHit}");
            sb.AppendLine($"[Claw] layers ClawItem={LayerMask.NameToLayer("ClawItem")} ClawMachine={LayerMask.NameToLayer("ClawMachine")} ClawHeld={LayerMask.NameToLayer("ClawHeld")}" +
                          (LayerMask.NameToLayer("ClawItem") < 0 ? "  ⚠ 레이어 없음 — 메뉴 XREAL/30" : ""));
            var pool = Object.FindAnyObjectByType<ClawItemPool>(FindObjectsInactive.Include);
            var poolPrefab = pool != null ? new SerializedObject(pool).FindProperty("prefab")?.objectReferenceValue : null;
            sb.AppendLine(poolPrefab != null ? $"[Claw] ClawItemPool prefab OK ({poolPrefab.name}), active={pool.ActiveCount}" : "⚠ [Claw] ClawItemPool prefab 미배선 — 메뉴 XREAL/30");
            var basket = Object.FindAnyObjectByType<ClawBasket>(FindObjectsInactive.Include);
            if (basket != null) sb.AppendLine($"[Claw] basket escapes={basket.EscapeCount} chute={basket.ChuteWorld} stress='{basket.LastStressReport}'");
            int item = LayerMask.NameToLayer("ClawItem"), held = LayerMask.NameToLayer("ClawHeld");
            if (item >= 0 && held >= 0)
                sb.AppendLine($"[Claw] matrix item↔item={(Physics.GetIgnoreLayerCollision(item, item) ? "OFF⚠" : "on")} item↔Default={(Physics.GetIgnoreLayerCollision(item, 0) ? "off" : "ON⚠")} held↔item={(Physics.GetIgnoreLayerCollision(held, item) ? "off" : "ON⚠")}");
            sb.AppendLine(ClawPadInput.Root != null ? $"[Claw] 오버레이 등록 OK active={ClawPadInput.RootActive} rotation={ClawPadInput.Rotation} relative={ClawPadInput.Relative}" : "[Claw] 오버레이 미등록(프리팹 재생 전이거나 메뉴 14 미실행)");
        }

        // 운영자 패널 2뷰·탭 존 (09-03) — 프리팹 인스턴스가 있을 때(재생 중)만
        var opc = Object.FindAnyObjectByType<OperatorPanelController>(FindObjectsInactive.Include);
        if (opc != null)
        {
            var zoneGo = GameObject.Find("CornerTapZone");
            if (zoneGo != null)
            {
                var zrt = (RectTransform)zoneGo.transform;
                sb.AppendLine($"[Operator] 탭 존 anchors {zrt.anchorMin}~{zrt.anchorMax} (Main 우상단 (0.7,0.85)~(1,1) / Claw 우하단 (0.8,0)~(1,0.15))");
            }
            foreach (var v in Object.FindObjectsByType<OperatorPanelView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"[Operator] 뷰 '{v.name}' open={v.IsOpen} contentRotZ={(v.Content != null ? v.Content.localEulerAngles.z : 0f):F0} counts={(v.Counts != null ? "있음" : "없음")}" +
                              (opc.ActiveView == v ? "  ← 활성" : ""));
        }

        int active = 0;
        foreach (var c in Object.FindObjectsByType<Collectible>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.gameObject.activeInHierarchy) continue;
            active++;
            var r = c.GetComponentInChildren<Renderer>();
            sb.AppendLine($"Collectible[{active}] pos={c.transform.position} " +
                          $"mesh={(r && r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh.name : "?")} " +
                          $"visible={(r ? r.isVisible.ToString() : "?")} boundsSize={(r ? r.bounds.size.ToString() : "?")}");
        }
        sb.AppendLine($"활성 Collectible 수={active}");

        var panel = GameObject.Find("StartPanel");
        sb.AppendLine(panel != null
            ? $"StartPanel pos={panel.transform.position} activeSelf={panel.activeSelf}"
            : "StartPanel: Find 실패(비활성이면 못 찾음)");

        // Phase 4: 조명·포스트·성능 상태
        sb.AppendLine($"라이트맵 {(LightmapSettings.lightmaps != null ? LightmapSettings.lightmaps.Length : 0)}장, " +
                      $"프로브 {(LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0)}개, " +
                      $"ambient={RenderSettings.ambientMode}, sun={(RenderSettings.sun ? RenderSettings.sun.name + "/" + RenderSettings.sun.shadows : "none")}");
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (rp != null)
            sb.AppendLine($"URP '{rp.name}': mainShadows={rp.supportsMainLightShadows} renderScale={rp.renderScale} HDR={rp.supportsHDR} MSAA={rp.msaaSampleCount}");
        foreach (var v in Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var comps = new System.Collections.Generic.List<string>();
            if (v.sharedProfile != null) foreach (var c in v.sharedProfile.components) comps.Add(c.GetType().Name + (c.active ? "" : "(off)"));
            sb.AppendLine($"Volume '{v.name}' global={v.isGlobal} profile={(v.sharedProfile ? v.sharedProfile.name : "null")} [{string.Join(", ", comps)}]");
        }
        var cam0 = Camera.main;
        if (cam0 != null)
        {
            var d = cam0.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            sb.AppendLine($"MainCamera postProcessing={(d ? d.renderPostProcessing.ToString() : "no URP data")} aa={(d ? d.antialiasing.ToString() : "-")}");
        }
        sb.AppendLine($"VFX pool={(CollectVfxPool.Instance ? "OK" : "none")}, Perf: {(PerfMonitor.Instance ? PerfMonitor.Instance.Summary() : "none")}");

        Debug.Log(sb.ToString());
    }

    static string FullPath(Transform t)
    {
        var path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
