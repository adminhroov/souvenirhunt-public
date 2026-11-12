using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SouvenirHunt;

// 인형뽑기 부스 = 임포트 모델 Model1 "XR EXPERIENCE" (6차, 09-14). SouvenirHuntSetup partial — 메뉴 30이 호출.
// 사용자 결정: 실물 비율 그대로, 가운데 구조물(지구본 받침) 바로 앞에 인형뽑기 기계. 기계와 함께 정면 재정렬 뒤 슬라이드 인(MachineVisibility).
// 흔들림(MachineShake)에는 넣지 않는다 — 폭발은 기계만 흔든다. 시각 전용(콜라이더 없음), static 금지(재정렬로 움직임).
public static partial class SouvenirHuntSetup
{
    const string Model1PrefabPath = "Assets/Models/Model1/Model1.prefab";
    const string BoothPedestalPartName = "Model_1.009";   // 가운데 구조물(원형 받침, 위에 지구본 Model_1.010)

    // 10-19 행사 부스 = StartUpPark(전시 홀·기둥·양옆 부스·카펫·울타리·"인형뽑기 이벤트" 안내판). 메뉴 33이 재질·프리팹을 만든다.
    // 모델 가운데 인형뽑기 기계(Gacha_Machine.005 몸체 + .006 유리 상자 + .007 배출구 판)는 겉모양뿐(집게·캐리지 등 움직이는 부품 없음)이라 지우고,
    // 그 발자국 중심에 게임 기계를 세운다(같은 STARTUP PARK 디자인). 모델 앞(+z, 조이스틱 쪽)이 체험자를 향하게 yaw 180, 실물 크기.
    // 기계 바닥면 = 원본 기계 밑면(카펫 위). 프리팹이 없으면 Model1 부스로 대체.
    const string StartUpParkPrefabPath = "Assets/Models/StartUpPark/StartUpPark.prefab";
    const string StartUpParkMachinePart = "StartUpPark_Gacha_Machine.005";
    const string StartUpParkMachinePrefix = "StartUpPark_Gacha_Machine";
    const float StartUpParkYaw = 180f;
    const float StartUpParkScale = 1f;

    /// <summary>부스 배치. 모델이 없으면 경고만(부스 없이도 게임은 완전) — null 반환.</summary>
    static GameObject BuildClawBooth(Transform boothRoot, int layer, ClawModelParts machineParts, StringBuilder report)
    {
        var sup = AssetDatabase.LoadAssetAtPath<GameObject>(StartUpParkPrefabPath);
        if (sup != null) return BuildStartUpParkBooth(boothRoot, layer, machineParts, sup, report);
        report.AppendLine($"  ⚠ {StartUpParkPrefabPath} 없음(메뉴 33 필요) — Model1 부스로 대체");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Model1PrefabPath);
        if (prefab == null)
        {
            report.AppendLine($"  ⚠ 부스 {Model1PrefabPath} 없음 — 부스 없이 구성(Model1 임포트 후 메뉴 33 → 30)");
            return null;
        }

        // 실측(모델 로컬): 받침 바운즈, 기계 뒷면(부스 방향 +z 쪽) 돌출
        var pedestal = MeasurePartBounds(prefab, BoothPedestalPartName);
        if (pedestal == null)
        {
            report.AppendLine($"  ⚠ 부스 가운데 구조물 '{BoothPedestalPartName}' 없음 — 모델 원점을 기계 뒤 1.5m에 둠");
            pedestal = new Bounds(Vector3.zero, Vector3.one * 2f);
        }
        var machineB = machineParts.machineBoundsModel ?? MeasureModelBounds(machineParts.prefab); // 10-13 Model3는 구운 메시 바운즈
        // 기계는 yaw 180 → 모델 로컬 −z 끝이 부스 방향 뒤(+z). 9차: 기계만 MachineScale(부스는 실물 크기 그대로)
        float machineBackZ = ClawLayout.MachineOriginLocal.z - machineB.min.z * ClawLayout.MachineScale;
        // 부스도 yaw 180 → 받침 앞 가장자리(모델 +z 끝) = 원점z − max.z × BoothScale
        // 9차: BoothScale — 받침 앞 가장자리·바닥 높이(모델 최저면)는 배율과 무관하게 같은 자리
        float bs = ClawLayout.BoothScale;
        var pb = pedestal.Value;
        var boothB = MeasureModelBounds(prefab);
        float originZ = machineBackZ + ClawLayout.BoothGap + pb.max.z * bs;
        float originX = pb.center.x * bs; // yaw 180 → 받침 중심 x를 기계 중심선(0)에 맞춤
        float originY = ClawLayout.MachineBaseY + boothB.min.y * (1f - bs);
        var origin = new Vector3(originX, originY, originZ);

        var root = new GameObject("Booth") { layer = layer };
        root.transform.SetParent(boothRoot, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "BoothModel";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = origin;
        model.transform.localRotation = Quaternion.Euler(0f, ClawLayout.BoothYaw, 0f);
        model.transform.localScale = Vector3.one * bs;
        int renderers = 0;
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (var mr in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (mr.enabled) renderers++;
        }

        report.AppendLine($"  · 부스 = Model1 (×{bs:F2} → {boothB.size.x * bs:F2}×{boothB.size.y * bs:F2}×{boothB.size.z * bs:F2}m, 모델 최저면 y {boothB.min.y:F3}, 원점 {origin}, 입구 z {originZ - boothB.max.z * bs:F2}, yaw {ClawLayout.BoothYaw:F0}) — " +
                          $"가운데 구조물 앞 가장자리 z {originZ - pb.max.z * bs:F2} = 기계 뒷면 {machineBackZ:F2} + {ClawLayout.BoothGap:F2}, 렌더러 {renderers}개, 기계와 함께 슬라이드 인");
        return root;
    }

