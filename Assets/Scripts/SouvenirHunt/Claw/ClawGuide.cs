using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// "지금 잡기를 누르면 무엇이 잡히는지"를 보여주는 가이드 — 라이트 추가 없음(실시간 광원 1개 예산):
    /// 헤드 하단→대상 윗면 가산(additive) 원뿔 + 대상 둘레 링(LineRenderer) + 대상 아이템 은은한 발광(MPB).
    /// 10-15 미션형: 집게가 실제로 잡을 아이템(ClawBasket.PickUnder)과 가이드가 가리키는 아이템이 같다(헤드가 같은 함수로 고름).
    /// 종류(그릇·캡슐·폭탄)는 가이드가 구분하지 않는다 — 생김새로 구분. 흡착 연출용 도넛 레이저(대상 폭 → 0.02, 알파 1→0)도 담당.
    /// 비주얼 2차(10-15): 원뿔은 스캔 줄무늬가 흐르는 셰이더(SouvenirHunt/ClawGuideScan), 링은 숨 쉬듯 밝기 변화 + 1.2초마다 바깥으로 퍼지는 핑 링.
    /// 10-19: 집게발이 실제로 여닫히게 되어(ClawFingers) 메뉴 30은 원뿔·도넛 레이저를 배선하지 않는다 — 대상 링·핑·대상 발광만. 각 요소는 없으면 건너뜀.
    /// 10-22 물리 집게로 여러 개를 쥐게 되어 "가리킨 1개만 금색"은 논리가 안 맞음 → 사용자 선택(3안 → 원뿔·원기둥 → 은은한 스포트라이트 비교 렌더): **S2 흰색** =
    ///   집게 몸통에서 대상 허리까지 아주 옅은 원뿔(줄무늬 없음) + 그 아래 잡기 범위를 비추는 가짜 스포트라이트(ClawItemLit 전역 값 — 실시간 광원 추가 없음:
    ///   원 안 표면을 색 곱으로 살짝 밝히고 둘레를 살짝 어둡게). 상품 발광 없음 — 단 가리킨 것이 폭탄이면(닿으면 폭발) 은은한 빨강.
    ///   예전 바닥 표시판·빛 선·링은 켜지 않는다(배선은 남김). 색 = config clawGuideColor(기본 white)
    /// </summary>
    public class ClawGuide : MonoBehaviour
    {
        [SerializeField] Transform cone;
        [SerializeField] LineRenderer ring;
        [SerializeField] LineRenderer laser;
        [SerializeField] LineRenderer ping;            // 비주얼 2차: 링에서 바깥으로 퍼지는 고리(없으면 생략)
        // 10-19 조준 표시(사용자 선택 = 시안 3 스포트라이트 + 골드): 대상 밑 바닥 표시판(SouvenirHunt/ClawGuideMarker)
        // + 집게→대상 가는 선(SouvenirHunt/ClawGuideBeam, 떨어지는 빛 방울). 색은 config clawGuideColor(MPB)
        [SerializeField] Renderer marker;              // 눕힌 사각 판(1×1, 로컬 XY) — 대상 밑면에 링 반지름 × Extent 크기로
        [SerializeField] LineRenderer beam;
        [SerializeField] float markerExtent = 1.9f;    // 판 반폭 = 링 반지름 × 이 값(바깥 눈금·물결 자리)
        [SerializeField] float markerPad = 0.015f;     // 링 반지름에 더하는 여유(대상 실루엣보다 확실히 바깥)
        /// <summary>
        /// 10-20 조준 표시 원판 높이(대상 밑면 0 ~ 윗면 1). 바닥에 한 층이던 때는 밑면(바닥을 비추는 스포트라이트)이었는데,
        /// 더미가 높아지면 밑면이 옆 캡슐들 사이에 묻혀 거의 안 보인다 → 사용자 선택(밑면·허리·윗면 비교 렌더): 허리 — 대상을 두른 고리로 보인다
        /// </summary>
        public float MarkerHeight01 { get; set; } = 0.5f;
        [SerializeField] float pingPeriod = 1.2f;
        [SerializeField] Color laserColor = new Color(0.3f, 0.85f, 0.9f, 1f);
        [Tooltip("아이템 규격 — 링·레이저 기본 크기. 미배선이면 Default")]
        [SerializeField] ClawItemSpec spec;
        [Tooltip("10-22 조준 원뿔 재질(SouvenirHunt/ClawGuideScan — 메뉴 30이 배선). 없으면 원뿔 없이 스포트라이트만")]
        [SerializeField] Material spotConeMaterial;
        [Tooltip("스포트라이트 잡기 범위 반지름 = 헤드 반지름 × 이 값(오므린 발이 감싸는 범위 — 렌더 비교 0.17m)")]
        [SerializeField] float spotRangeFactor = 0.87f;
        [SerializeField] float spotSoftness = 0.6f;
        [Tooltip("둘레(범위 1~2.2배)를 어둡게 — 빛 웅덩이 대비")]
        [SerializeField] float spotSurroundDim = 0.22f;
        [Tooltip("원뿔 밝기(가이드 색 × 이 값)")]
        [SerializeField] float coneIntensity = 0.55f;
        [SerializeField] float spotFadeSpeed = 7f;   // 1/초 — 약 0.15초에 켜지고 꺼짐

        const int Segs = 40;
        readonly Vector3[] laserPts = new Vector3[Segs];
        ClawItem current;
        float laserRadius = 0.08f;
        Color ringBase = new Color(0.3f, 0.85f, 0.9f, 1f);

        public bool Showing => spotTarget > 0f;
        /// <summary>스포트라이트 지금 세기(0~1 — 서서히 켜지고 꺼짐). 셀프테스트용</summary>
        public float SpotLevel => spotW;
        /// <summary>지금 발광 중인 대상(셀프테스트: 가이드 대상 = 실제로 잡히는 것)</summary>
        public ClawItem Current => current;

        public static ClawGuide Instance { get; private set; }
        static readonly int ColorId = Shader.PropertyToID("_Color");
        Color guideColor = new Color(1f, 1f, 1f);
        // 가짜 스포트라이트·원뿔 상태
        static readonly int SpotPosId = Shader.PropertyToID("_ClawSpotPos");
        static readonly int SpotParamsId = Shader.PropertyToID("_ClawSpotParams");
        static readonly int SpotColorId = Shader.PropertyToID("_ClawSpotColor");
        static readonly int BaseId = Shader.PropertyToID("_Base");
        static readonly int StripeId = Shader.PropertyToID("_Stripe");
        static readonly Color BombGlow = new Color(1.8f, 0.22f, 0.12f, 1f);
        float spotTarget, spotW;
        Vector3 spotApex;
        float spotPlaneY, spotRadius;
        MeshRenderer coneRenderer;
        Transform coneT;
        /// <summary>표시·선·대상 발광 색(config clawGuideColor). 대상 발광(ClawItem.GlowColor)은 이 색 × 1.6(HDR 가장자리 빛)</summary>
        public Color GuideColor
        {
            get => guideColor;
            set { guideColor = value; ClawItem.GlowColor = new Color(value.r * 1.6f, value.g * 1.6f, value.b * 1.6f, 1f); }
        }

        /// <summary>색 이름(cyan·gold·pink·lime·white-cyan) 또는 #RRGGBB</summary>
        public static bool TryParseColor(string v, out Color c)
        {
            switch ((v ?? "").Trim().ToLowerInvariant())
            {
                case "white": c = new Color(1f, 1f, 1f); return true;
                case "cyan": c = new Color(0f, 0.85f, 1f); return true;
                case "white-cyan": case "whitecyan": c = new Color(0.6f, 1f, 1f); return true;
                case "gold": c = new Color(1f, 0.7f, 0.08f); return true;
                case "pink": c = new Color(1f, 0.15f, 0.6f); return true;
                case "lime": c = new Color(0.55f, 1f, 0.1f); return true;
            }
            return ColorUtility.TryParseHtmlString((v ?? "").Trim(), out c);
        }
        static readonly int ExtentId = Shader.PropertyToID("_Extent");
        static readonly int LengthId = Shader.PropertyToID("_Length");
        MaterialPropertyBlock mpb;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Awake()
        {
            Instance = this;
            mpb = new MaterialPropertyBlock();
            if (marker != null) marker.enabled = false;
            if (beam != null)
            {
                beam.useWorldSpace = true;
                beam.positionCount = 2;
                beam.textureMode = LineTextureMode.Stretch;
                beam.enabled = false;
            }
            if (ring != null)
            {
                ring.useWorldSpace = false;
                ring.loop = true;
                ring.positionCount = Segs;
                for (int i = 0; i < Segs; i++)
                {
                    float a = i / (float)Segs * Mathf.PI * 2f;
                    ring.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a))); // 반지름 1 — transform 스케일로 맞춤
                }
                ring.enabled = false;
                ringBase = ring.startColor;
            }
            if (ping != null)
            {
                ping.useWorldSpace = false;
                ping.loop = true;
                ping.positionCount = Segs;
                for (int i = 0; i < Segs; i++)
                {
                    float a = i / (float)Segs * Mathf.PI * 2f;
                    ping.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                }
                ping.enabled = false;
            }
            if (laser != null)
            {
                laser.useWorldSpace = true;
                laser.loop = true;
                laser.positionCount = Segs;
                laser.enabled = false;
            }
            if (cone != null) cone.gameObject.SetActive(false);
            BuildSpotCone();
            Shader.SetGlobalVector(SpotPosId, Vector4.zero);
        }

        void OnDisable() => Shader.SetGlobalVector(SpotPosId, Vector4.zero);

        /// <summary>조준 원뿔(위 반지름 0.18 → 아래 1, 높이 1 — transform 스케일로 맞춤). UV y: 0 = 위(집게), 1 = 아래(대상)</summary>
        void BuildSpotCone()
        {
            if (spotConeMaterial == null) return;
            const int segs = 32;
            const float top = 0.18f;
            var v = new Vector3[(segs + 1) * 2]; var uv = new Vector2[(segs + 1) * 2]; var t = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float a = i / (float)segs * Mathf.PI * 2f, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i] = new Vector3(c * top, 0f, s * top); uv[i] = new Vector2(i / (float)segs, 0f);
                v[segs + 1 + i] = new Vector3(c, -1f, s); uv[segs + 1 + i] = new Vector2(i / (float)segs, 1f);
            }
            for (int i = 0; i < segs; i++)
            {
                int k = i * 6;
                t[k] = i; t[k + 1] = segs + 1 + i; t[k + 2] = segs + 2 + i;
                t[k + 3] = i; t[k + 4] = segs + 2 + i; t[k + 5] = i + 1;
            }
            var mesh = new Mesh { name = "ClawSpotCone" };
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = t;
            mesh.RecalculateBounds();
            var go = new GameObject("SpotCone") { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            coneRenderer = go.AddComponent<MeshRenderer>();
            coneRenderer.sharedMaterial = spotConeMaterial;
            coneRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coneRenderer.receiveShadows = false;
            coneRenderer.enabled = false;
            coneT = go.transform;
        }

        /// <summary>
        /// S2 흰색 조준: 꼭짓점 = 집게 몸통 가운데(headCenter), 기준 높이 = 대상 허리(없으면 아래 표면), 그 높이에서 잡기 범위 반지름.
        /// 대상이 폭탄이면 그 폭탄만 은은한 빨강(닿으면 폭발 — 가리킨 것 = 일어날 일). 다른 것은 발광 없음(빛을 받을 뿐)
        /// </summary>
        public void ShowSpot(ClawItem item, Vector3 headCenter, float planeY)
        {
            spotApex = headCenter;
            spotPlaneY = Mathf.Min(planeY, headCenter.y - 0.02f);
            spotRadius = ClawLayout.HeadRadius * spotRangeFactor;
            spotTarget = 1f;
            if (marker != null && marker.enabled) marker.enabled = false;
            if (beam != null && beam.enabled) beam.enabled = false;
            if (ring != null && ring.enabled) ring.enabled = false;
            if (ping != null && ping.enabled) ping.enabled = false;
            if (!ReferenceEquals(current, item))
            {
                if (current != null) current.SetGlow(false);
                current = item;
                if (current != null && current.ItemKind == ClawItem.Kind.Bomb)
                {
                    var keep = ClawItem.GlowColor;
                    ClawItem.GlowColor = BombGlow;
                    current.SetGlow(true);
                    ClawItem.GlowColor = keep;
                }
            }
            ApplySpot();
        }

        void LateUpdate()
        {
            float target = spotTarget;
            if (Mathf.Approximately(spotW, target)) return;
            spotW = Mathf.MoveTowards(spotW, target, spotFadeSpeed * Time.deltaTime);
            ApplySpot();
        }

        void ApplySpot()
        {
            if (spotW <= 0.001f)
            {
                Shader.SetGlobalVector(SpotPosId, Vector4.zero);
                if (coneRenderer != null && coneRenderer.enabled) coneRenderer.enabled = false;
                return;
            }
            var c = guideColor;
            // 색 곱: 흰색 1.3배(밝게) — 색이 있으면 그 색으로 물듦(0.9 + 0.4 × 색)
            Shader.SetGlobalVector(SpotPosId, new Vector4(spotApex.x, spotApex.y, spotApex.z, spotW));
            Shader.SetGlobalVector(SpotParamsId, new Vector4(spotRadius, spotPlaneY, spotSoftness, 1f));
            Shader.SetGlobalColor(SpotColorId, new Color(0.9f + 0.4f * c.r, 0.9f + 0.4f * c.g, 0.9f + 0.4f * c.b, spotSurroundDim));
            if (coneRenderer != null)
            {
                float h = Mathf.Max(0.01f, spotApex.y - spotPlaneY);
                coneT.SetPositionAndRotation(spotApex, transform.parent != null ? transform.parent.rotation : Quaternion.identity);
                coneT.localScale = new Vector3(spotRadius, h, spotRadius);
                coneRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(ColorId, c * (coneIntensity * spotW));
                mpb.SetFloat(BaseId, 0.10f);
                mpb.SetFloat(StripeId, 0f);
                coneRenderer.SetPropertyBlock(mpb);
                if (!coneRenderer.enabled) coneRenderer.enabled = true;
            }
        }

        /// <summary>매 프레임(조준 중·헤드 유휴) — 원뿔·링·발광을 대상 아이템에 맞춘다. item = null이면 바닥 빈자리(링만).</summary>
        public void Show(ClawItem item, Vector3 ringCenter, float ringRadius, Vector3 headBottom)
        {
            ringRadius = Mathf.Max(0.02f, ringRadius);
            if (cone != null)
            {
                cone.position = headBottom;
                cone.rotation = Quaternion.identity;
                float h = Mathf.Max(0.01f, headBottom.y - ringCenter.y);
                cone.localScale = new Vector3(ringRadius, h, ringRadius); // 원뿔 메시: 꼭지 y=0, 밑면 y=−1 반지름 1
                if (!cone.gameObject.activeSelf) cone.gameObject.SetActive(true);
            }
            if (ring != null)
            {
                ring.transform.position = ringCenter + Vector3.up * 0.006f;
                ring.transform.rotation = Quaternion.identity;
                ring.transform.localScale = new Vector3(ringRadius, 1f, ringRadius);
                if (!ring.enabled) ring.enabled = true;
                // 숨 쉬듯 밝기(가산 재질 — 알파 = 밝기)
                var rc = ringBase;
                rc.a = ringBase.a * (0.7f + 0.3f * Mathf.Sin(Time.time * 4f));
                ring.startColor = rc;
                ring.endColor = rc;
            }
            if (ping != null)
            {
                float k = Mathf.Repeat(Time.time / Mathf.Max(0.2f, pingPeriod), 1f);
                ping.transform.position = ringCenter + Vector3.up * 0.006f;
                ping.transform.rotation = Quaternion.identity;
                float r = ringRadius * (1f + 0.8f * k);
                ping.transform.localScale = new Vector3(r, 1f, r);
                var pc = ringBase;
                pc.a = ringBase.a * 0.8f * (1f - k) * (1f - k);
                ping.startColor = pc;
                ping.endColor = pc;
                if (!ping.enabled) ping.enabled = true;
            }
            if (marker != null)
            {
                // 대상 밑면~윗면 사이 MarkerHeight01 높이(10-20 허리) + 2mm — 대상을 두른 빛 고리. 빈자리면 링 높이
                float bottom = item != null && item.Visual != null ? item.Visual.bounds.min.y : ringCenter.y;
                float topY = item != null && item.Visual != null ? item.Visual.bounds.max.y : ringCenter.y;
                var mt = marker.transform;
                mt.position = new Vector3(ringCenter.x, Mathf.Lerp(bottom, topY, Mathf.Clamp01(MarkerHeight01)) + 0.002f, ringCenter.z);
                mt.rotation = Quaternion.Euler(90f, 0f, 0f);
                float size = (ringRadius + markerPad) * markerExtent * 2f;
                mt.localScale = new Vector3(size, size, 1f);
                marker.GetPropertyBlock(mpb);
                mpb.SetFloat(ExtentId, markerExtent);
                mpb.SetColor(ColorId, guideColor);
                marker.SetPropertyBlock(mpb);
                if (!marker.enabled) marker.enabled = true;
            }
            if (beam != null)
            {
                // 집게 하단에서 곧게 아래로(집게는 수직으로만 내려간다) → 대상 윗면 높이 1.5cm 위까지.
                // 대상이 잡기 반지름 안에서 옆에 있어도 선은 기울이지 않는다(10-19 사용자 "옆 물체가 잡힐 때 선이 삐뚤어지게 쏴짐") — 대상은 바닥 표시·발광으로
                var end = new Vector3(headBottom.x, ringCenter.y + 0.015f, headBottom.z);
                beam.SetPosition(0, headBottom);
                beam.SetPosition(1, end);
                beam.GetPropertyBlock(mpb);
                mpb.SetColor(ColorId, guideColor);
                mpb.SetFloat(LengthId, Mathf.Max(0.01f, Vector3.Distance(headBottom, end)));
                beam.SetPropertyBlock(mpb);
                bool show = headBottom.y - end.y > 0.03f;
                if (beam.enabled != show) beam.enabled = show;
            }
            if (!ReferenceEquals(current, item))
            {
                if (current != null) current.SetGlow(false);
                current = item;
                if (current != null) current.SetGlow(true);
            }
        }

        public void Hide()
        {
            spotTarget = 0f; // 스포트라이트·원뿔은 LateUpdate에서 서서히 꺼짐
            if (cone != null && cone.gameObject.activeSelf) cone.gameObject.SetActive(false);
            if (ring != null && ring.enabled) ring.enabled = false;
            if (ping != null && ping.enabled) ping.enabled = false;
            if (marker != null && marker.enabled) marker.enabled = false;
            if (beam != null && beam.enabled) beam.enabled = false;
            if (current != null) { current.SetGlow(false); current = null; }
        }

        public void BeginLaser(Vector3 center, float radius)
        {
            if (laser == null) return;
            laserRadius = Mathf.Max(0.03f, radius);
            laser.enabled = true;
            UpdateLaser(center, 0f);
        }

        /// <param name="k">0→1 진행도</param>
        public void UpdateLaser(Vector3 center, float k)
        {
            if (laser == null || !laser.enabled) return;
            float r = Mathf.Lerp(laserRadius, 0.02f, k);
            for (int i = 0; i < Segs; i++)
            {
                float a = i / (float)Segs * Mathf.PI * 2f;
                laserPts[i] = center + new Vector3(Mathf.Cos(a) * r, -0.01f, Mathf.Sin(a) * r);
            }
            laser.SetPositions(laserPts);
            var c = laserColor; c.a = 1f - k;
            laser.startColor = c;
            laser.endColor = c;
        }

        public void EndLaser()
        {
            if (laser != null) laser.enabled = false;
        }
    }
}
