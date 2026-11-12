using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SouvenirHunt
{
    /// <summary>
    /// 11-10 한/영 전환(사용자 결정: 별도 영어 APK가 아니라 실행 앱 화면에서 한국어/English 선택 → 게임 UI 한영 전환).
    /// 언어 결정 우선순위: Android 인텐트 extra "lang"(실행 앱 → 브리지 BeamEntryActivity → NRXRActivity, BeforeSceneLoad에서 읽음)
    ///   > config clawLanguage(ko|en — ConfigLoader.Load 끝에서 ApplyConfig) > ko. 결정·변경 때 로그 "[Loc] lang=en (source=intent|config|default)".
    /// 코드 문구 = 호출부에서 Loc.T("한", "영"). 씬·프리팹에 구워진 문구 = 아래 사전(한글 원문 → 영어)으로 런타임 치환 —
    ///   SceneManager.sceneLoaded(첫 씬 포함)마다 그 씬의 모든 TMP_Text·UI.Text(비활성 포함), Beam Pro 오버레이 프리팹은 ClawPadRegistrar.Awake에서 ApplyTo.
    ///   공백·줄바꿈을 한 칸으로 정규화한 뒤 정확히 일치할 때만 바꾼다. 영어 → 한국어로 바뀌면 같은 사전을 거꾸로 써서 되돌린다.
    /// 제외: 이미지·텍스처에 그려진 글자, 운영자·디버그 패널, 로그(사전에 없으니 그대로).
    /// 앱이 다시 앞으로 오면(ClawPadRegistrar 포커스) 인텐트를 다시 읽어 실행 앱에서 언어를 바꿔 이어하기 한 경우도 따른다(Unity 활동이 setIntent).
    /// </summary>
    public static class Loc
    {
        /// <summary>지금 영어 화면인가</summary>
        public static bool En { get; private set; }
        public static string Lang => En ? "en" : "ko";
        /// <summary>intent | config | default</summary>
        public static string Source { get; private set; } = "default";
        /// <summary>언어가 바뀜(코드 문구를 다시 그릴 쪽이 구독)</summary>
        public static event Action Changed;

        static string intentLang;   // 인텐트 extra(ko/en) — 없으면 null
        static string configLang;   // config clawLanguage(줄이 있을 때만) — 없으면 null
        static bool logged;

        /// <summary>코드 문구 — 영어 화면이면 en, 아니면 ko</summary>
        public static string T(string ko, string en) => En ? en : ko;

        /// <summary>ko/en(대소문자·korean/english 허용) → "ko"/"en", 그 밖은 null</summary>
        public static string Normalize(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            switch (v.Trim().ToLowerInvariant())
            {
                case "ko": case "kr": case "korean": case "한국어": return "ko";
                case "en": case "english": return "en";
                default: return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            En = false;
            Source = "default";
            configLang = null;
            logged = false;
            intentLang = ReadIntentLang();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Decide();
        }

        /// <summary>ConfigLoader.Load 끝 — config clawLanguage(인텐트가 있으면 무시)</summary>
        public static void ApplyConfig(GameConfig cfg)
        {
            configLang = cfg != null && cfg.clawLanguageSet ? Normalize(cfg.clawLanguage) : null;
            Decide();
        }

        /// <summary>앱이 다시 앞으로 옴 — 실행 앱에서 언어를 바꿔 [이어하기]로 들어온 경우(새 인텐트)</summary>
        public static void RecheckIntent()
        {
            string v = ReadIntentLang();
            if (v == null || v == intentLang) return;
            intentLang = v;
            Decide();
        }

        static void Decide()
        {
            string lang, src;
            if (intentLang != null) { lang = intentLang; src = "intent"; }
            else if (configLang != null) { lang = configLang; src = "config"; }
            else { lang = "ko"; src = "default"; }
            bool en = lang == "en";
            bool changed = en != En;
            if (!changed && src == Source && logged) return;
            En = en;
            Source = src;
            logged = true;
            Debug.Log($"[Loc] lang={Lang} (source={Source})");
            if (!changed) return;
            int n = ApplyToLoadedScenes();
            var pad = ClawPadInput.Registrar;
            if (pad != null) n += ApplyTo(pad.transform.root.gameObject);
            if (n > 0) Debug.Log($"[Loc] 언어 전환 — 화면 문구 {n}개 다시 씀");
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static string ReadIntentLang()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) return null;
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                if (intent == null) return null;
                return Normalize(intent.Call<string>("getStringExtra", "lang"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Loc] 인텐트 lang 읽기 실패 — config/기본값 사용: " + e.Message);
                return null;
            }
#else
            return null; // 에디터 — 인텐트 없음(config clawLanguage로 시험)
#endif
        }

        // ─ 구운 문구 치환 ─────────────────────────────────────────

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            int n = ApplyToScene(scene);
            if (n > 0) Debug.Log($"[Loc] 씬 {scene.name}: 구운 문구 {n}개 {(En ? "영어" : "한국어")}로");
        }

        static int ApplyToLoadedScenes()
        {
            int n = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) n += ApplyToScene(SceneManager.GetSceneAt(i));
            return n;
        }

        static readonly List<GameObject> roots = new(64);

        static int ApplyToScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;
            int n = 0;
            roots.Clear();
            scene.GetRootGameObjects(roots);
            foreach (var r in roots) n += ApplyTo(r);
            roots.Clear();
            return n;
        }

        /// <summary>root 아래 모든 TMP_Text·UI.Text(비활성 포함)를 지금 언어로 — 사전에 있는 문구만. 바꾼 개수</summary>
        public static int ApplyTo(GameObject root)
        {
            if (root == null) return 0;
            int n = 0;
            try
            {
                foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (TryTranslate(t.text, out string s)) { Fit(t); t.text = s; n++; }
                }
                foreach (var t in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (TryTranslate(t.text, out string s)) { t.text = s; n++; }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Loc] 문구 치환 실패: " + e.Message); }
            return n;
        }

        /// <summary>
        /// 영어 화면에서 긴 문구가 고정 폭 상자를 넘지 않게 — 넘칠 때만 줄어드는 자동 크기(최대 = 원래 크기, 최소 = minRatio배).
        /// 한국어 화면에서는 손대지 않는다(기존 모습 그대로)
        /// </summary>
        public static void Fit(TMP_Text t, float minRatio = 0.6f)
        {
            if (!En || t == null || t.enableAutoSizing) return;
            float max = t.fontSize;
            t.fontSizeMax = max;
            t.fontSizeMin = max * minRatio;
            t.enableAutoSizing = true;
        }

        /// <summary>사전 문구면 지금 언어 쪽으로(영어 화면: 한→영, 한국어 화면: 영→한)</summary>
        public static bool TryTranslate(string text, out string result)
        {
            result = text;
            if (string.IsNullOrEmpty(text)) return false;
            EnsureMaps();
            string key = Norm(text);
            if (En ? koToEn.TryGetValue(key, out var en) : enToKo.TryGetValue(key, out en))
            {
                if (en == text) return false;
                result = en;
                return true;
            }
            return false;
        }

        static string Norm(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static Dictionary<string, string> koToEn, enToKo;

        static void EnsureMaps()
        {
            if (koToEn != null) return;
            koToEn = new Dictionary<string, string>(Baked.Length);
            enToKo = new Dictionary<string, string>(Baked.Length);
            foreach (var (ko, en) in Baked)
            {
                koToEn[Norm(ko)] = en;
                enToKo[Norm(en)] = ko;
            }
        }

        /// <summary>
        /// 씬·프리팹에 구워진 참가자용 문구(한글 원문 그대로 — 메뉴 30 SouvenirHuntClawSetup·RevealSetup, 34 VipSetup, 14 Beam Pro 오버레이).
        /// 셋업 코드의 문구를 바꾸면 여기도 같이. 코드가 런타임에 덮어쓰는 문구(점수판·결과 제목 등)도 첫 프레임용으로 넣어 둔다
        /// </summary>
        static readonly (string ko, string en)[] Baked =
        {
            // Claw 씬 — 시작 패널(Ready)
            ("상품 미션", "Prize Mission"),
            ("제한 시간 안에 키링 큐브를 모아 넣으세요\n그릇은 점수가 없어요 — 큐브로 목표 수를 채우면 성공!",
             "Drop keyring cubes into the chute before time runs out\nBowls don't score — reach the cube goal to win!"),
            ("Beam Pro 큰 버튼 [공간 열기]를 눌러 주세요", "Press the big [Open Space] button on Beam Pro"),
            // 가이드(전환 끔) · START(디졸브 뒤)
            ("Beam Pro를 가로로 눕혀 가슴 앞에 들고", "Hold Beam Pro sideways at your chest"),
            ("↓ 아래 틀에 맞춘 뒤 [잡기]", "↓ Fit it to the frame, then [Grab]"),
            ("Beam Pro를 가로로 들고 [START]", "Hold Beam Pro sideways, press [START]"),
            ("↓ 가슴 앞 틀에 맞추세요", "↓ Fit it to the frame at your chest"),
            // 결과·보상
            ("성공!", "Success!"),
            ("[잡기] = 보상 확인", "[Grab] = View reward"),
            ("[다시 시작] = 처음 화면", "[Restart] = Back to start"),
            ("보상 안내", "Your Reward"),
            // 점수판(첫 프레임 — 코드가 바로 덮어씀)
            ("큐브 0 / 5\n그릇은 점수 없음", "Cubes 0 / 5\nBowls don't score"),
            ("캡슐 0 · 폭탄 0", "Capsules 0 · Bombs 0"),
            // 종료/취소 창(게임 씬·인증 씬)
            ("앱을 종료할까요?", "Quit the app?"),
            ("종료", "Quit"),
            ("취소", "Cancel"),
            ("← 종료 · 취소 →   [잡기] 확인 · 10초 후 자동 취소", "← Quit · Cancel →   [Grab] OK · auto-cancel in 10s"),
            // ClawIntro — VIP 인증
            ("STEP 01 · 카드키 인증", "STEP 01 · Card Key"),
            ("Beam Pro를 가슴 앞 인증 패널에 올려놓거나\n[인증] 버튼을 누르세요 ↓", "Place Beam Pro on the panel at your chest\nor press [Verify] ↓"),
            ("화면을 바라봐 주세요", "Please look at the screen"),
            ("STEP 02 · 가운데 홍채를 2초간 바라보세요", "STEP 02 · Look at the iris in the center for 2s"),
            ("인증 완료", "Verified"),
            // Beam Pro 오버레이(메뉴 14) — 운영자 패널 문구는 제외
            ("잡기", "Grab"),
            ("조이스틱", "Joystick"),
            ("홈", "Home"),
            ("길게: 정면", "Hold: Center"),
        };

        /// <summary>사전 항목 수(보고·셀프 점검용)</summary>
        public static int BakedCount => Baked.Length;
    }
}
