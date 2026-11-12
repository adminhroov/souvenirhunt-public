using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SouvenirHunt;

// 인형뽑기 기계 연출(10-15 비주얼 2차 — "살아 있는 연출" 묶음). 메뉴 30(SetupClawScene)이 부른다.
//   · 기계 안쪽 네온 선(SouvenirHunt/ClawNeon): 바닥 테두리(배출구 칸은 비움) + 천장 테두리, 빛이 경로를 따라 흐름
//     (모서리 세로선은 뺐다 — Model3 기둥이 안쪽으로 5.4cm 튀어나와 선을 가림. 1cm 선은 1.5m에서 안 보여 바닥 1.8·천장 1.4cm)
//   · 바닥 디지털 그리드(SouvenirHunt/ClawFloorGrid): 가는 격자 + 스캔 띠 + 이벤트 물결
//   · 조준 원뿔 재질(SouvenirHunt/ClawGuideScan)
// 전부 ClawLayout 내부 치수(HalfX·FrontZ·BackZ·FloorY·LidY·배출구 벽)에서 만든다 — 기계가 바뀌어도 치수만 맞으면 그대로 따라간다.
// Environment 자식이라 기계 표시(MachineVisibility)·흔들림(MachineShake)·재정렬을 같이 받는다. 메시·재질은 같은 경로에 다시 구워 GUID 유지.
public static partial class SouvenirHuntSetup
{
    const string NeonShaderName = "SouvenirHunt/ClawNeon";
    const string FloorGridShaderName = "SouvenirHunt/ClawFloorGrid";
    const string GuideScanShaderName = "SouvenirHunt/ClawGuideScan";

    /// <summary>셰이더를 바꿔 끼운 재질(없으면 생성). 셰이더가 없으면(컴파일 실패) null + 경고 — 호출자가 생략</summary>
    static Material FxShaderMat(string name, string shaderName)
    {
        var sh = Shader.Find(shaderName);
        if (sh == null) { Debug.LogWarning($"[SouvenirHuntSetup] 셰이더 {shaderName} 없음 — {name} 생략"); return null; }
        var m = GetOrCreateMat(name, shaderName);
        if (m.shader != sh) m.shader = sh;
        m.shaderKeywords = new string[0];
        m.renderQueue = -1;
        return m;
    }

