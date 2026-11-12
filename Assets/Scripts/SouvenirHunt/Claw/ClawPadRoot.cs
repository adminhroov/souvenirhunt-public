using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// 오버레이 루트. 실기기에서는 화면 전체를 덮고(아래 Trigger/App/Home 차단), 에디터 재생 중에는
    /// SDK의 XREALControllerLayout처럼 우하단 소형으로 줄여 Game 뷰를 가리지 않게 한다(마우스로 조작 가능).
    /// </summary>
    public class ClawPadRoot : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] float editorScale = 0.45f;

        void OnEnable()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(1080f, 2340f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * editorScale;
        }
#endif
    }
}
