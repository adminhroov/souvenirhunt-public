using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SouvenirHunt;

// 인형뽑기 조명 묶음(10-20 자연광 1단계). SouvenirHuntSetup partial — 메뉴 30이 호출해 ClawLighting에 연결.
//   · daylight: 코드로 만든 낮 하늘(위 파랑 → 수평선 흰빛 → 아래 따뜻한 바닥 반사) + 맑은 날 햇빛(약 5500K, 높은 각도)
//   · lebombo / warehouse: Poly Haven CC0 실사 HDRI(Assets/Lighting/HDRI, SOURCE.txt) — 가장 밝은 쪽(창·조명)을 해 방향·색으로 뽑고 나머지를 주변광으로
// 주변광 = 환경 이미지를 구면 조화(L2)로 투영(텍셀마다 AddDirectionalLight, 세기 = 입체각/π → 균일 밝기 1이면 주변광 1).
// 밝기: 평균 주변광·평균 반사를 지금 조명(studio = 메뉴 17 트라이라이트·스튜디오 반사)과 같게 맞춘다(OST는 어두우면 투명).
// 큐브맵 텍셀 → 방향: D3D 규약 + 행 순서는 낮 하늘(위가 밝음)로 자동 판별. 원본 해시가 같으면 다시 계산하지 않는다.
public static partial class SouvenirHuntSetup
{
    const string ClawLightingFolder = "Assets/Settings/ClawLighting";
    const string DaylightEnvPath = ClawLightingFolder + "/Claw_DaylightEnv.exr";
    const int LightingCalcVersion = 1;
    static readonly (string id, string path, string label)[] HdriSources =
    {
        ("lebombo", "Assets/Lighting/HDRI/lebombo_1k.hdr", "실내 자연 채광 HDRI"),
        ("warehouse", "Assets/Lighting/HDRI/empty_warehouse_01_1k.hdr", "형광등+창 HDRI"),
    };
    // studio(메뉴 17) 트라이라이트 — 밝기 맞춤 기준
    static readonly Color StudioSky = new Color(0.62f, 0.65f, 0.72f), StudioEquator = new Color(0.45f, 0.46f, 0.50f), StudioGround = new Color(0.30f, 0.29f, 0.28f);
    // 낮 하늘 해: 맑은 날 정오 무렵 햇빛(약 5500K), 체험자 뒤-왼쪽 위 62°에서 기계 앞면을 비춤
    static readonly Vector3 DaylightSunEuler = new Vector3(62f, -35f, 0f);
    static readonly Color DaylightSunColor = new Color(1f, 0.94f, 0.85f);
    const float DaylightSunIntensity = 1.05f, HdriSunIntensity = 0.95f;

