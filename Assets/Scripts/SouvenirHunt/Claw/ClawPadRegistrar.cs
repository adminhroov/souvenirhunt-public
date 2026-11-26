using TMPro;
using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// Beam Pro 화면 프리팹(SouvenirVirtualController) 안의 항상-활성 앵커에 붙어, 비활성 상태로 시작하는
    /// ClawPadRoot를 ClawPadInput에 등록한다(비활성 오브젝트는 스스로 Awake를 못 돌리므로). GameObject.Find·XREAL 타입 불필요.
    /// 메뉴 14(BuildClawPad)가 배선.
    /// </summary>
    public class ClawPadRegistrar : MonoBehaviour
    {
        [SerializeField] GameObject padRoot;
        [SerializeField] TMP_Text grabLabel;
        [SerializeField] TMP_Text stickLabel;   // 다이얼로그 열림 시 "← 종료 · 취소 →" 안내
        [SerializeField] GameObject notice;     // 10-26 안경 벗음 안내(카운트다운) — 패드 위 전체 화면, 터치는 막지 않음
        [SerializeField] TMP_Text noticeText;

        public GameObject PadRoot => padRoot;
        /// <summary>지금 잡기 버튼 글자(셀프테스트)</summary>
        public string GrabLabelText => grabLabel != null ? grabLabel.text : "";

        void Awake()
        {
            ClawPadInput.Register(this);
            Loc.ApplyTo(transform.root.gameObject); // 11-10 구운 패드 문구(잡기·조이스틱·홈·길게: 정면)를 지금 언어로 — 운영자 패널 문구는 사전에 없어 그대로
            // 11-10 영어 라벨("View Reward"·"Requesting Prize")이 길어 상자를 넘지 않게 — 넘칠 때만 줄어든다(최대 = 원래 크기)
            FitAuto(grabLabel, 60f);
            FitAuto(noticeText, 36f);
        }

        static void FitAuto(TMP_Text t, float min)
        {
            if (t == null || t.enableAutoSizing) return;
            float max = t.fontSize;
            t.enableAutoSizing = true;
            t.fontSizeMax = max;
            t.fontSizeMin = Mathf.Min(min, max);
        }
        void OnDestroy() => ClawPadInput.Unregister(this);

        // 백그라운드 전환 시 눌린 채로 남는 상태 방지 · 10-26 돌아오면 지금 화면이 패드를 다시 입힌다(ClawPadInput.Resumed)
        void OnApplicationPause(bool pause)
        {
            if (pause) ClawPadInput.Clear();
            else { Loc.RecheckIntent(); ClawPadInput.NotifyResumed(); } // 11-10 실행 앱에서 언어를 바꿔 이어하기
        }

        void OnApplicationFocus(bool focus) { if (focus) { Loc.RecheckIntent(); ClawPadInput.NotifyResumed(); } }

        public void SetGrabLabel(string text)
        {
            if (grabLabel != null && grabLabel.text != text) grabLabel.text = text;
        }

        public void SetStickLabel(string text)
        {
            if (stickLabel != null && stickLabel.text != text) stickLabel.text = text;
        }

        /// <summary>10-26 안경 벗음 안내(ClawWearWatch). 빈 문자열이면 숨김. 메뉴 14로 다시 만들기 전 프리팹이면 아무 것도 안 함</summary>
        public void SetNotice(string text)
        {
            if (notice == null) return;
            bool show = !string.IsNullOrEmpty(text);
            if (show && noticeText != null && noticeText.text != text) noticeText.text = text;
            if (notice.activeSelf != show) notice.SetActive(show);
        }

        // ─ 11-26 [강제 종료] 버튼 — 프리팹을 다시 만들지 않게 처음 필요할 때 코드로 만든다(안내 위·패드 위 맨 앞 형제 = 터치 우선) ─
        GameObject forceQuit;
        TMP_Text forceQuitText;

        /// <summary>빈 문자열이면 숨김. 자리는 Beam Pro를 가로로 잡았을 때 화면 위쪽 가운데(왼쪽 90° 눕힘 = 세로 화면 오른쪽 변)</summary>
        public void SetForceQuit(string label)
        {
            bool show = !string.IsNullOrEmpty(label);
            if (show && forceQuit == null) BuildForceQuit();
            if (forceQuit == null) return;
            if (show)
            {
                PlaceForceQuit();
                forceQuit.transform.SetAsLastSibling();
                if (forceQuitText != null && forceQuitText.text != label) forceQuitText.text = label;
            }
            if (forceQuit.activeSelf != show) forceQuit.SetActive(show);
        }

        void BuildForceQuit()
        {
            var parent = notice != null ? notice.transform.parent : transform.parent;
            if (parent == null) return;
            forceQuit = new GameObject("ForceQuitButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
            forceQuit.layer = 5;
            forceQuit.transform.SetParent(parent, false);
            var img = forceQuit.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.78f, 0.13f, 0.15f, 0.97f);
            img.raycastTarget = true;
            forceQuit.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(ClawPadInput.PressForceQuit);
            if (noticeText != null) forceQuitText = Instantiate(noticeText, forceQuit.transform);
            else forceQuitText = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            forceQuitText.transform.SetParent(forceQuit.transform, false);
            forceQuitText.gameObject.name = "Text";
            forceQuitText.gameObject.layer = 5;
            forceQuitText.gameObject.SetActive(true);
            forceQuitText.raycastTarget = false;
            forceQuitText.color = Color.white;
            forceQuitText.alignment = TextAlignmentOptions.Center;
            forceQuitText.enableAutoSizing = true;
            forceQuitText.fontSizeMin = 24f;
            forceQuitText.fontSizeMax = 60f;
            forceQuitText.text = "";
            forceQuit.SetActive(false);
        }

        void PlaceForceQuit()
        {
            var rt = (RectTransform)forceQuit.transform;
            float z;
            switch (ClawPadInput.Rotation)
            {
                case ClawPadInput.PadRotation.Right90: rt.anchorMin = new Vector2(0.015f, 0.30f); rt.anchorMax = new Vector2(0.17f, 0.70f); z = 90f; break;
                case ClawPadInput.PadRotation.None: rt.anchorMin = new Vector2(0.20f, 0.90f); rt.anchorMax = new Vector2(0.80f, 0.985f); z = 0f; break;
                default: rt.anchorMin = new Vector2(0.83f, 0.30f); rt.anchorMax = new Vector2(0.985f, 0.70f); z = -90f; break;
            }
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            if (forceQuitText == null) return;
            Canvas.ForceUpdateCanvases();
            var size = rt.rect.size;
            var trt = forceQuitText.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.localRotation = Quaternion.Euler(0f, 0f, z);
            trt.sizeDelta = (Mathf.Approximately(z, 0f) ? size : new Vector2(size.y, size.x)) - new Vector2(24f, 16f);
        }
    }
}
