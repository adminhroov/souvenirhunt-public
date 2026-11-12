using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SouvenirHunt;

// 10-22 뼈 집게 — 사용자 "오늘 임포트한 메시의 뼈와 애니메이션을 사용하지 않았다는거야? 그렇다면 그걸로 교체해서 진행해".
// StartUpPark_set.fbx의 기계 몸체(StartUpPark_Gacha_Machine.005)는 스킨 메시다:
//   뼈 12개 — root(몸체) · R_1(캐리지) · R_2(허브) · 발 3개 × Bone001(팔) → Bone002(윗마디) → Bone003(갈고리), 클립 "Take 001"(1.87초 시연: 이동·하강·오므림·펼침).
//   휴식 자세(= 바인드 포즈)의 정점은 옛 프리팹 몸체와 같은 공간·같은 자리(10-22 실측 — 집게 영역 정점 855개 바운즈 일치).
// 메뉴 30: 집게 성분(캐리지 제외 — 캐리지는 Sup_Carriage로 따로 굽고 레일을 따라 움직인다)만 떼어 스킨 메시 Sup_ClawSkin으로 굽고,
//   BuildClawRig가 헤드 아래에 뼈 계층(R_2 → 발별 관절 3개)과 SkinnedMeshRenderer를 다시 만든다.
// 클립은 한 줄짜리 시연이라 그대로 재생하지 않는다 — 발 관절 자세 두 개(발끝이 가장 안으로 모인 순간 = 오므림, 가장 벌어진 순간 = 펼침)를 뽑아
//   ClawFingers가 게임 로직의 각도(조준·하강·물리 잡기 그대로)에 따라 휴식 자세와 섞는다 — 세 마디가 원작 애니메이션 모양으로 굽는다.
// 스프링(R_1 43 · R_2 9 정점)은 캐리지와 허브 사이를 늘이는 설계지만, 게임의 하강 거리(최대 약 1m)는 클립(22cm)보다 훨씬 길어 굵은 관처럼 늘어나므로
//   허브(R_2)에 붙인다(지금처럼 케이블이 캐리지와 잇는다). 몸체 뼈(root) 가중치도 허브로.
public static partial class SouvenirHuntSetup
{
    const string SupRigFbxPath = "Assets/Models/StartUpPark/StartUpPark_set.fbx";
    const string SupRigHub = "R_2";
    const int SupRigJoints = 3; // 발마다 관절 수(팔·윗마디·갈고리)

    /// <summary>뼈 집게 굽기 결과(메뉴 30 한 번 — NewScene 뒤 메시는 경로로 다시 잡는다)</summary>
    sealed class SupRig
    {
        public Mesh mesh;                          // 정점 = 세트 공간(= 옛 프리팹 공간), 바인드포즈 = 휴식 자세
        public string[] names;
        public int[] parent;                       // -1 = 집게 리그 루트(세트 공간)
        public Vector3[] localPos;
        public Quaternion[] localRot;
        public int[] fingerRoots;                  // 발별 Bone001 인덱스(발 순서 = 윗마디 방향각)
        public Quaternion[] closePose, openPose;   // 발 관절 로컬 회전(발 × 관절)
    }

    static SupRig BuildSupRig(StringBuilder report)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(SupRigFbxPath);
        if (model == null)
        {
            report.AppendLine($"  ⚠ {SupRigFbxPath} 없음 — 뼈 없는 집게(발 메시를 관절에서 통째로 회전)");
            return null;
        }
        var go = UnityEngine.Object.Instantiate(model);
        try { return BakeSupRig(go, report); }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    static Vector3 SupSkin(Matrix4x4[] m, BoneWeight w, Vector3 x, bool point)
    {
        Vector3 F(int j) => point ? m[j].MultiplyPoint3x4(x) : m[j].MultiplyVector(x);
        return F(w.boneIndex0) * w.weight0 + F(w.boneIndex1) * w.weight1 + F(w.boneIndex2) * w.weight2 + F(w.boneIndex3) * w.weight3;
    }

