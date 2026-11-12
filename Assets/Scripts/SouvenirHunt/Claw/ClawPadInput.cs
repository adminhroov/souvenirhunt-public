using UnityEngine;

namespace SouvenirHunt
{
    /// <summary>
    /// Beam Pro 화면 가로 오버레이(조이스틱·잡기)의 입력 상태 — 씬 스크립트가 읽는 유일한 창구.
    /// SDK의 Primary2DAxis를 쓰지 않는 이유: 기기=절대 위치 / 에디터=터치 시작점 상대값으로 의미가 다르고,
    /// 드래그 없는 탭은 0으로 돌아오지 않는다. 오버레이의 uGUI 포인터 이벤트로 직접 계산하면 에디터·기기 동작이 같다.
    ///
    /// 축 변환: Beam Pro를 왼쪽으로 90° 눕히면 세로화면 +Y가 사용자 왼쪽, +X가 멀어지는 방향.
    ///   기계 오른쪽(+x) = −패드y, 기계 앞(+z) = +패드x. 실기기에서 반대로 느껴지면 config clawPadRotation=right.
    /// </summary>
    public static class ClawPadInput
    {
        public enum PadRotation { Left90, Right90, None }

        public static PadRotation Rotation = PadRotation.Left90;
        /// <summary>true면 첫 터치점을 중심으로(상대), false면 패드 중심에서 떨어진 거리(절대, 사용자 결정 기본).</summary>
        public static bool Relative;

        public static ClawPadRegistrar Registrar { get; private set; }
        public static GameObject Root => Registrar != null ? Registrar.PadRoot : null;
        public static bool RootActive => Root != null && Root.activeSelf;

        /// <summary>패드 로컬 정규화 스틱(|v|≤1). 세로화면 기준 축.</summary>
        public static Vector2 RawStick { get; private set; }
        /// <summary>기계 좌표 스틱: x=오른쪽, y=앞(멀어지는 방향).</summary>
        public static Vector2 MachineStick => ToMachine(RawStick);
        public static bool IsTouching { get; private set; }
        public static bool GrabHeld { get; private set; }
        /// <summary>큰 버튼 활성 표시(8차: 영상 시청 종료 대기 중 false → 회색). 누름 자체는 막지 않는다 — 판정은 GM.</summary>
        public static bool GrabEnabled { get; set; } = true;

        static bool grabLatch;
        static bool homeLatch;
        static bool recenterLatch;

        public static Vector2 ToMachine(Vector2 raw)
        {
            switch (Rotation)
            {
                case PadRotation.Left90: return new Vector2(-raw.y, raw.x);
                case PadRotation.Right90: return new Vector2(raw.y, -raw.x);
                default: return raw;
            }
        }

        public static Vector2 FromMachine(Vector2 m)
        {
            switch (Rotation)
            {
                case PadRotation.Left90: return new Vector2(m.y, -m.x);
                case PadRotation.Right90: return new Vector2(-m.y, m.x);
                default: return m;
            }
        }

        public static bool TryParseRotation(string s, out PadRotation r)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "left": r = PadRotation.Left90; return true;
                case "right": r = PadRotation.Right90; return true;
                case "none": r = PadRotation.None; return true;
                default: r = PadRotation.Left90; return false;
            }
        }

        // ── 오버레이(프리팹) 쪽에서 호출 ──
        public static void Register(ClawPadRegistrar r) { Registrar = r; }
        public static void Unregister(ClawPadRegistrar r) { if (Registrar == r) Registrar = null; }

        public static void SetStick(Vector2 raw, bool touching)
        {
            RawStick = Vector2.ClampMagnitude(raw, 1f);
            IsTouching = touching;
        }

        /// <summary>키보드 폴백 등 기계 좌표로 주입할 때.</summary>
        public static void SetMachineStick(Vector2 machine) => SetStick(FromMachine(machine), machine.sqrMagnitude > 0f);

        public static void PressGrab() { GrabHeld = true; grabLatch = true; }
        public static void ReleaseGrab() { GrabHeld = false; }

        /// <summary>오버레이 홈 버튼(3차) — 종료/취소 다이얼로그 토글 요청 래치.</summary>
        public static void PressHome() { homeLatch = true; }
        public static bool TryConsumeHome()
        {
            if (!homeLatch) return false;
            homeLatch = false;
            return true;
        }

        /// <summary>홈 길게 누르기(4차) — 정면 재정렬 요청 래치.</summary>
        public static void PressRecenter() { recenterLatch = true; }
        public static bool TryConsumeRecenter()
        {
            if (!recenterLatch) return false;
            recenterLatch = false;
            return true;
        }

        // ── 씬 쪽에서 호출 ──
        /// <summary>눌림 래치를 소비(한 번의 누름 = 한 번의 true). 이벤트 순서와 무관하게 안전.</summary>
        public static bool TryConsumeGrab()
        {
            if (!grabLatch) return false;
            grabLatch = false;
            return true;
        }

        public static void SetRootActive(bool on)
        {
            var root = Root;
            if (root != null && root.activeSelf != on) root.SetActive(on);
            if (!on) Clear();
        }

        public static void SetGrabLabel(string text) => Registrar?.SetGrabLabel(text);
        /// <summary>지금 잡기 버튼 글자(오버레이가 없으면 빈 문자열 — 셀프테스트)</summary>
        public static string GrabLabel => Registrar != null ? Registrar.GrabLabelText : "";
        public static void SetStickLabel(string text) => Registrar?.SetStickLabel(text);
        /// <summary>10-26 Beam Pro 화면 안내(안경 벗음 카운트다운 — ClawWearWatch). 빈 문자열이면 숨김</summary>
        public static void SetNotice(string text) => Registrar?.SetNotice(text);

        /// <summary>
        /// 10-26 앱이 백그라운드에서 돌아옴(ClawPadRegistrar) — 멈출 때 Clear가 GrabEnabled를 켜고 누름을 지운 뒤 아무도 다시 입히지 않아
        /// 버튼 색·글자가 상태와 어긋났다("다시 실행하면 컨트롤러가 꼬인다"). 지금 화면(ClawPadPresenter·VipIntro·ClawIntro)이 패드를 다시 입힌다
        /// </summary>
        public static event System.Action Resumed;
        public static void NotifyResumed() => EventUtil.SafeInvoke(Resumed);

        /// <summary>Clear 횟수 — 누른 채 들고 있는 버튼(홈)이 '중간에 비워졌음'을 알아채 상태를 맞춘다(10-26 [다시 시작]·씬 전환 때 홈 길게가 넘어가지 않게)</summary>
        public static int ClearCount { get; private set; }

        public static void Clear()
        {
            ClearCount++;
            RawStick = Vector2.zero;
            IsTouching = false;
            GrabHeld = false;
            grabLatch = false;
            homeLatch = false;
            recenterLatch = false;
            GrabEnabled = true;
        }
    }
}
