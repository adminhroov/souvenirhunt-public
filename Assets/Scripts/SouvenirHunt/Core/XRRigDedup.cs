using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// XR 리그 중복 제거(10-15 성능). 리그 프리팹(XR Interaction Setup) 루트의 XREALSessionManager는 SDK 싱글턴이라
    /// 루트에 있으면 DontDestroyOnLoad → 인증 씬(ClawIntro)의 리그가 게임 씬(Claw)까지 살아남고, 게임 씬 리그도 따로 생긴다
    /// (새 쪽 세션 관리자는 컴포넌트만 지워지고 리그·카메라는 남음). 실기기 vc44 로그: Main Camera 2대가 둘 다 양눈에 장면 전체를 그림 = 매 프레임 2번 렌더.
    /// 새 씬 리그가 깨어날 때(세션 관리자 Awake −500보다 먼저) 이전 씬에서 넘어온 리그를 즉시 지운다 →
    /// 지워진 쪽 OnDestroy가 싱글턴을 비우고 이 씬 리그의 세션 관리자가 이어받는다. 씬 오브젝트의 직렬화 참조는 모두 이 씬 리그를 가리키므로 그대로 유효.
    /// XREAL 타입을 직접 참조하지 않는다(그 클래스는 Android+InputSystem 조건부 컴파일).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class XRRigDedup : MonoBehaviour
    {
        static XRRigDedup current;

        /// <summary>지금까지 지운 이전 리그 수(셀프테스트·로그용)</summary>
        public static int Removed { get; private set; }

        void Awake()
        {
            if (current != null && current != this && current.gameObject != gameObject)
            {
                var old = current.gameObject;
                Debug.Log($"[XRRig] 이전 씬 리그 '{old.name}'(씬 {old.scene.name}) 제거 — 카메라 중복 렌더 방지, 이 씬 리그('{gameObject.scene.name}')만 사용");
                Removed++;
                DestroyImmediate(old);
            }
            current = this;
        }

        void OnDestroy()
        {
            if (current == this) current = null;
        }
    }
}
