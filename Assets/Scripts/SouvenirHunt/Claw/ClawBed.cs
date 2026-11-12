using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SouvenirHunt
{
    /// <summary>
    /// 받침 더미(10-20, 사용자 "4~5층 높이만큼 상품을" → 결정 "아래층 고정 + 위층 실물"). 바스켓 바닥을 덮는 움직이지 않는 캡슐 더미:
    /// 그림 = 메뉴 30이 물리로 쌓아 구운 메시 1개(캡슐 5색 = 하위 메시 5개, BoothRoot/PileBed — 기계와 함께 등장), 충돌 = 조각마다 정적 구(ClawBed 레이어, 아이템하고만).
    /// 맨 위 한 층(그릇·폭탄·캡슐)만 진짜 물리(풀)라 성능은 한 층일 때와 비슷하다. 보이지 않는 깊은 조각은 굽지 않는다(덩어리 상자 콜라이더가 대신).
    /// 조각 하나를 숨기면 그 인덱스 범위만 퇴화 삼각형으로 바꿔 쓴다(메시 사본 — 에셋은 그대로). 승격(겨눈 받침 캡슐 → 같은 자리 진짜 캡슐)이 이걸 쓴다.
    /// 좌표는 바스켓 로컬 = ClawLayout. config clawBed=false면 그림·충돌 모두 끔(예전 한 층 배치).
    /// 10-26(사용자 "모든 상품을 큐브/그릇1/그릇2로, 1:1:1 — 캡슐 더미까지"): 조각 = 세라믹 그릇·유리 그릇·키링(1:1:1), 충돌체는 실물과 같은 모양(헐·상자),
    ///   조각마다 종류(PieceKind)·윗면 높이(LocalTop — 종류·기운 방향마다 다름). 승격·맨 윗층 진짜 상품은 그 종류로
    /// </summary>
    public class ClawBed : MonoBehaviour
    {
        [SerializeField] MeshRenderer render;
        [SerializeField] GameObject colliderRoot;
        [SerializeField] Collider[] pieceColliders;
        [SerializeField] Vector3[] pieceLocal;
        [SerializeField] Quaternion[] pieceRot;
        [SerializeField] int[] pieceKind;           // ClawItem.Kind
        [SerializeField] float[] pieceTop;          // 중심 → 윗면(바스켓 로컬 y)
        [SerializeField] int[] pieceIndexStart;     // 렌더 메시 인덱스 버퍼 시작(전체 기준)
        [SerializeField] int[] pieceIndexCount;
        [SerializeField] int[] pieceFirstVertex;    // 숨길 때 모든 인덱스를 이 정점으로(퇴화)
        [SerializeField] float pieceRadius;         // 대표 반 폭(이웃 판정 거리)
        [SerializeField] float bedTopLocalY;        // 구운 받침 윗면(조각 윗면 최대)
        [SerializeField] int bakedHash;             // 구울 때의 배율·크기 지문(바뀌면 메뉴 30 재실행 필요)

        /// <summary>받침 지문 — 상품 3종 폭·받침 윗면 예산(메뉴 30이 구울 때 저장, 셀프테스트가 지금 규격과 대조 → 다르면 메뉴 30 재실행). 10-26 상품 더미 = 버전 2</summary>
        public static int ComputeHash(ClawItemSpec spec) =>
            ((Mathf.RoundToInt(spec.Width(ClawItem.Kind.Ceramic) * 10000f) * 31 + Mathf.RoundToInt(spec.Width(ClawItem.Kind.Glass) * 10000f)) * 31
             + Mathf.RoundToInt(spec.Width(ClawItem.Kind.Keyring) * 10000f)) * 31 + Mathf.RoundToInt(ClawLayout.BedTopY(spec.MaxItemHeight) * 10000f) + 2;

        public int Count => pieceLocal != null ? pieceLocal.Length : 0;
        public float PieceRadius => pieceRadius;
        public float TopLocalY => bedTopLocalY;
        public int BakedHash => bakedHash;
        public bool Active { get; private set; } = true;
        public int HiddenCount { get; private set; }
        public Renderer Render => render;

        Mesh runtimeMesh;
        bool use32;
        ushort[] orig16, scratch16;
        int[] orig32, scratch32;
        bool[] hidden;
        Dictionary<Collider, int> lookup;
        bool ready;

        void Awake() => Init();

        void OnDestroy()
        {
            if (runtimeMesh != null) Destroy(runtimeMesh);
        }

        void Init()
        {
            if (ready) return;
            ready = true;
            int n = Count;
            hidden = new bool[n];
            lookup = new Dictionary<Collider, int>(n);
            if (pieceColliders != null)
                for (int i = 0; i < pieceColliders.Length; i++)
                    if (pieceColliders[i] != null) lookup[pieceColliders[i]] = i;
            var mf = render != null ? render.GetComponent<MeshFilter>() : null;
            var src = mf != null ? mf.sharedMesh : null;
            if (src == null) return;
            // 메시 사본(고급 API — 인덱스 버퍼 일부만 바꿔 쓰기 위해). 원본 에셋은 건드리지 않는다(재생 중 에셋이 더러워지지 않게)
            use32 = src.indexFormat == IndexFormat.UInt32;
            int subs = src.subMeshCount, total = 0;
            for (int s = 0; s < subs; s++) total += (int)src.GetIndexCount(s);
            var all = new int[total];
            var descs = new SubMeshDescriptor[subs];
            int at = 0;
            for (int s = 0; s < subs; s++)
            {
                var idx = src.GetIndices(s);
                System.Array.Copy(idx, 0, all, at, idx.Length);
                descs[s] = new SubMeshDescriptor(at, idx.Length);
                at += idx.Length;
            }
            runtimeMesh = new Mesh { name = src.name + " (runtime)", indexFormat = src.indexFormat };
            runtimeMesh.SetVertices(src.vertices);
            runtimeMesh.SetNormals(src.normals);
            if (src.tangents.Length == src.vertexCount) runtimeMesh.SetTangents(src.tangents);
            runtimeMesh.SetUVs(0, src.uv);
            runtimeMesh.SetIndexBufferParams(total, src.indexFormat);
            if (use32)
            {
                orig32 = all;
                runtimeMesh.SetIndexBufferData(orig32, 0, 0, total);
                scratch32 = new int[MaxPieceIndexCount()];
            }
            else
            {
                orig16 = new ushort[total];
                for (int i = 0; i < total; i++) orig16[i] = (ushort)all[i];
                runtimeMesh.SetIndexBufferData(orig16, 0, 0, total);
                scratch16 = new ushort[MaxPieceIndexCount()];
            }
            runtimeMesh.subMeshCount = subs;
            for (int s = 0; s < subs; s++) runtimeMesh.SetSubMesh(s, descs[s]);
            runtimeMesh.bounds = src.bounds;
            runtimeMesh.UploadMeshData(false);
            mf.sharedMesh = runtimeMesh;
        }

        int MaxPieceIndexCount()
        {
            int m = 0;
            if (pieceIndexCount != null) foreach (int c in pieceIndexCount) m = Mathf.Max(m, c);
            return m;
        }

        /// <summary>config clawBed — 끄면 그림·충돌 모두 끈다(렌더러 컴포넌트만: PileBed 활성은 MachineVisibility 몫)</summary>
        public void SetEnabled(bool on)
        {
            Init();
            Active = on && Count > 0;
            if (render != null) render.enabled = Active;
            if (colliderRoot != null) colliderRoot.SetActive(Active);
        }

        /// <summary>collider가 받침 조각이면 그 번호</summary>
        public bool TryGetPiece(Collider c, out int index)
        {
            Init();
            index = -1;
            return c != null && lookup != null && lookup.TryGetValue(c, out index);
        }

        public bool IsHidden(int i) => hidden != null && i >= 0 && i < hidden.Length && hidden[i];
        /// <summary>조각 종류(10-26 — 세라믹·유리·키링). 예전 캡슐 받침이면 Capsule</summary>
        public ClawItem.Kind PieceKind(int i) => pieceKind != null && i >= 0 && i < pieceKind.Length ? (ClawItem.Kind)pieceKind[i] : ClawItem.Kind.Capsule;
        public Vector3 LocalPosition(int i) => pieceLocal[i];
        /// <summary>조각 윗면(바스켓 로컬 y)</summary>
        public float LocalTop(int i) => pieceLocal[i].y + Top(i);
        float Top(int i) => pieceTop != null && i < pieceTop.Length ? pieceTop[i] : pieceRadius;
        public Vector3 WorldPosition(int i) => transform.TransformPoint(pieceLocal[i]);
        public Quaternion WorldRotation(int i) => transform.rotation * pieceRot[i];
        public float TopWorldY(int i) => WorldPosition(i).y + Top(i);
        public Collider PieceCollider(int i) => pieceColliders != null && i >= 0 && i < pieceColliders.Length ? pieceColliders[i] : null;

        /// <summary>조각 숨김(그림·충돌) — 승격(진짜 캡슐로 바꿔치기) 전에</summary>
        public void Hide(int i)
        {
            Init();
            if (i < 0 || i >= Count || hidden[i]) return;
            hidden[i] = true;
            HiddenCount++;
            var col = PieceCollider(i);
            if (col != null) col.enabled = false;
            WriteIndices(i, true);
        }

        /// <summary>조각 다시 보이기(그림·충돌)</summary>
        public void Show(int i)
        {
            Init();
            if (i < 0 || i >= Count || !hidden[i]) return;
            hidden[i] = false;
            HiddenCount--;
            var col = PieceCollider(i);
            if (col != null) col.enabled = true;
            WriteIndices(i, false);
        }

        /// <summary>리셋 — 숨긴 조각 전부 복원</summary>
        public void RestoreAll()
        {
            Init();
            if (HiddenCount == 0) return;
            for (int i = 0; i < Count; i++) if (hidden[i]) Show(i);
        }

        void WriteIndices(int i, bool hide)
        {
            if (runtimeMesh == null || pieceIndexCount == null || i >= pieceIndexCount.Length) return;
            int start = pieceIndexStart[i], count = pieceIndexCount[i];
            if (count <= 0) return;
            const MeshUpdateFlags f = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontResetBoneBounds;
            if (use32)
            {
                if (hide) { int v = pieceFirstVertex[i]; for (int k = 0; k < count; k++) scratch32[k] = v; runtimeMesh.SetIndexBufferData(scratch32, 0, start, count, f); }
                else runtimeMesh.SetIndexBufferData(orig32, start, start, count, f);
            }
            else
            {
                if (hide) { ushort v = (ushort)pieceFirstVertex[i]; for (int k = 0; k < count; k++) scratch16[k] = v; runtimeMesh.SetIndexBufferData(scratch16, 0, start, count, f); }
                else runtimeMesh.SetIndexBufferData(orig16, start, start, count, f);
            }
        }

        int[] topLayer;
        /// <summary>
        /// 10-22 맨 윗층 조각 번호(구운 자리 기준 — 둘레 폭 안 조각 윗면 최고에서 반 폭×0.9 안). 사용자 "더미 맨 위 1층 분량은 진짜 캡슐로" —
        /// 바스켓이 배치 때 이 조각들을 숨기고 같은 자리·종류 진짜 상품을 놓는다(ClawBasket.SpawnRealTopLayer)
        /// </summary>
        public int[] TopLayer
        {
            get
            {
                if (topLayer != null) return topLayer;
                int n = Count;
                var list = new List<int>(n / 3);
                float d = pieceRadius * 2f, d2 = d * 0.9f * (d * 0.9f);
                for (int i = 0; i < n; i++)
                {
                    var p = pieceLocal[i];
                    float top = LocalTop(i), surf = top;
                    for (int j = 0; j < n; j++)
                    {
                        var q = pieceLocal[j];
                        float dx = q.x - p.x, dz = q.z - p.z;
                        if (dx * dx + dz * dz < d2) surf = Mathf.Max(surf, LocalTop(j));
                    }
                    if (top >= surf - pieceRadius * 0.9f) list.Add(i);
                }
                topLayer = list.ToArray();
                return topLayer;
            }
        }

        /// <summary>바스켓 로컬 (x, z) 주변 반경 radius 안 조각 윗면 최대(로컬 y). 없으면 float.MinValue</summary>
        public float LocalTopNear(float x, float z, float radius)
        {
            float best = float.MinValue, r2 = radius * radius;
            for (int i = 0; i < Count; i++)
            {
                if (hidden != null && hidden[i]) continue;
                var p = pieceLocal[i];
                float dx = p.x - x, dz = p.z - z;
                if (dx * dx + dz * dz <= r2) best = Mathf.Max(best, LocalTop(i));
            }
            return best;
        }

#if UNITY_EDITOR
        /// <summary>메뉴 30(SouvenirHuntClawBed.cs)이 구운 결과를 넣는다</summary>
        public void EditorAssign(MeshRenderer renderer, GameObject colliders, Collider[] cols, Vector3[] local, Quaternion[] rot, int[] kind, float[] top,
                                 int[] indexStart, int[] indexCount, int[] firstVertex, float radius, float topLocalY, int hash)
        {
            render = renderer; colliderRoot = colliders; pieceColliders = cols; pieceLocal = local; pieceRot = rot; pieceKind = kind; pieceTop = top;
            pieceIndexStart = indexStart; pieceIndexCount = indexCount; pieceFirstVertex = firstVertex;
            pieceRadius = radius; bedTopLocalY = topLocalY; bakedHash = hash;
        }
#endif
    }
}
