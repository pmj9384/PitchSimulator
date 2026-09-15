using UnityEditor;
using UnityEngine;

// 개발용 치트를 한 창에 모은 패널 — Tools/Cheats 메뉴로 열며, 매니저를 선택하지 않아도 항상 접근 가능.
// 이 파일(base)은 모든 프로젝트 공통 치트(코인 등)만 담는다 — 템플릿에서 새 프로젝트로 그대로 복사돼도 컴파일된다.
// 프로젝트 고유 치트(스킬 등)는 같은 프로젝트에 CheatWindow.<프로젝트>.cs 를 만들어 partial로 DrawProjectCheats()를 채운다.
// Editor 폴더라 빌드엔 안 들어간다.
public partial class CheatWindow : EditorWindow
{
    [MenuItem("Tools/Cheats")]
    private static void Open() => GetWindow<CheatWindow>("Cheats");

    private int coinAmount = 1000;

    private void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Play 중에만 동작합니다.", MessageType.Info);
            return;
        }

        DrawCoinCheats();       // 공통 (템플릿)
        DrawProjectCheats();    // 프로젝트 고유 (partial — 없으면 no-op)
    }

    // 모든 프로젝트 공통 — 코인
    private void DrawCoinCheats()
    {
        EditorGUILayout.LabelField("코인", EditorStyles.boldLabel);
        if (GameDataManager.Instance != null)
            EditorGUILayout.LabelField($"현재: {GameDataManager.Instance.PlayerAccountData.Coins}");
        coinAmount = EditorGUILayout.IntField("조절량", coinAmount);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button($"+{coinAmount}"))
            {
                GameDataManager.Instance.PlayerAccountData.AddCoins(coinAmount);
                SaveLoadSystem.Instance.Save();
            }
            if (GUILayout.Button("0으로"))
            {
                var data = GameDataManager.Instance.PlayerAccountData;
                data.SpendCoin(data.Coins);
                SaveLoadSystem.Instance.Save();
            }
        }
    }

    // 프로젝트가 CheatWindow.<프로젝트>.cs 에서 구현. 미구현이면 호출 자체가 제거된다(빈 no-op).
    partial void DrawProjectCheats();
}
