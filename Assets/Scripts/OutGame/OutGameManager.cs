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

        UIManager.OpenScreen<LobbyScreen>();   // 경기에서 돌아와도 홈이다(스펙 §2-1, 10-15). 홈의 [일정 진행]이 매치데이를 연다
    }

    private void OnDestroy()
    {
        foreach (var manager in managers)
        {
            manager.Clear();
        }
    }
}
