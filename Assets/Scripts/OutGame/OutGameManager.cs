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
            manager.Initialize();

        UIManager.OpenScreen<LobbyScreen>();
    }

    private void OnDestroy()
    {
        foreach (var manager in managers)
            manager.Clear();
    }
}
