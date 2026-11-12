using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using Unity.XR.XREAL;

// 메뉴 0 — Android Player + XR 플러그인 설정 (구 XrealAutoSetup 메뉴 1에서 이관, 08-27).
// 차이점: productName/appId를 덮어쓰지 않는다(ApplyIdentity가 단일 소유). 재실행 안전.
// 손대지 않는 것: targetSdk(Auto 유지), 화면 방향, Unity 스플래시(해제는 Pro 라이선스 필요), VirtualController(메뉴 14 소유).
public static partial class SouvenirHuntSetup
{
    [MenuItem("XREAL/0. Apply Player + XR Settings (IL2CPP·ARM64·GLES3·3DoF)")]
    public static void ApplyPlayerSettings()
    {
        try
        {
            ApplyIdentity();

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)31;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.enableFrameTimingStats = true; // 10-15: [Perf] 로그에 CPU·렌더 스레드·GPU 시간(병목 진단)
#pragma warning disable 618
            PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, false);
#pragma warning restore 618
            Debug.Log("[SouvenirHuntSetup] Player settings: IL2CPP · ARM64 · minSdk 31 · GLES3 · MT off");

            // XR Management: Android 설정 + XREAL 로더
            XRGeneralSettingsPerBuildTarget generalSettings;
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out generalSettings) || generalSettings == null)
            {
                EnsureFolder("Assets/XR");
                generalSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(generalSettings, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, generalSettings, true);
            }
            var androidSettings = generalSettings.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings == null)
            {
                androidSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                androidSettings.name = "Android Settings";
                generalSettings.SetSettingsForBuildTarget(BuildTargetGroup.Android, androidSettings);
                AssetDatabase.AddObjectToAsset(androidSettings, generalSettings);
            }
            if (androidSettings.Manager == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = "Android Providers";
                androidSettings.Manager = manager;
                AssetDatabase.AddObjectToAsset(manager, generalSettings);
            }
            bool assigned = XRPackageMetadataStore.AssignLoader(androidSettings.Manager, typeof(XREALXRLoader).FullName, BuildTargetGroup.Android);
            Debug.Log("[SouvenirHuntSetup] XREAL loader assigned: " + assigned);

            // XREALSettings: 3DoF · Controller · MultiResume (VirtualController는 메뉴 14가 관리 — 여기서 건드리지 않음)
            XREALSettings xrealSettings;
            if (!EditorBuildSettings.TryGetConfigObject(XREALSettings.k_SettingsKey, out xrealSettings) || xrealSettings == null)
            {
                xrealSettings = AssetDatabase.LoadAssetAtPath<XREALSettings>(XrealSettingsPath);
                if (xrealSettings == null)
                {
                    EnsureFolder("Assets/XR");
                    EnsureFolder("Assets/XR/Settings");
                    xrealSettings = ScriptableObject.CreateInstance<XREALSettings>();
                    AssetDatabase.CreateAsset(xrealSettings, XrealSettingsPath);
                }
                EditorBuildSettings.AddConfigObject(XREALSettings.k_SettingsKey, xrealSettings, true);
            }
            xrealSettings.InitialTrackingType = TrackingType.MODE_3DOF;
            xrealSettings.InitialInputSource = InputSource.Controller;
            xrealSettings.SupportMultiResume = true;
            xrealSettings.EnableAutoLogcat = true; // 탈착·재실행 검은 화면 진단 (현장 검증 후 끌 수 있음)
            EditorUtility.SetDirty(xrealSettings);
            EditorUtility.SetDirty(generalSettings);
            AssetDatabase.SaveAssets();

            Debug.Log("[SouvenirHuntSetup] XREAL settings: 3DoF · Controller · MultiResume");
            if (!Application.isBatchMode && !Quiet)
                EditorUtility.DisplayDialog("SouvenirHunt Setup",
                    $"Player/XR 설정 적용 완료\nSouvenirHunt v{AppVersion} · IL2CPP · ARM64 · GLES3 · 3DoF · Controller · MultiResume", "확인");
        }
        catch (Exception e)
        {
            Debug.LogError("[SouvenirHuntSetup] ApplyPlayerSettings FAILED: " + e);
        }
    }
}
