using System.Text;

namespace SouvenirHunt
{
    /// <summary>
    /// 보상 테이블 (Phase 0). GameConfig.rewards(문구+meshIndex)와 MeshLibrary를 묶는 얇은 래퍼.
    /// 생성 시 meshIndex 범위를 검증해 config.warnings에 남긴다 (§4-2 "결과 노출" — 운영자 패널 배지에 표시).
    /// 실제 폴백(0번)은 MeshLibrary.Get이 담당 — 앱은 죽지 않는다.
    /// </summary>
    public class RewardTable
    {
        readonly GameConfig config;
        readonly MeshLibrary library;

        public RewardTable(GameConfig config, MeshLibrary library)
        {
            this.config = config;
            this.library = library;
            ValidateMeshIndices();
        }

        void ValidateMeshIndices()
        {
            if (config == null || library == null) return;
            int n = library.Count;
            for (int i = 0; i < config.rewards.Count; i++)
            {
                int mi = config.rewards[i].meshIndex;
                if (n == 0)
                {
                    config.warnings.Add("메시 라이브러리가 비어 있음 — 기념품이 기본 모양으로만 보임");
                    return;
                }
                if (mi < 0 || mi >= n)
                    config.warnings.Add($"reward{i + 1}.meshIndex={mi} 범위(0~{n - 1}) 초과 → 0번 메시 사용");
            }
        }

        public int Count => config.rewards.Count;

        public string GetText(int i) =>
            Count == 0 ? "" : config.rewards[Mod(i)].text;

        public MeshLibrary.Entry GetMeshEntry(int i) =>
            Count == 0 || library == null ? default : library.Get(config.rewards[Mod(i)].meshIndex);

        /// <summary>보상 화면(SCR-06)용 보상 문구 (1차: 전 항목 나열 스텁, 2차: 3개 중 1개 선택 UI로 대체).</summary>
        public string BuildRewardText()
        {
            if (Count == 0) return "출구에서 실물 기념품을 수령하세요";
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
                sb.Append(i == 0 ? "" : " · ").Append("<nobr>").Append(config.rewards[i].text).Append("</nobr>"); // 품목 중간에서 줄이 끊기지 않게(10-15 "신 / 라약과")
            return sb.ToString();
        }

        int Mod(int i) => ((i % Count) + Count) % Count;
    }
}
