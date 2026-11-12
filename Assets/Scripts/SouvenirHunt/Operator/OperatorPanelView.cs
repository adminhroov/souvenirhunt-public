using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SouvenirHunt
{
    /// <summary>
    /// 운영자 패널(SCR-07) 한 레이아웃의 참조 묶음 — 세로(Main)·가로(Claw, Beam Pro를 눕힌 방향으로 회전) 두 개가 프리팹에 있다.
    /// 로직 없음: OperatorPanelController가 세션·회전 설정에 따라 하나를 고르고 텍스트·버튼을 이 참조로 다룬다.
    /// 메뉴 14(BuildOperatorView)가 생성·배선.
    /// </summary>
    public class OperatorPanelView : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [Tooltip("가로 뷰: 2340×1080 콘텐츠(회전 대상). 세로 뷰는 root와 동일 stretch")]
        [SerializeField] RectTransform content;
        [SerializeField] TMP_Text configText;
        [SerializeField] GameObject warningsBadge;
        [SerializeField] TMP_Text warningsText;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Button closeButton;
        [SerializeField] Button resetButton;
        [Tooltip("인형뽑기 개수 스테퍼(가로 뷰만)")]
        [SerializeField] OperatorClawCounts counts;

        public GameObject Root => root;
        public RectTransform Content => content;
        public TMP_Text ConfigText => configText;
        public GameObject WarningsBadge => warningsBadge;
        public TMP_Text WarningsText => warningsText;
        public TMP_Text StatusText => statusText;
        public Button CloseButton => closeButton;
        public Button ResetButton => resetButton;
        public OperatorClawCounts Counts => counts;

        public bool IsOpen => root != null && root.activeSelf;

        public void SetOpen(bool on)
        {
            if (root != null && root.activeSelf != on) root.SetActive(on);
        }
    }
}
