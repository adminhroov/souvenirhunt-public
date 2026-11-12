using System;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 10-28 기계 다가오기 연출(사용자 "눈 인증 후 배경이 보인 상태에서 카메라가 줌 인하면서 게임 시작 — 조금 멀리 시작해 지금 위치까지, 점점 가속하듯").
    /// XR에서는 카메라(머리)를 못 움직이므로 BoothRoot를 정면(눈에서 기계 쪽, 수평)으로 distance m 멀리 두었다가
    /// seconds 동안 ease-in(k^power)으로 제자리(정렬 자세)까지 끌어온다. 마지막 프레임에 정렬 자세로 정확히 맞춘다.
    /// 평행이동만(스케일 금지 — 상품 크기·중력·집게발 막대 캐시가 깨진다). 회전은 정렬 자세 그대로.
    /// 매 프레임 루트를 직접 쓰고 이동 델타(= 지금 행렬 × 자기가 마지막에 쓴 행렬의 역)를 OnMoved로 알린다 — 세션이 월드 공간 상품·헤드 연출 좌표를
    /// 같은 델타로 옮긴다(ClawSessionBase.CarryWorld). 중간에 누가 루트를 옮겨도(BoothAligner.Align 등) 다음 쓰기에서 자기 자세로 돌아오며 상품도 맞춰진다.
    /// 세션(ClawSessionBase)이 BoothRoot에 런타임 자동 부착(PrizeLink 패턴) — 메뉴 30도 미리 붙인다. 다른 스크립트보다 먼저(−50) 움직인다.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class BoothApproach : MonoBehaviour
    {
        Vector3 finalPos;
        Quaternion finalRot = Quaternion.identity;
        float distance, seconds = 1.5f, power = 3f, t;
        Matrix4x4 lastApplied = Matrix4x4.identity;
        Action onDone;
        bool finishing;

        /// <summary>Prepare부터 도착(Finish)·Stop까지 — 루트가 연출 자세에 있음</summary>
        public bool Active { get; private set; }
        /// <summary>Begin부터 도착까지 — 다가오는 중</summary>
        public bool Running { get; private set; }
        /// <summary>시간 진행 0~1(대기 중 0, 끝나면 1)</summary>
        public float Progress => Running ? Mathf.Clamp01(t / seconds) : Active ? 0f : 1f;
        public float Distance => distance;
        public Vector3 FinalPosition => finalPos;
        public Quaternion FinalRotation => finalRot;
        /// <summary>이번 이동 중 가장 긴 프레임(ms)·프레임 수 — 로그·셀프테스트</summary>
        public float MaxFrameMs { get; private set; }
        public int Frames { get; private set; }
        /// <summary>루트가 옮겨짐(월드 델타) — 세션이 상품·헤드 좌표를 같이 옮긴다</summary>
        public event Action<Matrix4x4> OnMoved;

        /// <summary>멀어지는 방향 = 정렬 자세의 정면(눈 → 기계, 수평)</summary>
        Vector3 Away
        {
            get
            {
                var f = finalRot * Vector3.forward;
                f.y = 0f;
                return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            }
        }

        /// <summary>진행 k(0 = 가장 멀리, 1 = 제자리)의 루트 위치</summary>
        public Vector3 PositionAt(float k) => finalPos + Away * (distance * (1f - k));

        float CurrentK => Running ? Mathf.Pow(Mathf.Clamp01(t / seconds), power) : 0f;

        /// <summary>제자리(정렬 자세)를 기억하고 루트를 distance m 멀리 놓는다 — 상품 배치 전에 부른다(델타 알림 없음)</summary>
        public void Prepare(Vector3 pos, Quaternion rot, float dist)
        {
            finalPos = pos;
            finalRot = rot;
            distance = Mathf.Max(0f, dist);
            t = 0f;
            Running = false;
            onDone = null;
            MaxFrameMs = 0f;
            Frames = 0;
            Active = true;
            transform.SetPositionAndRotation(PositionAt(0f), finalRot);
            lastApplied = transform.localToWorldMatrix;
            Debug.Log($"[Approach] PREPARE {distance:F1}m 멀리에서 대기");
        }

        /// <summary>다가오기 시작 — 도착하면 done(같은 프레임). 준비 안 됐으면 바로 done</summary>
        public void Begin(float sec, float pow, Action done)
        {
            if (!Active) { done?.Invoke(); return; }
            seconds = Mathf.Max(0.05f, sec);
            power = Mathf.Max(1f, pow);
            onDone = done;
            t = 0f;
            MaxFrameMs = 0f;
            Frames = 0;
            Running = true;
            Debug.Log($"[Approach] START {distance:F1}m {seconds:F1}s p={power:0.#}");
        }

        /// <summary>재정렬 — 제자리만 새 정렬 자세로(진행률 그대로). 루트·상품은 지금 진행률 자세로 바로 옮긴다</summary>
        public void Retarget(Vector3 pos, Quaternion rot)
        {
            if (!Active) return;
            finalPos = pos;
            finalRot = rot;
            Apply(CurrentK);
        }

        /// <summary>바로 도착(제자리로 정확히) → done. 재진입 안전</summary>
        public void Finish()
        {
            if (!Active || finishing) return;
            finishing = true;
            try
            {
                Apply(1f);
                Active = false;
                Running = false;
                Debug.Log($"[Approach] ARRIVED (max frame {MaxFrameMs:F1} ms, {Frames} frames)");
                var cb = onDone;
                onDone = null;
                try { cb?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally { finishing = false; }
        }

        /// <summary>리셋 — 콜백 없이 끝. 루트는 제자리로(상품은 세션이 풀로 돌려보낸다)</summary>
        public void Stop()
        {
            if (!Active) return;
            Active = false;
            Running = false;
            onDone = null;
            transform.SetPositionAndRotation(finalPos, finalRot);
            lastApplied = transform.localToWorldMatrix;
            Debug.Log("[Approach] STOP — 리셋, 루트 제자리");
        }

        void Update()
        {
            if (!Running) return;
            float dt = Time.deltaTime;
            Frames++;
            MaxFrameMs = Mathf.Max(MaxFrameMs, dt * 1000f);
            t += dt;
            if (t >= seconds) { Finish(); return; }
            Apply(CurrentK);
        }

        void Apply(float k)
        {
            transform.SetPositionAndRotation(PositionAt(k), finalRot);
            var now = transform.localToWorldMatrix;
            var delta = now * lastApplied.inverse;
            lastApplied = now;
            try { OnMoved?.Invoke(delta); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
