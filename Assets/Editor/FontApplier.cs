using TMPro;
using UnityEditor;
using UnityEngine;

public class FontApplier
{
    [MenuItem("Tools/Apply Kostar Font to All TMP")]
    public static void ApplyKostarFont()
    {
        string[] guids = AssetDatabase.FindAssets("Kostar SDF 2", new[] { "Assets/Font" });
        if (guids.Length == 0)
        {
            Debug.LogError("Kostar SDF 2 폰트를 찾을 수 없습니다.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);

        var allTMP = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var tmp in allTMP)
        {
            Undo.RecordObject(tmp, "Apply Kostar Font");
            tmp.font = font;
            tmp.enabled = true;
            EditorUtility.SetDirty(tmp);
            count++;
        }

        Debug.Log($"Kostar SDF 2 폰트를 {count}개 TMP 컴포넌트에 적용했습니다.");
    }
}
