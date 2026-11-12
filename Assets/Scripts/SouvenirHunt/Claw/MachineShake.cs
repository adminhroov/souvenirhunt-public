using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 폭발 시 기계 흔들림 — 시각 루트(기계 몸체·리그)만 감쇠 노이즈로 흔든다. 콜라이더 상자는 움직이지 않아
    /// 접촉 요동·autoSyncTransforms 비용이 없다(잡기 판정은 폭발 시퀀스 중 잠겨 있어 1cm 오프셋 무해).
    /// </summary>
    public class MachineShake : MonoBehaviour
    {
        [SerializeField] Transform[] targets;
        [SerializeField] float amplitude = 0.012f;
        [SerializeField] float duration = 0.7f;
        [SerializeField] float frequency = 22f;
        [SerializeField] float decayTau = 0.25f;

        Vector3[] basePos;
        float t = -1f;
        float curAmp, curDur; // 10-28 이번 흔들림 값 — 예전엔 Shake(amp, dur)가 직렬화 기본값(amplitude·duration)을 덮어써 무인자 Shake()가 바뀌었다

        public bool Shaking => t >= 0f;

        public void Shake() => Shake(amplitude, duration);

        public void Shake(float amp, float dur)
        {
            if (targets == null || targets.Length == 0) return;
            if (t < 0f)
            {
                basePos = new Vector3[targets.Length];
                for (int i = 0; i < targets.Length; i++) basePos[i] = targets[i] != null ? targets[i].localPosition : Vector3.zero;
            }
            curAmp = amp;
            curDur = dur;
            t = 0f;
        }

        public void StopAndRestore()
        {
            if (t < 0f) return;
            t = -1f;
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null) targets[i].localPosition = basePos[i];
        }

        void Update()
        {
            if (t < 0f) return;
            t += Time.deltaTime;
            if (t >= curDur) { StopAndRestore(); return; }

            float env = Mathf.Exp(-t / decayTau) * curAmp;
            float w = t * frequency * Mathf.PI * 2f;
            var offset = new Vector3(Mathf.Sin(w) * env, Mathf.Sin(w * 1.3f + 1f) * env * 0.6f, Mathf.Cos(w * 0.8f) * env);
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null) targets[i].localPosition = basePos[i] + offset;
        }

        void OnDisable() => StopAndRestore();
    }
}