    static ClawMachineFx BuildMachineFx(GameObject env, int layer, StringBuilder report)
    {
        EnsureFolder(ClawItemFolder);
        var root = new GameObject("MachineFx") { layer = layer };
        root.transform.SetParent(env.transform, false);
        var fx = root.AddComponent<ClawMachineFx>();

        float hx = ClawLayout.HalfX, fz = ClawLayout.FrontZ, bz = ClawLayout.BackZ;
        float floorY = ClawLayout.FloorY, topY = ClawLayout.LidY - 0.014f;
        const float inset = 0.012f, width = 0.018f;
        float x0 = -hx + inset, x1 = hx - inset, z0 = fz + inset, z1 = bz - inset, yF = floorY + 0.010f;

        // 바닥 테두리: 배출구 칸(앞-왼쪽)은 피해 'ㄷ'자로 / 천장 테두리: 닫힌 사각
        var floorPath = new[]
        {
            new Vector3(ClawLayout.ChuteWallX + 0.012f, yF, z0), new Vector3(x1, yF, z0), new Vector3(x1, yF, z1),
            new Vector3(x0, yF, z1), new Vector3(x0, yF, ClawLayout.ChuteWallZ + 0.012f),
        };
        var topPath = new[] { new Vector3(x0, topY, z0), new Vector3(x1, topY, z0), new Vector3(x1, topY, z1), new Vector3(x0, topY, z1) };
        var neonMat = FxShaderMat("ClawItems/Claw_Neon", NeonShaderName);
        var neonRenderers = new List<Renderer>();
        if (neonMat != null)
        {
            neonMat.SetColor("_Color", new Color(0.25f, 0.9f, 1f, 1f));
            neonMat.SetColor("_Color2", new Color(1f, 0.3f, 0.85f, 1f));
            neonMat.SetFloat("_Base", 0.8f);
            neonMat.SetFloat("_Chase", 2.0f);
            neonMat.SetFloat("_Density", 2.5f);
            neonMat.SetFloat("_Speed", 0.5f);
            neonMat.SetFloat("_Flash", 0f);
            neonMat.SetFloat("_Warn", 0f);
            EditorUtility.SetDirty(neonMat);

            var floorMesh = SaveMesh(BuildNeonMesh(new List<(Vector3[], bool)> { (floorPath, false) }, width), $"{ClawItemFolder}/Claw_NeonFloor.asset");
            var frameMesh = SaveMesh(BuildNeonMesh(new List<(Vector3[], bool)> { (topPath, true) }, width * 0.8f), $"{ClawItemFolder}/Claw_NeonFrame.asset");
            neonRenderers.Add(FxMeshObject(root.transform, "NeonFloor", floorMesh, neonMat, layer));
            neonRenderers.Add(FxMeshObject(root.transform, "NeonFrame", frameMesh, neonMat, layer));
            if (ClawLayout.CabinetSup)
            {
                // 10-20 아크릴 칸막이 테두리(위 끝 'ㄱ'자 + 모서리 세로선) — 거의 투명한 판이 어디까지인지 보이게, 이벤트 때 같이 번쩍임
                float gx = ClawLayout.ChuteWallX - ClawLayout.ChuteGuardThickness * 0.5f, gz = ClawLayout.ChuteWallZ - ClawLayout.ChuteGuardThickness * 0.5f;
                float gTop = ClawLayout.ChuteGuardTopY, gBottom = ClawLayout.FloorY + ClawLayout.ChuteWallHeight;
                var rim = new[] { new Vector3(gx, gTop, fz + 0.004f), new Vector3(gx, gTop, gz), new Vector3(-hx + 0.004f, gTop, gz) };
                var post = new[] { new Vector3(gx, gBottom, gz), new Vector3(gx, gTop, gz) };
                var guardMesh = SaveMesh(BuildNeonMesh(new List<(Vector3[], bool)> { (rim, false), (post, false) }, width * 0.6f), $"{ClawItemFolder}/Claw_NeonChuteGuard.asset");
                neonRenderers.Add(FxMeshObject(root.transform, "NeonChuteGuard", guardMesh, neonMat, layer));
            }
        }

        Renderer grid = null;
        var gridMat = FxShaderMat("ClawItems/Claw_FloorGrid", FloorGridShaderName);
        if (gridMat != null)
        {
            gridMat.SetFloat("_MinZ", fz);
            gridMat.SetFloat("_MaxZ", bz);
            gridMat.SetFloat("_HalfX", hx);
            gridMat.SetFloat("_ChuteX", ClawLayout.ChuteWallX);
            gridMat.SetFloat("_ChuteZ", ClawLayout.ChuteWallZ);
            gridMat.SetFloat("_Cell", 0.07f);
            gridMat.SetFloat("_GridAlpha", 0.16f);
            gridMat.SetFloat("_ScanStrength", 0.35f);
            EditorUtility.SetDirty(gridMat);
            var quad = new Mesh();
            float y = floorY + 0.003f;
            quad.SetVertices(new List<Vector3> { new(-hx, y, fz), new(hx, y, fz), new(hx, y, bz), new(-hx, y, bz) });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quad.RecalculateNormals();
            quad.RecalculateBounds();
            var gridMesh = SaveMesh(quad, $"{ClawItemFolder}/Claw_FloorGridMesh.asset");
            grid = FxMeshObject(root.transform, "FloorGrid", gridMesh, gridMat, layer);
        }

        SetRefArray(fx, "neon", neonRenderers.ToArray());
        if (grid != null) SetRef(fx, "floorGrid", grid);
        report.AppendLine($"  · 기계 연출: 네온 선 {neonRenderers.Count}(바닥 ㄷ자·천장 테두리, 흐르는 빛) · 바닥 그리드 {(grid != null ? "있음" : "없음")} — ClawLayout 치수");
        return fx;
    }

    static Renderer FxMeshObject(Transform parent, string name, Mesh mesh, Material mat, int layer)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return mr;
    }

    /// <summary>
    /// 경로(점 목록, 닫힘 여부)마다 선분을 십자 리본 두 장(서로 수직)으로 — 어느 방향에서 봐도 선이 보인다(가산이라 겹침 = 모서리 점이 조금 더 밝음).
    /// UV x = 경로 누적 길이(m, 흐르는 빛), y = 단면 0~1.
    /// </summary>
    static Mesh BuildNeonMesh(List<(Vector3[] pts, bool closed)> paths, float width)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var tris = new List<int>();
        float hw = width * 0.5f;
        foreach (var (pts, closed) in paths)
        {
            float acc = 0f;
            int segs = closed ? pts.Length : pts.Length - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % pts.Length];
                Vector3 d = b - a;
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                Vector3 dir = d / len;
                Vector3 s1 = Vector3.Cross(dir, Vector3.up);
                if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.right;
                s1.Normalize();
                Vector3 s2 = Vector3.Cross(dir, s1).normalized;
                foreach (var s in new[] { s1, s2 })
                {
                    int bi = v.Count;
                    v.Add(a - s * hw); uv.Add(new Vector2(acc, 0f));
                    v.Add(a + s * hw); uv.Add(new Vector2(acc, 1f));
                    v.Add(b + s * hw); uv.Add(new Vector2(acc + len, 1f));
                    v.Add(b - s * hw); uv.Add(new Vector2(acc + len, 0f));
                    tris.AddRange(new[] { bi, bi + 1, bi + 2, bi, bi + 2, bi + 3 });
                }
                acc += len;
            }
        }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>조준 원뿔 재질 — 아래로 흐르는 스캔 줄무늬(셰이더 없으면 예전 가산 재질)</summary>
    static Material GuideScanMaterial(Material fallback)
    {
        var m = FxShaderMat("Claw_GuideScan", GuideScanShaderName);
        if (m == null) return fallback;
        m.SetColor("_Color", new Color(0.3f, 0.85f, 0.9f, 1f));
        m.SetFloat("_Base", 0.16f);
        m.SetFloat("_Stripe", 0.45f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
