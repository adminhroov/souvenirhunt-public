using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using SouvenirHunt;

// MR VIP 인증 연출(10-12, 기획 MR_VIP_인증연출_시각화_개발기획서.pptx) — 리소스·ClawIntro 씬 인트로·보상 카드.
// 리소스는 Assets/VipIntro/에 기획서 이름 그대로(R01 auth_pad … R07 reward_card, 효과음 4종). 파일이 없을 때만 임시본을 코드로 그린다 —
// 디자이너·AI 이미지로 같은 이름 파일을 덮어쓰면 메뉴를 다시 돌려도 보존되고 코드 수정 없이 바뀐다(기획서 원칙 ⑥).
// 이미지는 흰색 + 알파로 그리고 색은 런타임 Image.color(시안·골드)로 입힌다 — OST에서 검정은 투명이라 밝은 색만 쓴다.
// R06 game_logo(선택)는 만들지 않음.
// 10-13 연출안(XR 인형뽑기 3단계 인증 시나리오 업데이트): STEP 01 패드 = 카드 스캐너 3D 모델(Assets/Models/CardScanner) + 화면 오버레이,
// STEP 02 = EYE SCAN 패널(홍채·눈 아이콘·파형), 완료 = 초록 ACCESS GRANTED(VIP 배지 대신). 새 이미지 7종·전환 효과음도 같은 규칙(없을 때만 임시본).
public static partial class SouvenirHuntSetup
{
    public const string VipFolder = "Assets/VipIntro";
    const string VipPad = "auth_pad.png", VipScan = "scan_ring.png", VipGaze = "gaze_target.png",
                 VipProgress = "progress_ring.png", VipBadge = "vip_badge.png", VipCard = "reward_card.png";
    const string VipScanLine = "scan_line.png", VipWindow = "window_glow.png", VipCheck = "check_circle.png", VipEye = "eye_icon.png",
                 VipIris = "iris.png", VipFrame = "scan_frame.png", VipStartHex = "start_hex.png", VipHexFill = "hex_fill.png";
    const string VipSndLink = "link.wav", VipSndScan = "scan_success.wav", VipSndVip = "vip_success.wav", VipSndReward = "reward.wav",
                 VipSndReveal = "reveal.wav";
    const string CardScannerModelPath = "Assets/Models/CardScanner/futuristic+card+scanner+3d+model.fbx";
    const string CardScannerMatPath = "Assets/Settings/CardScanner_Unlit.mat";

    /// <summary>패드: 가슴 앞(Guide 프레임과 같은 자리, 눈 원점 기준). 10-13: 카드 스캐너 폭 0.30m, 체험자 쪽으로 40° 기울여 눈을 거의 정면으로 향함</summary>
    static readonly Vector3 VipPadLocal = new Vector3(0f, -0.42f, 0.30f);
    const float VipScannerWidth = 0.30f, VipPadTiltDeg = 40f;
    /// <summary>스캐너 화면 오버레이 해상도(px/m) — 0.001(1000px/m)보다 촘촘하게 해 작은 글자도 또렷하게</summary>
    const float VipOverlayPxPerM = 2000f;
    /// <summary>패드 안내 문구: 정면 시야 하단(Guide 문구와 같은 자리)</summary>
    static readonly Vector3 VipHintLocal = new Vector3(0f, -0.24f, 1.0f);

    static readonly Color VipCyan = new Color(0.35f, 0.9f, 1f, 1f);
    static readonly Color VipGold = new Color(1f, 0.8f, 0.3f, 1f);
    static readonly Color VipGreen = new Color(0.35f, 1f, 0.6f, 1f);

    // ─ 리소스 ──────────────────────────────────────────────────

    /// <summary>없는 리소스만 임시본 생성 + 스프라이트 임포트 설정(있는 파일은 그대로 — 교체본 보존)</summary>
    public static void EnsureVipAssets(StringBuilder report)
    {
        EnsureFolder(VipFolder);
        var made = new StringBuilder();
        if (MakePng(VipPad, 1024, 1024, DrawAuthPad)) made.Append(VipPad).Append(' ');
        if (MakePng(VipScan, 1024, 1024, DrawScanRing)) made.Append(VipScan).Append(' ');
        if (MakePng(VipGaze, 1024, 1024, DrawGazeTarget)) made.Append(VipGaze).Append(' ');
        if (MakePng(VipProgress, 1024, 1024, DrawProgressRing)) made.Append(VipProgress).Append(' ');
        if (MakePng(VipBadge, 1024, 1024, DrawVipBadge)) made.Append(VipBadge).Append(' ');
        if (MakePng(VipCard, 1024, 683, DrawRewardCard)) made.Append(VipCard).Append(' ');
        if (MakePng(VipScanLine, 1024, 128, DrawScanLine)) made.Append(VipScanLine).Append(' ');
        if (MakePng(VipWindow, 1024, 800, DrawWindowGlow)) made.Append(VipWindow).Append(' ');
        if (MakePng(VipCheck, 1024, 1024, DrawCheckCircle)) made.Append(VipCheck).Append(' ');
        if (MakePng(VipEye, 1024, 1024, DrawEyeIcon)) made.Append(VipEye).Append(' ');
        if (MakePng(VipIris, 1024, 1024, DrawIris)) made.Append(VipIris).Append(' ');
        if (MakePng(VipFrame, 1536, 1024, DrawScanFrame)) made.Append(VipFrame).Append(' ');
        if (MakePng(VipStartHex, 1024, 1024, DrawStartHex)) made.Append(VipStartHex).Append(' ');
        if (MakePng(VipHexFill, 512, 512, DrawHexFill)) made.Append(VipHexFill).Append(' ');
        if (MakeWav(VipSndLink, SynthLink)) made.Append(VipSndLink).Append(' ');
        if (MakeWav(VipSndScan, SynthScanSuccess)) made.Append(VipSndScan).Append(' ');
        if (MakeWav(VipSndVip, SynthVipSuccess)) made.Append(VipSndVip).Append(' ');
        if (MakeWav(VipSndReward, SynthReward)) made.Append(VipSndReward).Append(' ');
        if (MakeWav(VipSndReveal, SynthReveal)) made.Append(VipSndReveal).Append(' ');
        AssetDatabase.Refresh();
        foreach (var f in new[] { VipPad, VipScan, VipGaze, VipProgress, VipBadge, VipCard, VipScanLine, VipWindow, VipCheck, VipEye, VipIris, VipFrame, VipStartHex, VipHexFill })
            ConfigureVipSprite($"{VipFolder}/{f}");
        report?.AppendLine(made.Length > 0
            ? $"  · VIP 리소스 임시본 생성: {made}(같은 이름으로 덮어쓰면 교체)"
            : $"  · VIP 리소스: {VipFolder} 기존 파일 사용");
    }