    /// <summary>원본 뼈 번호 → 새 뼈 번호로 옮기고(같은 뼈로 모이면 합침) 큰 순 4개를 정규화</summary>
    static BoneWeight SupRemapWeight(BoneWeight w, int[] map)
    {
        var acc = new Dictionary<int, float>();
        void Add(int i, float x) { if (x <= 0f) return; int k = map[i]; acc[k] = (acc.TryGetValue(k, out float y) ? y : 0f) + x; }
        Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
        var top = acc.OrderByDescending(e => e.Value).Take(4).ToArray();
        float sum = top.Sum(e => e.Value);
        var r = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        if (top.Length == 0 || sum <= 1e-6f) return r;
        r.boneIndex0 = top[0].Key; r.weight0 = top[0].Value / sum;
        if (top.Length > 1) { r.boneIndex1 = top[1].Key; r.weight1 = top[1].Value / sum; }
        if (top.Length > 2) { r.boneIndex2 = top[2].Key; r.weight2 = top[2].Value / sum; }
        if (top.Length > 3) { r.boneIndex3 = top[3].Key; r.weight3 = top[3].Value / sum; }
        return r;
    }

    static int SupDominant(BoneWeight w)
    {
        int d = w.boneIndex0; float m = w.weight0;
        if (w.weight1 > m) { d = w.boneIndex1; m = w.weight1; }
        if (w.weight2 > m) { d = w.boneIndex2; m = w.weight2; }
        if (w.weight3 > m) { d = w.boneIndex3; }
        return d;
    }

    static SupRig BakeSupRig(GameObject go, StringBuilder report)
    {
        var smr = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == SupBodyPart);
        if (smr == null || smr.sharedMesh == null)
            throw new Exception($"{SupRigFbxPath}: 스킨 메시 '{SupBodyPart}' 없음 — 모델이 바뀌었으면 SouvenirHuntClawSupRig.cs 갱신");
        var src = smr.sharedMesh;
        var bones = smr.bones;
        var bind = src.bindposes;
        var bw = src.boneWeights;
        var skinM = bones.Select((b, j) => b.localToWorldMatrix * bind[j]).ToArray(); // 휴식(바인드) 자세 → 세트 공간(인스턴스 원점)
        var v0 = src.vertices;
        var n0 = src.normals;
        var p = new SupPart { v = new Vector3[v0.Length], n = new Vector3[v0.Length], uv = src.uv, tris = src.triangles, mat = smr.sharedMaterial };
        for (int i = 0; i < v0.Length; i++)
        {
            p.v[i] = SupSkin(skinM, bw[i], v0[i], true);
            p.n[i] = n0.Length > i ? SupSkin(skinM, bw[i], n0[i], false).normalized : Vector3.up;
        }

        // 집게 성분(옛 분류와 같은 IsSupClaw) 중 캐리지(밑면 = 케이블 앵커 이상)를 뺀 것 = 스프링·허브·팔·발
        var claw = SupComponents(p).Select(c => (tris: c, b: SupBounds(p, c)))
            .Where(c => IsSupClaw(c.b) && c.b.min.y < ClawLayout.SupCarriageBottomY - 0.005f).ToList();
        if (claw.Count < 7) throw new Exception($"{SupRigFbxPath}: 집게 성분 {claw.Count}개(7개 이상 기대) — 모델이 바뀌었으면 ClawLayout.Sup* 갱신");

        // 뼈: 허브 + 발 3개 × 관절 3개(발 순서 = 윗마디 방향각 — 옛 발 메시 순서와 같은 기준)
        var hub = bones.FirstOrDefault(b => b != null && b.name == SupRigHub);
        if (hub == null) throw new Exception($"{SupRigFbxPath}: 허브 뼈 '{SupRigHub}' 없음");
        var roots = Enumerable.Range(0, hub.childCount).Select(k => hub.GetChild(k)).Where(t => t.childCount > 0)
            .OrderBy(t => { var d = t.GetChild(0).position - hub.position; return Mathf.Atan2(d.z, d.x); }).ToList();
        if (roots.Count != 3) throw new Exception($"{SupRigFbxPath}: 허브 아래 발 뼈 {roots.Count}개(3개 기대)");
        var list = new List<Transform> { hub };
        var fingerRoots = new int[roots.Count];
        for (int f = 0; f < roots.Count; f++)
        {
            fingerRoots[f] = list.Count;
            var t = roots[f];
            for (int j = 0; j < SupRigJoints; j++)
            {
                if (t == null || Array.IndexOf(bones, t) < 0) throw new Exception($"{SupRigFbxPath}: 발 {f} 관절 {j} 뼈 없음(스킨에 없는 뼈)");
                list.Add(t);
                t = t.childCount > 0 ? t.GetChild(0) : null;
            }
        }
        var map = bones.Select(b => Mathf.Max(0, list.IndexOf(b))).ToArray(); // 캐리지(R_1)·몸체(root) → 허브

