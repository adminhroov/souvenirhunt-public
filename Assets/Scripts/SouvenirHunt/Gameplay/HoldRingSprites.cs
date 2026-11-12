using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 홀드 폴백(useHoldFallback) 게이지 링 스프라이트를 에셋 없이 코드로 생성 (프로토타입 MiniGame에서 이식).
    /// Collectible.BuildHoldRing만 사용 — 홀드 폴백이 꺼져 있으면 절대 호출되지 않는다(텍스처 생성 비용 0).
    /// </summary>
    public static class HoldRingSprites
    {
        static Sprite s_Ring;

        public static Sprite Ring
        {
            get
            {
                if (s_Ring == null) s_Ring = Build(0.40f);
                return s_Ring;
            }
        }

        // innerRatio: 0.4 → 두께 10%짜리 링
        static Sprite Build(float innerRatio)
        {
            const int size = 128;
            float outer = 0.5f * size;
            float inner = innerRatio * size;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var center = new Vector2(size * 0.5f, size * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float a = Mathf.Clamp01(outer - d);         // 바깥 경계 1px 스무딩
                    if (inner > 0f) a *= Mathf.Clamp01(d - inner); // 안쪽 경계 1px 스무딩
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