    static GameObject BuildStartUpParkBooth(Transform boothRoot, int layer, ClawModelParts machineParts, GameObject prefab, StringBuilder report)
    {
        float bs = StartUpParkScale;
        var rot = Quaternion.Euler(0f, StartUpParkYaw, 0f);
        var boothB = MeasureModelBounds(prefab);
        var anchor = MeasurePartBounds(prefab, StartUpParkMachinePart);
        if (anchor == null) report.AppendLine($"  ⚠ 부스 기계 '{StartUpParkMachinePart}' 없음 — 모델 원점·최저면 기준으로 배치");
        var a = anchor ?? new Bounds(new Vector3(0f, boothB.min.y, 0f), Vector3.zero);

        // 게임 기계 발자국 중심(BoothRoot 로컬) = 기계 원점 + yaw 회전 × (모델 바운즈 중심 × 배율)
        var machineB = machineParts.machineBoundsModel ?? MeasureModelBounds(machineParts.prefab);
        var mScale = new Vector3(ClawLayout.MachineScale * ClawLayout.MachineWidthScale, ClawLayout.MachineScale, ClawLayout.MachineScale);
        var machineCenter = ClawLayout.MachineOriginLocal + Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f) * Vector3.Scale(machineB.center, mScale);
        // 부스 원점: 원본 기계 발자국 중심(xz)이 게임 기계 중심에, 원본 기계 밑면(y)이 게임 기계 바닥(MachineBaseY)에 오게
        var anchorOffset = rot * new Vector3(a.center.x, 0f, a.center.z) * bs;
        var origin = new Vector3(machineCenter.x - anchorOffset.x, ClawLayout.MachineBaseY - a.min.y * bs, machineCenter.z - anchorOffset.z);
        // 10-19: 게임 기계 몸체가 바로 이 모델의 기계(ClawLayout.CabinetSup) → 부스 루트 = 기계 원점(같은 프리팹 루트라 지운 원본 기계와 정확히 겹침)
        if (ClawLayout.CabinetSup)
        {
            origin = ClawLayout.MachineOriginLocal;
            rot = Quaternion.Euler(0f, ClawLayout.MachineYaw, 0f);
            bs = 1f;
            machineCenter = ClawLayout.MachineOriginLocal + rot * new Vector3(a.center.x, 0f, a.center.z);
        }

        var root = new GameObject("Booth") { layer = layer };
        root.transform.SetParent(boothRoot, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "BoothModel";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = origin;
        model.transform.localRotation = rot;
        model.transform.localScale = Vector3.one * bs;

        // 겉모양뿐인 원본 기계(몸체·유리 상자·배출구 판) 제거 — 게임 기계가 그 자리에 선다
        var removed = model.GetComponentsInChildren<Transform>(true)
            .Where(t => t != model.transform && t.name.StartsWith(StartUpParkMachinePrefix)).Select(t => t.gameObject).ToList();
        foreach (var go in removed) Object.DestroyImmediate(go);

        int renderers = 0;
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        foreach (var mr in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (mr.enabled) renderers++;
        }

        var size = boothB.size * bs;
        report.AppendLine($"  · 부스 = StartUpPark (×{bs:F2} → {size.x:F2}×{size.y:F2}×{size.z:F2}m, yaw {StartUpParkYaw:F0}, 원점 {origin}) — " +
                          $"원본 인형뽑기 기계 {removed.Count}개 파트 제거, 그 자리(발자국 중심 {a.center.x:F2},{a.center.z:F2})에 게임 기계 중심 ({machineCenter.x:F2},{machineCenter.z:F2}), " +
                          $"기계 밑면 = 원본 기계 밑면(모델 y {a.min.y:F3}), 렌더러 {renderers}개, 기계와 함께 슬라이드 인");
        return root;
    }

    /// <summary>프리팹 자식 파트의 모델 로컬 바운즈(메시 정점을 루트 기준으로 변환해 측정)</summary>
    static Bounds? MeasurePartBounds(GameObject prefab, string partName)
    {
        var t = prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == partName);
        var mf = t != null ? t.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) return null;
        var m = prefab.transform.worldToLocalMatrix * t.localToWorldMatrix;
        var v = mf.sharedMesh.vertices;
        var b = new Bounds(m.MultiplyPoint3x4(v[0]), Vector3.zero);
        foreach (var p in v) b.Encapsulate(m.MultiplyPoint3x4(p));
        return b;
    }

    /// <summary>프리팹 전체(빈 메시 제외) 모델 로컬 바운즈</summary>
    static Bounds MeasureModelBounds(GameObject prefab)
    {
        Bounds? all = null;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var b = MeasurePartBounds(prefab, mf.name);
            if (b == null) continue;
            if (all == null) all = b; else { var a = all.Value; a.Encapsulate(b.Value); all = a; }
        }
        return all ?? new Bounds(Vector3.zero, Vector3.zero);
    }
}
