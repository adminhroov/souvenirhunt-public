using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// 운영자 패널(가로 뷰)의 인형뽑기 개수 스테퍼 — 10-15 미션형: 그릇(종류별 같은 수, 1~6)·캡슐(0~50)·폭탄(0~8), 합계 ≤ 풀(60).
    /// 그릇 스테퍼는 세라믹·유리를 같은 수로 쓴다(clawCeramicCount = clawGlassCount). 성공 조건(clawTargetCount)은 config로만.
    /// 10-26(상품 3종 1:1:1): 그릇 스테퍼 = 세라믹·유리·키링을 같은 수로(1~20)
    /// 10-28(점수 = 키링 큐브만, 그릇은 방해물): 스테퍼 2개 — 큐브(1~20, clawKeyringCount) / 그릇(종류별, 0~20, 세라믹 = 유리).
    /// 캡슐·폭탄은 config 전용(스테퍼 없음 — 합계에는 지금 세션 값 포함). 합계 = 큐브 + 그릇×2 + 캡슐 + 폭탄 ≤ 배치 상한.
    /// [−]/[+] 누름 → config.txt에 기록(ConfigLoader.SetValues) → 0.6초 디바운스 후 세션 ResetGame(config 재로드로 반영).
    /// Refresh가 1초마다 Sync를 부르므로 편집 중(dirty)에는 표시값을 세션 값으로 덮지 않는다.
    /// Claw 세션이 아니면 root를 숨긴다. 예외를 던지지 않는다.
    /// </summary>
    public class OperatorClawCounts : MonoBehaviour
    {
        enum Which { Cube, Bowl }

        [SerializeField] GameObject root;
        [SerializeField] TMP_Text cubeValue;
        [SerializeField] TMP_Text bowlValue;
        [SerializeField] TMP_Text sumText;
        [SerializeField] Button cubeMinus, cubePlus;
        [SerializeField] Button bowlMinus, bowlPlus;
        [Tooltip("마지막 누름 뒤 리셋(적용)까지 대기 — 연타 시 리셋 1회")]
        [SerializeField] float applyDelaySec = 0.6f;
        [SerializeField] Color sumNormalColor = new Color(1f, 1f, 1f, 0.7f);
        [SerializeField] Color sumFullColor = new Color(1f, 0.45f, 0.4f, 1f);

        // ConfigLoader clamp·DefaultConfigText와 같은 범위 — 셋 같이 고칠 것(10차 함정). 큐브는 게임이 1개 이상으로 맞춤
        public const int CubeLo = 1, CubeHi = 20, BowlLo = 0, BowlHi = 20;
        int sumMax = 60;   // = 배치 상한(gm.PlaceCapacity) — Sync에서 갱신

        OperatorPanelController panel;
        int cube = 10, bowl = 5, filler = 0, bomb = 0, target = 5;
        bool dirty;
        float applyAt = -1f;

        public void Init(OperatorPanelController p) => panel = p;

        void Awake()
        {
            Wire(cubeMinus, Which.Cube, -1); Wire(cubePlus, Which.Cube, +1);
            Wire(bowlMinus, Which.Bowl, -1); Wire(bowlPlus, Which.Bowl, +1);
        }

        void Wire(Button b, Which w, int delta)
        {
            if (b != null) b.onClick.AddListener(() => Step(w, delta));
        }

        int Sum(int c, int b) => c + b * 2 + filler + bomb; // 세라믹·유리 같은 수 + config 전용 캡슐·폭탄

        /// <summary>패널 Refresh마다 호출. gm이 null(Main·인증 씬)이면 숨김.</summary>
        public void Sync(ClawGameManager gm)
        {
            if (root != null && root.activeSelf != (gm != null)) root.SetActive(gm != null);
            if (gm == null) return;
            sumMax = gm.PlaceCapacity; // 10-20 받침 더미면 그 위 한 층 셀 수
            target = gm.TargetCount;
            filler = gm.FillerCount;
            bomb = gm.BombCount;
            if (!dirty)
            {
                cube = gm.KeyringCount;
                bowl = Mathf.Max(gm.CeramicCount, gm.GlassCount);
            }
            RefreshTexts();
        }

        void Step(Which w, int delta)
        {
            int c = cube, b = bowl;
            switch (w)
            {
                case Which.Cube: c = Mathf.Clamp(c + delta, CubeLo, CubeHi); break;
                case Which.Bowl: b = Mathf.Clamp(b + delta, BowlLo, BowlHi); break;
            }
            panel?.MarkInteraction();
            if (Sum(c, b) > sumMax || (c == cube && b == bowl)) return;

            cube = c; bowl = b;
            dirty = true;
            RefreshTexts();

            bool ok = ConfigLoader.SetValues(new Dictionary<string, string>
            {
                { "clawKeyringCount", c.ToString() },
                { "clawCeramicCount", b.ToString() },
                { "clawGlassCount", b.ToString() },
            }, out string msg);
            panel?.ShowStatus(ok
                ? $"저장 — 큐브 {c} · 그릇 {b}씩(세라믹·유리) (리셋 적용 중…)"
                : "config 저장 실패: " + msg);
            applyAt = Time.unscaledTime + applyDelaySec;
        }

        void Update()
        {
            if (applyAt >= 0f && Time.unscaledTime >= applyAt) Apply();
        }

        void OnDisable()
        {
            if (applyAt >= 0f || dirty) Apply(); // 누르고 바로 닫아도 반영
        }

        void Apply()
        {
            applyAt = -1f;
            dirty = false;
            GameSession.Current?.ResetGame(); // config 재로드 → OnGameReset → 패널 Refresh → Sync(클램프 적용값)
        }

        void RefreshTexts()
        {
            if (cubeValue) cubeValue.SetText("{0}", cube);
            if (bowlValue) bowlValue.SetText("{0}", bowl);
            int sum = Sum(cube, bowl);
            bool full = sum >= sumMax;
            if (sumText)
            {
                sumText.SetText("큐브 {0} · 그릇 {1}씩 · 합계 {2} / {3} · 성공 큐브 {4}개", cube, bowl, sum, sumMax, target);
                sumText.color = full ? sumFullColor : sumNormalColor;
            }
            if (cubeMinus) cubeMinus.interactable = cube > CubeLo;
            if (cubePlus) cubePlus.interactable = cube < CubeHi && sum + 1 <= sumMax;
            if (bowlMinus) bowlMinus.interactable = bowl > BowlLo;
            if (bowlPlus) bowlPlus.interactable = bowl < BowlHi && sum + 2 <= sumMax;
        }
    }
}
