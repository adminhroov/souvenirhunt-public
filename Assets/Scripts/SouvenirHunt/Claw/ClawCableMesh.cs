using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// UFO 케이블(Model2 코일·중심축) 메시를 앵커→헤드 상단 사이로 늘인다(5차, 09-14). 메시 원점 = 앵커, 원형 길이 = ClawLayout.CableRestLength.
    /// 원형에서 헤드 상단보다 위(앵커~상단) 정점은 길이 비율로 늘이고, 아래로 삐져나온 부분(돔 옆 코일 끝)은 늘이지 않고 헤드를 따라간다 —
    /// 통째로 스케일하면 코일 끝이 UFO 본체를 뚫고 내려간다. 정점 60여 개라 CPU 변형 비용은 무시할 수준, 길이가 안 바뀌면 건너뛴다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class ClawCableMesh : MonoBehaviour
    {
        Mesh mesh;
        Vector3[] rest, work;
        float lastLen = -1f;
        const float MaxLength = 1.2f; // 바운즈 고정(매 프레임 RecalculateBounds 회피)

        public float CurrentLength => lastLen;

        void Awake()
        {
            var mf = GetComponent<MeshFilter>();
            if (mf.sharedMesh == null) { enabled = false; return; }
            mesh = Instantiate(mf.sharedMesh);
            mesh.name = mf.sharedMesh.name + " (deform)";
            mesh.MarkDynamic();
            rest = mesh.vertices;
            work = new Vector3[rest.Length];
            float r = ClawLayout.HeadRadius;
            mesh.bounds = new Bounds(new Vector3(0f, -MaxLength * 0.5f, 0f), new Vector3(r * 2f, MaxLength + 0.1f, r * 2f));
            mf.sharedMesh = mesh;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        /// <summary>월드 앵커·헤드 상단으로 자세·길이 갱신. yawRotation = 부스 회전(메시의 수평 방향 기준).</summary>
        public void SetSpan(Vector3 anchorWorld, Vector3 headTopWorld, Quaternion yawRotation)
        {
            if (mesh == null) return;
            var d = headTopWorld - anchorWorld;
            // 10-20 쉬는 동안 헤드 윗부분이 캐리지 상자 안(앵커 위)으로 감겨 올라가 있으면 케이블은 안 보인다(크기 0)
            bool show = Vector3.Dot(d, yawRotation * Vector3.down) > 0.002f;
            if (!show) { if (transform.localScale != Vector3.zero) transform.localScale = Vector3.zero; return; }
            if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            transform.position = anchorWorld;
            transform.rotation = Quaternion.FromToRotation(Vector3.down, d / len) * yawRotation;
            if (Mathf.Abs(len - lastLen) < 0.0005f) return;
            lastLen = len;

            float restLen = ClawLayout.CableRestLength;
            float k = len / restLen;
            for (int i = 0; i < rest.Length; i++)
            {
                var v = rest[i];
                // v.y: 0 = 앵커, −restLen = 헤드 상단. 그 아래는 강체로 따라감
                v.y = v.y >= -restLen ? v.y * k : -len + (v.y + restLen);
                work[i] = v;
            }
            mesh.vertices = work;
        }
    }
}
