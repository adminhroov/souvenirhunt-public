using System;
using System.Text;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 씬별 게임 매니저(Main=GameManager, Claw=ClawGameManager)를 운영자 패널·렌더 품질 적용기·정렬기 같은
    /// 공용 컴포넌트가 "어느 씬인지" 몰라도 쓸 수 있게 하는 최소 계약(09-03 인형뽑기 미니게임 추가 시 도입).
    /// Beam Pro 화면 프리팹은 씬 로드 전에 SDK가 Instantiate하므로 씬 참조가 불가 →
    /// 정적 GameSession.Current를 TryBind 패턴으로 재시도해 찾는다.
    /// </summary>
    public interface IGameSession
    {
        GameConfig Config { get; }
        string StateName { get; }
        float LastResetMs { get; }
        event Action OnGameReset;
        /// <summary>운영자 리셋(SCR-07). 어느 상태에서든 Ready로, config 재로드 포함.</summary>
        void ResetGame();
        /// <summary>BoothAligner.Start가 첫 정렬을 끝낸 뒤 호출 — 정렬 전 좌표에 스폰된 것을 재배치할 기회.</summary>
        void OnBoothAligned();
        /// <summary>Home 리센터로 BoothRoot가 재정렬된 뒤 호출 — 상태 변경 없이 위치만 보정.</summary>
        void OnRecentered();
        /// <summary>SCR-07 운영자 패널의 "적용된 설정" 블록에 세션 고유 줄을 추가.</summary>
        void AppendOperatorSummary(StringBuilder sb);
    }

    public static class GameSession
    {
        public static IGameSession Current { get; private set; }

        /// <summary>세션 교체 알림(null = 세션 종료). 프리팹 쪽 구독자는 이걸로 재바인드한다.</summary>
        public static event Action<IGameSession> Changed;

        public static void Register(IGameSession session)
        {
            if (session == null) return;
            Current = session;
            EventUtil.SafeInvoke(Changed, session);
        }

        public static void Unregister(IGameSession session)
        {
            if (session == null || !ReferenceEquals(Current, session)) return;
            Current = null;
            EventUtil.SafeInvoke(Changed, (IGameSession)null);
        }
    }

    /// <summary>
    /// 구독자 한 명의 예외가 나머지 배선을 끊지 않도록 격리해 이벤트를 발행한다 (Phase 5-1, 앱은 절대 죽지 않게).
    /// GameManager 전용이던 SafeInvoke를 ClawGameManager 등과 공유하기 위해 분리.
    /// </summary>
    public static class EventUtil
    {
        public static void SafeInvoke(Action evt)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
                try { ((Action)d).Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
        }

        public static void SafeInvoke<T>(Action<T> evt, T a)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
                try { ((Action<T>)d).Invoke(a); }
                catch (Exception e) { Debug.LogException(e); }
        }

        public static void SafeInvoke<T1, T2>(Action<T1, T2> evt, T1 a, T2 b)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
                try { ((Action<T1, T2>)d).Invoke(a, b); }
                catch (Exception e) { Debug.LogException(e); }
        }

        public static void SafeInvoke<T1, T2, T3>(Action<T1, T2, T3> evt, T1 a, T2 b, T3 c)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
                try { ((Action<T1, T2, T3>)d).Invoke(a, b, c); }
                catch (Exception e) { Debug.LogException(e); }
        }

        public static void SafeInvoke<T1, T2, T3, T4>(Action<T1, T2, T3, T4> evt, T1 a, T2 b, T3 c, T4 d4)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
                try { ((Action<T1, T2, T3, T4>)d).Invoke(a, b, c, d4); }
                catch (Exception e) { Debug.LogException(e); }
        }
    }
}
