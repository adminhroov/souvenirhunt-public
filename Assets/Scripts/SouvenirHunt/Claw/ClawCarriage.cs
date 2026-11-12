using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 레일 위 캐리지(kinematic Transform, Rigidbody 없음). 조이스틱 강도(중심 거리) ∝ 속도.
    /// 데드존 0.12 재스케일 후 1.5제곱(중심 근처 미세 조작), 가감속 1.5 m/s²로 부드럽게 → 진자 헤드가 자연스럽게 따라온다.
    /// 잡기 시퀀스 중엔 ManualLocked, 슈트로 갈 때는 StartAutoMove. 좌표는 BoothRoot 로컬(x=오른쪽, z=앞).
    /// </summary>
    public class ClawCarriage : MonoBehaviour
    {
        [Tooltip("config clawCarriageSpeed — 최대 속도 m/s")]
        public float MaxSpeed = 0.35f;

        const float Accel = 1.5f;      // m/s²
        const float AutoAccel = 1.1f;  // 10-22 배출구로 자동 이동: 천천히 출발·멈춤(사다리꼴 속도) — 멈출 때 집게가 앞뒤로 흔들린다
        const float Deadzone = 0.12f;
        const float Curve = 1.5f;

        /// <summary>true면 조이스틱 입력 무시(잡기 시퀀스 중) — 헤드가 Return에서 해제한다</summary>
        public bool ManualLocked { get; set; }
        /// <summary>true면 조이스틱 입력 무시(종료 다이얼로그 열림 등 UI가 스틱을 쓰는 동안) — GM이 관리</summary>
        public bool InputBlocked { get; set; }
        /// <summary>최근 프레임 가속도(BoothRoot 로컬 x,z) — 진자 구동용, |a| ≤ 3</summary>
        public Vector2 LocalAcceleration { get; private set; }
        public Vector2 Velocity => vel;
        public bool AutoMoving => auto;
        public bool AutoArrived { get; private set; }

        Vector2 vel;
        bool auto;
        Vector2 autoTarget;
        float autoSpeed;

        public Vector2 LocalXZ
        {
            get { var p = transform.localPosition; return new Vector2(p.x, p.z); }
        }

        public static Vector2 ClampToRail(Vector2 xz) => new Vector2(
            Mathf.Clamp(xz.x, ClawLayout.CarriageMinX, ClawLayout.CarriageMaxX),
            Mathf.Clamp(xz.y, ClawLayout.CarriageMinZ, ClawLayout.CarriageMaxZ));

        /// <summary>슈트 등 목표 지점으로 자동 이동 시작 (도착 판정 AutoArrived)</summary>
        public void StartAutoMove(Vector2 xz, float speed)
        {
            auto = true;
            autoTarget = ClampToRail(xz);
            autoSpeed = Mathf.Max(0.05f, speed);
            AutoArrived = false;
        }

        public void StopAuto()
        {
            auto = false;
            AutoArrived = false;
        }

        /// <summary>즉시 위치 지정(리셋·테스트). 속도 0.</summary>
        public void SetLocalXZ(Vector2 xz)
        {
            xz = ClampToRail(xz);
            var p = transform.localPosition;
            p.x = xz.x; p.z = xz.y; p.y = ClawLayout.RailY;
            transform.localPosition = p;
            vel = Vector2.zero;
            LocalAcceleration = Vector2.zero;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var prev = vel;
            var pos = LocalXZ;

            if (auto)
            {
                var to = autoTarget - pos;
                float dist = to.magnitude;
                if (dist < 0.003f && vel.magnitude < 0.06f)
                {
                    pos = autoTarget;
                    vel = Vector2.zero;
                    AutoArrived = true;
                }
                else
                {
                    // 가속·감속(남은 거리에서 멈출 수 있는 속도 = √(2·a·d)) — 예전엔 순간 출발·순간 정지
                    float want = Mathf.Min(autoSpeed, Mathf.Sqrt(2f * AutoAccel * Mathf.Max(0f, dist - 0.002f)));
                    var dir = dist > 1e-5f ? to / dist : Vector2.zero;
                    vel = Vector2.MoveTowards(vel, dir * want, AutoAccel * 1.6f * dt);
                    var step = vel * dt;
                    if (step.sqrMagnitude >= dist * dist) { pos = autoTarget; vel = dir * Mathf.Min(vel.magnitude, 0.05f); }
                    else pos += step;
                }
            }
            else
            {
                var target = Vector2.zero;
                if (!ManualLocked && !InputBlocked)
                {
                    var s = ClawPadInput.MachineStick;
                    float m = s.magnitude;
                    if (m > Deadzone)
                    {
                        float k = Mathf.Pow((m - Deadzone) / (1f - Deadzone), Curve);
                        target = s / m * (k * MaxSpeed);
                    }
                }
                vel = Vector2.MoveTowards(vel, target, Accel * dt);
                pos += vel * dt;
                var c = ClampToRail(pos);
                if (c.x != pos.x) vel.x = 0f;
                if (c.y != pos.y) vel.y = 0f;
                pos = c;
            }

            var lp = transform.localPosition;
            lp.x = pos.x; lp.z = pos.y; lp.y = ClawLayout.RailY;
            transform.localPosition = lp;

            LocalAcceleration = Vector2.ClampMagnitude((vel - prev) / dt, 3f);
        }
    }
}