    static ClawLightingPreset[] EnsureClawLightingPresets(StringBuilder report)
    {
        var list = new List<ClawLightingPreset>();
        try
        {
            EnsureFolder(ClawLightingFolder);
            // 기준(studio): 평균 주변광·평균 반사 밝기
            var studioSH = ProjectSH(TrilightRadiance);
            float targetAmbient = MeanIrradiance(studioSH);
            float targetRefl = MeanRadianceOf(d => StudioEnvDir(d));

            // 낮 하늘(코드 생성)
            var dayCube = EnsureLatLongCube(DaylightEnvPath, DaylightEnv);
            bool topToBottom = CalibrateRowOrder(dayCube, out string calib);
            var day = LoadOrCreatePreset("daylight", "코드 생성 낮 하늘");
            string dayHash = Hash128.Compute(File.ReadAllBytes(DaylightEnvPath)).ToString() + LightingCalcVersion;
            if (day.sourceHash != dayHash || day.reflection != dayCube)
            {
                var px = ReadCube(dayCube, 2, out int size);
                var sh = ProjectCubeSH(px, size, topToBottom, float.MaxValue, out _, out _);
                day.AmbientProbe = ScaleSH(sh, targetAmbient / Mathf.Max(1e-4f, MeanIrradiance(sh)));
                day.sunEuler = DaylightSunEuler; day.sunColor = DaylightSunColor; day.sunIntensity = DaylightSunIntensity;
                day.reflection = dayCube;
                day.reflectionIntensity = Mathf.Clamp(targetRefl / Mathf.Max(1e-4f, MeanRadianceCube(px, size)), 0.25f, 2.5f);
                day.sourceHash = dayHash;
                EditorUtility.SetDirty(day);
            }
            list.Add(day);

            // 실사 HDRI
            foreach (var (id, path, label) in HdriSources)
            {
                if (!File.Exists(path)) { report.AppendLine($"  ⚠ 조명 {id}: {path} 없음 — 건너뜀"); continue; }
                var cube = ImportAsEnvCube(path);
                var p = LoadOrCreatePreset(id, label);
                string hash = Hash128.Compute(File.ReadAllBytes(path)).ToString() + LightingCalcVersion + (topToBottom ? "t" : "b");
                if (p.sourceHash != hash || p.reflection != cube)
                {
                    var px = ReadCube(cube, 2, out int size);
                    // 가장 밝은 0.7% 텍셀 = 해(창·조명) → 방향광, 나머지 → 주변광
                    var lums = px.Select(c => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f).OrderBy(x => x).ToArray();
                    float thr = lums[Mathf.Clamp((int)(lums.Length * 0.993f), 0, lums.Length - 1)];
                    var sh = ProjectCubeSH(px, size, topToBottom, thr, out Vector3 sunDir, out Color sunCol);
                    p.AmbientProbe = ScaleSH(sh, targetAmbient / Mathf.Max(1e-4f, MeanIrradiance(sh)));
                    // 해는 아래서 비추지 않게(최소 20°) — 방향광 forward = 해에서 장면으로
                    var flat = new Vector3(sunDir.x, 0f, sunDir.z);
                    flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                    float el = Mathf.Max(20f, Mathf.Asin(Mathf.Clamp(sunDir.y, -1f, 1f)) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    var toSun = flat * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el);
                    p.sunEuler = Quaternion.LookRotation(-toSun).eulerAngles;
                    float mx = Mathf.Max(sunCol.r, Mathf.Max(sunCol.g, sunCol.b), 1e-4f);
                    p.sunColor = Color.Lerp(Color.white, new Color(sunCol.r / mx, sunCol.g / mx, sunCol.b / mx), 0.7f);
                    p.sunIntensity = HdriSunIntensity;
                    p.reflection = cube;
                    p.reflectionIntensity = Mathf.Clamp(targetRefl / Mathf.Max(1e-4f, MeanRadianceCube(px, size)), 0.25f, 2.5f);
                    p.sourceHash = hash;
                    EditorUtility.SetDirty(p);
                }
                list.Add(p);
            }
            AssetDatabase.SaveAssets();
            report.AppendLine($"  · 조명 묶음 {list.Count}개(+ studio = 씬 시작 값): " + string.Join(" · ", list.Select(p =>
                $"{p.id} 해 {p.sunEuler.x:F0}°/{p.sunEuler.y:F0}° 색 ({p.sunColor.r:F2},{p.sunColor.g:F2},{p.sunColor.b:F2}) 반사 ×{p.reflectionIntensity:F2}")) + $" — {calib}");
        }
        catch (Exception e)
        {
            report.AppendLine("  ⚠ 조명 묶음 생성 실패(지금 조명만): " + e.Message);
            Debug.LogWarning("[SouvenirHuntSetup] 조명 묶음 생성 실패: " + e);
        }
        return list.ToArray();
    }

