using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 런타임 예외 안전망 (Phase 5-1, 인수인계 §6 "앱은 반드시 계속 실행").
    /// Application.logMessageReceived로 Exception/Error를 세어 운영자 패널이 배지로 표시한다.
    /// 개발 도구(logcat) 없이도 현장에서 "뭔가 터졌다"를 운영자가 알 수 있게 하는 목적.
    /// 무한 누적 방지: 카운트와 마지막 메시지 1줄만 보관.
    /// </summary>
    public class RuntimeExceptionCounter : MonoBehaviour
    {
        public static RuntimeExceptionCounter Instance { get; private set; }

        public int Count { get; private set; }
        public string LastMessage { get; private set; } = "";

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (Instance == this) Instance = null;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            Count++;
            // 첫 줄만 (스택 제외) — 길면 자름
            LastMessage = condition.Length > 120 ? condition.Substring(0, 120) : condition;
        }

        /// <summary>운영자 패널용 1줄 (예외 0이면 null 반환).</summary>
        public string Summary() => Count > 0 ? $"⚠ 런타임 예외 {Count}건: {LastMessage}" : null;
    }
}