    static Sprite VipSprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>($"{VipFolder}/{file}");
    static AudioClip VipClip(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{VipFolder}/{file}");

    static void ConfigureVipSprite(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        bool dirty = false;
        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; dirty = true; }
        if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
        if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }   // 월드 공간에서 작게 보여 밉맵이 반짝임을 줄인다
        if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
        if (ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; dirty = true; }
        if (dirty) ti.SaveAndReimport();
    }

    /// <summary>파일이 없을 때만 그린다. draw(u, v, px) = 알파(0~1), u·v는 짧은 변 기준 −1~1, px = 픽셀 1칸 크기</summary>
    static bool MakePng(string file, int w, int h, Func<float, float, float, float, float, float> draw)
    {
        string path = $"{VipFolder}/{file}";
        if (File.Exists(path)) return false;
        var px = new Color32[w * h];
        float s = 2f / Mathf.Min(w, h);
        float hw = w * s * 0.5f, hh = h * s * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) * s - hw, v = (y + 0.5f) * s - hh;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(draw(u, v, s, hw, hh)) * 255f);
                px[y * w + x] = new Color32(255, 255, 255, a);
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        return true;
    }

    // SDF 헬퍼 — d < 0 안쪽. 경계를 1.5px로 부드럽게
    static float Fill(float d, float px) => Mathf.Clamp01(0.5f - d / (1.5f * px));
    static float Over(float a, float b) => a + b * (1f - a);
    static float Ring(float r, float radius, float halfW, float px) => Fill(Mathf.Abs(r - radius) - halfW, px);

    /// <summary>원점 중심 방사형 눈금(개수 n, 각 r0~r1, 반폭)</summary>
    static float Ticks(float u, float v, int n, float r0, float r1, float halfW, float px)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float step = Mathf.PI * 2f / n;
        float ang = Mathf.Atan2(v, u);
        float k = Mathf.Round(ang / step) * step;
        float d = ang - k;
        float perp = Mathf.Abs(r * Mathf.Sin(d));
        float along = Mathf.Max(r0 - r, r - r1);
        return Fill(Mathf.Max(perp - halfW, along), px);
    }

    static float RoundBox(float u, float v, float bx, float by, float rad)
    {
        float qx = Mathf.Abs(u) - bx + rad, qy = Mathf.Abs(v) - by + rad;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rad;
    }

    /// <summary>점이 위를 향한 별(꼭짓점 n, 바깥 ro, 안쪽 ri)의 부호 거리</summary>
    static float Star(float u, float v, int n, float ro, float ri)
    {
        int m = n * 2;
        float best = float.MaxValue;
        bool inside = false;
        for (int i = 0; i < m; i++)
        {
            float a0 = Mathf.PI * 0.5f + Mathf.PI * i / n, a1 = Mathf.PI * 0.5f + Mathf.PI * (i + 1) / n;
            float r0 = i % 2 == 0 ? ro : ri, r1 = (i + 1) % 2 == 0 ? ro : ri;
            float x0 = Mathf.Cos(a0) * r0, y0 = Mathf.Sin(a0) * r0, x1 = Mathf.Cos(a1) * r1, y1 = Mathf.Sin(a1) * r1;
            float ex = x1 - x0, ey = y1 - y0, wx = u - x0, wy = v - y0;
            float t = Mathf.Clamp01((wx * ex + wy * ey) / (ex * ex + ey * ey));
            float dx = wx - ex * t, dy = wy - ey * t;
            best = Mathf.Min(best, dx * dx + dy * dy);
            if ((y0 > v) != (y1 > v) && u < (x1 - x0) * (v - y0) / (y1 - y0) + x0) inside = !inside;
        }
        float dist = Mathf.Sqrt(best);
        return inside ? -dist : dist;
    }

    // R01 인증 패드 — 바깥·안쪽 링, 눈금 36개(3개마다 길게), 옅은 원판, 가운데 Beam Pro 윤곽
    static float DrawAuthPad(float u, float v, float px, float hw, float hh)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Ring(r, 0.92f, 0.018f, px);
        a = Over(a, 0.8f * Ring(r, 0.70f, 0.008f, px));
        a = Over(a, 0.9f * Ticks(u, v, 36, 0.76f, 0.84f, 0.006f, px));
        a = Over(a, Ticks(u, v, 12, 0.74f, 0.88f, 0.009f, px));
        a = Over(a, 0.10f * Fill(r - 0.70f, px));
        a = Over(a, 0.9f * Fill(Mathf.Abs(RoundBox(u, v, 0.20f, 0.34f, 0.05f)) - 0.012f, px));
        return a;
    }

    // R02 스캔 링 — 호 4개(회전이 보이게) + 부드러운 빛
    static float DrawScanRing(float u, float v, float px, float hw, float hh)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float deg = (Mathf.Atan2(v, u) * Mathf.Rad2Deg + 360f) % 90f;
        float arc = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01((70f - deg) / 3f) * Mathf.Clamp01(deg / 3f));
        float core = Ring(r, 0.80f, 0.014f, px);
        float glow = 0.45f * Mathf.Exp(-Mathf.Pow((r - 0.80f) / 0.05f, 2f));
        return arc * Over(core, glow);
    }

    // R03 시선 타깃 — 링 + 가는 바깥 링 + 가운데 점 + 십자 표식
    static float DrawGazeTarget(float u, float v, float px, float hw, float hh)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Ring(r, 0.55f, 0.02f, px);
        a = Over(a, 0.6f * Ring(r, 0.85f, 0.008f, px));
        a = Over(a, Fill(r - 0.07f, px));
        a = Over(a, Ticks(u, v, 4, 0.62f, 0.78f, 0.012f, px));
        return a;
    }

    // R04 진행 링 — 굵은 단색 링(Image Filled Radial360으로 채움)
    static float DrawProgressRing(float u, float v, float px, float hw, float hh) =>
        Ring(Mathf.Sqrt(u * u + v * v), 0.86f, 0.06f, px);

    // R05 VIP 배지 — 이중 링, 점 16개, 가운데 별
    static float DrawVipBadge(float u, float v, float px, float hw, float hh)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Ring(r, 0.93f, 0.02f, px);
        a = Over(a, Ring(r, 0.80f, 0.01f, px));
        float step = Mathf.PI * 2f / 16f, ang = Mathf.Atan2(v, u), k = Mathf.Round(ang / step) * step;
        float dx = u - Mathf.Cos(k) * 0.865f, dy = v - Mathf.Sin(k) * 0.865f;
        a = Over(a, Fill(Mathf.Sqrt(dx * dx + dy * dy) - 0.018f, px));
        a = Over(a, 0.5f * Ring(r, 0.62f, 0.006f, px));
        a = Over(a, Fill(Star(u, v, 5, 0.52f, 0.22f), px));
        return a;
    }

    /// <summary>선분 (ax,ay)-(bx,by)까지 거리</summary>
    static float Seg(float u, float v, float ax, float ay, float bx, float by)
    {
        float px = u - ax, py = v - ay, ex = bx - ax, ey = by - ay;
        float t = Mathf.Clamp01((px * ex + py * ey) / (ex * ex + ey * ey));
        float dx = px - ex * t, dy = py - ey * t;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>모서리를 깎은 사각형(반폭 bx·by, 깎음 ch)의 부호 거리(근사)</summary>
    static float ChamferBox(float u, float v, float bx, float by, float ch)
    {
        float qx = Mathf.Abs(u), qy = Mathf.Abs(v);
        return Mathf.Max(Mathf.Max(qx - bx, qy - by), (qx + qy - (bx + by - ch)) * 0.70710678f);
    }

    /// <summary>꼭짓점이 좌우(±r)인 육각형(위아래가 평평)의 부호 거리</summary>
    static float Hex(float u, float v, float r)
    {
        float qx = Mathf.Abs(u), qy = Mathf.Abs(v), a = r * 0.8660254f;
        return Mathf.Max(qy - a, qx * 0.8660254f + qy * 0.5f - a);
    }

    // 10-13 스캔 라인(가로 8:1) — 가는 심 + 위아래 빛, 양 끝은 흐리게
    static float DrawScanLine(float u, float v, float px, float hw, float hh)
    {
        float ends = Mathf.Clamp01((hw - Mathf.Abs(u)) / (hw * 0.25f));
        float core = Fill(Mathf.Abs(v) - 0.07f, px);
        float glow = 0.55f * Mathf.Exp(-Mathf.Pow(v / 0.4f, 2f));
        return ends * Over(core, glow);
    }

    // 10-13 스캐너 가운데 창 테두리 — 모서리 ㄱ자 4개 + 아주 옅은 바깥선
    static float DrawWindowGlow(float u, float v, float px, float hw, float hh)
    {
        float bx = hw - 0.04f, by = hh - 0.04f, len = 0.28f, th = 0.022f;
        float qx = Mathf.Abs(u), qy = Mathf.Abs(v);
        float corner = Mathf.Min(Seg(qx, qy, bx - len, by, bx, by), Seg(qx, qy, bx, by - len, bx, by));
        float a = Fill(corner - th, px);
        a = Over(a, 0.25f * Fill(Mathf.Abs(RoundBox(u, v, bx, by, 0.02f)) - 0.004f, px));
        return a;
    }

    // 10-13 완료 체크 — 원 + 체크 + 옅은 안쪽
    static float DrawCheckCircle(float u, float v, float px, float hw, float hh)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Ring(r, 0.88f, 0.035f, px);
        float chk = Mathf.Min(Seg(u, v, -0.42f, 0.02f, -0.12f, -0.30f), Seg(u, v, -0.12f, -0.30f, 0.46f, 0.34f));
        a = Over(a, Fill(chk - 0.075f, px));
        a = Over(a, 0.12f * Fill(r - 0.85f, px));
        return a;
    }

    // 10-13 눈 아이콘 — 아몬드(두 원의 교집합) 외곽선 + 홍채 링 + 동공
    static float DrawEyeIcon(float u, float v, float px, float hw, float hh)
    {
        const float w = 0.9f, h = 0.48f;
        float R = (w * w + h * h) / (2f * h), c = R - h;
        float lens = Mathf.Max(Mathf.Sqrt(u * u + (v + c) * (v + c)) - R, Mathf.Sqrt(u * u + (v - c) * (v - c)) - R);
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Fill(Mathf.Abs(lens) - 0.03f, px);
        a = Over(a, Ring(r, 0.30f, 0.035f, px));
        a = Over(a, Fill(r - 0.13f, px));
        return a;
    }

    // 10-13 홍채 — 바깥 링·눈금, 방사 섬유(길이 교차), 동공 링, 옅은 안쪽
    static float DrawIris(float u, float v, float px, float hw, float hh)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float a = Ring(r, 0.95f, 0.012f, px);
        a = Over(a, 0.85f * Ring(r, 0.84f, 0.022f, px));
        a = Over(a, 0.7f * Ticks(u, v, 60, 0.88f, 0.92f, 0.006f, px));
        float ang = Mathf.Atan2(v, u);
        int n = 96;
        float step = Mathf.PI * 2f / n;
        float k = Mathf.Round(ang / step);
        float rOut = ((int)k & 1) == 0 ? 0.80f : 0.66f;
        float perp = Mathf.Abs(r * Mathf.Sin(ang - k * step));
        float fiber = Fill(Mathf.Max(perp - 0.006f, Mathf.Max(0.36f - r, r - rOut)), px);
        a = Over(a, 0.55f * fiber);
        a = Over(a, Ring(r, 0.32f, 0.03f, px));
        a = Over(a, 0.18f * Fill(r - 0.80f, px));
        a = Over(a, 0.9f * Fill(Mathf.Sqrt((u + 0.1f) * (u + 0.1f) + (v - 0.12f) * (v - 0.12f)) - 0.06f, px)); // 하이라이트
        return a;
    }

    // 10-13 EYE SCAN·승인 패널 테두리(3:2) — 모서리 깎은 외곽선, 안쪽 가는 선, 모서리 강조, 옅은 바탕
    static float DrawScanFrame(float u, float v, float px, float hw, float hh)
    {
        float bx = hw - 0.03f, by = hh - 0.03f;
        float d = ChamferBox(u, v, bx, by, 0.16f);
        float a = Fill(Mathf.Abs(d) - 0.007f, px);
        float d2 = ChamferBox(u, v, bx - 0.05f, by - 0.05f, 0.12f);
        a = Over(a, 0.35f * Fill(Mathf.Abs(d2) - 0.003f, px));
        float qx = Mathf.Abs(u), qy = Mathf.Abs(v);
        bool corner = qx > bx - 0.42f && qy > by - 0.42f;
        if (corner) a = Over(a, Fill(Mathf.Abs(d) - 0.018f, px));
        if (d < 0f) a = Over(a, 0.07f);
        float tick = Mathf.Abs(qx - Mathf.Round(qx / 0.12f) * 0.12f);   // 아래 눈금
        if (v < 0f && qx < bx - 0.5f) a = Over(a, 0.5f * Fill(Mathf.Max(tick - 0.006f, Mathf.Abs(v + by - 0.09f) - 0.02f), px));
        return a;
    }

    // 10-13 START 육각형(연출안 STEP 05) — 굵은 외곽 + 안쪽 선 + 옅은 바탕
    static float DrawStartHex(float u, float v, float px, float hw, float hh)
    {
        float d = Hex(u, v, 0.94f);
        float a = Fill(Mathf.Abs(d) - 0.03f, px);
        a = Over(a, 0.5f * Fill(Mathf.Abs(Hex(u, v, 0.82f)) - 0.008f, px));
        if (d < 0f) a = Over(a, 0.18f + 0.25f * Mathf.Exp(d / 0.06f));
        return a;
    }

    // 10-13 육각형 채움(START 뒤 어두운 판 — OST에서 뒤 기계를 육각형으로 비워 글자가 읽히게)
    static float DrawHexFill(float u, float v, float px, float hw, float hh) => Fill(Hex(u, v, 0.94f), px);

    // R07 보상 카드(3:2) — 둥근 테두리 2겹, 테두리 안쪽 빛, 옅은 바탕, 모서리 마름모
    static float DrawRewardCard(float u, float v, float px, float hw, float hh)
    {
        float inset = 16f * px;
        float d = RoundBox(u, v, hw - inset, hh - inset, 56f * px);
        float a = Fill(Mathf.Abs(d) - 5f * px, px);
        float d2 = RoundBox(u, v, hw - inset - 24f * px, hh - inset - 24f * px, 36f * px);
        a = Over(a, 0.6f * Fill(Mathf.Abs(d2) - 1.5f * px, px));
        if (d < 0f) a = Over(a, 0.35f * Mathf.Exp(d / (30f * px)) + 0.10f);
        float cx = hw - inset - 44f * px, cy = hh - inset - 44f * px;
        float md = Mathf.Abs(Mathf.Abs(u) - cx) + Mathf.Abs(Mathf.Abs(v) - cy) - 12f * px; // 마름모(L1 거리)
        a = Over(a, Fill(md, px));
        return a;
    }

    // ─ 효과음(44.1kHz 16bit 모노) ──────────────────────────────

    const int SndRate = 44100;

    static bool MakeWav(string file, Func<float[]> synth)
    {
        string path = $"{VipFolder}/{file}";
        if (File.Exists(path)) return false;
        var s = synth();
        float peak = 1e-6f;
        foreach (var x in s) peak = Mathf.Max(peak, Mathf.Abs(x));
        using (var bw = new BinaryWriter(File.Create(path)))
        {
            int dataLen = s.Length * 2;
            bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + dataLen); bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(SndRate); bw.Write(SndRate * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(dataLen);
            foreach (var x in s) bw.Write((short)Mathf.RoundToInt(Mathf.Clamp(x / peak * 0.8f, -1f, 1f) * 32767f));
        }
        return true;
    }

    static float[] SndBuf(float sec) => new float[Mathf.CeilToInt(sec * SndRate)];

    /// <summary>음 하나(배음 h2·h3, 지수 감쇠, 5ms 어택). 끝 20ms는 페이드아웃(클릭 방지)</summary>
    static void SndTone(float[] b, float start, float freq, float dur, float amp, float decay, float h2 = 0f, float h3 = 0f)
    {
        int s0 = Mathf.RoundToInt(start * SndRate), n = Mathf.RoundToInt(dur * SndRate);
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float t = (float)i / SndRate;
            float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * decay) * Mathf.Min(1f, (dur - t) / 0.02f);
            float w = 2f * Mathf.PI * freq * t;
            b[s0 + i] += amp * env * (Mathf.Sin(w) + h2 * Mathf.Sin(2f * w) + h3 * Mathf.Sin(3f * w));
        }
    }

    static void SndChirp(float[] b, float start, float f0, float f1, float dur, float amp)
    {
        int s0 = Mathf.RoundToInt(start * SndRate), n = Mathf.RoundToInt(dur * SndRate);
        float phase = 0f;
        for (int i = 0; i < n && s0 + i < b.Length; i++)
        {
            float k = (float)i / n;
            phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, k * k) / SndRate;
            b[s0 + i] += amp * Mathf.Sin(Mathf.PI * k) * Mathf.Sin(phase);
        }
    }

    // 짧은 두 음(인증 시작)
    static float[] SynthLink()
    {
        var b = SndBuf(0.3f);
        SndTone(b, 0f, 1046.5f, 0.12f, 0.5f, 25f);
        SndTone(b, 0.07f, 1568f, 0.2f, 0.5f, 18f);
        return b;
    }

    // 올라가는 스윕 + 맑은 핑(스캔 완료)
    static float[] SynthScanSuccess()
    {
        var b = SndBuf(0.65f);
        SndChirp(b, 0f, 500f, 1600f, 0.25f, 0.4f);
        SndTone(b, 0.22f, 2093f, 0.4f, 0.5f, 9f, 0.2f);
        return b;
    }

    // 장조 아르페지오 종소리(VIP 승인)
    static float[] SynthVipSuccess()
    {
        var b = SndBuf(1.6f);
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
        for (int i = 0; i < notes.Length; i++) SndTone(b, i * 0.09f, notes[i], 1.2f, 0.35f, 3.5f, 0.3f, 0.1f);
        SndTone(b, 0.27f, 2093f, 1.0f, 0.12f, 4f);
        return b;
    }

    // 10-13 디지털 디졸브 전환(4.2초, DigitalReveal 타이밍과 맞춤): ① 낮게 꺼지는 음 → ② 그리드 틱 → ③ 픽셀 반짝임 → ④ 올라가는 스윕·화음 → ⑤ 종소리
    static float[] SynthReveal()
    {
        var b = SndBuf(4.6f);
        SndChirp(b, 0f, 330f, 140f, 0.6f, 0.25f);
        var rnd = new System.Random(7);
        for (int i = 0; i < 16; i++)
        {
            float t = 0.6f + 1.0f * Mathf.Sqrt(i / 16f);                       // 점점 촘촘하게
            SndTone(b, t, 1200f + (float)rnd.NextDouble() * 1200f, 0.05f, 0.22f, 60f);
        }
        for (int i = 0; i < 12; i++)
            SndTone(b, 1.6f + i * 0.06f + (float)rnd.NextDouble() * 0.03f, 2400f + (float)rnd.NextDouble() * 1600f, 0.06f, 0.12f, 45f);
        SndChirp(b, 1.9f, 260f, 1100f, 2.0f, 0.28f);
        foreach (var f in new[] { 523.25f, 659.25f, 783.99f }) SndTone(b, 2.4f, f, 1.6f, 0.12f, 1.2f, 0.2f);
        SndTone(b, 3.9f, 1046.5f, 0.7f, 0.3f, 5f, 0.2f);
        SndTone(b, 3.95f, 1568f, 0.6f, 0.2f, 6f);
        return b;
    }

    // 반짝이는 고음 + 화음(보상)
    static float[] SynthReward()
    {
        var b = SndBuf(1.4f);
        float[] sparkle = { 1568f, 2093f, 2637f, 3136f };
        for (int i = 0; i < sparkle.Length; i++) SndTone(b, i * 0.06f, sparkle[i], 0.6f, 0.25f, 8f);
        foreach (var f in new[] { 523.25f, 659.25f, 783.99f }) SndTone(b, 0.05f, f, 1.25f, 0.2f, 2.5f, 0.2f);
        return b;
    }

    // ─ ClawIntro 씬 인트로 ─────────────────────────────────────

    static Image VipImage(Transform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
    {
        var img = CreateBar(parent, name, color, pos, size);
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>VIP 인증 연출 오브젝트 — 메뉴 34가 NewScene 뒤에 부른다(리소스는 여기서 다시 불러옴).
    /// 10-13 연출안: STEP 01 = 카드 스캐너 3D 패널 + 화면 오버레이, STEP 02 = EYE SCAN 패널, 완료 = 초록 ACCESS GRANTED.</summary>
    static VipIntro BuildVipIntro(TMP_FontAsset font, StringBuilder report)
    {
        var scanSprite = VipSprite(VipScan);
        var progressSprite = VipSprite(VipProgress);
        var lineSprite = VipSprite(VipScanLine);
        var windowSprite = VipSprite(VipWindow);
        var checkSprite = VipSprite(VipCheck);
        var eyeSprite = VipSprite(VipEye);
        var irisSprite = VipSprite(VipIris);
        var frameSprite = VipSprite(VipFrame);
        if (scanSprite == null || progressSprite == null || lineSprite == null || windowSprite == null || checkSprite == null ||
            eyeSprite == null || irisSprite == null || frameSprite == null)
            report.AppendLine($"  ⚠ VIP 이미지 일부 없음({VipFolder}) — 빈 사각형으로 표시됨");

        var root = new GameObject("VipIntro");
        var intro = root.AddComponent<VipIntro>();
        var prize = root.AddComponent<PrizeLink>(); // 승인 화면의 참가자 확인(EVT-001만) — 게임 씬의 PrizeLink와 별개
        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;

        // 패드·안내 — 눈 원점, 시선 yaw 감쇠 추종
        var follow = new GameObject("FollowRoot").transform;
        follow.SetParent(root.transform, false);
        var dock = new GameObject("Dock");
        dock.transform.SetParent(follow, false);

        // STEP 01 카드키 — 가슴 앞 카드 스캐너. padRoot −z가 눈 쪽(캔버스 앞면과 같은 방향), +y가 패널 위쪽
        var padRoot = new GameObject("Pad").transform;
        padRoot.SetParent(dock.transform, false);
        padRoot.localPosition = VipPadLocal;
        padRoot.localRotation = Quaternion.Euler(90f - VipPadTiltDeg, 0f, 0f);
        var scanner = BuildCardScanner(padRoot, out var face, report);

        // 화면 오버레이 — 스캐너 앞면 바로 앞(4mm). 크기 = 앞면, 가운데 창(카드 아이콘 자리)에 링·스캔 라인·체크
        float W = face.x * VipOverlayPxPerM, H = face.y * VipOverlayPxPerM;
        var ov = CreateWorldCanvas("PadOverlay", padRoot, new Vector2(W, H));
        ov.transform.localPosition = new Vector3(0f, 0f, -0.004f);
        ov.transform.localScale = Vector3.one / VipOverlayPxPerM;
        var window = new GameObject("Window").AddComponent<RectTransform>();
        window.SetParent(ov.transform, false);
        window.sizeDelta = new Vector2(W * 0.40f, H * 0.74f);
        var windowGlow = VipImage(window, "WindowGlow", windowSprite, VipCyan, Vector2.zero, window.sizeDelta * 1.04f);
        windowGlow.preserveAspect = false;
        var padRing = VipImage(window, "PadRing", scanSprite, VipCyan, Vector2.zero, Vector2.one * H * 0.56f);
        var scanRing = VipImage(window, "ScanRing", scanSprite, new Color(VipCyan.r, VipCyan.g, VipCyan.b, 0f), Vector2.zero, Vector2.one * H * 0.5f);
        var scanLine = VipImage(window, "ScanLine", lineSprite, new Color(VipCyan.r, VipCyan.g, VipCyan.b, 0f), Vector2.zero, new Vector2(W * 0.38f, H * 0.06f));
        scanLine.preserveAspect = false;
        // 체크 뒤 어두운 원 — 텍스처의 카드 아이콘을 가려 체크만 보이게(OST에서 어두움 = 비움)
        var checkGroup = VipImage(window, "CheckGroup", AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                                  new Color(0.02f, 0.05f, 0.1f, 0.95f), new Vector2(0f, H * 0.05f), Vector2.one * H * 0.5f);
        var cardCheck = VipImage(checkGroup.transform, "CardCheck", checkSprite, VipGreen, Vector2.zero, Vector2.one * H * 0.48f);
        checkGroup.gameObject.SetActive(false);
        var cardStatus = CreateText(window, "Status", "", font, H * 0.075f, VipCyan, new Vector2(0f, -H * 0.29f), new Vector2(W * 0.40f, H * 0.12f));
        cardStatus.textWrappingMode = TextWrappingModes.NoWrap;
        VipImage(ov.transform, "ScanBarTrack", null, new Color(VipCyan.r, VipCyan.g, VipCyan.b, 0.22f), new Vector2(0f, -H * 0.43f), new Vector2(W * 0.36f, H * 0.03f)).preserveAspect = false;
        var scanBar = VipImage(ov.transform, "ScanBar", null, VipCyan, new Vector2(0f, -H * 0.43f), new Vector2(W * 0.36f, H * 0.03f));
        scanBar.preserveAspect = false;
        scanBar.type = Image.Type.Filled;
        scanBar.fillMethod = Image.FillMethod.Horizontal;
        scanBar.fillOrigin = (int)Image.OriginHorizontal.Left;
        scanBar.fillAmount = 0f;

        var hintRoot = new GameObject("Hint").transform;
        hintRoot.SetParent(dock.transform, false);
        hintRoot.localPosition = VipHintLocal;
        hintRoot.localRotation = Quaternion.LookRotation(VipHintLocal.normalized, Vector3.up);
        hintRoot.localScale = Vector3.one * 0.5f;
        var hintCanvas = CreateWorldCanvas("HintCanvas", hintRoot, new Vector2(820f, 220f));
        var dockTitle = CreateText(hintCanvas.transform, "Title", "STEP 01 · 카드키 인증", font, 56, VipCyan, new Vector2(0f, 60f), new Vector2(800f, 80f));
        var dockHint = CreateText(hintCanvas.transform, "Body", "Beam Pro를 가슴 앞 인증 패널에 올려놓거나\n[인증] 버튼을 누르세요 ↓", font, 40, Color.white,
                                  new Vector2(0f, -40f), new Vector2(800f, 120f));

        // STEP 02 홍채·시선(EYE SCAN) — 단계 시작 때 시선 정면에 놓고 월드 고정. 가운데(홍채) = 시선 판정 중심
        var gazeRoot = new GameObject("GazeRoot").transform;
        gazeRoot.SetParent(root.transform, false);
        var gazeCanvas = CreateWorldCanvas("GazeCanvas", gazeRoot, new Vector2(760f, 520f));
        var gazeFrame = VipImage(gazeCanvas.transform, "Frame", frameSprite, VipCyan, Vector2.zero, new Vector2(760f, 507f));
        gazeFrame.preserveAspect = false;
        CreateText(gazeCanvas.transform, "Title", "EYE SCAN", font, 46, VipCyan, new Vector2(0f, 200f), new Vector2(600f, 60f));
        CreateText(gazeCanvas.transform, "Sub", "화면을 바라봐 주세요", font, 28, Color.white, new Vector2(0f, 156f), new Vector2(600f, 40f));
        VipImage(gazeCanvas.transform, "ProgressTrack", progressSprite, new Color(VipCyan.r, VipCyan.g, VipCyan.b, 0.2f), new Vector2(0f, -5f), new Vector2(290f, 290f));
        var progress = VipImage(gazeCanvas.transform, "ProgressRing", progressSprite, VipCyan, new Vector2(0f, -5f), new Vector2(290f, 290f));
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Radial360;
        progress.fillOrigin = (int)Image.Origin360.Top;
        progress.fillClockwise = true;
        progress.fillAmount = 0f;
        var iris = VipImage(gazeCanvas.transform, "Iris", irisSprite, VipCyan, new Vector2(0f, -5f), new Vector2(240f, 240f));
        var eye = VipImage(gazeCanvas.transform, "EyeIcon", eyeSprite, VipCyan, new Vector2(-265f, 20f), new Vector2(150f, 150f));
        var bars = new UnityEngine.Object[9];
        for (int i = 0; i < bars.Length; i++)
        {
            var bar = CreateBar(gazeCanvas.transform, $"Wave{i}", new Color(VipCyan.r, VipCyan.g, VipCyan.b, 0.9f), new Vector2(-345f + i * 20f, -95f), new Vector2(10f, 70f));
            bar.raycastTarget = false;
            bars[i] = bar.rectTransform;
        }
        var percent = CreateText(gazeCanvas.transform, "Percent", "0%", font, 52, VipCyan, new Vector2(265f, 22f), new Vector2(200f, 70f));
        CreateText(gazeCanvas.transform, "PercentLabel", "SCANNING", font, 24, Color.white, new Vector2(265f, -28f), new Vector2(200f, 36f));
        var gazeHint = CreateText(gazeCanvas.transform, "Hint", "STEP 02 · 가운데 홍채를 2초간 바라보세요", font, 28, Color.white, new Vector2(0f, -212f), new Vector2(700f, 44f));

        // 완료 — 초록 ACCESS GRANTED(연출안: VIP 배지 대신). 경고(참가자 확인)는 같은 화면 아래
        var grantedCanvas = CreateWorldCanvas("GrantedCanvas", gazeRoot, new Vector2(760f, 560f));
        var grantedFrame = VipImage(grantedCanvas.transform, "Frame", frameSprite, VipGreen, Vector2.zero, new Vector2(760f, 545f));
        grantedFrame.preserveAspect = false;
        var burst = VipImage(grantedCanvas.transform, "BurstRing", scanSprite, new Color(VipGreen.r, VipGreen.g, VipGreen.b, 0f), new Vector2(0f, 95f), new Vector2(240f, 240f));
        var badge = VipImage(grantedCanvas.transform, "Check", checkSprite, VipGreen, new Vector2(0f, 95f), new Vector2(200f, 200f));
        var title = CreateText(grantedCanvas.transform, "Title", "ACCESS GRANTED", font, 58, VipGreen, new Vector2(0f, -40f), new Vector2(740f, 70f));
        CreateText(grantedCanvas.transform, "Sub", "인증 완료", font, 32, Color.white, new Vector2(0f, -92f), new Vector2(740f, 46f));
        var participant = CreateText(grantedCanvas.transform, "Participant", "", font, 28, VipCyan, new Vector2(0f, -134f), new Vector2(740f, 40f));
        var warning = CreateText(grantedCanvas.transform, "Warning", "", font, 28, new Color(1f, 0.55f, 0.35f), new Vector2(0f, -205f), new Vector2(740f, 100f));
        warning.gameObject.SetActive(false);

        SetRef(intro, "followRoot", follow);
        SetRef(intro, "dockGroup", dock);
        SetRef(intro, "gazeRoot", gazeRoot);
        SetRef(intro, "gazeGroup", gazeCanvas);
        SetRef(intro, "grantedGroup", grantedCanvas);
        SetRef(intro, "scannerModel", scanner);
        SetRef(intro, "windowGlow", windowGlow);
        SetRef(intro, "padRing", padRing.rectTransform);
        SetRef(intro, "scanRing", scanRing);
        SetRef(intro, "scanLine", scanLine);
        SetRef(intro, "scanBar", scanBar);
        SetRef(intro, "cardCheck", cardCheck);
        SetRef(intro, "cardCheckGroup", checkGroup.gameObject);
        SetRef(intro, "cardStatus", cardStatus);
        SetRef(intro, "dockTitle", dockTitle);
        SetRef(intro, "dockHint", dockHint);
        SetRef(intro, "gazeTarget", iris);
        SetRef(intro, "progressRing", progress);
        SetRef(intro, "gazeFrame", gazeFrame);
        SetRef(intro, "eyeIcon", eye);
        SetRefArray(intro, "waveBars", bars);
        SetRef(intro, "gazePercent", percent);
        SetRef(intro, "gazeHint", gazeHint);
        SetRef(intro, "badge", badge.rectTransform);
        SetRef(intro, "burstRing", burst);
        SetRef(intro, "grantedFrame", grantedFrame);
        SetRef(intro, "grantedTitle", title);
        SetRef(intro, "participantText", participant);
        SetRef(intro, "warningText", warning);
        SetRef(intro, "audioSource", audio);
        SetRef(intro, "linkClip", VipClip(VipSndLink));
        SetRef(intro, "scanSuccessClip", VipClip(VipSndScan));
        SetRef(intro, "vipSuccessClip", VipClip(VipSndVip));
        SetRef(intro, "prize", prize);

        report.AppendLine($"  · VIP 인증 연출(카드 앞, 10-13 연출안): STEP 01 카드 스캐너 {face.x:F2}×{face.y:F3}m 가슴 앞 {VipPadLocal} {VipPadTiltDeg:0}° 기울임" +
                          $"{(scanner != null ? "" : "(모델 없음 — 오버레이만)")} · STEP 02 EYE SCAN 1.4m −5°(월드 고정) · ACCESS GRANTED · 효과음 3종 · 참가자 확인 PrizeLink");
        return intro;
    }

    /// <summary>
    /// 카드 스캐너 모델(Tripo FBX)을 padRoot 아래에 — 앞면(모델 +z)이 padRoot −z(눈 쪽)를 보게 y 180°, 폭 VipScannerWidth로 균일 배율,
    /// 바운즈 가운데를 원점·앞면을 z 0에. 재질은 Unlit(텍스처에 글자가 그려진 패널 — 조명과 무관하게 같은 밝기, OST에서 어두운 바탕은 투명).
    /// 모델이 없으면 null(오버레이만, face = 기본 비율).
    /// </summary>
    static Transform BuildCardScanner(Transform padRoot, out Vector2 face, StringBuilder report)
    {
        face = new Vector2(VipScannerWidth, VipScannerWidth * 0.42f);
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(CardScannerModelPath);
        if (src == null)
        {
            report.AppendLine($"  ⚠ 카드 스캐너 모델 없음({CardScannerModelPath}) — 패널 그림 없이 오버레이만");
            return null;
        }
        var mat = GetOrCreateMat("CardScanner_Unlit", "Universal Render Pipeline/Unlit");
        var tex = FindModelTexture(CardScannerModelPath);
        if (tex != null) mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);

        var holder = new GameObject("Scanner").transform;
        holder.SetParent(padRoot, false);
        var model = (GameObject)UnityEngine.Object.Instantiate(src);
        model.name = "CardScannerModel";
        model.transform.SetParent(holder, false);
        model.transform.localPosition = Vector3.zero;
        var srcRot = src.transform.localRotation;   // 임포트 루트 회전(축 변환)은 유지하고 그 위에 y 180°
        var srcScale = src.transform.localScale;
        model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * srcRot;
        model.transform.localScale = srcScale;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);

        // holder 공간 바운즈 → 폭 맞춤 배율, 가운데·앞면 정렬
        var b = LocalRendererBounds(model, holder);
        if (b.size.x < 1e-5f) { report.AppendLine("  ⚠ 카드 스캐너 바운즈 0 — 배치 생략"); return holder; }
        float s = VipScannerWidth / b.size.x;
        model.transform.localScale = srcScale * s;
        b = LocalRendererBounds(model, holder);
        model.transform.localPosition = new Vector3(-b.center.x, -b.center.y, -b.min.z);
        face = new Vector2(b.size.x, b.size.y);
        report.AppendLine($"  · 카드 스캐너: 임포트 루트 회전 {srcRot.eulerAngles} · 배율 ×{s:F3} → {b.size.x:F3}×{b.size.y:F3}×{b.size.z:F3}m, 재질 {Path.GetFileName(CardScannerMatPath)}(Unlit, 텍스처 {(tex != null ? tex.name : "없음")})");
        return holder;
    }

    /// <summary>모델 폴더(.fbm 포함)에서 basecolor 텍스처를 찾고 안드로이드 용량을 위해 최대 2048로</summary>
    static Texture2D FindModelTexture(string modelPath)
    {
        string dir = Path.GetDirectoryName(modelPath).Replace('\\', '/');
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.ToLowerInvariant().Contains("basecolor")) continue;
            if (AssetImporter.GetAtPath(p) is TextureImporter ti && ti.maxTextureSize > 2048)
            {
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        return null;
    }

    /// <summary>모든 렌더러의 메시 바운즈 8꼭짓점을 space 로컬로 모은 바운즈</summary>
    static Bounds LocalRendererBounds(GameObject go, Transform space)
    {
        bool any = false;
        var acc = new Bounds();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(c);
                if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                else acc.Encapsulate(p);
            }
        }
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var mb = smr.sharedMesh.bounds;
            var m = space.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(c);
                if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                else acc.Encapsulate(p);
            }
        }
        return acc;
    }

    /// <summary>보상 패널에 보상 카드·효과음 — 메뉴 30이 NewScene 뒤에 부른다</summary>
    static void AddVipRewardCard(GameObject rewardPanel, GameObject rewardCanvas, ClawRewardPanel rp)
    {
        var card = VipImage(rewardCanvas.transform, "RewardCard", VipSprite(VipCard), VipGold, Vector2.zero, new Vector2(900f, 600f));
        card.preserveAspect = false;
        card.transform.SetSiblingIndex(1); // 배경 다음, 글자 아래
        var audio = rewardPanel.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;
        SetRef(rp, "card", card);
        SetRef(rp, "audioSource", audio);
        SetRef(rp, "rewardClip", VipClip(VipSndReward));
    }
}
