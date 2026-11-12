using System.Collections.Generic;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 실제 부스 설치 모드 (config: envOcclusionMode=true).
    /// 환경 메시(방 목업 — 벽·바닥·가구)를 "보이지 않지만 깊이는 쓰는" 오클루전 마스크
    /// (Shaders/DepthOcclusion)로 일괄 교체한다. OST 글래스에서 아무것도 그리지 않은 영역은
    /// 현실이 그대로 보이므로 가상 가구는 사라지고, 그 뒤의 수집품은 깊이 테스트에 막혀
    /// 실제 부스 구조물에 가려진 것처럼 숨는다 (환경 메시를 실제 부스 모양에 맞춰둔 전제).
    /// 리셋(config 재로드)마다 재적용 — 현장에서 config.txt 한 줄로 켜고 끈다.
    /// Environment 루트에 부착 (메뉴 XREAL/13이 배선).
    /// </summary>
    public class EnvironmentOcclusionSwitch : MonoBehaviour
    {
        [SerializeField] Material occlusionMaterial;   // SouvenirHunt/DepthOcclusion 머티리얼

        GameManager gm;
        bool bound;
        readonly Dictionary<Renderer, Material[]> originals = new();

        void Awake()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                originals[r] = r.sharedMaterials;
        }

        // GameManager.Awake 순서 비보장 → HudController와 동일 재시도 패턴
        void OnEnable() => TryBind();

        void Start()
        {
            TryBind();
            Apply();
        }

        void OnDisable()
        {
            if (bound && gm != null) gm.OnGameReset -= Apply;
            bound = false;
        }

        void TryBind()
        {
            if (bound) return;
            gm = GameManager.Instance;
            if (gm == null) return;
            gm.OnGameReset += Apply;
            bound = true;
        }

        void Apply()
        {
            bool occlude = gm != null && gm.Config != null && gm.Config.envOcclusionMode;
            foreach (var kv in originals)
            {
                var r = kv.Key;
                if (r == null) continue;
                if (occlude)
                {
                    if (occlusionMaterial == null) continue; // 배선 누락 시 보이는 상태 유지 (죽지 않기)
                    var mats = new Material[kv.Value.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = occlusionMaterial;
                    r.sharedMaterials = mats;
                }
                else
                {
                    r.sharedMaterials = kv.Value;
                }
            }
        }
    }
}
