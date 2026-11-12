using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using SouvenirHunt;

// 10-13 연출안(XR 인형뽑기 3단계 인증 시나리오 업데이트) — 게임 씬(메뉴 30/35) 쪽:
// STEP 04 디지털 디졸브 전환 돔(DigitalReveal, 셰이더 SouvenirHunt/DigitalReveal) + STEP 05 [START] 패널(기계 앞, 부스 고정).
public static partial class SouvenirHuntSetup
{
    const string RevealShaderName = "SouvenirHunt/DigitalReveal";
    /// <summary>돔 반지름 — 기계(콘솔 앞면 약 0.83m)보다 가깝게: 그리드·픽셀이 기계 '앞'에 보여 깊이가 어긋나지 않게</summary>
    const float RevealDomeRadius = 0.75f;

    /// <summary>씬 루트 DigitalReveal(돔 + 효과음). 세션이 Video 뒤에 Begin — 메뉴 30/35가 NewScene 뒤에 부른다</summary>
    internal static DigitalReveal BuildDigitalReveal(StringBuilder report)
    {
        var shader = Shader.Find(RevealShaderName);
        if (shader == null)
        {
            report.AppendLine($"  ⚠ 셰이더 {RevealShaderName} 없음 — 디졸브 전환 생략(예전처럼 안내 → 기계 솟음)");
            return null;
        }
        var mat = GetOrCreateMat("Claw_DigitalReveal", RevealShaderName);
        if (mat.shader != shader) mat.shader = shader;
        mat.renderQueue = -1; // 셰이더 큐(Overlay) 그대로
        EditorUtility.SetDirty(mat);

        var root = new GameObject("DigitalReveal");
        var reveal = root.AddComponent<DigitalReveal>();
        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;

        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Dome";
        Object.DestroyImmediate(dome.GetComponent<Collider>());
        dome.transform.SetParent(root.transform, false);
        dome.transform.localScale = Vector3.one * (RevealDomeRadius * 2f); // 기본 구 반지름 0.5
        var mr = dome.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        dome.SetActive(false);

        SetRef(reveal, "dome", mr);
        SetRef(reveal, "audioSource", audio);
        SetRef(reveal, "revealClip", VipClip(VipSndReveal));
        report.AppendLine($"  · 디지털 디졸브 전환(연출안 STEP 04): 돔 반지름 {RevealDomeRadius}m(깊이 무시·Overlay 큐) · 효과음 {VipSndReveal} — 영상 끝 → 검정 → 그리드 → 픽셀 분해 → 기계·부스 등장");
        return reveal;
    }

    /// <summary>
    /// [START](연출안 STEP 05) — 기계 앞 결과 패널 자리(ClawLayout.PanelLocal, 부스 고정). 육각형 START + Beam Pro 가로 안내.
    /// 육각형 뒤에 어두운 채움을 깔아 OST에서 뒤 기계를 비운다(밝은 기계 위 글자는 안 읽힘). Guide 패널(ClawGuidePanel)이 켜고 끈다.
    /// </summary>
    static (GameObject root, RectTransform hex) BuildStartPrompt(Transform boothRoot, TMP_FontAsset font)
    {
        var root = new GameObject("StartPrompt");
        root.transform.SetParent(boothRoot, false);
        root.transform.localPosition = ClawLayout.PanelLocal;
        root.transform.localRotation = Quaternion.LookRotation(ClawLayout.PanelLocal.normalized, Vector3.up);
        root.transform.localScale = Vector3.one * ClawLayout.StartPanelScale;

        var canvas = CreateWorldCanvas("StartCanvas", root.transform, new Vector2(640f, 470f));
        var hexRoot = new GameObject("Hex").AddComponent<RectTransform>();
        hexRoot.SetParent(canvas.transform, false);
        hexRoot.anchoredPosition = new Vector2(0f, 60f);
        hexRoot.sizeDelta = new Vector2(340f, 340f);
        VipImage(hexRoot, "Fill", VipSprite(VipHexFill), new Color(0.03f, 0.06f, 0.12f, 0.92f), Vector2.zero, new Vector2(340f, 340f));
        VipImage(hexRoot, "Outline", VipSprite(VipStartHex), VipCyan, Vector2.zero, new Vector2(340f, 340f));
        CreateText(hexRoot, "Label", "START", font, 92, Color.white, new Vector2(0f, 4f), new Vector2(320f, 120f));

        var textBox = CreateBar(canvas.transform, "TextBg", new Color(0.06f, 0.1f, 0.16f, 0.85f), new Vector2(0f, -170f), new Vector2(620f, 110f));
        textBox.raycastTarget = false;
        var l1 = CreateText(canvas.transform, "Line1", "Beam Pro를 가로로 들고 [START]", font, 36, Color.white, new Vector2(0f, -150f), new Vector2(600f, 50f));
        var l2 = CreateText(canvas.transform, "Line2", "↓ 가슴 앞 틀에 맞추세요", font, 30, new Color(0.4f, 0.9f, 1f), new Vector2(0f, -195f), new Vector2(600f, 44f));
        l1.textWrappingMode = TextWrappingModes.NoWrap;
        l2.textWrappingMode = TextWrappingModes.NoWrap;
        root.SetActive(false);
        return (root, hexRoot);
    }
}
