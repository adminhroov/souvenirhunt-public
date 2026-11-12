using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 폭탄 폭발 연출(10-15 미션형) — 번쩍임·불꽃·파편·연기 파티클 + 폭발음(config audio일 때만). 바스켓 자식이라 재정렬 때 같이 움직인다.
    /// 파티클 시스템은 에디터(메뉴 30)가 코드로 만든다. 게임은 2개를 번갈아 써 연속 폭발에도 끊기지 않게 한다.
    /// OST에서 어두운 색은 투명이라 밝은 주황·노랑 가산만 쓴다.
    /// </summary>
    public class ClawExplosionVfx : MonoBehaviour
    {
        [SerializeField] ParticleSystem[] systems;
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip clip;

        public bool Playing
        {
            get
            {
                if (systems == null) return false;
                foreach (var ps in systems) if (ps != null && ps.IsAlive(true)) return true;
                return false;
            }
        }

        public void Play(Vector3 worldPos, bool audio)
        {
            transform.position = worldPos;
            if (systems != null)
                foreach (var ps in systems)
                {
                    if (ps == null) continue;
                    ps.Clear(true);
                    ps.Play(true);
                }
            if (audio && audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        public void StopAll()
        {
            if (systems != null)
                foreach (var ps in systems)
                    if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (audioSource != null) audioSource.Stop();
        }
    }
}