    static ClawLightingPreset LoadOrCreatePreset(string id, string label)
    {
        string path = $"{ClawLightingFolder}/Light_{id}.asset";
        var p = AssetDatabase.LoadAssetAtPath<ClawLightingPreset>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<ClawLightingPreset>();
            AssetDatabase.CreateAsset(p, path);
        }
        p.id = id;
        p.label = label;
        return p;
    }

    // ── 환경 함수 ──
    static Color TrilightRadiance(Vector3 d) =>
        d.y >= 0f ? Color.Lerp(StudioEquator, StudioSky, d.y) : Color.Lerp(StudioEquator, StudioGround, -d.y);

    static Color StudioEnvDir(Vector3 d) =>
        StudioEnv(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg);

    /// <summary>낮 하늘: 위 파랑 → 수평선 흰빛 → 아래 따뜻한 바닥 반사(검정 금지 — 아랫면 반사가 OST에서 구멍)</summary>
    static Color DaylightEnv(float az, float el)
    {
        var zenith = new Color(0.36f, 0.52f, 0.90f);
        var horizon = new Color(0.82f, 0.88f, 0.95f);
        var ground = new Color(0.40f, 0.37f, 0.33f);
        Color c = el >= 0f ? Color.Lerp(horizon, zenith, Mathf.Pow(el / 90f, 0.7f))
                           : Color.Lerp(horizon * 0.9f, ground, Mathf.Pow(-el / 90f, 0.5f));
        c.a = 1f;
        return c;
    }

    // ── 큐브맵 만들기·읽기 ──
    static Cubemap EnsureLatLongCube(string path, Func<float, float, Color> env)
    {
        const int W = 1024, H = 512;
        var tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            float el = ((y + 0.5f) / H - 0.5f) * 180f;
            for (int x = 0; x < W; x++) px[y * W + x] = env(((x + 0.5f) / W - 0.5f) * 360f, el);
        }
        tex.SetPixels(px);
        tex.Apply(false);
        var bytes = tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
        UnityEngine.Object.DestroyImmediate(tex);
        string full = Path.GetFullPath(path);
        if (!File.Exists(full) || !File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes))
        {
            File.WriteAllBytes(full, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        return ImportAsEnvCube(path);
    }

    /// <summary>위도·경도 이미지 → 큐브맵(원통 매핑·스페큘러 컨볼루션·256) — 스튜디오 반사와 같은 설정</summary>
    static Cubemap ImportAsEnvCube(string path)
    {
        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            bool dirty = s.textureShape != TextureImporterShape.TextureCube || s.generateCubemap != TextureImporterGenerateCubemap.Cylindrical ||
                         s.cubemapConvolution != TextureImporterCubemapConvolution.Specular || s.sRGBTexture || !s.mipmapEnabled || ti.maxTextureSize != 256 || ti.isReadable;
            if (dirty)
            {
                s.textureShape = TextureImporterShape.TextureCube;
                s.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                s.cubemapConvolution = TextureImporterCubemapConvolution.Specular;
                s.sRGBTexture = false;
                s.mipmapEnabled = true;
                s.readable = false;
                ti.SetTextureSettings(s);
                ti.maxTextureSize = 256;
                ti.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
    }

    /// <summary>큐브맵 한 밉의 6면 텍셀(면 순서 +X −X +Y −Y +Z −Z) — 잠깐 읽기 가능으로 바꿨다가 되돌린다(빌드 메모리에 사본이 남지 않게)</summary>
    static Color[] ReadCube(Cubemap cube, int mip, out int size)
    {
        string path = AssetDatabase.GetAssetPath(cube);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.isReadable = true;
        ti.SaveAndReimport();
        try
        {
            cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            mip = Mathf.Clamp(mip, 0, cube.mipmapCount - 1);
            size = Mathf.Max(1, cube.width >> mip);
            var all = new Color[size * size * 6];
            for (int f = 0; f < 6; f++)
            {
                var face = cube.GetPixels((CubemapFace)f, mip);
                Array.Copy(face, 0, all, f * size * size, size * size);
            }
            return all;
        }
        finally
        {
            ti.isReadable = false;
            ti.SaveAndReimport();
        }
    }

    static Vector3 CubeDir(int face, float u, float v)
    {
        switch (face)
        {
            case 0: return new Vector3(1f, -v, -u).normalized;   // +X
            case 1: return new Vector3(-1f, -v, u).normalized;   // −X
            case 2: return new Vector3(u, 1f, v).normalized;     // +Y
            case 3: return new Vector3(u, -1f, -v).normalized;   // −Y
            case 4: return new Vector3(u, -v, 1f).normalized;    // +Z
            default: return new Vector3(-u, -v, -1f).normalized; // −Z
        }
    }

    /// <summary>큐브 텍셀 → 구면 조화. 밝기 thr 이상 텍셀은 빼고(해) 그 가중 평균 방향·색을 돌려준다</summary>
    static SphericalHarmonicsL2 ProjectCubeSH(Color[] px, int size, bool topToBottom, float thr, out Vector3 sunDir, out Color sunCol)
    {
        var sh = new SphericalHarmonicsL2();
        Vector3 sd = Vector3.zero; Color sc = Color.black; float sw = 0f;
        float texel = 2f / size;
        for (int f = 0; f < 6; f++)
            for (int row = 0; row < size; row++)
            {
                float vRaw = (row + 0.5f) * texel - 1f;
                float v = topToBottom ? vRaw : -vRaw;
                for (int col = 0; col < size; col++)
                {
                    float u = (col + 0.5f) * texel - 1f;
                    var c = px[f * size * size + row * size + col];
                    float dw = texel * texel / Mathf.Pow(1f + u * u + v * v, 1.5f);
                    var d = CubeDir(f, u, v);
                    float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    if (lum >= thr)
                    {
                        float w = lum * dw;
                        sd += d * w; sc += c * w; sw += w;
                        continue;
                    }
                    sh.AddDirectionalLight(d, new Color(c.r, c.g, c.b), dw / Mathf.PI);
                }
            }
        sunDir = sw > 0f ? (sd / sw).normalized : Vector3.up;
        sunCol = sw > 0f ? sc / sw : Color.white;
        return sh;
    }

    /// <summary>해석 함수 → 구면 조화(피보나치 구 2048점)</summary>
    static SphericalHarmonicsL2 ProjectSH(Func<Vector3, Color> radiance)
    {
        var sh = new SphericalHarmonicsL2();
        const int N = 2048;
        float dw = 4f * Mathf.PI / N;
        foreach (var d in Fibonacci(N)) { var c = radiance(d); sh.AddDirectionalLight(d, new Color(c.r, c.g, c.b), dw / Mathf.PI); }
        return sh;
    }

    static IEnumerable<Vector3> Fibonacci(int n)
    {
        float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / n, r = Mathf.Sqrt(1f - y * y), t = ga * i;
            yield return new Vector3(Mathf.Cos(t) * r, y, Mathf.Sin(t) * r);
        }
    }

    static float MeanIrradiance(SphericalHarmonicsL2 sh)
    {
        var dirs = Fibonacci(64).ToArray();
        var res = new Color[dirs.Length];
        sh.Evaluate(dirs, res);
        return res.Average(c => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f);
    }

    static SphericalHarmonicsL2 ScaleSH(SphericalHarmonicsL2 sh, float k)
    {
        for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) sh[c, i] *= k;
        return sh;
    }

    static float MeanRadianceOf(Func<Vector3, Color> radiance) =>
        Fibonacci(2048).Average(d => { var c = radiance(d); return c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f; });

    static float MeanRadianceCube(Color[] px, int size)
    {
        double sum = 0, wsum = 0;
        float texel = 2f / size;
        for (int f = 0; f < 6; f++)
            for (int row = 0; row < size; row++)
                for (int col = 0; col < size; col++)
                {
                    float u = (col + 0.5f) * texel - 1f, v = (row + 0.5f) * texel - 1f;
                    float dw = texel * texel / Mathf.Pow(1f + u * u + v * v, 1.5f);
                    var c = px[f * size * size + row * size + col];
                    sum += (c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f) * dw;
                    wsum += dw;
                }
        return (float)(sum / Math.Max(1e-9, wsum));
    }

    /// <summary>GetPixels 행 순서 판별: 낮 하늘은 위(하늘)가 아래(바닥)보다 확실히 밝다 → 두 가정 중 위가 밝게 나오는 쪽</summary>
    static bool CalibrateRowOrder(Cubemap dayCube, out string note)
    {
        var px = ReadCube(dayCube, 3, out int size);
        float Score(bool ttb)
        {
            var sh = ProjectCubeSH(px, size, ttb, float.MaxValue, out _, out _);
            var res = new Color[2];
            sh.Evaluate(new[] { Vector3.up, Vector3.down }, res);
            return (res[0].b - res[1].b) + (res[0].grayscale - res[1].grayscale);
        }
        float a = Score(true), b = Score(false);
        bool ttb = a >= b;
        note = $"큐브 행 순서 {(ttb ? "위→아래" : "아래→위")}(위−아래 밝기 차 {Mathf.Max(a, b):F2} vs {Mathf.Min(a, b):F2})";
        if (Mathf.Max(a, b) <= 0f) Debug.LogWarning("[SouvenirHuntSetup] 조명: 낮 하늘에서 위가 더 밝게 나오는 행 순서를 못 찾음 — 주변광 위아래가 뒤집혔을 수 있음");
        return ttb;
    }
}
