using System.Collections.Generic;
using UnityEngine;

public class OutGameManager : MonoBehaviour
{
    private List<IManager> managers = new();

    public OutGameUIManager UIManager { get; private set; }

    private void Start()
    {
        InitializeManagers();
    }

    private void InitializeManagers()
    {
        GameObject.FindGameObjectWithTag("UIManager").TryGetComponent(out OutGameUIManager uiManager);
        UIManager = uiManager;
        UIManager.SetOutGameManager(this);
        managers.Add(UIManager);

        foreach (var manager in managers)
        {
            manager.Initialize();
        }

        // 경기에서 돌아온 길이면 허브로 바로 연다(결과 화면 [다음 경기]가 켠다. GameManager.SkipTitle과 같은 꼴의 씬 간 한 번짜리 신호)
        if (MatchdayScreen.OpenOnLobbyLoad)
        {
            MatchdayScreen.OpenOnLobbyLoad = false;
            UIManager.OpenScreen<MatchdayScreen>();
            return;
        }
        UIManager.OpenScreen<LobbyScreen>();
    }

    private void OnDestroy()
    {
        foreach (var manager in managers)
        {
            manager.Clear();
        }
    }
}
