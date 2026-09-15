using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameUIManager))]
public class GameUIManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Generate UIElement Enum"))
        {
            var uiManager = (GameUIManager)target;

            UpdateUIElementsList(uiManager);

            var sb = new StringBuilder();
            sb.AppendLine("public enum UIElementEnums");
            sb.AppendLine("{");
            foreach (var element in uiManager.uiElements)
            {
                sb.AppendLine($"\t{element.gameObject.name},");
            }
            sb.AppendLine("}");

            var path = EditorUtility.SaveFilePanel("Save", "Assets/Scripts/Defines", "UIElementEnums.cs", "cs");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, sb.ToString());
                AssetDatabase.Refresh();
            }

            EditorUtility.SetDirty(uiManager);
        }

        if (GUILayout.Button("Apply Kostar Font to All TMP"))
        {
            FontApplier.ApplyKostarFont();
        }

        base.OnInspectorGUI();
    }

    private void UpdateUIElementsList(GameUIManager uiManager)
    {
        var elements = uiManager.GetComponentsInChildren<UIElement>(true);
        uiManager.uiElements = elements.ToList();
    }
}
