using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// MeshLibrary 항목을 자식 Visual 렌더러에 입히고 최장축을 size(m)로 정규화, 콜라이더를 바운즈에 맞춘다.
    /// Collectible.SetVisual의 본문을 그대로 분리(09-03) — ClawItem(인형뽑기 목표물)이 같은 규칙을 재사용한다.
    /// </summary>
    public static class MeshFit
    {
        /// <param name="colliderScale">콜라이더를 시각 바운즈 대비 몇 배로(기념품 1.2 = 선택 성공률, 물리 아이템 1.0)</param>
        /// <returns>메시·렌더러가 없어 적용하지 못했으면 false</returns>
        public static bool Apply(Renderer visual, MeshLibrary.Entry entry, float size, BoxCollider box, float colliderScale)
        {
            if (entry.mesh == null || visual == null) return false;

            var filter = visual.GetComponent<MeshFilter>();
            if (filter == null) return false;
            filter.sharedMesh = entry.mesh;
            if (entry.material != null) visual.sharedMaterial = entry.material;

            var b = entry.mesh.bounds; // 메시 로컬 바운즈
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float s = maxDim > 0f ? size / maxDim : 1f;
            var t = visual.transform;
            t.localScale = Vector3.one * s;
            t.localPosition = -b.center * s; // 시각적 중심을 루트 원점으로

            if (box != null)
            {
                box.center = Vector3.zero;
                box.size = b.size * s * colliderScale;
            }
            return true;
        }
    }
}
