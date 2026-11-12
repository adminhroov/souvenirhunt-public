using System;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 아이템 규격(10-15 미션형 복원: 세라믹 그릇·유리 그릇·캡슐·폭탄). 헤드·바스켓·풀·가이드·아이템 프리팹이 같은 에셋을 배선받는다(메뉴 30).
    /// 크기는 **헤드 지름 대비 비율** — 기계(ClawLayout)가 바뀌면 아이템이 자동으로 비례한다(사용자: 기계·배경은 또 바뀔 예정).
    /// 그릇 메시·충돌체는 폭 1(정규화)로 구워져 있고, 런타임에 Width(kind)로 늘린다. 미배선이면 Default — 앱은 계속.
    /// </summary>
    [CreateAssetMenu(menuName = "SouvenirHunt/Claw Item Spec", fileName = "ClawSpec")]
    public class ClawItemSpec : ScriptableObject
    {
        [Serializable]
        public struct KindSize
        {
            [Tooltip("폭 = 헤드 지름 × 이 값")] public float widthToHead;
            [Tooltip("높이 = 폭 × 이 값(구는 1)")] public float heightToWidth;
            [Tooltip("질량 kg")] public float mass;
        }

        [Header("종류별 크기(헤드 지름 대비) — Model3 헤드 지름 0.26m 기준 세라믹 0.13·유리 0.12·캡슐/폭탄 0.10m")]
        public KindSize ceramic = new KindSize { widthToHead = 0.50f, heightToWidth = 0.50f, mass = 0.06f };
        public KindSize glass = new KindSize { widthToHead = 0.46f, heightToWidth = 0.50f, mass = 0.05f };
        public KindSize capsule = new KindSize { widthToHead = 0.38f, heightToWidth = 1f, mass = 0.04f };
        public KindSize bomb = new KindSize { widthToHead = 0.38f, heightToWidth = 1f, mass = 0.06f };
        [Tooltip("10-22 키링 — 폭 = 큐브 몸통 크기(체인·고리는 바깥으로 약 1.8배까지 시각만). 사용자 '그릇 정도'")]
        public KindSize keyring = new KindSize { widthToHead = 0.42f, heightToWidth = 1f, mass = 0.05f };

        [Header("배치")]
        [Tooltip("배치 격자 간격 여유(가장 큰 폭 + 이 값)")]
        public float gap = 0.02f;
        [Tooltip("배치 셀 위치 지터")]
        public float jitter = 0.008f;
        [Tooltip("배치 최대 층")]
        public int maxPlaceLayers = 2;

        [Header("개수")]
        [Tooltip("풀 사전 생성 수 = 배치 개수 합 상한")]
        public int poolSize = 280; // 10-15 60(캡슐 40) → 10-20 100: 받침 더미 위 한 층(약 43~52) + 승격 몫(ClawBasket.PromotionReserve 30) → 10-22 280: + 진짜 맨 윗층 캡슐(받침 맨 윗층 조각 수)

        [Header("물리(개별 바디 — 전역 설정은 건드리지 않음)")]
        public float linearDamping = 0.6f;
        public float angularDamping = 1.5f;
        public float sleepThreshold = 0.02f;
        public int solverIterations = 8;
        public int solverVelocityIterations = 2;
        public float contactOffset = 0.005f;

        /// <summary>폭이 집게 안에 들어가게 상한(헤드 반지름 × 1.9)</summary>
        static float MaxWidth => ClawLayout.HeadRadius * 1.9f;

        public KindSize Size(ClawItem.Kind k) => k switch
        {
            ClawItem.Kind.Ceramic => ceramic,
            ClawItem.Kind.Glass => glass,
            ClawItem.Kind.Capsule => capsule,
            ClawItem.Kind.Keyring => keyring,
            _ => bomb,
        };

        /// <summary>종류별 폭 m(그릇 = 테두리 지름, 구 = 지름)</summary>
        public float Width(ClawItem.Kind k) => Mathf.Min(ClawLayout.HeadRadius * 2f * Mathf.Max(0.05f, Size(k).widthToHead) * ClawLayout.ItemSizeFactor, MaxWidth); // 10-20 아이템 배율 따로
        public float Height(ClawItem.Kind k) => Width(k) * Mathf.Max(0.05f, Size(k).heightToWidth);
        public float HalfHeight(ClawItem.Kind k) => Height(k) * 0.5f;

        public float MaxItemWidth => Mathf.Max(Mathf.Max(Mathf.Max(Width(ClawItem.Kind.Ceramic), Width(ClawItem.Kind.Glass)), Mathf.Max(Width(ClawItem.Kind.Capsule), Width(ClawItem.Kind.Bomb))), Width(ClawItem.Kind.Keyring));
        public float MaxItemHeight => Mathf.Max(Mathf.Max(Mathf.Max(Height(ClawItem.Kind.Ceramic), Height(ClawItem.Kind.Glass)), Mathf.Max(Height(ClawItem.Kind.Capsule), Height(ClawItem.Kind.Bomb))), Height(ClawItem.Kind.Keyring));
        /// <summary>배치 셀 한 칸(가장 큰 폭 + 여유)</summary>
        public float CellSize => MaxItemWidth + gap;
        /// <summary>셀 중심이 벽에서 떨어져야 하는 거리</summary>
        public float CellClear => MaxItemWidth * 0.5f + jitter + 0.005f;
        /// <summary>집게 아래에서 잡을 수 있는 수평 반경(아이템 중심 기준)</summary>
        public float GrabRadius => ClawLayout.HeadRadius * 0.8f;
        /// <summary>집게 하단에서 아래로 이만큼까지 윗면이 닿아 있는 아이템이 후보</summary>
        public float GrabReach => MaxItemHeight * 0.6f;
        /// <summary>잡은 아이템 중심 = 헤드 중심 아래(집게발 안쪽으로 HeadAttachLift만큼 올림)</summary>
        public float AttachOffsetY(ClawItem.Kind k) => -ClawLayout.HeadHalfHeight + HoldTop(k) - HalfHeight(k);
        /// <summary>
        /// 쥔 상품 윗면이 발끝 위 몇 m인가(10-22) — 발이 감쌀 수 있는 높이(HeadAttachLift)와 상품 높이 + 1cm 중 작은 것.
        /// 납작한 그릇은 바닥이 발끝 1cm 위(발이 밑을 받침), 키 큰 것은 아래쪽이 발끝 밑으로 조금 나온다. 집게가 이 자세가 되도록 내려가 쥔다(ClawHead.GripTip)
        /// </summary>
        public float HoldTop(ClawItem.Kind k) => Mathf.Min(ClawLayout.HeadAttachLift, Height(k) + 0.01f);

        static ClawItemSpec fallback;
        public static ClawItemSpec Default
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<ClawItemSpec>();
                    fallback.name = "ClawSpec(Default)";
                    fallback.hideFlags = HideFlags.HideAndDontSave;
                }
                return fallback;
            }
        }

        public static ClawItemSpec Or(ClawItemSpec s) => s != null ? s : Default;

        /// <summary>개별 바디 물리 설계값(종류별 질량)</summary>
        public void ConfigureBody(Rigidbody rb, ClawItem.Kind k)
        {
            rb.mass = Mathf.Max(0.005f, Size(k).mass);
            rb.linearDamping = linearDamping;
            rb.angularDamping = angularDamping;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 2f;
            rb.maxAngularVelocity = 20f;
            rb.sleepThreshold = sleepThreshold;
            rb.solverIterations = solverIterations;
            rb.solverVelocityIterations = solverVelocityIterations;
            rb.constraints = RigidbodyConstraints.None;
        }
    }
}
