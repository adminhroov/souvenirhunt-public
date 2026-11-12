using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SouvenirHunt
{
    /// <summary>
    /// 부스·기계 렌더 등급(10-15 성능). config clawBooth = full | lite | off, clawMachine = full | lite.
    /// 실기기 측정(vc41, 기계가 보이는 Guide): 부스(Model1 — URP Lit PBR·노멀맵·구멍·유리, 화면을 넓게 덮음)가 GPU 약 24ms, 기계 약 8ms.
    ///   · lite = 원본 URP Lit 재질을 가벼운 셰이더(SouvenirHunt/LiteLit: 정점 조명 + 텍스처 + 발광)로 바꾼 사본.
    ///     텍스처·색·발광·구멍(알파 클립)·반투명·컬링은 원본에서 가져오고 노멀맵·금속성·반사·스페큘러는 버린다.
    ///   · off = 부스 렌더러만 끔(오브젝트·등장 연출·재정렬은 그대로). 기계는 게임이라 off가 없다.
    /// URP Lit이 아닌 재질(네온·바닥 그리드·아이템 셰이더)과 그런 재질만 가진 렌더러는 건드리지 않는다 — 기계·부스 모델이 바뀌어도 그대로 동작.
    /// 리셋마다 config 값으로 다시 적용(바뀐 게 없으면 아무 것도 안 함). 예외를 던지지 않는다.
    /// </summary>
    public class ClawRenderTier : MonoBehaviour
    {
        public static ClawRenderTier Instance { get; private set; }

        [SerializeField] Shader liteShader;       // 에디터가 연결 — 씬이 참조해야 빌드에 셰이더가 들어간다
        [SerializeField] GameObject[] boothRoots;
        [SerializeField] GameObject[] machineRoots;

        public string BoothTier { get; private set; } = "full";
        public string MachineTier { get; private set; } = "full";
        /// <summary>마지막 적용 결과 1줄(운영자 패널·로그)</summary>
        public string Summary { get; private set; } = "부스 full · 기계 full";

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        static readonly int Surface = Shader.PropertyToID("_Surface");
        static readonly int SrcBlend = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlend = Shader.PropertyToID("_DstBlend");
        static readonly int ZWrite = Shader.PropertyToID("_ZWrite");
        static readonly int Cull = Shader.PropertyToID("_Cull");

        class Slot { public Renderer r; public Material[] original; public bool enabled; }

        List<Slot> boothSlots, machineSlots;
        readonly Dictionary<Material, Material> liteOf = new();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            foreach (var m in liteOf.Values) if (m != null) Destroy(m);
            liteOf.Clear();
        }

        public static string NormalizeBooth(string v)
        {
            v = (v ?? "").Trim().ToLowerInvariant();
            return v == "lite" || v == "off" ? v : "full";
        }

        public static string NormalizeMachine(string v) => (v ?? "").Trim().ToLowerInvariant() == "lite" ? "lite" : "full";

        public void Apply(string booth, string machine)
        {
            booth = NormalizeBooth(booth);
            machine = NormalizeMachine(machine);
            if (boothSlots != null && booth == BoothTier && machine == MachineTier) return;
            try
            {
                boothSlots ??= Capture(boothRoots);
                machineSlots ??= Capture(machineRoots);
                int lb = Set(boothSlots, booth), lm = Set(machineSlots, machine);
                BoothTier = booth;
                MachineTier = machine;
                Summary = $"부스 {booth} · 기계 {machine}";
                Debug.Log($"[RenderTier] {Summary} — 부스 렌더러 {boothSlots.Count}개(가벼운 재질 {lb}), 기계 렌더러 {machineSlots.Count}개(가벼운 재질 {lm})" +
                          (liteShader == null ? " ⚠ LiteLit 셰이더 없음 → lite는 원본 유지" : ""));
            }
            catch (Exception e)
            {
                Summary = "렌더 등급 적용 실패(원본 유지)";
                Debug.LogWarning("[RenderTier] 적용 실패(무시): " + e.Message);
            }
        }

        /// <summary>URP Lit 재질을 하나라도 가진 메시 렌더러의 원본 재질·표시 상태를 기억(처음 1번)</summary>
        static List<Slot> Capture(GameObject[] roots)
        {
            var list = new List<Slot>();
            if (roots == null) return list;
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    if (Array.Exists(mats, IsUrpLit)) list.Add(new Slot { r = r, original = mats, enabled = r.enabled });
                }
            }
            return list;
        }

        int Set(List<Slot> slots, string tier)
        {
            int lite = 0;
            foreach (var s in slots)
            {
                if (s.r == null) continue;
                if (tier == "lite" && liteShader != null)
                {
                    var mats = new Material[s.original.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        mats[i] = Lite(s.original[i]);
                        if (mats[i] != s.original[i]) lite++;
                    }
                    s.r.sharedMaterials = mats;
                }
                else s.r.sharedMaterials = s.original;
                s.r.enabled = tier != "off" && s.enabled;
            }
            return lite;
        }

        static bool IsUrpLit(Material m)
        {
            if (m == null || m.shader == null) return false;
            string n = m.shader.name;
            return n == "Universal Render Pipeline/Lit" || n == "Universal Render Pipeline/Simple Lit";
        }

        Material Lite(Material src)
        {
            if (!IsUrpLit(src)) return src;
            if (liteOf.TryGetValue(src, out var m)) return m;
            m = new Material(liteShader) { name = src.name + " (Lite)" };
            if (src.HasProperty(BaseMap))
            {
                m.SetTexture(BaseMap, src.GetTexture(BaseMap));
                m.SetTextureScale(BaseMap, src.GetTextureScale(BaseMap));
                m.SetTextureOffset(BaseMap, src.GetTextureOffset(BaseMap));
            }
            if (src.HasProperty(BaseColor)) m.SetColor(BaseColor, src.GetColor(BaseColor));
            bool emission = src.IsKeywordEnabled("_EMISSION") && src.HasProperty(EmissionColor);
            m.SetColor(EmissionColor, emission ? src.GetColor(EmissionColor) : Color.black);
            if (emission && src.HasProperty(EmissionMap) && src.GetTexture(EmissionMap) != null) m.SetTexture(EmissionMap, src.GetTexture(EmissionMap));
            if (src.HasProperty(Cull)) m.SetFloat(Cull, src.GetFloat(Cull));
            bool transparent = src.HasProperty(Surface) && src.GetFloat(Surface) > 0.5f;
            if (transparent)
            {
                m.SetFloat(Surface, 1f);
                m.SetFloat(SrcBlend, (float)BlendMode.SrcAlpha);
                m.SetFloat(DstBlend, (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat(ZWrite, 0f);
                m.SetOverrideTag("RenderType", "Transparent");
            }
            else if (src.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                m.EnableKeyword("_ALPHATEST_ON");
                if (src.HasProperty(Cutoff)) m.SetFloat(Cutoff, src.GetFloat(Cutoff));
            }
            m.renderQueue = src.renderQueue;
            liteOf[src] = m;
            return m;
        }
    }
}
