using UnityEngine;

namespace SouvenirHunt
{
    // ─ 2차 연동 인터페이스 (1차는 Mock으로 자리만 확보) ────────────

    /// <summary>DID 인증 게이트 자리. 2차: 오스리움 DID 연동.</summary>
    public interface IAuthGate
    {
        bool IsAuthorized();
    }

    /// <summary>보상 발급 자리. 2차: 3개 중 1개 선택 UI + NFT 발급 연동.</summary>
    public interface IRewardIssuer
    {
        void Issue(int collected, float elapsedSec);
    }

    public class MockAuthGate : IAuthGate
    {
        public bool IsAuthorized() => true; // 1차: 항상 통과
    }

    public class MockRewardIssuer : IRewardIssuer
    {
        public void Issue(int collected, float t)
            => Debug.Log($"[Mock 보상] {collected}개 · {t:F1}s — 출구에서 실물 수령 안내 (2차: NFT 발급)");
    }
}
