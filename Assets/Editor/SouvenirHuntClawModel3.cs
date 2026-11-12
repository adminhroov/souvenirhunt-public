using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SouvenirHunt;

// 인형뽑기 기계 = Model3 (10-13, ClawLayout.IsModel3). SouvenirHuntSetup partial — 메뉴 30(SetupClawScene)이 호출.
// 원본: Downloads "arcade claw machine 3d model"(Tripo AI 생성 FBX) → Assets/Models/Model3. 특징과 처리:
//   · tripo_part_N 100조각, 조각마다 basecolor JPEG 1장(32~1024px) → 아틀라스 1장으로 묶어 그리기 100회를 몸체 1회로
//   · 기계에 사람 뼈대(78개, Pelvis·Thigh…)가 자동으로 붙은 스킨 메시 → 바인드 자세 그대로 정적 메시로 굽고 뼈대는 버린다
//   · 앞·옆 창은 틀만 있고 유리판이 없다. 뒤 창(#91)은 불투명 회색 판 → Claw_MachineGlass(OST에서 공 뒤 배경이 밝으면 공 색이 묻힘)
//   · 3발 집게(#13)는 집게발·허브가 한 덩어리라 여닫기 불가 — 위아래·좌우 이동만(추후 피드백)
// 파트 분류는 정점 실측 + 강조 렌더(10-13)로 확인. 구운 메시는 부스 방향(yaw) + 배율 적용, 씬에서 로컬 회전 identity로 쓴다.
public static partial class SouvenirHuntSetup
{
    const string Model3Folder = "Assets/Models/Model3";
    const string Model3MeshFolder = "Assets/Settings/ClawModel3";
    const string Model3AtlasPath = Model3MeshFolder + "/Claw3_Atlas.png";

    static readonly int[] M3HeadParts = { 13, 15, 17, 20, 38, 51, 52, 54 };          // 3발 집게 + 허브 3단 + 볼트
    static readonly int[] M3CarriageParts = { 63, 28, 29, 35, 44, 37, 9, 56, 70 };    // 캐리지 상자·브래킷·상단 이음
    const int M3SideMechPart = 79;      // 허브 옆 기구(큰 성분 → 캐리지) + 레일 끝 캡(작은 성분 → 몸체)
    static readonly int[] M3JoystickParts = { 14, 61 };                               // 공 손잡이 + 스틱
    const int M3ButtonPart = 1;
    const int M3BackGlassPart = 91;     // 뒤 창 회색 판
    const int M3CarriageBoxPart = 63;   // 케이블 색(아틀라스에서 회색)
    const int M3FloorPart = 27;         // 받침 상자 — 윗면(0.391)이 내부 전체를 덮는 보이는 바닥

    /// <summary>Tripo 파트 하나 — 스킨 메시를 바인드 자세로 구운 모델 로컬 정점·법선·UV + 원본 텍스처 경로</summary>
    sealed class TripoPart
    {
        public int num;
        public Vector3[] v, n;
        public Vector2[] uv;
        public int[] tris;
        public string texPath;
        public Bounds bounds;

