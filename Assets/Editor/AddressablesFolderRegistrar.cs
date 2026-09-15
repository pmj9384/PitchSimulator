using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

// 폴더 안 에셋을 Addressables 그룹에 일괄 등록한다. 주소 규약 = "{AddressPrefix}/{파일명}"이라
// 코드에서 쓰는 키와 1:1로 맞는다 — 에셋을 추가하고 메뉴만 다시 누르면 된다.
//
// ┌─ 새 프로젝트에서 고칠 곳은 아래 상수 블록뿐이다. ─────────────────────┐
public static class AddressablesFolderRegistrar
{
    private const string SourceFolder = "Assets/Art/Units";
    private const string GroupName = "Units";
    private const string AddressPrefix = "Units";
    private const string SearchPattern = "*.png";       // 3D 모델이면 "*.prefab"
    // └──────────────────────────────────────────────────────────────────┘

    [MenuItem("Tools/Addressables/Register Folder")]
    public static void Register()
    {
        if (!Directory.Exists(SourceFolder))
        {
            Debug.LogError($"[AddressablesFolderRegistrar] 폴더가 없다: {SourceFolder}");
            return;
        }

        // 설정이 없으면 만든다 (Window > Asset Management > Addressables > Groups의 "Create"와 동일)
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);

        var group = settings.FindGroup(GroupName)
                    ?? settings.CreateGroup(GroupName, false, false, true, settings.DefaultGroup.Schemas);

        int count = 0;
        foreach (string file in Directory.GetFiles(SourceFolder, SearchPattern))
        {
            string guid = AssetDatabase.AssetPathToGUID(file.Replace("\\", "/"));
            if (string.IsNullOrEmpty(guid)) continue;

            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = $"{AddressPrefix}/{Path.GetFileNameWithoutExtension(file)}";
            count++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[AddressablesFolderRegistrar] {count}개 등록 완료 (그룹: {GroupName})");
    }
}
