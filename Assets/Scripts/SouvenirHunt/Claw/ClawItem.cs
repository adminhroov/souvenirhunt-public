using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 바스켓 안의 물리 아이템(10-15 미션형 복원, git 8d3d1ed 규칙). 종류: 세라믹 그릇·유리 그릇(= 목표물, 3개 넣으면 성공) /
    /// 캡슐(무득점) / 폭탄(집게가 닿으면 폭발). 프리팹 1종·풀 1개 — 종류마다 메시·재질·충돌체·질량만 바꾼다(SetKind).
    /// 충돌체: 캡슐·폭탄 = 루트 구, 그릇 = 자식 볼록 헐(원뿔대, 에디터가 정점 실측으로 구움 — 상자는 모서리로 서고 오목 메시는 포개져 못 잡음).
    /// 메시·헐은 폭 1로 정규화돼 있고 크기는 spec.Width(kind) — 기계가 바뀌면 헤드 지름 비례로 따라간다.
    /// 그릇은 무게중심을 낮춰 저절로 바로 선다. 들리면 kinematic·충돌체 off·ClawHeld 레이어.
    /// 10-22 키링(KoreaCubeKeyring, 사용자: "키링을 인형뽑기 상품으로" → 그릇처럼 목표물·3개·그릇 크기): 큐브 몸통 = 상자 충돌체(자식), 체인·고리는 시각만.
    /// 메시는 큐브 모서리 1로 정규화(큐브 중심 = 원점) — Width = 큐브 크기.
    /// 조준 대상 표시(비주얼 1차): 셰이더 SouvenirHunt/ClawItemLit의 _Highlight(가장자리 청록)를 MPB로 켠다 — 끄면 MPB를 비워 SRP Batcher 유지.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ClawItem : MonoBehaviour
    {
        public enum Kind { Ceramic, Glass, Capsule, Bomb, Keyring }   // 순서 유지(직렬화 값) — 새 종류는 끝에

        [SerializeField] ClawItemSpec spec;
        [SerializeField] Renderer visual;
        [SerializeField] SphereCollider sphere;           // 캡슐·폭탄
        [SerializeField] MeshCollider ceramicHull;        // 자식(폭 1 헐 × Width)
        [SerializeField] MeshCollider glassHull;
        [SerializeField] BoxCollider keyringBox;        // 10-22 자식(큐브 1 × Width)
        [Header("외형(폭 1 정규화 메시)")]
        [SerializeField] Mesh ceramicMesh;
        [SerializeField] Mesh glassMesh;
        [SerializeField] Mesh capsuleMesh;
        [SerializeField] Mesh bombMesh;
        [SerializeField] Mesh keyringMesh;
        [SerializeField] Material ceramicMaterial;
        [SerializeField] Material glassMaterial;
        [SerializeField] Material[] capsuleMaterials;     // 색 순환
        [SerializeField] Material bombMaterial;
        [SerializeField] Material keyringMaterial;

        public ClawItemSpec Spec => ClawItemSpec.Or(spec);
        public Kind ItemKind { get; private set; }
        public bool IsBowl => ItemKind == Kind.Ceramic || ItemKind == Kind.Glass;
        /// <summary>목표물(배출구에 넣으면 획득·경품 종류) — 그릇 2종 + 10-22 키링</summary>
        public bool IsTarget => IsBowl || ItemKind == Kind.Keyring;
        public static bool IsTargetKind(Kind k) => k == Kind.Ceramic || k == Kind.Glass || k == Kind.Keyring;
        /// <summary>10-28 점수 상품(배출구에 넣으면 진행·성공) — 키링 큐브만. 그릇은 방해물(IsTarget은 배치·배출 처리용으로 유지)</summary>
        public bool IsScoring => ItemKind == Kind.Keyring;
        public static bool IsScoringKind(Kind k) => k == Kind.Keyring;
        public Rigidbody Body { get; private set; }
        public Renderer Visual => visual;
        public bool Held { get; private set; }
        public float Width => Spec.Width(ItemKind);
        public float HalfHeight => Spec.HalfHeight(ItemKind);
        /// <summary>
        /// 10-22 물리 집게: other가 이 상품 바로 밑(중심이 이 상품 밑면보다 낮고, 수평으로 발자국 안)에서 받치고 있는가 —
        /// 발이 그것에는 멈추지 않고 지나가 이 상품 밑으로 파고든다. 옆에 낮게 놓인 것(덩어리 이웃)은 아니다
        /// </summary>
        public bool IsUnder(ClawItem other)
        {
            if (other == null || other == this) return false;
            float bottom = visual != null ? visual.bounds.min.y : transform.position.y - HalfHeight;
            if (other.transform.position.y >= bottom + 0.005f) return false;
            var d = other.transform.position - transform.position;
            float foot = Width * 0.5f + other.Width * 0.25f;
            return d.x * d.x + d.z * d.z <= foot * foot;
        }

        /// <summary>
        /// 10-22 진짜 맨 윗층: other가 이 상품보다 낮은 층(윗면이 이 상품 높이 가운데 + 1cm 아래 — 받친 캡슐 층·그 사이에 납작한 그릇이 묻힌 둘레)인가 —
        /// 발 갈고리는 그 층으로 파고들어 지나간다. 옆에 비슷한 높이로 놓인 것(덩어리 이웃 — 윗면이 이 상품 가운데보다 위)은 아니다.
        /// (밑면 + 2cm 기준은 납작한 그릇이 캡슐 사이 골에 앉으면 둘레 캡슐 윗면이 그보다 높아 발이 거기 막혔다)
        /// </summary>
        public bool IsLowerLevel(ClawItem other)
        {
            if (other == null || other == this) return false;
            float mid = visual != null ? visual.bounds.center.y : transform.position.y;
            return other.TopWorldY < mid + 0.01f;
        }

        /// <summary>중심에서 가장 먼 면까지(배출구 칸·울타리 판정)</summary>
        public float HalfExtent => Mathf.Max(Width, Spec.Height(ItemKind)) * 0.5f;
        /// <summary>현재 윗면 월드 높이 — 캡슐(구)은 중심 + 반지름(굴러 회전해도 정확), 그릇·폭탄(심지 포함)은 렌더러 바운즈(기울어져도 근사)</summary>
        public float TopWorldY => ItemKind == Kind.Capsule || visual == null ? transform.position.y + Width * 0.5f : visual.bounds.max.y;
        public Mesh CurrentMesh => visualFilter != null ? visualFilter.sharedMesh : null;
        /// <summary>지금 켜져 있어야 할 충돌체(종류별 하나)</summary>
        public Collider ActiveCollider => ItemKind switch
        {
            Kind.Ceramic => ceramicHull,
            Kind.Glass => glassHull,
            Kind.Keyring => keyringBox,
            _ => sphere,
        };
        public bool Glowing { get; private set; }
        /// <summary>10-20 받침 더미에서 승격된 진짜 캡슐이면 그 조각 번호(아니면 −1). 풀 반환·SetKind에서 비운다</summary>
        public int BedIndex { get; set; } = -1;
        /// <summary>10-22 받침 맨 윗층을 대신한 진짜 캡슐(배치 때 생김). 풀 반환·SetKind에서 비운다 — 재배치(리센터) 때 다시 만든다</summary>
        public bool TopLayer { get; set; }

        int itemLayer, heldLayer;
        MeshFilter visualFilter;
        MaterialPropertyBlock mpb;
        Vector3 visualBaseScale = Vector3.one;
        static readonly int HighlightId = Shader.PropertyToID("_Highlight");

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (visual == null) visual = GetComponentInChildren<Renderer>();
            if (sphere == null) sphere = GetComponent<SphereCollider>();
            visualFilter = visual != null ? visual.GetComponent<MeshFilter>() : null;
            mpb = new MaterialPropertyBlock();

            itemLayer = LayerMask.NameToLayer("ClawItem");
            heldLayer = LayerMask.NameToLayer("ClawHeld");
            if (itemLayer < 0) itemLayer = gameObject.layer;   // 레이어 미생성(메뉴 30 미실행)이면 현 레이어 유지 — 앱은 계속
            if (heldLayer < 0) heldLayer = itemLayer;
            gameObject.layer = itemLayer;

            var s = Spec;
            // 그릇 헐 크기는 종류별로 고정 — 한 번만(런타임 스케일 변경은 볼록 헐 재굽기 없음)
            if (ceramicHull != null) ceramicHull.transform.localScale = Vector3.one * s.Width(Kind.Ceramic);
            if (glassHull != null) glassHull.transform.localScale = Vector3.one * s.Width(Kind.Glass);
            if (keyringBox != null) keyringBox.transform.localScale = Vector3.one * s.Width(Kind.Keyring);
            foreach (var c in new Collider[] { sphere, ceramicHull, glassHull, keyringBox })
                if (c != null) c.contactOffset = s.contactOffset;
            SetKind(Kind.Capsule, 0);
        }

        /// <summary>풀에서 꺼낼 때 — 종류·외형·충돌체·질량 설정 + 물리 상태 초기화. variant = 캡슐 색 순번.</summary>
        public void SetKind(Kind kind, int variant)
        {
            ItemKind = kind;
            Held = false;
            BedIndex = -1;
            TopLayer = false;
            var s = Spec;
            Mesh mesh = kind switch { Kind.Ceramic => ceramicMesh, Kind.Glass => glassMesh, Kind.Capsule => capsuleMesh, Kind.Keyring => keyringMesh, _ => bombMesh };
            Material mat = kind switch
            {
                Kind.Ceramic => ceramicMaterial,
                Kind.Glass => glassMaterial,
                Kind.Bomb => bombMaterial,
                Kind.Keyring => keyringMaterial,
                _ => capsuleMaterials != null && capsuleMaterials.Length > 0 ? capsuleMaterials[Mathf.Abs(variant) % capsuleMaterials.Length] : null,
            };
            if (visualFilter != null && mesh != null) visualFilter.sharedMesh = mesh;
            if (visual != null)
            {
                if (mat != null) visual.sharedMaterial = mat;
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * s.Width(kind);
                visualBaseScale = visual.transform.localScale;
            }
            if (sphere != null) { sphere.radius = s.Width(kind) * 0.5f; sphere.center = Vector3.zero; }
            RestoreColliders();
            s.ConfigureBody(Body, kind);
            gameObject.layer = itemLayer;
            Body.isKinematic = false;
            Body.ResetCenterOfMass();
            Body.ResetInertiaTensor();
            if (IsBowl) Body.centerOfMass = new Vector3(0f, -0.35f * s.HalfHeight(kind), 0f); // 오뚝이처럼 바로 서게
            SetGlow(false);
        }

        void RestoreColliders()
        {
            if (sphere != null) sphere.enabled = ItemKind == Kind.Capsule || ItemKind == Kind.Bomb;
            if (ceramicHull != null) ceramicHull.enabled = ItemKind == Kind.Ceramic;
            if (glassHull != null) glassHull.enabled = ItemKind == Kind.Glass;
            if (keyringBox != null) keyringBox.enabled = ItemKind == Kind.Keyring;
        }

        void DisableColliders()
        {
            if (sphere != null) sphere.enabled = false;
            if (ceramicHull != null) ceramicHull.enabled = false;
            if (glassHull != null) glassHull.enabled = false;
            if (keyringBox != null) keyringBox.enabled = false;
        }

        /// <summary>집게에 붙음 — 물리 정지, 충돌 없음, 헤드가 위치를 직접 잡는다.</summary>
        public void BeginHeld()
        {
            Held = true;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.isKinematic = true;
            DisableColliders();
            gameObject.layer = heldLayer;
            SetGlow(false);
        }

        /// <summary>시간 초과 등으로 들고 있던 것을 현 위치에서 놓아 물리로 되돌림(바스켓 안으로 떨어진다).</summary>
        public void DropBack()
        {
            if (!Held) return;
            Held = false;
            SetShrink(1f);
            RestoreColliders();
            gameObject.layer = itemLayer;
            Body.isKinematic = false;
            Body.position = transform.position;
            Body.rotation = transform.rotation;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            Body.WakeUp();
        }

        /// <summary>풀로 반환(비활성). 다음 SetKind가 상태를 다시 세운다.</summary>
        public void ReleaseToPool()
        {
            Held = false;
            BedIndex = -1;
            TopLayer = false;
            SetShrink(1f);
            SetGlow(false);
            gameObject.layer = itemLayer;
            Body.isKinematic = false;
            RestoreColliders();
            gameObject.SetActive(false);
        }

        /// <summary>배출구 낙하 연출용 비주얼 축소. 충돌체는 건드리지 않는다.</summary>
        public void SetShrink(float factor)
        {
            if (visual == null) return;
            visual.transform.localScale = visualBaseScale * factor;
        }

        /// <summary>조준 대상 표시(가장자리 청록 하이라이트, 물체 색 유지). 끄면 MPB를 비운다(재질 그대로·SRP Batcher 유지)</summary>
        /// <summary>조준 발광 색(HDR) — ClawGuide.GuideColor가 맞춘다(10-19 config clawGuideColor). 기본 = 재질 기본 청록</summary>
        public static Color GlowColor = new Color(0f, 1.4f, 1.6f, 1f);
        static readonly int HighlightColorId = Shader.PropertyToID("_HighlightColor");

        public void SetGlow(bool on)
        {
            Glowing = on;
            if (visual == null || mpb == null) return;
            if (!on) { visual.SetPropertyBlock(null); return; }
            mpb.Clear();
            mpb.SetFloat(HighlightId, 1f);
            mpb.SetColor(HighlightColorId, GlowColor);
            visual.SetPropertyBlock(mpb);
        }

        /// <summary>물리 상태 초기화 후 순간이동(배치·이탈 복구). 호출 후 Physics.SyncTransforms는 호출자가.</summary>
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            transform.SetPositionAndRotation(pos, rot);
            if (Body.isKinematic) return;
            Body.position = pos;
            Body.rotation = rot;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }
    }
}