        /// <summary>연결 성분(삼각형 연결 + 같은 위치 정점 용접) — 삼각형 시작 인덱스 목록, 큰 것부터</summary>
        public List<List<int>> Components()
        {
            var parent = Enumerable.Range(0, v.Length).ToArray();
            int Root(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Root(a); b = Root(b); if (a != b) parent[a] = b; }
            var weld = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 1e4f), Mathf.RoundToInt(v[i].y * 1e4f), Mathf.RoundToInt(v[i].z * 1e4f));
                if (weld.TryGetValue(key, out int j)) Union(i, j); else weld[key] = i;
            }
            for (int t = 0; t < tris.Length; t += 3) { Union(tris[t], tris[t + 1]); Union(tris[t], tris[t + 2]); }
            var map = new Dictionary<int, List<int>>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int r = Root(tris[t]);
                if (!map.TryGetValue(r, out var l)) map[r] = l = new List<int>();
                l.Add(t);
            }
            return map.Values.OrderByDescending(l => l.Count).ToList();
        }

        public List<int> AllTris() => Enumerable.Range(0, tris.Length / 3).Select(i => i * 3).ToList();

        public Bounds BoundsOf(IEnumerable<int> triStarts)
        {
            Bounds? b = null;
            foreach (int t in triStarts)
                for (int k = 0; k < 3; k++)
                {
                    var p = v[tris[t + k]];
                    if (b == null) b = new Bounds(p, Vector3.zero); else { var x = b.Value; x.Encapsulate(p); b = x; }
                }
            return b ?? new Bounds();
        }
    }

    /// <summary>Model3 FBX 로드 → 파트 굽기 → 아틀라스·재질. parts의 헤드·캐리지·케이블·조이스틱·버튼·재질을 Model3 것으로 바꾼다
    /// (유리·공 재질은 Model2 것을 그대로). 파트를 못 찾으면 예외(반쪽 씬 금지).</summary>
    static void BuildClawModel3Parts(ClawModelParts parts, StringBuilder report)
    {
        string fbx = Directory.Exists(Model3Folder) ? Directory.GetFiles(Model3Folder, "*.fbx").Select(p => p.Replace('\\', '/')).FirstOrDefault() : null;
        var prefab = fbx != null ? AssetDatabase.LoadAssetAtPath<GameObject>(fbx) : null;
        if (prefab == null) throw new Exception($"{Model3Folder}/*.fbx 없음 — Downloads 'arcade claw machine' 압축을 이 폴더에 풀 것(ClawLayout.MachineModel=2면 Model2 사용)");
        EnsureFolder(Model3MeshFolder);

        var src = new Dictionary<int, TripoPart>();
        var inst = (GameObject)UnityEngine.Object.Instantiate(prefab);
        try
        {
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = System.Text.RegularExpressions.Regex.Match(smr.name, @"(\d+)$");
                if (!m.Success || smr.sharedMesh == null) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true); // 바인드 자세 그대로(애니메이션 없음), 정점 = 렌더러 로컬(배율 적용)
                var rot = smr.transform.rotation;
                var pos = smr.transform.position;
                var tex = smr.sharedMaterial != null ? smr.sharedMaterial.mainTexture : null;
                var p = new TripoPart
                {
                    num = int.Parse(m.Groups[1].Value),
                    v = baked.vertices.Select(x => pos + rot * x).ToArray(),
                    n = baked.normals.Select(x => (rot * x).normalized).ToArray(),
                    uv = baked.uv,
                    tris = baked.triangles,
                    texPath = tex != null ? AssetDatabase.GetAssetPath(tex) : null,
                };
                p.bounds = p.BoundsOf(p.AllTris());
                src[p.num] = p;
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(inst); }

        TripoPart Need(int n) => src.TryGetValue(n, out var p) ? p : throw new Exception($"Model3 파트 tripo_part_{n} 없음 — 모델이 바뀌었으면 SouvenirHuntClawModel3.cs 파트 번호 갱신");
        foreach (int n in M3HeadParts.Concat(M3CarriageParts).Concat(M3JoystickParts).Append(M3SideMechPart).Append(M3ButtonPart).Append(M3BackGlassPart)) Need(n);

        // ── 아틀라스: 유리 판을 뺀 모든 파트 텍스처를 한 장으로(UV는 전부 0~1 안 — 10-13 확인) ──
        var texPaths = src.Values.Where(p => p.num != M3BackGlassPart && p.texPath != null).Select(p => p.texPath).Distinct().ToList();
        var texes = texPaths.Select(path =>
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }).ToArray();
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var rects = atlas.PackTextures(texes, 4, 4096, false);
        // 같은 내용이면 쓰지도 다시 임포트하지도 않는다 — 2048×4096 Android 압축이 십수 분 걸림(10-13 메뉴 30에서 18분)
        var png = atlas.EncodeToPNG();
        bool changed = !File.Exists(Model3AtlasPath) || !File.ReadAllBytes(Model3AtlasPath).SequenceEqual(png);
        if (changed) File.WriteAllBytes(Model3AtlasPath, png);
        var rectOf = new Dictionary<string, Rect>();
        for (int i = 0; i < texPaths.Count; i++) rectOf[texPaths[i]] = rects[i];
        int atlasW = atlas.width, atlasH = atlas.height;
        foreach (var t in texes) UnityEngine.Object.DestroyImmediate(t);
        UnityEngine.Object.DestroyImmediate(atlas);
        if (changed) AssetDatabase.ImportAsset(Model3AtlasPath);
        if (AssetImporter.GetAtPath(Model3AtlasPath) is TextureImporter ti &&
            (ti.textureType != TextureImporterType.Default || !ti.sRGBTexture || !ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Clamp || ti.maxTextureSize != 4096))
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 4096;
            ti.SaveAndReimport();
        }
        var atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Model3AtlasPath);
        Rect RectOf(TripoPart p) => p.texPath != null && rectOf.TryGetValue(p.texPath, out var r) ? r : new Rect(0f, 0f, 1f, 1f);

        // ── 그룹 ──
        var yaw = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);
        float S = ClawLayout.MachineScale, us = ClawLayout.UfoBakeScale;
        var side = Need(M3SideMechPart).Components();
        var headPieces = M3HeadParts.Select(n => (Need(n), Need(n).AllTris())).ToList();
        var carriagePieces = M3CarriageParts.Select(n => (Need(n), Need(n).AllTris())).Append((Need(M3SideMechPart), side[0])).ToList();
        var stickPieces = M3JoystickParts.Select(n => (Need(n), Need(n).AllTris())).ToList();
        var buttonPiece = Need(M3ButtonPart);
        var moving = new HashSet<int>(M3HeadParts.Concat(M3CarriageParts).Concat(M3JoystickParts).Append(M3ButtonPart).Append(M3BackGlassPart).Append(M3SideMechPart));
        var bodyPieces = src.Values.Where(p => !moving.Contains(p.num)).Select(p => (p, p.AllTris())).ToList();
        foreach (var c in side.Skip(1)) bodyPieces.Add((Need(M3SideMechPart), c)); // 레일 끝 캡

        Bounds Union(IEnumerable<(TripoPart p, List<int> t)> pieces)
        {
            Bounds? b = null;
            foreach (var (p, t) in pieces) { var x = p.BoundsOf(t); if (b == null) b = x; else { var y = b.Value; y.Encapsulate(x); b = y; } }
            return b ?? new Bounds();
        }

        // 헤드: 피벗 = 헤드 바운즈 수평 중심, 높이 = ClawLayout 헤드 중심(위아래 대칭 가정)
        var hb = Union(headPieces);
        var headPivot = new Vector3(hb.center.x, ClawLayout.ModelUfoBodyCenterY, hb.center.z);
        WarnIfOff3(report, "집게 중심 y", hb.center.y, ClawLayout.ModelUfoBodyCenterY);
        WarnIfOff3(report, "집게 상단 y", hb.max.y, ClawLayout.ModelUfoBodyTopY);
        WarnIfOff3(report, "집게 반지름", Mathf.Max(hb.extents.x, hb.extents.z), ClawLayout.ModelUfoRadius);
        // 보이는 바닥 = 받침 상자(#27) 윗면 — 바닥 타일(#68·69·72·73, 0.360)은 이 판 아래라 안 보인다(10-13 공 가림)
        WarnIfOff3(report, "내부 바닥 y(받침 상자 #27 윗면)", Need(M3FloorPart).bounds.max.y, ClawLayout.ModelFloorY);
        WarnIfOff3(report, "옆 창틀 안쪽 x", Need(31).bounds.max.x * -1f, ClawLayout.ModelInnerHalfX);
        var mountPivot = new Vector3(headPivot.x, ClawLayout.ModelUfoMountY, headPivot.z);

        parts.ufoBody = BakeM3("Claw3_Head", headPieces, headPivot, yaw, us, RectOf);
        SplitClawFingers(parts, report); // 10-19: 발 3개를 떼어 관절에서 여닫기
        parts.ufoMount = BakeM3("Claw3_Carriage", carriagePieces, mountPivot, yaw, us, RectOf);
        parts.ufoCable = BakeCableM3("Claw3_Cable", RectOf(Need(M3CarriageBoxPart)).center);

        var stickB = Need(61).bounds;
        parts.joyPivotModel = new Vector3(stickB.center.x, stickB.min.y, stickB.center.z);
        parts.joyBase = null; // 받침은 콘솔 상자(몸체)에 포함
        parts.joyStick = BakeM3("Claw3_JoyStick", stickPieces, parts.joyPivotModel, yaw, S, RectOf);
        var bb = buttonPiece.bounds;
        parts.buttonPivotModel = new Vector3(bb.center.x, bb.min.y, bb.center.z);
        parts.button = BakeM3("Claw3_Button", new[] { (buttonPiece, buttonPiece.AllTris()) }, parts.buttonPivotModel, yaw, S, RectOf);

        parts.machineBody = BakeM3("Claw3_Body", bodyPieces, Vector3.zero, yaw, S, RectOf);
        var glassPart = Need(M3BackGlassPart);
        parts.machineGlass = BakeM3("Claw3_BackGlass", new[] { (glassPart, glassPart.AllTris()) }, Vector3.zero, yaw, S, _ => new Rect(0f, 0f, 1f, 1f));
        parts.machineBoundsModel = Union(src.Values.Select(p => (p, p.AllTris())));

        // 재질: 아틀라스 하나. OST(검정 = 투명)에서 기계가 보이도록 아틀라스 그대로 약하게 자체 발광(Model2_Mat의 이미션 맵과 같은 역할)
        var mat = GetOrCreateMat("Claw3_Body", "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", atlasTex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.35f);
        mat.SetTexture("_EmissionMap", atlasTex);
        mat.SetColor("_EmissionColor", new Color(0.3f, 0.3f, 0.3f));
        FinishLit(mat, emission: true);
        parts.mat = mat;
        AssetDatabase.SaveAssets();

        int bodyTris = bodyPieces.Sum(x => x.Item2.Count);
        report.AppendLine($"  · Model3 {Path.GetFileName(fbx)}: 파트 {src.Count}개(스킨 메시 → 정적), 아틀라스 {atlasW}×{atlasH}({texPaths.Count}장), " +
                          $"몸체 삼각형 {bodyTris} 한 메시, 헤드(집게) {headPieces.Sum(x => x.Item2.Count)}·캐리지 {carriagePieces.Sum(x => x.Item2.Count)}·조이스틱·버튼, 뒤 창 유리 → {Model3MeshFolder}");
    }

    // 집게발 분리 기준(헤드 공간 m — 구운 뒤 ×1.8, 원점 = 헤드 중심). 10-19 실측: 허브(몸통)는 반지름 7cm 안,
    // 발 3개는 중심 2cm 아래·반지름 8~13cm에서 허브에 붙어 끝(−0.18)까지 — 각도 약 82°·217°·307°(등간격 아님)
    const float FingerCutY = 0f, FingerCutR = 0.075f;

    /// <summary>
    /// 3발 집게를 허브 + 발 3개로 나눈다(Model3 집게는 발·허브가 한 연결 성분이라 성분 분리 불가 → 위치로 자름).
    /// 발 = 삼각형 무게중심이 FingerCutY 아래 · 반지름 FingerCutR 바깥. 각도순 정렬 후 가장 큰 틈 3곳에서 세 묶음.
    /// 관절 = 각 발 뿌리(맨 위 3cm 삼각형 무게중심 평균). Claw3_Head는 허브만 남기고 Claw3_Finger0~2(원점 = 관절)를 만든다.
    /// 경계 삼각형은 한쪽에만 속해 여닫을 때 관절에 mm 단위 틈이 생길 수 있다(작은 각도라 거의 안 보임).
    /// </summary>
    static void SplitClawFingers(ClawModelParts parts, StringBuilder report)
    {
        parts.fingers = null;
        parts.fingerHinges = null;
        var m = parts.ufoBody;
        var v = m.vertices; var n = m.normals; var uv = m.uv; var t = m.triangles;
        Vector3 C(int i) => (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
        float Ang(int i) { var c = C(i); return (Mathf.Atan2(c.z, c.x) * Mathf.Rad2Deg + 360f) % 360f; }

        var cand = new List<int>();
        var hub = new List<int>();
        for (int i = 0; i < t.Length; i += 3)
        {
            var c = C(i);
            if (c.y < FingerCutY && new Vector2(c.x, c.z).magnitude > FingerCutR) cand.Add(i); else hub.Add(i);
        }
        var sorted = cand.OrderBy(Ang).ToList();
        var angs = sorted.Select(Ang).ToList();
        if (sorted.Count < 30) { report.AppendLine($"  ⚠ 집게발 분리 실패(후보 삼각형 {sorted.Count}) — 여닫기 없음"); return; }
        var cuts = Enumerable.Range(0, angs.Count)
            .Select(k => (k, gap: (angs[(k + 1) % angs.Count] - angs[k] + 360f) % 360f))
            .OrderByDescending(x => x.gap).Take(3).ToList();
        if (cuts.Min(x => x.gap) < 10f) { report.AppendLine($"  ⚠ 집게발 분리 실패(발 사이 틈 {cuts.Min(x => x.gap):F0}° < 10°) — 여닫기 없음"); return; }
        var ends = cuts.Select(x => x.k).OrderBy(k => k).ToList();
        var groups = new List<List<int>>();
        for (int j = 0; j < 3; j++)
        {
            var g = new List<int>();
            for (int k = (ends[j] + 1) % sorted.Count; ; k = (k + 1) % sorted.Count)
            {
                g.Add(sorted[k]);
                if (k == ends[(j + 1) % 3]) break;
            }
            groups.Add(g);
        }

        Mesh Extract(string name, List<int> triStarts, Vector3 origin)
        {
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var remap = new Dictionary<int, int>();
            foreach (int i in triStarts)
                for (int k = 0; k < 3; k++)
                {
                    int s = t[i + k];
                    if (!remap.TryGetValue(s, out int d))
                    {
                        d = verts.Count; remap[s] = d;
                        verts.Add(v[s] - origin);
                        normals.Add(n.Length > s ? n[s] : Vector3.up);
                        uvs.Add(uv.Length > s ? uv[s] : Vector2.zero);
                    }
                    tris.Add(d);
                }
            return SaveMeshM3(name, verts, normals, uvs, tris);
        }

        var meshes = new Mesh[3];
        var hinges = new Vector3[3];
        var info = new StringBuilder();
        for (int j = 0; j < 3; j++)
        {
            var g = groups[j];
            float top = g.Max(i => C(i).y);
            var root = g.Where(i => C(i).y > top - 0.03f).ToList();
            var h = Vector3.zero;
            foreach (int i in root) h += C(i);
            hinges[j] = h / root.Count;
            meshes[j] = Extract($"Claw3_Finger{j}", g, hinges[j]);
            info.Append($" {Mathf.Atan2(hinges[j].z, hinges[j].x) * Mathf.Rad2Deg:F0}°(삼각형 {g.Count}, 관절 r {new Vector2(hinges[j].x, hinges[j].z).magnitude:F3} y {hinges[j].y:F3})");
        }
        parts.ufoBody = Extract("Claw3_Head", hub, Vector3.zero);
        parts.fingers = meshes;
        parts.fingerHinges = hinges;
        report.AppendLine($"  · 집게발 3개 분리(여닫기):{info} · 허브 삼각형 {hub.Count}");
    }

    static void WarnIfOff3(StringBuilder report, string what, float measured, float constant)
    {
        if (Mathf.Abs(measured - constant) <= 0.01f) return;
        string msg = $"[SouvenirHuntSetup] Model3 실측 {what} = {measured:F3} ≠ ClawLayout {constant:F3} — 모델이 바뀌었으면 ClawLayout.Model* 상수 갱신";
        Debug.LogWarning(msg);
        report.AppendLine("  ⚠ " + msg);
    }

    /// <summary>선택한 파트 조각들을 한 메시로: 모델 좌표 → (피벗 빼기) → ×배율 → yaw. UV는 아틀라스 칸으로. 기존 에셋이 있으면 내용만 교체(GUID 유지)</summary>
    static Mesh BakeM3(string name, IEnumerable<(TripoPart p, List<int> t)> pieces, Vector3 pivot, Quaternion yaw, float scale, Func<TripoPart, Rect> rectOf)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        foreach (var (p, triStarts) in pieces)
        {
            var rect = rectOf(p);
            var remap = new Dictionary<int, int>();
            foreach (int t in triStarts)
                for (int k = 0; k < 3; k++)
                {
                    int s = p.tris[t + k];
                    if (!remap.TryGetValue(s, out int d))
                    {
                        d = verts.Count;
                        remap[s] = d;
                        verts.Add(yaw * ((p.v[s] - pivot) * scale));
                        normals.Add(p.n.Length > s ? yaw * p.n[s] : Vector3.up);
                        var uv = p.uv.Length > s ? p.uv[s] : Vector2.zero;
                        uvs.Add(new Vector2(rect.x + Mathf.Clamp01(uv.x) * rect.width, rect.y + Mathf.Clamp01(uv.y) * rect.height));
                    }
                    tris.Add(d);
                }
        }
        return SaveMeshM3(name, verts, normals, uvs, tris);
    }

    /// <summary>케이블: 반지름 3.5mm 8각 원기둥, 원점 = 앵커, 아래로 원형 길이(ClawCableMesh가 늘인다). UV는 한 점(아틀라스의 회색)</summary>
    static Mesh BakeCableM3(string name, Vector2 uv)
    {
        const int seg = 8;
        float r = 0.0035f * ClawLayout.MachineScale, len = ClawLayout.CableRestLength;
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts.Add(d * r); verts.Add(d * r + Vector3.down * len);
            normals.Add(d); normals.Add(d);
            uvs.Add(uv); uvs.Add(uv);
        }
        for (int i = 0; i < seg; i++)
        {
            int a = i * 2, b = a + 2;
            tris.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
        }
        return SaveMeshM3(name, verts, normals, uvs, tris);
    }

    static Mesh SaveMeshM3(string name, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
    {
        string path = $"{Model3MeshFolder}/{name}.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null)
        {
            m = new Mesh();
            AssetDatabase.CreateAsset(m, path);
            m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }
        m.Clear();
        m.name = name;
        m.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetUVs(0, uvs);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        m.RecalculateTangents();
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>NewScene(Single)이 미사용 에셋을 내린다(10-01 교훈) — 씬 생성 뒤 Model3 메시·재질을 경로로 다시 잡는다</summary>
    static void ReloadModel3Parts(ClawModelParts parts)
    {
        Mesh M(string n) => AssetDatabase.LoadAssetAtPath<Mesh>($"{Model3MeshFolder}/{n}.asset");
        parts.ufoBody = M("Claw3_Head");
        if (parts.fingers != null) parts.fingers = Enumerable.Range(0, parts.fingers.Length).Select(k => M($"Claw3_Finger{k}")).ToArray();
        parts.ufoMount = M("Claw3_Carriage");
        parts.ufoCable = M("Claw3_Cable");
        parts.joyStick = M("Claw3_JoyStick");
        parts.button = M("Claw3_Button");
        parts.machineBody = M("Claw3_Body");
        parts.machineGlass = M("Claw3_BackGlass");
        parts.mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Claw3_Body.mat");
        parts.glass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/Claw_MachineGlass.mat");
    }

    /// <summary>Model3 기계: 몸체(한 메시)·뒤 창 유리를 Environment/Machine 아래에, 콘솔 조이스틱·버튼은 Model2와 같은 구조로 따로</summary>
    static ClawConsoleVisual BuildModel3Machine(GameObject env, int layer, ClawModelParts parts, StringBuilder report)
    {
        var machine = new GameObject("Machine") { layer = layer };
        machine.transform.SetParent(env.transform, false);
        machine.transform.localPosition = ClawLayout.MachineOriginLocal; // 메시에 yaw·배율이 이미 들어 있음
        MeshPart(machine.transform, "Body", parts.machineBody, Vector3.zero, parts.mat, layer);
        MeshPart(machine.transform, "BackGlass", parts.machineGlass, Vector3.zero, parts.glass, layer);

        var yaw = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);
        Vector3 ToBooth(Vector3 model) => ClawLayout.MachineOriginLocal + yaw * (model * ClawLayout.MachineScale);
        var console = new GameObject("Console") { layer = layer };
        console.transform.SetParent(env.transform, false);
        var pivot = new GameObject("JoystickPivot") { layer = layer };
        pivot.transform.SetParent(console.transform, false);
        pivot.transform.localPosition = ToBooth(parts.joyPivotModel);
        MeshPart(pivot.transform, "JoyStick", parts.joyStick, Vector3.zero, parts.mat, layer);
        var button = MeshPart(console.transform, "Button", parts.button, ToBooth(parts.buttonPivotModel), parts.mat, layer);
        var cv = console.AddComponent<ClawConsoleVisual>();
        SetRef(cv, "stickPivot", pivot.transform);
        SetRef(cv, "button", button.transform);
        var b = parts.machineBoundsModel ?? new Bounds();
        report.AppendLine($"  · 기계 = Model3 (×{ClawLayout.MachineScale:F2} → {b.size.x * ClawLayout.MachineScale:F2}×{b.size.y * ClawLayout.MachineScale:F2}×{b.size.z * ClawLayout.MachineScale:F2}m, " +
                          $"원점 {ClawLayout.MachineOriginLocal}, yaw {ClawLayout.MachineYaw:F0}) — 몸체 1메시·뒤 창 Claw_MachineGlass, 내부 {ClawLayout.InnerX:F2}×{ClawLayout.InnerZ:F2}m, 바닥 y {ClawLayout.FloorY:F2}");
        return cv;
    }
}
