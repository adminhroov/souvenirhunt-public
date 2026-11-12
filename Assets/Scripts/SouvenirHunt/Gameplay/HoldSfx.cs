using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 홀드 폴백 차징 루프음을 오디오 에셋 없이 PCM 합성 (프로토타입 MiniGameSfx에서 이식).
    /// Collectible.UpdateHoldAudio(useHoldFallback + enableSfx일 때)만 사용. 그 외 수집음은 GameAudio 슬롯이 담당.
    /// </summary>
    public static class HoldSfx
    {
        const int SampleRate = 44100;
        static AudioClip s_HoldLoop;

        public static AudioClip HoldLoop
        {
            get
            {
                if (s_HoldLoop == null) s_HoldLoop = BuildHoldLoop();
                return s_HoldLoop;
            }
        }

        // 220Hz 기본음+옥타브 배음, 0.4초에 정수 사이클이라 이음새 없는 루프. 재생 측에서 pitch를 올려 상승감.
        static AudioClip BuildHoldLoop()
        {
            const float dur = 0.4f;
            int n = (int)(dur * SampleRate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float s = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.6f
                        + Mathf.Sin(2f * Mathf.PI * 440f * t) * 0.25f;
                data[i] = s * 0.5f;
            }
            var clip = AudioClip.Create("SfxHold", data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
