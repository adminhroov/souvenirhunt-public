using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 방 구조물(바닥·벽·창문·천장 등기구)을 <b>런타임에만</b> 숨긴다 — 언리얼의 "Hidden in Game"과 같은 개념.
    /// 오브젝트·메시는 씬에 그대로 남아 에디터 씬 뷰에서 보이고, 라이트맵 베이크·에디터 가시성 검증도
    /// 종전대로 이 구조물들을 사용한다. 런타임에는 Renderer.enabled만 끄므로 GC·드로우콜 부담도 없다.
    ///
    /// 배경(08-31 글래스 실측): OST 글래스는 화면(FOV)이 좁아 벽·바닥 같은 큰 평면이 화면 밖에서 잘리면
    /// 그 경계선이 그대로 드러나 몰입을 깬다. 가구(책상·모니터·캐비닛)는 작아서 문제가 없고 은폐 기능도
    /// 담당하므로 계속 보인다.
    ///
    /// config `showStructures=true` 면 다시 보이게 할 수 있다(기본 false = 숨김). 리셋마다 재적용.
    /// Environment 루트에 부착 (메뉴 XREAL/13이 배선).
    /// </summary>
    public class StructureVisibility : MonoBehaviour
    {
        [SerializeField] Renderer[] structureRenderers = new Renderer[0];

        GameManager gm;
        bool bound;

        // GameManager.Awake 순서 비보장 → EnvironmentOcclusionSwitch와 동일 재시도 패턴
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
            // config 미로드(부팅 극초반)여도 기본값 = 숨김. 외부 입력 없으면 안전한 쪽으로 (앱은 죽지 않게)
            bool show = gm != null && gm.Config != null && gm.Config.showStructures;
            foreach (var r in structureRenderers)
                if (r != null) r.enabled = show;
        }
    }
}
