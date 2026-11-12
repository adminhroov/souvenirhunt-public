using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 오디오 훅 (Phase 5-2, 인수인계 §6 "오디오 훅은 코드에 미리 확보"). 1차는 무음 —
    /// 이벤트 지점만 배선하고 클립 슬롯은 비워둔다. 2차에서 인스펙터 슬롯에 음원만 꽂으면 소리가 난다.
    /// config.txt의 audio=true일 때만 재생. 클립이 null이면 무동작(예외 없음).
    /// GameManager 이벤트만 구독(직접 참조 금지) — HudController와 동일한 TryBind 재시도 패턴.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        [Header("2D UI 사운드 (패널·카운트다운·결과)")]
        [SerializeField] AudioClip start;
        [SerializeField] AudioClip countdownTick;
        [SerializeField] AudioClip countdownGo;
        [SerializeField] AudioClip warningEnter;
        [SerializeField] AudioClip success;
        [SerializeField] AudioClip timeout;
        [SerializeField] AudioClip reset;

        [Header("3D 월드 사운드 (수집 위치)")]
        [SerializeField] AudioClip collect;

        [Header("소스 (미지정 시 자동 생성)")]
        [SerializeField] AudioSource ui2d;
        [SerializeField] AudioSource world3d;

        GameManager gm;
        bool bound, audioOn;
        float prevRemain = float.MaxValue;

        void Awake()
        {
            if (ui2d == null)
            {
                ui2d = gameObject.AddComponent<AudioSource>();
                ui2d.spatialBlend = 0f;
                ui2d.playOnAwake = false;
            }
            if (world3d == null)
            {
                world3d = gameObject.AddComponent<AudioSource>();
                world3d.spatialBlend = 1f;
                world3d.playOnAwake = false;
            }
        }

        void OnEnable() => TryBind();
        void Start() => TryBind();

        void OnDisable()
        {
            if (!bound || gm == null) return;
            gm.OnStateChanged -= HandleState;
            gm.OnCountdown -= HandleCountdown;
            gm.OnCollected -= HandleCollected;
            gm.OnTimeTick -= HandleTimeTick;
            gm.OnGameResult -= HandleResult;
            gm.OnGameReset -= HandleReset;
            bound = false;
        }

        void TryBind()
        {
            if (bound) return;
            gm = GameManager.Instance;
            if (gm == null) return;
            gm.OnStateChanged += HandleState;
            gm.OnCountdown += HandleCountdown;
            gm.OnCollected += HandleCollected;
            gm.OnTimeTick += HandleTimeTick;
            gm.OnGameResult += HandleResult;
            gm.OnGameReset += HandleReset;
            bound = true;
            HandleReset();
        }

        void HandleReset()
        {
            audioOn = gm != null && gm.Config != null && gm.Config.audio;
            prevRemain = float.MaxValue;
            Play2D(reset);
        }

        void HandleState(GameManager.State s)
        {
            if (s == GameManager.State.Countdown) Play2D(start);
        }

        void HandleCountdown(int n)
        {
            if (n > 0) Play2D(countdownTick);
            else Play2D(countdownGo);
        }

        void HandleCollected(int cur, int total)
        {
            // 수집 위치 = GameManager가 기록한 마지막 수집품 좌표 (3D 정위)
            if (!audioOn || collect == null) return;
            world3d.transform.position = gm != null ? gm.LastCollectedPosition : transform.position;
            world3d.PlayOneShot(collect);
        }

        void HandleTimeTick(float remain)
        {
            float warn = gm != null ? gm.WarnThresholdSec : 30f;
            if (prevRemain > warn && remain <= warn) Play2D(warningEnter); // 경고 구간 진입 1회
            prevRemain = remain;
        }

        void HandleResult(bool ok, int collected, float elapsed)
        {
            Play2D(ok ? success : timeout);
        }

        void Play2D(AudioClip clip)
        {
            if (!audioOn || clip == null || ui2d == null) return;
            ui2d.PlayOneShot(clip);
        }
    }
}
