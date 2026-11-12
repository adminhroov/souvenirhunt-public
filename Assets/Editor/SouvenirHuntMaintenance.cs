using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SouvenirHuntMaintenance
{
    [MenuItem("XREAL/36. Remove movie (keep black reveal)")]
    public static void ApplyDirectStart()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Claw.unity");
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == "VideoScreen")
            {
                Object.DestroyImmediate(root);
                continue;
            }
            foreach (var label in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (label.text == "Beam Pro 큰 버튼 [영상 시청]을 눌러 주세요")
                    label.text = "Beam Pro 큰 버튼 [공간 열기]를 눌러 주세요";
        }
        var reveal = Object.FindAnyObjectByType<SouvenirHunt.DigitalReveal>(FindObjectsInactive.Include);
        if (reveal == null) reveal = SouvenirHuntSetup.BuildDigitalReveal(new System.Text.StringBuilder());
        var session = Object.FindAnyObjectByType<SouvenirHunt.ClawGameManager>();
        var serialized = new SerializedObject(session);
        serialized.FindProperty("reveal").objectReferenceValue = reveal;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        foreach (var path in new[] {
            "Assets/StreamingAssets/claw_video.mp4",
            "Assets/Settings/Claw_VideoRT.renderTexture",
            "Assets/Settings/Claw_VideoScreen.mat",
            "Assets/Settings/Claw_VideoFrame.mat" })
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        Debug.Log("[NoMovie] Removed VideoScreen and bundled movie. Auth -> black reveal -> space -> START.");
    }
}
