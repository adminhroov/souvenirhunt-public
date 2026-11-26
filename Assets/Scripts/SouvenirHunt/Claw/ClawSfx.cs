using System;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 11-26 인형뽑기 기계 효과음 — 사용자: "집게 움직일 때, 잡으러 내려올 때, 잡을 때, 올라갈 때, 잡힌 물건이 배출구로 통과될 때".
    /// · 움직일 때: 캐리지 속도(조이스틱·자동 이동 모두)에 따라 모터 소리가 켜지고 꺼진다(반복 재생, 속도에 따라 높낮이).
    /// · 내려올 때 / 올라갈 때: 케이블 길이가 변하는 속도로 윈치 소리 — 내려갈 땐 음이 낮아지고 올라갈 땐 높아진다(같은 소리, 높낮이만 다름).
    /// · 잡을 때: 발이 오므리기 시작(Attach)하면 서보 소리, 다 오므리고 올라가기 시작하면 '철컥'.
    /// · 배출구 통과: 떨어진 상품이 배출구 바닥에 닿는 순간(ClawHead.OnDelivered) '통통' — 점수 상품이면 기존 획득음과 겹쳐 난다.
    /// 소리는 시작 때 코드로 한 번 만든다(파일·씬 배선 없음 — A·B 같은 파일 하나로 동작). Resources/ClawSfx/{move,winch,grip,clack,chute}
    /// 이름의 AudioClip이 있으면 그것을 대신 쓴다(녹음한 소리로 바꿀 때). move·winch는 이음매 없이 반복되는 소리여야 한다.
    /// config audio=false 또는 clawSfxVolume=0이면 무음. 씬과 무관하게 하나(시작 때 자동 생성), 집게를 건드리지 않고 상태만 읽는다. 예외를 던지지 않는다.
    /// </summary>
    public class ClawSfx : MonoBehaviour
    {
        public static ClawSfx Instance { get; private set; }

        const int Rate = 44100;
        const double Tau = 2.0 * Math.PI;
        // 소리별 기본 크기(clawSfxVolume을 곱한다) — 반복음은 낮게, 순간음은 또렷하게
        const float MoveBase = 0.30f, WinchBase = 0.34f, GripBase = 0.45f, ClackBase = 0.75f, ChuteBase = 0.80f;

        AudioSource moveSrc, winchSrc, shotSrc;
        AudioClip moveClip, winchClip, gripClip, clackClip, chuteClip;
        ClawHead head, boundHead;
        ClawCarriage carriage;
        ClawHead.Phase prevPhase = ClawHead.Phase.Idle;
        float prevHang, nextFind, master;
        bool haveHang, ready;

        /// <summary>셀프테스트·로그 — 지금까지 낸 순간음 수</summary>
        public int GripCount { get; private set; }
        public int ChuteCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("ClawSfx");
            DontDestroyOnLoad(go);
            go.AddComponent<ClawSfx>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            try
            {
                float t0 = Time.realtimeSinceStartup;
                moveSrc = NewSource(true);
                winchSrc = NewSource(true);
                shotSrc = NewSource(false);
                shotSrc.volume = 1f;
                int fromFile = 0;
                moveClip = Load("move", () => SynthMotorLoop(96f, 700f, 520f, 12f, 0.18f, 1440f, 24f, 0.10f, 0.16f, 11u), ref fromFile);
                winchClip = Load("winch", () => SynthMotorLoop(168f, 1150f, 700f, 21f, 0.12f, 2016f, 42f, 0.14f, 0.20f, 23u), ref fromFile);
                gripClip = Load("grip", SynthGrip, ref fromFile);
                clackClip = Load("clack", SynthClack, ref fromFile);
                chuteClip = Load("chute", SynthChute, ref fromFile);
                moveSrc.clip = moveClip;
                winchSrc.clip = winchClip;
                ready = true;
                Debug.Log($"[Sfx] 인형뽑기 효과음 준비 — 움직임·오르내림·잡기·배출구 ({(Time.realtimeSinceStartup - t0) * 1000f:0}ms, 파일로 바꾼 것 {fromFile}개)");
            }
            catch (Exception e) { ready = false; Debug.LogWarning("[Sfx] 효과음 준비 실패 — 소리 없이 진행: " + e.Message); }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Bind(null);
            Instance = null;
        }

        AudioSource NewSource(bool loop)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = 0f; // 다른 효과음(획득·폭발·보상)과 같이 2D
            a.volume = 0f;
            return a;
        }

        static AudioClip Load(string name, Func<float[]> synth, ref int fromFile)
        {
            var file = Resources.Load<AudioClip>("ClawSfx/" + name);
            if (file != null) { fromFile++; return file; }
            var data = synth();
            var clip = AudioClip.Create("ClawSfx_" + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Bind(ClawHead h)
        {
            if (boundHead == h) return;
            if (boundHead != null) boundHead.OnDelivered -= HandleDelivered;
            boundHead = h;
            if (h != null) h.OnDelivered += HandleDelivered;
        }

        void HandleDelivered(ClawItem item)
        {
            if (!Shot(chuteClip, ChuteBase)) return;
            ChuteCount++;
            Debug.Log($"[Sfx] 배출구 통과음 #{ChuteCount}");
        }

        bool Shot(AudioClip clip, float volume)
        {
            if (!ready || master <= 0f || clip == null || shotSrc == null) return false;
            shotSrc.PlayOneShot(clip, volume * master);
            return true;
        }

        void Update()
        {
            if (!ready) return;
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);

            IGameSession s = GameSession.Current;
            var cfg = s is ClawSessionBase ? s.Config : null;
            master = cfg != null && cfg.audio ? Mathf.Clamp01(cfg.clawSfxVolume) : 0f;
            if (cfg == null)
            {
                // 인형뽑기 화면이 아님(인증·씬 전환) — 반복음을 끄고 집게를 놓는다
                Bind(null);
                head = null; carriage = null; haveHang = false; prevPhase = ClawHead.Phase.Idle;
                Drive(moveSrc, 0f, 1f, dt);
                Drive(winchSrc, 0f, 1f, dt);
                return;
            }
            if (head == null || carriage == null)
            {
                haveHang = false;
                if (Time.unscaledTime >= nextFind)
                {
                    nextFind = Time.unscaledTime + 0.5f;
                    head = FindFirstObjectByType<ClawHead>();
                    carriage = FindFirstObjectByType<ClawCarriage>();
                    Bind(head);
                    prevPhase = head != null ? head.Current : ClawHead.Phase.Idle;
                }
                if (head == null || carriage == null)
                {
                    Drive(moveSrc, 0f, 1f, dt);
                    Drive(winchSrc, 0f, 1f, dt);
                    return;
                }
            }

            // ── 움직일 때: 캐리지 속도(조이스틱, 배출구로·가운데로 자동 이동) ──
            float mv = Mathf.Clamp(carriage.Velocity.magnitude / Mathf.Max(0.05f, carriage.MaxSpeed), 0f, 1.6f);
            float moveVol = mv > 0.06f ? Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(mv)) : 0f;
            Drive(moveSrc, moveVol * MoveBase * master, 0.9f + 0.22f * mv, dt);

            // ── 내려올 때 / 올라갈 때: 케이블 길이(캐리지 ~ 집게)가 변하는 속도 ──
            var ph = head.Current;
            float hang = (carriage.transform.position - head.transform.position).magnitude;
            float vy = haveHang ? (hang - prevHang) / dt : 0f; // + = 내려감
            prevHang = hang;
            haveHang = true;
            if (Mathf.Abs(vy) > 4f) vy = 0f; // 리셋·재정렬로 자리가 순간 바뀜
            bool down = ph == ClawHead.Phase.Descend || ph == ClawHead.Phase.DropLower;
            bool up = ph == ClawHead.Phase.Ascend || ph == ClawHead.Phase.Return || ph == ClawHead.Phase.Recoil;
            float wn = 0f, pitch = 1f;
            if (down && vy > 0.03f) { wn = Mathf.Clamp01(vy / Mathf.Max(0.05f, head.DescendSpeed)); pitch = 0.92f - 0.14f * wn; }
            else if (up && vy < -0.03f) { wn = Mathf.Clamp01(-vy / Mathf.Max(0.05f, head.AscendSpeed)); pitch = 1.02f + 0.20f * wn; }
            Drive(winchSrc, (wn > 0f ? Mathf.Lerp(0.5f, 1f, wn) : 0f) * WinchBase * master, pitch, dt);

            // ── 잡을 때: 오므리기 시작 = 서보, 다 오므리고 올라가기 시작 = 철컥 ──
            if (ph != prevPhase)
            {
                if (ph == ClawHead.Phase.Attach)
                {
                    if (Shot(gripClip, GripBase)) { GripCount++; Debug.Log($"[Sfx] 잡기음 #{GripCount}"); }
                }
                else if (prevPhase == ClawHead.Phase.Attach && ph == ClawHead.Phase.Ascend) Shot(clackClip, ClackBase);
                prevPhase = ph;
            }
        }

        /// <summary>반복음 하나를 목표 크기·높낮이로 부드럽게(딸깍 소리 없이 켜고 끔)</summary>
        static void Drive(AudioSource src, float target, float pitch, float dt)
        {
            if (src == null || src.clip == null) return;
            if (target > 0f && !src.isPlaying) { src.volume = 0f; src.pitch = pitch; src.Play(); }
            src.volume = Mathf.MoveTowards(src.volume, target, (target > src.volume ? 3f : 2.2f) * dt);
            if (src.isPlaying)
            {
                src.pitch = Mathf.Lerp(src.pitch, pitch, 1f - Mathf.Exp(-dt * 12f));
                if (target <= 0f && src.volume <= 0.0005f) src.Stop();
            }
        }

        // ─────────────────────────── 소리 만들기(시작 때 한 번) ───────────────────────────
        // 안경 스피커는 작아 200Hz 아래가 거의 안 들린다 → 기음은 낮게 두되 300~3000Hz 배음에 힘을 싣는다

        struct Rng
        {
            uint s;
            public Rng(uint seed) { s = seed; }
            public float Next() { s = s * 1664525u + 1013904223u; return ((s >> 8) & 0xFFFFFF) / 8388608f - 1f; }
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 0f;
            foreach (float v in d) m = Mathf.Max(m, Mathf.Abs(v));
            if (m < 1e-6f) return;
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        static void FadeOut(float[] d, float seconds)
        {
            int n = Mathf.Min(d.Length, (int)(seconds * Rate));
            for (int i = 0; i < n; i++) d[d.Length - 1 - i] *= i / (float)n;
        }

        /// <summary>
        /// 모터 반복음 1초 — 배음 묶음(포먼트로 음색) + 톱니 맞물림 떨림(amHz) + 높은 기어 음(whineHz) + 걸러낸 잡음.
        /// 주파수가 모두 정수 Hz라 1초 끝과 처음이 그대로 이어진다(잡음은 끝 50ms를 처음에 겹쳐 잇는다)
        /// </summary>
        static float[] SynthMotorLoop(float f0, float formantHz, float formantWidth, float amHz, float amDepth,
                                      float whineHz, float fmHz, float whineAmp, float noiseAmp, uint seed)
        {
            int n = Rate;
            var d = new float[n];
            var rng = new Rng(seed);
            int harmonics = Mathf.Clamp((int)(6000f / f0), 4, 40);
            var w = new float[harmonics + 1];
            var p = new double[harmonics + 1];
            for (int h = 1; h <= harmonics; h++)
            {
                float x = (h * f0 - formantHz) / formantWidth;
                w[h] = (0.3f + Mathf.Exp(-x * x)) / Mathf.Pow(h, 0.75f);
                p[h] = (rng.Next() + 1f) * Math.PI; // 위상을 흩어 뾰족한 펄스가 아니라 고른 웅웅 소리로
            }
            float tonalPeak = 0f;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double v = 0.0;
                for (int h = 1; h <= harmonics; h++) v += w[h] * Math.Sin(Tau * h * f0 * t + p[h]);
                v *= 1.0 + amDepth * Math.Sin(Tau * amHz * t);
                v += whineAmp * Math.Sin(Tau * whineHz * t + 0.6 * Math.Sin(Tau * fmHz * t));
                d[i] = (float)v;
                tonalPeak = Mathf.Max(tonalPeak, Mathf.Abs(d[i]));
            }
            // 잡음: 900~3200Hz만 남겨 벨트·공기 소리
            int overlap = Rate / 20;
            var nz = new float[n + overlap];
            float lp = 0f, prevLp = 0f, hp = 0f, noisePeak = 0f;
            float aLp = 1f - Mathf.Exp(-(float)Tau * 3200f / Rate), aHp = Mathf.Exp(-(float)Tau * 900f / Rate);
            for (int i = 0; i < nz.Length; i++)
            {
                lp += aLp * (rng.Next() - lp);
                hp = aHp * (hp + lp - prevLp);
                prevLp = lp;
                nz[i] = hp;
                noisePeak = Mathf.Max(noisePeak, Mathf.Abs(hp));
            }
            float k = noisePeak > 1e-6f ? noiseAmp * tonalPeak / noisePeak : 0f;
            for (int i = 0; i < n; i++)
            {
                float z = nz[i];
                if (i < overlap) { float a = i / (float)overlap; z = nz[i] * Mathf.Sqrt(a) + nz[n + i] * Mathf.Sqrt(1f - a); }
                d[i] += k * z;
            }
            Normalize(d, 0.8f);
            return d;
        }

        /// <summary>잡기(오므림) — 짧게 음이 올라가는 서보 소리 0.2초</summary>
        static float[] SynthGrip()
        {
            const float dur = 0.2f;
            int n = (int)(dur * Rate);
            var d = new float[n];
            var rng = new Rng(77u);
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                phase += 380.0 * Math.Pow(2.4, t / dur) / Rate; // 380 → 912Hz
                double v = 0.0;
                for (int h = 1; h <= 6; h++) v += Math.Sin(Tau * h * phase) / h;
                v *= 1.0 + 0.25 * Math.Sin(Tau * 38.0 * t);
                v += 0.12 * rng.Next();
                float env = Mathf.Min(1f, t / 0.008f) * Mathf.Min(1f, (dur - t) / 0.04f);
                d[i] = (float)v * env;
            }
            Normalize(d, 0.7f);
            return d;
        }

        /// <summary>잡기(물림) — 금속 발이 맞물리는 '철컥' + 작은 뒤울림</summary>
        static float[] SynthClack()
        {
            var d = new float[(int)(0.32f * Rate)];
            var rng = new Rng(131u);
            Hit(d, 0f, 1f, ref rng);
            Hit(d, 0.062f, 0.32f, ref rng);
            FadeOut(d, 0.02f);
            Normalize(d, 0.8f);
            return d;
        }

        static void Hit(float[] d, float t0, float amp, ref Rng rng)
        {
            float prevRaw = 0f, hp = 0f, aHp = Mathf.Exp(-(float)Tau * 1400f / Rate);
            for (int i = (int)(t0 * Rate); i < d.Length; i++)
            {
                float t = i / (float)Rate - t0;
                float raw = rng.Next();
                hp = aHp * (hp + raw - prevRaw);
                prevRaw = raw;
                double v = 0.9 * hp * Math.Exp(-t / 0.010);
                v += 0.34 * Math.Sin(Tau * 1240.0 * t) * Math.Exp(-t / 0.045);
                v += 0.30 * Math.Sin(Tau * 2170.0 * t) * Math.Exp(-t / 0.060);
                v += 0.20 * Math.Sin(Tau * 3390.0 * t) * Math.Exp(-t / 0.038);
                v += 0.28 * Math.Sin(Tau * 540.0 * t) * Math.Exp(-t / 0.030);
                d[i] += amp * (float)v;
            }
        }

        /// <summary>배출구 통과 — 속 빈 통에 떨어져 튀는 '통, 토독'(튈수록 작고 촘촘하게)</summary>
        static float[] SynthChute()
        {
            var d = new float[(int)(0.62f * Rate)];
            var rng = new Rng(211u);
            float[] at = { 0f, 0.13f, 0.22f, 0.285f, 0.33f };
            float[] amp = { 1f, 0.5f, 0.28f, 0.16f, 0.09f };
            float[] tune = { 1f, 1.07f, 0.95f, 1.10f, 1.02f };
            for (int b = 0; b < at.Length; b++) Bump(d, at[b], amp[b], tune[b], ref rng);
            FadeOut(d, 0.03f);
            Normalize(d, 0.8f);
            return d;
        }

        static void Bump(float[] d, float t0, float amp, float tune, ref Rng rng)
        {
            float lp = 0f, aLp = 1f - Mathf.Exp(-(float)Tau * 2200f / Rate);
            double low = 0.0;
            for (int i = (int)(t0 * Rate); i < d.Length; i++)
            {
                float t = i / (float)Rate - t0;
                if (t > 0.35f) break;
                lp += aLp * (rng.Next() - lp);
                low += (92.0 + 58.0 * Math.Exp(-t / 0.03)) * tune / Rate; // 150 → 92Hz(큰 스피커에서만 들리는 무게감)
                double v = 0.8 * lp * Math.Exp(-t / 0.009);
                v += 0.50 * Math.Sin(Tau * 310.0 * tune * t) * Math.Exp(-t / 0.036);
                v += 0.42 * Math.Sin(Tau * 468.0 * tune * t) * Math.Exp(-t / 0.028);
                v += 0.30 * Math.Sin(Tau * 742.0 * tune * t) * Math.Exp(-t / 0.018);
                v += 0.60 * Math.Sin(Tau * low) * Math.Exp(-t / 0.05);
                d[i] += amp * (float)v;
            }
        }
    }
}