        var remap = new Dictionary<int, int>();
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var tris = new List<int>();
        foreach (var c in claw)
            foreach (int t in c.tris)
                for (int k = 0; k < 3; k++)
                {
                    int s = p.tris[t + k];
                    if (!remap.TryGetValue(s, out int d))
                    {
                        d = verts.Count;
                        remap[s] = d;
                        verts.Add(p.v[s]);
                        normals.Add(p.n[s]);
                        uvs.Add(p.uv.Length > s ? p.uv[s] : Vector2.zero);
                        weights.Add(SupRemapWeight(bw[s], map));
                    }
                    tris.Add(d);
                }
        var mesh = SaveSupMesh("Sup_ClawSkin", verts, normals, uvs, tris);
        mesh.boneWeights = weights.ToArray();
        mesh.bindposes = list.Select(b => b.worldToLocalMatrix).ToArray(); // 메시 공간 = 세트 공간(리그 루트)
        EditorUtility.SetDirty(mesh);

        int n = list.Count;
        var rig = new SupRig
        {
            mesh = mesh,
            names = list.Select(b => b.name).ToArray(),
            parent = new int[n],
            localPos = new Vector3[n],
            localRot = new Quaternion[n],
            fingerRoots = fingerRoots,
        };
        for (int k = 0; k < n; k++)
        {
            var b = list[k];
            int pi = list.IndexOf(b.parent);
            rig.parent[k] = pi;
            rig.localPos[k] = pi < 0 ? b.position : b.localPosition;
            rig.localRot[k] = pi < 0 ? b.rotation : b.localRotation;
        }

        // 클립에서 발 자세 두 개: 발끝(갈고리 뼈에 붙은 정점 중 뼈에서 가장 먼 점)이 허브 축에 가장 가까운 순간 = 오므림, 가장 먼 순간 = 펼침
        var clip = AssetDatabase.LoadAllAssetsAtPath(SupRigFbxPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
        if (clip == null) throw new Exception($"{SupRigFbxPath}: 애니메이션 클립 없음(발 자세를 뽑을 수 없음)");
        var tipLocal = new Vector3[fingerRoots.Length];
        for (int f = 0; f < fingerRoots.Length; f++)
        {
            int jt = fingerRoots[f] + SupRigJoints - 1;
            float best = -1f;
            for (int d = 0; d < verts.Count; d++)
            {
                if (SupDominant(weights[d]) != jt) continue;
                var lp = list[jt].InverseTransformPoint(verts[d]);
                if (lp.sqrMagnitude > best) { best = lp.sqrMagnitude; tipLocal[f] = lp; }
            }
        }
        (float r, float y) Tips()
        {
            float r = 0f, y = 0f;
            for (int f = 0; f < fingerRoots.Length; f++)
            {
                var w = list[fingerRoots[f] + SupRigJoints - 1].TransformPoint(tipLocal[f]) - hub.position;
                r += new Vector2(w.x, w.z).magnitude; y += w.y;
            }
            return (r / fingerRoots.Length, y / fingerRoots.Length);
        }
        var rest = Tips();
        float closeT = 0f, openT = 0f, closeR = float.MaxValue, openR = -1f;
        int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
        for (int fr = 0; fr <= frames; fr++)
        {
            float t = fr / clip.frameRate;
            clip.SampleAnimation(go, t);
            float r = Tips().r;
            if (r < closeR) { closeR = r; closeT = t; }
            if (r > openR) { openR = r; openT = t; }
        }
        Quaternion[] Pose(float t)
        {
            clip.SampleAnimation(go, t);
            var q = new Quaternion[fingerRoots.Length * SupRigJoints];
            for (int f = 0; f < fingerRoots.Length; f++)
                for (int j = 0; j < SupRigJoints; j++) q[f * SupRigJoints + j] = list[fingerRoots[f] + j].localRotation;
            return q;
        }
        rig.closePose = Pose(closeT); var closeTip = Tips();
        rig.openPose = Pose(openT); var openTip = Tips();
        clip.SampleAnimation(go, 0f);
        AssetDatabase.SaveAssets();
        report.AppendLine($"  · 뼈 집게 = {System.IO.Path.GetFileName(SupRigFbxPath)} 스킨(정점 {verts.Count} · 뼈 {n} = 허브 + 발 {fingerRoots.Length}×{SupRigJoints}) — " +
                          $"클립 '{clip.name}' {clip.length:F2}s에서 오므림 t={closeT:F2}(발끝 반지름 {closeTip.r:F3}·높이 {closeTip.y:+0.000;-0.000}) · " +
                          $"펼침 t={openT:F2}({openTip.r:F3}·{openTip.y:+0.000;-0.000}) · 휴식({rest.r:F3}·{rest.y:+0.000;-0.000}) — 허브 기준 m");
        return rig;
    }

