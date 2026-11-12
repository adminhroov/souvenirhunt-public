using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 집게발 여닫기(10-19, 사용자 "실제로 잡는 행동을 취할 때 오므려지고 펼쳐지게") + 10-22 **물리 집게발**(사용자 "3개 제한이 아니라 진짜 집게의 물리력으로")
    /// + 10-22 **뼈 집게**(사용자 "오늘 임포트한 메시의 뼈와 애니메이션을 … 그걸로 교체해서 진행해").
    /// 발마다 관절 사슬을 돌린다. 각도(deg) 하나가 발 자세를 정한다: 0 = 모델 휴식 자세, − = 오므림, + = 펼침.
    ///   · 뼈 집게(StartUpPark_set.fbx, 메뉴 30 기본): 관절 3개(팔 Bone001 → 윗마디 Bone002 → 갈고리 Bone003, 스킨 메시가 따라 굽는다).
    ///     클립에서 뽑은 두 자세(closePose = 발끝이 가장 모인 순간, openPose = 가장 벌어진 순간)와 휴식 자세를 각도로 섞는다
    ///     (deg/poseCloseDeg 또는 deg/poseOpenDeg 만큼 — 세 마디가 원작 애니메이션 모양으로 함께 굽는다).
    ///   · 옛 집게(closePose 비어 있음): 관절 하나(발 메시 피벗)를 관절 위치의 지름 방향 × 위 축으로 deg만큼 회전.
    ///   하강·바닥 대기 = 펼침 → 잡기(Attach) = 발마다 따로 오므리다 **상품에 닿으면 그 발만 멈춤**(빈손이면 끝까지)
    ///   → 상승·이동 = 그 각도 유지 → 배출구에서 놓기(Drop) = 펼침 → 복귀 = 기본 모양. 폭탄 반동(Recoil) = 크게 펼침.
    /// 물리: 관절마다 막대 모양 캡슐 사슬(메시 정점을 뼈 방향으로 잘라 가운데를 이음 — 기계가 바뀌어도 메시에서 다시 만든다)을 kinematic 바디에 붙여
    /// FixedUpdate마다 관절 자세로 MovePosition/MoveRotation — 상품(동적)을 실제로 밀고, 끝의 갈고리가 밑을 받쳐 들어 올린다(ClawFinger 레이어, 아이템하고만).
    /// 예외를 던지지 않는다(관절이 없으면 아무 것도 안 함).
    /// </summary>
    public class ClawFingers : MonoBehaviour
    {
        [SerializeField] ClawHead head;
        [Tooltip("발 뿌리 관절(뼈 집게 = Bone001, 옛 집게 = 발 메시 피벗)")]
        [SerializeField] Transform[] pivots;
        [SerializeField] float restDeg = -15f;       // 기본(유휴·복귀) — 옛 집게: 발끝(관절에서 약 25cm)이 약 6.5cm 안으로(반지름 0.195 → 0.13)
        [SerializeField] float openDeg = 15f;        // 하강·놓기 — 발끝이 약 6.5cm 바깥으로
        [Tooltip("10-22 끝까지 오므림(빈손·쥘 때 한계) — 발끝이 거의 가운데서 만남. 예전 −24°는 발끝 고리가 지름 18cm라 그릇·큐브 밑이 빠져나갔다")]
        [SerializeField] float fullCloseDeg = -48f;
        [SerializeField] float recoilDeg = 28f;      // 폭탄에 튕겨 크게 펼침
        [SerializeField] float tau = 0.07f;          // 지수 접근 시간 상수(약 0.2초에 도달)
        [Header("10-22 물리 집게발")]
        [Tooltip("잡기 때 오므리는 속도(°/s) — 발마다 상품에 닿으면 멈춤")]
        [SerializeField] float closeSpeed = 110f;
        [Tooltip("발 막대 캡슐 반지름(m, 집게 배율 1 기준) — StartUpPark 발 두께 약 1.2cm")]
        [SerializeField] float rodRadius = 0.008f;
        [SerializeField] float rodFriction = 0.9f;
        [Header("10-22 뼈 집게(StartUpPark_set.fbx)")]
        [Tooltip("발 관절 로컬 회전(발 × 관절) — 클립에서 발끝이 가장 모인 순간. 비어 있으면 옛 집게(관절 하나를 지름 축으로 회전)")]
        [SerializeField] Quaternion[] closePose;
        [Tooltip("발 관절 로컬 회전(발 × 관절) — 클립에서 발끝이 가장 벌어진 순간")]
        [SerializeField] Quaternion[] openPose;
        [Tooltip("스킨 메시(막대·허브 높이를 정점 가중치에서 잰다)")]
        [SerializeField] SkinnedMeshRenderer skin;
        [Tooltip("closePose에 해당하는 각도(−). 0과 사이는 휴식 자세와 섞음, 넘으면 연장")]
        [SerializeField] float poseCloseDeg = -48f;
        [Tooltip("openPose에 해당하는 각도(+)")]
        [SerializeField] float poseOpenDeg = 60f;

        int J = 1;                 // 발마다 관절 수(뼈 집게 3, 옛 집게 1)
        Transform[][] joints;      // [발][관절] — null = 이 발 없음
        Quaternion[] restQ, closeQ, openQ;   // [발 × J + 관절]
        Vector3[] jointLp, jointLs;
        float[] angles;
        bool[] stopped;            // 잡기 중 이 발이 멈춤(상품에 닿음 또는 끝까지)
        bool[] touching;           // 상품에 닿아 멈춤
        ClawItem[] stopItem;       // 잡기에서 그 발이 닿아 멈춘 상품(끝까지 오므렸으면 null)

        struct Seg { public int j; public Vector3 a, b; }  // 막대 조각(관절 로컬 두 점)
        Seg[][] segs;              // [발] 위 → 발끝 순
        int[] tipJ;                // 발끝(마지막 조각 끝)의 관절
        Vector3[] tipLocal;
        Rigidbody[] bodies;        // 막대가 있는 관절마다 하나
        Transform[] bodyJoint;
        float rodR;
        int itemMask = 0;
        readonly Collider[] hitBuf = new Collider[16];
        readonly Matrix4x4[] poseBuf = new Matrix4x4[8];
        bool gripping;             // Attach 시작 ~ Return/Drop 전
        readonly List<CapsuleCollider> allRods = new();
        readonly List<Transform> rodJoint = new();
        bool rodsOn = true;
        // 막대가 켜질 때 겹쳐 있던 상품 — 잠깐 천천히 밀려나게(maxDepenetrationVelocity) 했다가 되돌린다
        readonly List<(Rigidbody rb, float until, float keep)> softened = new();
        const float SoftDepen = 0.25f, SoftSec = 0.6f;

        /// <summary>현재 발 각도 평균(+ 펼침, − 오므림) — 셀프테스트용</summary>
        public float Angle { get; private set; }
        public int FingerCount => pivots != null ? pivots.Length : 0;
        /// <summary>물리 막대 바디 수(0이면 물리 집게 없음 — 레이어 미생성 등). 뼈 집게는 발마다 윗마디·갈고리 2개</summary>
        public int BodyCount { get; private set; }
        /// <summary>발마다 관절 수(뼈 집게 3, 옛 집게 1) — 셀프테스트용</summary>
        public int JointCount => J;
        /// <summary>잡기(Attach)에서 모든 발이 멈춤(닿았거나 끝까지)</summary>
        public bool GripSettled { get; private set; }
        /// <summary>잡기에서 상품에 닿아 멈춘 발 수</summary>
        public int TouchCount { get; private set; }
        float[] snug;

        bool Valid(int i) => joints != null && i < joints.Length && joints[i] != null;

        // ─ 자세 ─────────────────────────────────────────────────────
        Quaternion JointRot(int i, int j, float deg)
        {
            int k = i * J + j;
            return deg <= 0f ? Quaternion.SlerpUnclamped(restQ[k], closeQ[k], deg / poseCloseDeg)
                             : Quaternion.SlerpUnclamped(restQ[k], openQ[k], deg / poseOpenDeg);
        }

        /// <summary>발 i를 각도 deg로 놓았을 때 관절별 월드 행렬(지금 헤드 자리) → poseBuf</summary>
        void PoseMatrices(int i, float deg)
        {
            var p = joints[i][0].parent;
            var m = p != null ? p.localToWorldMatrix : Matrix4x4.identity;
            for (int j = 0; j < J; j++)
            {
                int k = i * J + j;
                m *= Matrix4x4.TRS(jointLp[k], JointRot(i, j, deg), jointLs[k]);
                poseBuf[j] = m;
            }
        }

        /// <summary>발 i의 지금 관절 행렬 → poseBuf</summary>
        void CurrentMatrices(int i)
        {
            for (int j = 0; j < J; j++) poseBuf[j] = joints[i][j].localToWorldMatrix;
        }

        Vector3 SegA(Seg s, Vector3 off) => poseBuf[s.j].MultiplyPoint3x4(s.a) + off;
        Vector3 SegB(Seg s, Vector3 off) => poseBuf[s.j].MultiplyPoint3x4(s.b) + off;
        float Scale(int i) => joints[i][0].lossyScale.x;
        Vector3 TipNow(int i) => joints[i][tipJ[i]].TransformPoint(tipLocal[i]);

        /// <summary>
        /// 쥘 것이 정해진 순간(헤드가 고정하기 전 — 아직 충돌체가 켜져 있을 때): 쥔 것에 닿아 멈춘 발은 그대로, 나머지 발은 지금 각도에서
        /// 더 오므렸을 때 쥔 것(held)에 처음 닿는 각도(없으면 끝까지)를 목표로 — 올라가는 동안 그 각도로 마저 오므린다. 쥔 것이 아닌 둘레 상품은 무시(지나감)
        /// </summary>
        public void SnugTo(IReadOnlyList<ClawItem> held)
        {
            if (angles == null) return;
            if (snug == null || snug.Length != angles.Length) snug = new float[angles.Length];
            for (int i = 0; i < angles.Length; i++)
            {
                snug[i] = angles[i];
                if (held == null || held.Count == 0 || segs == null || segs[i] == null || !Valid(i)) continue;
                bool onHeld = false;
                foreach (var h in held) if (h == stopItem[i]) { onHeld = true; break; }
                if (onHeld) continue;
                float target = fullCloseDeg;
                for (float d = angles[i] - 1f; d >= fullCloseDeg; d -= 1f)
                {
                    if (RodTouchesAny(i, d, held)) { target = d + 1f; break; }
                }
                snug[i] = Mathf.Min(angles[i], target);
            }
        }

        bool RodTouchesAny(int i, float deg, IReadOnlyList<ClawItem> items)
        {
            PoseMatrices(i, deg);
            float rr = rodR * Scale(i) + 0.002f;
            foreach (var s in segs[i])
            {
                int n = Physics.OverlapCapsuleNonAlloc(SegA(s, Vector3.zero), SegB(s, Vector3.zero), rr, hitBuf, itemMask, QueryTriggerInteraction.Ignore);
                for (int j = 0; j < n; j++)
                {
                    var it = hitBuf[j] != null ? hitBuf[j].GetComponentInParent<ClawItem>() : null;
                    if (it == null) continue;
                    foreach (var h in items) if (h == it) return true;
                }
            }
            return false;
        }

        /// <summary>잡기에서 발 i가 닿아 멈춘 상품(없으면 null) — 헤드가 두 발 이상에 끼인 것을 쥔다</summary>
        public ClawItem StopItem(int i) => stopItem != null && i >= 0 && i < stopItem.Length ? stopItem[i] : null;
        /// <summary>
        /// 헤드 가운데 몸통(허브) 맨 아래가 헤드 밑면(발끝 높이)보다 몇 m 위인가 — 이보다 키 큰 상품 위로는 못 내려간다(몸통이 상품을 뚫는다).
        /// 발(막대 관절)을 뺀 헤드 메시 정점 중 막대 시작 관절 반지름 안쪽의 가장 낮은 점(메뉴 30 메시 — 기계가 바뀌어도 다시 잰다).
        /// 뼈 집게는 스킨 정점 중 막대 관절(윗마디·갈고리)에 가장 많이 붙은 것을 뺀다
        /// </summary>
        public float HubClearance { get; private set; } = 0.10f;

        /// <summary>발 i의 막대 시작 관절(뼈 집게 = 윗마디 = 팔 끝, 옛 집게 = 피벗)</summary>
        Transform RodRoot(int i) => segs != null && segs[i] != null && segs[i].Length > 0 ? joints[i][segs[i][0].j] : joints[i][0];

        void MeasureHub()
        {
            if (head == null) return;
            float hingeR = 0f;
            for (int i = 0; i < FingerCount; i++)
                if (Valid(i)) { var hl = head.transform.InverseTransformPoint(RodRoot(i).position); hingeR = Mathf.Max(hingeR, new Vector2(hl.x, hl.z).magnitude); }
            float minY = float.MaxValue;
            void Consider(Vector3 hl) { if (new Vector2(hl.x, hl.z).magnitude < hingeR * 0.8f) minY = Mathf.Min(minY, hl.y); }
            if (skin != null && skin.sharedMesh != null && skin.sharedMesh.isReadable)
            {
                var mesh = skin.sharedMesh;
                var vs = mesh.vertices;
                var bw = mesh.boneWeights;
                var bl = skin.bones;
                var rodSet = new HashSet<Transform>(bodyJoint ?? new Transform[0]);
                var m = head.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix; // 메시 공간 = 휴식 자세
                for (int v = 0; v < vs.Length; v++)
                {
                    if (v < bw.Length)
                    {
                        var w = bw[v];
                        int d = w.boneIndex0; float mw = w.weight0;
                        if (w.weight1 > mw) { d = w.boneIndex1; mw = w.weight1; }
                        if (w.weight2 > mw) { d = w.boneIndex2; mw = w.weight2; }
                        if (w.weight3 > mw) d = w.boneIndex3;
                        if (d < bl.Length && rodSet.Contains(bl[d])) continue;
                    }
                    Consider(m.MultiplyPoint3x4(vs[v]));
                }
            }
            else
            {
                foreach (var mf in head.GetComponentsInChildren<MeshFilter>(true))
                {
                    bool finger = false;
                    foreach (var pv in pivots) if (pv != null && mf.transform.IsChildOf(pv)) { finger = true; break; }
                    var mesh = mf.sharedMesh;
                    if (finger || mesh == null || !mesh.isReadable) continue;
                    var m = head.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    foreach (var v in mesh.vertices) Consider(m.MultiplyPoint3x4(v));
                }
            }
            if (minY < float.MaxValue) HubClearance = Mathf.Max(0.03f, minY + ClawLayout.HeadHalfHeight);
        }

        /// <summary>오므리다 닿은 폭탄(닿는 순간 폭발 — 헤드가 처리하고 비운다)</summary>
        public ClawItem TouchedBomb { get; set; }

        // 발끝(사슬 끝) 궤적: 펼침 → 끝까지 오므림 각도별 헤드 로컬 (반지름, 높이)
        const int TipSamples = 48;
        float[] tipR, tipY, tipDeg;

        /// <summary>
        /// 오므리다 발끝이 헤드 축에서 r 안으로 처음 들어올 때 발끝 높이 − 헤드 밑면(+ = 위) — 헤드가 이만큼 더 내려가야 그 반지름에서 발끝이 상품 밑에 온다(ClawHead.GripTip).
        /// 옛 집게는 오므릴수록 발끝이 올라가고(+), 뼈 집게는 팔이 아래로 접혀 조금 내려간다(−). 첫 발 기준(세 발 대칭). 곡선이 없으면 0.05
        /// </summary>
        public float TipLiftForRadius(float r)
        {
            if (tipR == null) return 0.05f;
            for (int k = 0; k < TipSamples; k++) if (tipR[k] <= r) return tipY[k] + ClawLayout.HeadHalfHeight;
            return tipY[TipSamples - 1] + ClawLayout.HeadHalfHeight;
        }

        /// <summary>
        /// 펼친 발 막대 위 점들 + 관절 + 허브 밑 둘레(헤드 로컬) — 배출구 아크릴 칸막이에 집게가 닿지 않는 가장 낮은 높이(ClawHead.GuardTipY).
        /// 예전에는 집게 반경(0.25m)이 칸에 걸치기만 해도 칸막이 위에서 멈춰 기계의 약 1/4이 못 잡는 자리였다
        /// </summary>
        public IReadOnlyList<Vector3> OpenClawPoints => openPts;
        readonly List<Vector3> openPts = new();

        void BuildOpenPoints()
        {
            openPts.Clear();
            if (segs == null || head == null) return;
            var ht = head.transform;
            float hubR = 0f, hubY = 0f;
            for (int i = 0; i < FingerCount; i++)
            {
                if (segs[i] == null || !Valid(i)) continue;
                PoseMatrices(i, openDeg);
                foreach (var s in segs[i])
                    for (int q = 0; q < 4; q++)
                        openPts.Add(ht.InverseTransformPoint(Vector3.Lerp(SegA(s, Vector3.zero), SegB(s, Vector3.zero), q / 4f)));
                openPts.Add(ht.InverseTransformPoint(poseBuf[tipJ[i]].MultiplyPoint3x4(tipLocal[i])));
                for (int j = 0; j < J; j++) openPts.Add(ht.InverseTransformPoint(poseBuf[j].GetColumn(3)));
                var rl = ht.InverseTransformPoint(poseBuf[segs[i][0].j].GetColumn(3)); // 막대 시작 관절(팔 끝·피벗)
                hubR = Mathf.Max(hubR, new Vector2(rl.x, rl.z).magnitude); hubY = rl.y;
            }
            // 허브(헤드 몸통)·팔 밑 둘레 — 막대 시작 관절 높이·반지름
            for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4f; openPts.Add(new Vector3(Mathf.Cos(a) * hubR, hubY, Mathf.Sin(a) * hubR)); }
        }

        void BuildTipCurve()
        {
            BuildOpenPoints();
            if (segs == null || segs.Length == 0 || segs[0] == null || !Valid(0) || head == null) return;
            tipR = new float[TipSamples]; tipY = new float[TipSamples]; tipDeg = new float[TipSamples];
            for (int k = 0; k < TipSamples; k++)
            {
                float deg = Mathf.Lerp(openDeg, fullCloseDeg, k / (float)(TipSamples - 1));
                PoseMatrices(0, deg);
                var hl = head.transform.InverseTransformPoint(poseBuf[tipJ[0]].MultiplyPoint3x4(tipLocal[0]));
                tipR[k] = new Vector2(hl.x, hl.z).magnitude;
                tipY[k] = hl.y;
                tipDeg[k] = deg;
            }
        }

        void LogRig()
        {
            if (J <= 1 || tipR == null) return;
            var sb = new System.Text.StringBuilder($"[ClawFingers] 뼈 집게: 발 {FingerCount} × 관절 {J} · 막대 바디 {BodyCount} · 허브 발끝 위 {HubClearance * 100f:F1}cm · 발끝 곡선(각도 반지름cm 높이cm, 헤드 밑면 기준):");
            for (int k = 0; k < TipSamples; k += 8) sb.Append($" {tipDeg[k]:F0}° {tipR[k] * 100f:F1} {(tipY[k] + ClawLayout.HeadHalfHeight) * 100f:+0.0;-0.0}");
            Debug.Log(sb.ToString());
        }

        /// <summary>휴식 자세(각도 0)의 발끝 반지름(헤드 축, 첫 발) — 셀프테스트(ClawLayout.HeadRadius와 맞는지)</summary>
        public float RestTipRadius()
        {
            if (segs == null || segs.Length == 0 || segs[0] == null || !Valid(0) || head == null) return -1f;
            PoseMatrices(0, 0f);
            var hl = head.transform.InverseTransformPoint(poseBuf[tipJ[0]].MultiplyPoint3x4(tipLocal[0]));
            return new Vector2(hl.x, hl.z).magnitude;
        }

        void Awake()
        {
            if (head == null) head = GetComponent<ClawHead>();
            int n = pivots != null ? pivots.Length : 0;
            bool rig = n > 0 && closePose != null && openPose != null && closePose.Length == openPose.Length
                       && closePose.Length >= n && closePose.Length % n == 0;
            J = rig ? Mathf.Min(closePose.Length / n, poseBuf.Length) : 1;
            if (Mathf.Abs(poseCloseDeg) < 1f) poseCloseDeg = -48f;
            if (Mathf.Abs(poseOpenDeg) < 1f) poseOpenDeg = 60f;
            joints = new Transform[n][];
            restQ = new Quaternion[n * J]; closeQ = new Quaternion[n * J]; openQ = new Quaternion[n * J];
            jointLp = new Vector3[n * J]; jointLs = new Vector3[n * J];
            angles = new float[n];
            stopped = new bool[n];
            touching = new bool[n];
            stopItem = new ClawItem[n];
            for (int i = 0; i < n; i++)
            {
                var chain = new Transform[J];
                var t = pivots[i];
                for (int j = 0; j < J && t != null; j++)
                {
                    chain[j] = t;
                    int k = i * J + j;
                    restQ[k] = t.localRotation;
                    jointLp[k] = t.localPosition;
                    jointLs[k] = t.localScale;
                    if (rig) { closeQ[k] = closePose[k]; openQ[k] = openPose[k]; }
                    else
                    {
                        // 관절 위치의 지름 방향(헤드 로컬 xz) × 위 = 바깥으로 펼치는 회전축
                        var p = t.localPosition;
                        var radial = new Vector3(p.x, 0f, p.z);
                        var axis = radial.sqrMagnitude > 1e-8f ? Vector3.Cross(radial.normalized, Vector3.up) : Vector3.right;
                        closeQ[k] = restQ[k] * Quaternion.AngleAxis(poseCloseDeg, axis);
                        openQ[k] = restQ[k] * Quaternion.AngleAxis(poseOpenDeg, axis);
                    }
                    t = j + 1 < J && t.childCount > 0 ? t.GetChild(0) : null;
                }
                joints[i] = System.Array.IndexOf(chain, null) < 0 ? chain : null;
                angles[i] = restDeg;
            }
            // 시작부터 기본 모양(0에서 오므려 들어가는 움직임이 보이지 않게)
            Angle = restDeg;
            ApplyPivots();
            BuildBodies();
            BuildTipCurve();
            MeasureHub();
            LogRig();
        }

        // ─ 물리 발 만들기 ─────────────────────────────────────────────
        void BuildBodies()
        {
            int layer = LayerMask.NameToLayer("ClawFinger");
            itemMask = LayerMask.GetMask("ClawItem");
            int n = FingerCount;
            segs = new Seg[n][];
            tipJ = new int[n];
            tipLocal = new Vector3[n];
            if (layer < 0 || itemMask == 0 || n == 0)
            {
                Debug.LogWarning("[ClawFingers] ClawFinger/ClawItem 레이어 없음 — 물리 집게발 없이(메뉴 XREAL/30 실행 필요)");
                return;
            }
            var mat = new PhysicsMaterial("ClawFingerRod")
            {
                dynamicFriction = rodFriction, staticFriction = rodFriction, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            rodR = rodRadius * ClawLayout.SupClawScale;
            var bodyList = new List<Rigidbody>();
            var jointList = new List<Transform>();
            var parent = head != null && head.transform.parent != null ? head.transform.parent : transform.parent;
            float ps = parent != null ? parent.lossyScale.x : 1f;
            for (int i = 0; i < n; i++)
            {
                if (!Valid(i)) continue;
                var chains = J > 1 ? SkinChains(i) : new[] { BuildChain(pivots[i]) };
                var list = new List<Seg>();
                for (int j = 0; j < J; j++)
                {
                    var chain = chains[j];
                    if (chain == null || chain.Length < 2) continue;
                    var jt = joints[i][j];
                    var go = new GameObject(J > 1 ? $"FingerPhys{i}_{j}" : $"FingerPhys{i}") { layer = layer };
                    go.transform.SetParent(parent, false);
                    go.transform.SetPositionAndRotation(jt.position, jt.rotation);
                    go.transform.localScale = Vector3.one * (jt.lossyScale.x / Mathf.Max(1e-4f, ps));
                    var rb = go.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.interpolation = RigidbodyInterpolation.None;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    for (int k = 0; k + 1 < chain.Length; k++)
                    {
                        var a = chain[k]; var b = chain[k + 1];
                        var seg = b - a;
                        float len = seg.magnitude;
                        if (len < 1e-4f) continue;
                        var cgo = new GameObject($"Rod{k}") { layer = layer };
                        cgo.transform.SetParent(go.transform, false);
                        cgo.transform.localPosition = (a + b) * 0.5f;
                        cgo.transform.localRotation = Quaternion.LookRotation(seg / len, Mathf.Abs(Vector3.Dot(seg / len, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
                        var cap = cgo.AddComponent<CapsuleCollider>();
                        cap.direction = 2;
                        cap.radius = rodR;
                        cap.height = len + rodR * 2f;
                        cap.sharedMaterial = mat;
                        cap.contactOffset = 0.004f;
                        allRods.Add(cap);
                        rodJoint.Add(jt);
                        list.Add(new Seg { j = j, a = a, b = b });
                    }
                    bodyList.Add(rb);
                    jointList.Add(jt);
                    tipJ[i] = j;
                    tipLocal[i] = chain[chain.Length - 1];
                }
                segs[i] = list.Count > 0 ? list.ToArray() : null;
            }
            bodies = bodyList.ToArray();
            bodyJoint = jointList.ToArray();
            BodyCount = bodies.Length;
        }

        /// <summary>
        /// 옛 집게: 발 메시 정점(피벗 로컬)에서 막대 사슬 — 관절(피벗 원점) + 아래 70%를 6칸으로 잘라 칸마다 가운데. 위 30%는 허브로 이어지는 팔이라 뺀다
        /// </summary>
        static Vector3[] BuildChain(Transform pivot)
        {
            var mf = pivot.GetComponentInChildren<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return null;
            var vs = mesh.vertices;
            if (vs.Length == 0) return null;
            var m = pivot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            float yTop = float.MinValue, yBot = float.MaxValue;
            for (int i = 0; i < vs.Length; i++) { vs[i] = m.MultiplyPoint3x4(vs[i]); yTop = Mathf.Max(yTop, vs[i].y); yBot = Mathf.Min(yBot, vs[i].y); }
            float yCut = yTop - 0.3f * (yTop - yBot);
            const int Bins = 6;
            var sum = new Vector3[Bins];
            var cnt = new int[Bins];
            for (int i = 0; i < vs.Length; i++)
            {
                if (vs[i].y > yCut) continue;
                int b = Mathf.Clamp((int)((yCut - vs[i].y) / Mathf.Max(1e-5f, yCut - yBot) * Bins), 0, Bins - 1);
                sum[b] += vs[i]; cnt[b]++;
            }
            var pts = new List<Vector3> { Vector3.zero };
            for (int b = 0; b < Bins; b++) if (cnt[b] > 0) pts.Add(sum[b] / cnt[b]);
            return pts.ToArray();
        }

        /// <summary>
        /// 뼈 집게: 관절마다 막대 사슬(관절 로컬) — 스킨 정점 중 그 뼈에 가장 많이 붙은 것을 바인드포즈로 뼈 공간에 옮겨, 뼈 방향(다음 관절 쪽, 마지막은 가장 먼 점 쪽)으로
        /// 4칸 잘라 칸마다 가운데. 관절 원점에서 시작, 다음 관절이 있으면 그 원점에서 끝(마디끼리 이어짐). 첫 관절(팔)은 막대 없음 — 예전처럼 허브 쪽 팔은 상품을 밀지 않는다
        /// </summary>
        Vector3[][] SkinChains(int i)
        {
            var res = new Vector3[J][];
            var mesh = skin != null ? skin.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) return res;
            var vs = mesh.vertices;
            var bw = mesh.boneWeights;
            var bp = mesh.bindposes;
            var bl = skin.bones;
            const int Bins = 4;
            for (int j = 1; j < J; j++)
            {
                int b = System.Array.IndexOf(bl, joints[i][j]);
                if (b < 0 || b >= bp.Length) continue;
                var pts = new List<Vector3>();
                for (int v = 0; v < vs.Length && v < bw.Length; v++)
                {
                    var w = bw[v];
                    int d = w.boneIndex0; float mw = w.weight0;
                    if (w.weight1 > mw) { d = w.boneIndex1; mw = w.weight1; }
                    if (w.weight2 > mw) { d = w.boneIndex2; mw = w.weight2; }
                    if (w.weight3 > mw) d = w.boneIndex3;
                    if (d == b) pts.Add(bp[b].MultiplyPoint3x4(vs[v]));
                }
                if (pts.Count < Bins) continue;
                bool last = j + 1 >= J;
                Vector3 dir;
                if (!last) dir = jointLp[i * J + j + 1];
                else { dir = Vector3.zero; foreach (var q in pts) if (q.sqrMagnitude > dir.sqrMagnitude) dir = q; }
                float len = dir.magnitude;
                if (len < 1e-4f) continue;
                dir /= len;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var q in pts) { float s = Vector3.Dot(q, dir); lo = Mathf.Min(lo, s); hi = Mathf.Max(hi, s); }
                lo = Mathf.Max(lo, 0f);
                var sum = new Vector3[Bins];
                var cnt = new int[Bins];
                foreach (var q in pts)
                {
                    float s = Vector3.Dot(q, dir);
                    if (s < lo) continue;
                    int k = Mathf.Clamp((int)((s - lo) / Mathf.Max(1e-5f, hi - lo) * Bins), 0, Bins - 1);
                    sum[k] += q; cnt[k]++;
                }
                var chain = new List<Vector3> { Vector3.zero };
                for (int k = 0; k < Bins; k++) if (cnt[k] > 0) chain.Add(sum[k] / cnt[k]);
                if (!last) chain.Add(jointLp[i * J + j + 1]);
                res[j] = chain.ToArray();
            }
            return res;
        }

        /// <summary>
        /// 막대 충돌 켜기·끄기 — 10-22 하강·바닥 대기 중에는 끈다: 펼친 발이 더미 속으로 빠르게 내려가며 상품을 고정 받침 사이에 끼워 0.5m씩 튕겨 냈다(시험 영상).
        /// 오므리기 시작할 때 켜고, 그때 막대와 겹쳐 있던 상품은 천천히(0.25m/s) 밀려나게
        /// </summary>
        void SetRods(bool on)
        {
            if (rodsOn == on) return;
            rodsOn = on;
            foreach (var c in allRods) if (c != null) c.enabled = on;
            if (on) SoftenOverlaps();
        }

        void SoftenOverlaps()
        {
            if (segs == null) return;
            for (int i = 0; i < FingerCount; i++)
            {
                if (segs[i] == null || !Valid(i)) continue;
                CurrentMatrices(i);
                float rr = rodR * Scale(i) + 0.004f;
                foreach (var s in segs[i])
                {
                    int n = Physics.OverlapCapsuleNonAlloc(SegA(s, Vector3.zero), SegB(s, Vector3.zero), rr, hitBuf, itemMask, QueryTriggerInteraction.Ignore);
                    for (int j = 0; j < n; j++)
                    {
                        var rb = hitBuf[j] != null ? hitBuf[j].attachedRigidbody : null;
                        if (rb == null || rb.isKinematic) continue;
                        // 잡을 것 밑·아래층(받친 캡슐)이면 밀지 말고 바로 지나가게 — 켜지는 순간 막대가 받침 캡슐을 밀어 잡을 것을 굴려 냈다
                        var oi = rb.GetComponent<ClawItem>();
                        if (oi != null && gripTarget != null && (gripTarget.IsUnder(oi) || gripTarget.IsLowerLevel(oi))) { PassThrough(oi); continue; }
                        Soften(rb);
                    }
                }
            }
        }

        void Soften(Rigidbody rb)
        {
            if (rb == null || rb.isKinematic) return;
            foreach (var e in softened) if (e.rb == rb) return;
            softened.Add((rb, Time.time + SoftSec, rb.maxDepenetrationVelocity));
            rb.maxDepenetrationVelocity = SoftDepen;
        }

        void TickSoftened()
        {
            for (int k = softened.Count - 1; k >= 0; k--)
            {
                var e = softened[k];
                if (e.rb != null && Time.time < e.until) continue;
                if (e.rb != null) e.rb.maxDepenetrationVelocity = e.keep;
                softened.RemoveAt(k);
            }
        }

        // ─ 폭탄 닿음(10-22 물리 집게 — "닿는 순간 펑"을 발 막대 기준으로) ─────────────────
        /// <summary>물리 발 막대가 있음(없으면 헤드가 예전 판 판정)</summary>
        public bool HasRods => BodyCount > 0;

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        /// <summary>poseBuf에 놓인 발 i의 막대가 (off만큼 옮겨) 점 c에서 rr 안인가</summary>
        bool RodHits(int i, Vector3 off, Vector3 c, float rr)
        {
            foreach (var s in segs[i])
                if (SegDist(c, SegA(s, off), SegB(s, off)) < rr) return true;
            return false;
        }

        float BombReach(ClawItem b) => b.Width * 0.5f + rodR + 0.003f;

        /// <summary>지금 발 막대(현재 각도·자세)에 닿은 폭탄 — 하강·바닥 대기 중 매 프레임(막대 충돌은 꺼져 있어도 닿음은 판정)</summary>
        public ClawItem BombTouchingNow(IReadOnlyList<ClawItem> items)
        {
            if (segs == null || items == null) return null;
            var hp = head.transform.position;
            for (int b = 0; b < items.Count; b++)
            {
                var it = items[b];
                if (it.ItemKind != ClawItem.Kind.Bomb || !it.gameObject.activeSelf || it.Held) continue;
                var c = it.transform.position;
                if (new Vector2(c.x - hp.x, c.z - hp.z).sqrMagnitude > 0.25f) continue;
                float rr = BombReach(it);
                for (int i = 0; i < FingerCount; i++)
                {
                    if (segs[i] == null || !Valid(i)) continue;
                    CurrentMatrices(i);
                    if (RodHits(i, Vector3.zero, c, rr)) return it;
                }
            }
            return null;
        }

        /// <summary>
        /// 조준 예측(가이드): 헤드가 지금 높이(fromHeadY)에서 stopHeadPos까지 펼친 채 내려가고, 거기서 끝까지 오므릴 때 발 막대가 지나가는 폭탄.
        /// 각 발은 자기 지름 방향 판 안에서만 돈다 — 발 사이 틈에 있는 폭탄은 안 닿는다. 오므리다 상품에 먼저 막히는 경우는 무시(조금 넉넉히 경고)
        /// </summary>
        public ClawItem SweepBomb(Vector3 stopHeadPos, float fromHeadY, IReadOnlyList<ClawItem> items)
        {
            if (segs == null || items == null) return null;
            var hp = head.transform.position;
            var stopOff = stopHeadPos - hp;
            for (int b = 0; b < items.Count; b++)
            {
                var it = items[b];
                if (it.ItemKind != ClawItem.Kind.Bomb || !it.gameObject.activeSelf || it.Held) continue;
                var c = it.transform.position;
                if (new Vector2(c.x - stopHeadPos.x, c.z - stopHeadPos.z).sqrMagnitude > 0.25f) continue;
                if (c.y > fromHeadY + 0.05f) continue;
                float rr = BombReach(it);
                for (int i = 0; i < FingerCount; i++)
                {
                    if (segs[i] == null || !Valid(i)) continue;
                    // 내려가는 길(펼친 채, 3cm 간격)
                    PoseMatrices(i, openDeg);
                    for (float y = Mathf.Min(fromHeadY, c.y + 0.5f); y > stopHeadPos.y; y -= 0.03f)
                        if (RodHits(i, new Vector3(stopOff.x, y - hp.y, stopOff.z), c, rr)) return it;
                    // 바닥에서 끝까지 오므림(3° 간격)
                    for (float d = openDeg; d >= fullCloseDeg - 0.01f; d -= 3f)
                    {
                        PoseMatrices(i, d);
                        if (RodHits(i, stopOff, c, rr)) return it;
                    }
                }
            }
            return null;
        }

        /// <summary>지금 발끝이 헤드 축에서 떨어진 거리 평균 — 오므린 발끝 고리 안에 든 것만 받쳐 함께 올라온다</summary>
        public float TipsRadius
        {
            get
            {
                if (segs == null || head == null) return ClawLayout.HeadRadius;
                var hp = head.transform.position;
                float sum = 0f; int n = 0;
                for (int i = 0; i < FingerCount; i++)
                {
                    if (segs[i] == null || !Valid(i)) continue;
                    var t = TipNow(i);
                    sum += new Vector2(t.x - hp.x, t.z - hp.z).magnitude; n++;
                }
                return n > 0 ? sum / n : ClawLayout.HeadRadius;
            }
        }

        /// <summary>지금 발끝(사슬 끝) 높이 평균(월드) — 발끝보다 위에 있어야 받쳐진다</summary>
        public float TipsWorldY
        {
            get
            {
                if (segs == null) return head != null ? head.transform.position.y - ClawLayout.HeadHalfHeight : 0f;
                float sum = 0f; int n = 0;
                for (int i = 0; i < FingerCount; i++)
                {
                    if (segs[i] == null || !Valid(i)) continue;
                    sum += TipNow(i).y; n++;
                }
                return n > 0 ? sum / n : 0f;
            }
        }

        /// <summary>
        /// 지금 발 자세에서 월드 높이 worldY의 발 고리 반지름(헤드 축 기준, 발 3개 평균) — 그 높이에서 발 안쪽인지 판정(덩어리로 같이 쥘 것).
        /// 그 높이를 지나는 막대 조각이 없으면(발끝 아래·관절 위) 가장 가까운 끝점
        /// </summary>
        public float RingRadiusAt(float worldY)
        {
            if (segs == null || head == null) return ClawLayout.HeadRadius;
            var hp = head.transform.position;
            float sum = 0f; int n = 0;
            for (int i = 0; i < FingerCount; i++)
            {
                if (segs[i] == null || !Valid(i)) continue;
                CurrentMatrices(i);
                float best = float.MaxValue, r = 0f;
                foreach (var s in segs[i])
                {
                    var a = SegA(s, Vector3.zero); var b = SegB(s, Vector3.zero);
                    float lo = Mathf.Min(a.y, b.y), hi = Mathf.Max(a.y, b.y);
                    Vector3 q;
                    if (worldY >= lo && worldY <= hi && hi - lo > 1e-5f) q = Vector3.Lerp(a, b, (worldY - a.y) / (b.y - a.y));
                    else q = Mathf.Abs(a.y - worldY) < Mathf.Abs(b.y - worldY) ? a : b;
                    float dy = Mathf.Abs(q.y - worldY);
                    if (dy < best) { best = dy; r = new Vector2(q.x - hp.x, q.z - hp.z).magnitude; }
                }
                sum += r; n++;
            }
            return n > 0 ? sum / n : ClawLayout.HeadRadius;
        }

        // ─ 잡기(헤드가 부른다) ─────────────────────────────────────────
        /// <summary>
        /// Attach 시작 — 발마다 지금 각도에서 오므리기 시작. passBelowY(월드) 아래에 중심이 있는 상품(잡을 것 밑을 받친 캡슐 등)에는 멈추지 않고
        /// 지나간다(막대와 충돌 끔 — 갈고리가 더미 속으로 파고들어 잡을 것 밑으로 들어가게). 폭탄은 어디든 닿으면 폭발
        /// </summary>
        public void BeginGrip(float passBelowY = float.NegativeInfinity, ClawItem target = null)
        {
            snug = null;
            RestorePassThrough();
            passBelow = passBelowY;
            gripTarget = target;
            if (pushTotal.Length != pushT.Length) pushTotal = new float[pushT.Length];
            for (int i = 0; i < pushT.Length; i++) { pushT[i] = 0f; pushItem[i] = null; pushTotal[i] = 0f; }
            gripping = true;
            GripSettled = false;
            TouchCount = 0;
            TouchedBomb = null;
            for (int i = 0; i < stopped.Length; i++) { stopped[i] = !Valid(i); touching[i] = false; stopItem[i] = null; }
        }

        /// <summary>발 i가 각도 deg에서 상품에 닿았는가(안쪽에 있는 것만 — 바깥에 기대 있던 것은 오므릴 때 떨어진다)</summary>
        bool Touches(int i, float deg, out ClawItem hit)
        {
            hit = null;
            if (segs == null || segs[i] == null || !Valid(i)) return false;
            PoseMatrices(i, deg);
            float rr = rodR * Scale(i) + 0.002f;
            var axis = head != null ? head.transform.position : transform.position;
            foreach (var s in segs[i])
            {
                var a = SegA(s, Vector3.zero);
                var b = SegB(s, Vector3.zero);
                int n = Physics.OverlapCapsuleNonAlloc(a, b, rr, hitBuf, itemMask, QueryTriggerInteraction.Ignore);
                if (n == 0) continue;
                var mid = (a + b) * 0.5f;
                var inward = new Vector3(axis.x - mid.x, 0f, axis.z - mid.z);
                for (int j = 0; j < n; j++)
                {
                    var it = hitBuf[j] != null ? hitBuf[j].GetComponentInParent<ClawItem>() : null;
                    if (it == null || it.Held || !it.gameObject.activeSelf || passSet.Contains(it)) continue;
                    var d = it.transform.position - mid;
                    if (Vector3.Dot(new Vector3(d.x, 0f, d.z), inward) < -0.005f) continue; // 막대 바깥쪽
                    hit = it;
                    return true;
                }
            }
            return false;
        }

        float passBelow = float.NegativeInfinity;
        ClawItem gripTarget;
        float[] pushT = new float[3];
        ClawItem[] pushItem = new ClawItem[3];
        /// <summary>
        /// 옆 상품(잡을 것 아님)에 닿으면 멈추지 않고 천천히(PushSpeedFactor) 밀며 오므린다 — 밀리는 동안(속도 PinSpeed 이상)은 계속,
        /// PinSec 넘게 안 밀리면(꽉 끼임) 멈춘다. 모터 힘이 한정된 진짜 집게처럼. 잡을 것에 닿으면 바로 멈춤.
        /// 10-22 시험: "닿은 지 0.12초면 멈춤"은 펼친 발(지름 약 60cm)이 둘레 상품에 걸려 잡을 것까지 못 오므렸다
        /// </summary>
        const float PushSpeedFactor = 0.5f, PinSec = 0.15f, PinSpeed = 0.03f;
        /// <summary>한 발이 옆 상품을 민 시간 합이 이만큼이면 멈춰 누른다(덩어리를 끼움) — 계속 밀면 작은 상품이 틈으로 빠져나가 발이 빈손으로 끝까지 오므렸다</summary>
        const float PushTotalSec = 0.25f;
        float[] pushTotal = new float[3];
        readonly List<Collider> passed = new();
        readonly HashSet<ClawItem> passSet = new();

        void PassThrough(ClawItem it)
        {
            if (!passSet.Add(it)) return;
            var c = it.ActiveCollider;
            if (c == null) return;
            passed.Add(c);
            foreach (var r in allRods) if (r != null) Physics.IgnoreCollision(r, c, true);
        }

        /// <summary>지나가게 했던 상품과 막대 충돌을 되돌림(잡기 끝·리셋)</summary>
        void RestorePassThrough()
        {
            foreach (var c in passed)
                if (c != null) foreach (var r in allRods) if (r != null) Physics.IgnoreCollision(r, c, false);
            passed.Clear();
            passSet.Clear();
        }

        void TickGrip(float dt)
        {
            bool all = true;
            if (pushT.Length != angles.Length) { pushT = new float[angles.Length]; pushItem = new ClawItem[angles.Length]; }
            for (int i = 0; i < angles.Length; i++)
            {
                if (stopped[i]) continue;
                float next = Mathf.Max(fullCloseDeg, angles[i] - closeSpeed * dt);
                if (bodies != null && Touches(i, next, out var hit))
                {
                    if (hit.ItemKind == ClawItem.Kind.Bomb) { if (TouchedBomb == null) TouchedBomb = hit; }
                    else if (gripTarget != null && (gripTarget.IsUnder(hit) || gripTarget.IsLowerLevel(hit))) { PassThrough(hit); all = false; continue; } // 잡을 것 밑·아래층(받친 캡슐) — 이번 프레임은 그대로, 다음부터 지나감
                    else if (hit != gripTarget)
                    {
                        // 옆 상품: 천천히 밀며 오므림 — 안 밀리고 오래 끼어 있으면 멈춤
                        if (pushItem[i] != hit) { pushItem[i] = hit; pushT[i] = 0f; Soften(hit.Body); }
                        bool moving = hit.Body != null && !hit.Body.isKinematic && hit.Body.linearVelocity.sqrMagnitude > PinSpeed * PinSpeed;
                        pushT[i] = moving ? 0f : pushT[i] + dt;
                        if (pushTotal.Length == angles.Length) pushTotal[i] += dt;
                        if (pushT[i] < PinSec && (pushTotal.Length != angles.Length || pushTotal[i] < PushTotalSec))
                        {
                            angles[i] = Mathf.Max(fullCloseDeg, angles[i] - closeSpeed * PushSpeedFactor * dt);
                            all = false;
                            continue;
                        }
                    }
                    stopped[i] = true; touching[i] = true; TouchCount++;
                    if (hit.ItemKind != ClawItem.Kind.Bomb) stopItem[i] = hit;
                    continue;
                }
                pushItem[i] = null;
                angles[i] = next;
                if (next <= fullCloseDeg)
                {
                    stopped[i] = true;
                    // 밀며 끝까지 왔다 — 밀던 상품을 누르고 있는 것(덩어리를 끼움)
                    if (pushItem[i] != null && pushItem[i].gameObject.activeSelf && !pushItem[i].Held) { stopItem[i] = pushItem[i]; touching[i] = true; TouchCount++; }
                }
                else all = false;
            }
            GripSettled = all;
        }

        float Target()
        {
            switch (head.Current)
            {
                case ClawHead.Phase.Descend:
                case ClawHead.Phase.Dwell: return openDeg;
                case ClawHead.Phase.Drop: return openDeg;
                case ClawHead.Phase.Recoil: return recoilDeg;
                default: return restDeg; // Idle·Return
            }
        }

        void LateUpdate()
        {
            if (head == null || joints == null || angles == null) return;
            float dt = Time.deltaTime;
            var ph = head.Current;
            // 쥐고 들어 올리는 동안만 막대 충돌 — 폭탄 반동(크게 펼침)·복귀·유휴 중 막대가 날아다니는 상품을 쳐 유리 밖으로 밀어냈다(셀프테스트 이탈 8~25)
            SetRods(ph == ClawHead.Phase.Attach || ph == ClawHead.Phase.Ascend);
            if (softened.Count > 0) TickSoftened();
            if (ph == ClawHead.Phase.Attach)
            {
                if (!gripping) BeginGrip();
                TickGrip(dt);
            }
            else if (gripping && (ph == ClawHead.Phase.Ascend || ph == ClawHead.Phase.Travel || ph == ClawHead.Phase.DropLower))
            {
                // 닿은 발은 그 각도 그대로(물리: 상품을 받친 채 올라간다). 10-22 사용자 "두 발에 닿아 올라오면 나머지 한 발이 벌어져 있다":
                // 안 닿은 발은 쥔 상품 표면까지(SnugTo) 마저 오므린다 — 아무것도 못 쥐었으면 끝까지(빈 집게)
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau));
                bool empty = head.HeldCount == 0;
                for (int i = 0; i < angles.Length; i++)
                {
                    float t = empty ? fullCloseDeg : snug != null && i < snug.Length ? snug[i] : angles[i];
                    angles[i] = Mathf.Lerp(angles[i], Mathf.Min(angles[i], t), k);
                }
            }
            else
            {
                if (gripping) RestorePassThrough();
                gripping = false;
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau));
                float t = Target();
                for (int i = 0; i < angles.Length; i++) angles[i] = Mathf.Lerp(angles[i], t, k);
            }
            ApplyPivots();
        }

        void ApplyPivots()
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < FingerCount; i++)
            {
                if (!Valid(i)) continue;
                for (int j = 0; j < J; j++) joints[i][j].localRotation = JointRot(i, j, angles[i]);
                sum += angles[i]; n++;
            }
            Angle = n > 0 ? sum / n : 0f;
        }

        void FixedUpdate()
        {
            if (bodies == null) return;
            for (int b = 0; b < bodies.Length; b++)
            {
                var rb = bodies[b];
                var jt = bodyJoint[b];
                if (rb == null || jt == null) continue;
                rb.MovePosition(jt.position);
                rb.MoveRotation(jt.rotation);
            }
            if (rodsOn) ClipRodsToMachine();
        }

        /// <summary>
        /// 10-22: 레일 끝에서 펼친 발은 유리 밖으로 약 5cm 나간다 — 그 조각이 오므리며 상품을 유리에 끼워 밖으로 밀어냈다(셀프테스트 이탈 12).
        /// 유리 안쪽(막대 반지름 여유)을 벗어나거나 배출구 칸에 걸친 막대 조각은 충돌을 끈다(보이는 발은 그대로)
        /// </summary>
        void ClipRodsToMachine()
        {
            var bt = head != null && head.Basket != null ? head.Basket.transform : null;
            if (bt == null) return;
            float m = rodR + 0.005f;
            for (int r = 0; r < allRods.Count; r++)
            {
                var c = allRods[r];
                if (c == null) continue;
                var t = c.transform;
                var jt = rodJoint[r];
                // 이번 스텝 자세(바디가 관절로 옮겨 감) — 관절 기준으로 다시 계산
                var lc = t.localPosition; var lf = t.localRotation * Vector3.forward * (c.height * 0.5f - c.radius);
                Vector3 a, b;
                if (jt != null) { a = jt.TransformPoint(lc + lf); b = jt.TransformPoint(lc - lf); }
                else { var ax = t.forward * (c.height * 0.5f - c.radius) * t.lossyScale.x; a = t.position + ax; b = t.position - ax; }
                bool ok = Inside(bt.InverseTransformPoint(a), m) && Inside(bt.InverseTransformPoint(b), m);
                if (c.enabled != ok) c.enabled = ok;
            }
        }

        static bool Inside(Vector3 p, float m) =>
            p.x > -ClawLayout.HalfX + m && p.x < ClawLayout.HalfX - m && p.z > ClawLayout.FrontZ + m && p.z < ClawLayout.BackZ - m
            && !(p.x < ClawLayout.ChuteWallX + m && p.z < ClawLayout.ChuteWallZ + m);

        /// <summary>물리 발을 지금 관절 자세로 순간 이동(리셋·재정렬 — MovePosition이면 큰 속도로 주변을 친다)</summary>
        public void SnapBodies()
        {
            if (bodies == null) return;
            for (int b = 0; b < bodies.Length; b++)
            {
                var rb = bodies[b];
                var jt = bodyJoint[b];
                if (rb == null || jt == null) continue;
                rb.transform.SetPositionAndRotation(jt.position, jt.rotation);
                rb.position = jt.position;
                rb.rotation = jt.rotation;
            }
        }

        /// <summary>리셋 — 기본 모양으로 즉시</summary>
        public void ResetFingers()
        {
            RestorePassThrough();
            gripping = false;
            GripSettled = false;
            TouchedBomb = null;
            if (angles == null) return;
            for (int i = 0; i < angles.Length; i++) angles[i] = restDeg;
            ApplyPivots();
            SnapBodies();
        }
    }
}