    /// <summary>헤드 비주얼 아래 뼈 집게: 리그 루트(세트 공간 → 헤드 로컬: 피벗·배율·yaw) + 뼈 계층 + 스킨 렌더러. 발 뿌리 관절 3개를 돌려준다</summary>
    static Transform[] BuildSupRigVisual(Transform headVisual, SupRig r, Material mat, int layer)
    {
        var yaw = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);
        float hs = ClawLayout.SupClawScale;
        var headPivot = new Vector3(0f, (ClawLayout.SupHeadTopY + ClawLayout.SupHeadBottomY) * 0.5f, 0f);
        var root = new GameObject("ClawRig") { layer = layer };
        root.transform.SetParent(headVisual, false);
        root.transform.localPosition = yaw * (-headPivot * hs);
        root.transform.localRotation = yaw;
        root.transform.localScale = Vector3.one * hs;
        var ts = new Transform[r.names.Length];
        for (int k = 0; k < ts.Length; k++)
        {
            var b = new GameObject(r.names[k]) { layer = layer };
            b.transform.SetParent(r.parent[k] < 0 ? root.transform : ts[r.parent[k]], false);
            b.transform.localPosition = r.localPos[k];
            b.transform.localRotation = r.localRot[k];
            ts[k] = b.transform;
        }
        var skinGo = new GameObject("ClawSkin") { layer = layer };
        skinGo.transform.SetParent(root.transform, false);
        var smr = skinGo.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = r.mesh;
        smr.bones = ts;
        smr.rootBone = ts[0];
        smr.sharedMaterial = mat;
        smr.quality = SkinQuality.Auto;
        smr.updateWhenOffscreen = false;
        // 허브 기준 — 펼침(발끝 약 0.4m)·스프링(위 0.15m)·발끝(아래 0.4m)을 넉넉히
        smr.localBounds = new Bounds(new Vector3(0f, -0.12f, 0f), new Vector3(1.0f, 0.75f, 1.0f));
        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        smr.receiveShadows = false;
        return r.fingerRoots.Select(k => ts[k]).ToArray();
    }

    static void SetFloatField(Component comp, string field, float value)
    {
        var so = new SerializedObject(comp);
        var prop = so.FindProperty(field);
        if (prop == null) { Debug.LogError($"[SouvenirHuntSetup] {comp.GetType().Name}.{field} 필드 없음 — 배선 실패"); return; }
        prop.floatValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetQuatArray(Component comp, string field, Quaternion[] values)
    {
        var so = new SerializedObject(comp);
        var prop = so.FindProperty(field);
        if (prop == null || !prop.isArray) { Debug.LogError($"[SouvenirHuntSetup] {comp.GetType().Name}.{field} 배열 필드 없음 — 배선 실패"); return; }
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).quaternionValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
