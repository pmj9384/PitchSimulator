using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameUIManager : InGameManager
{
    public List<UIElement> uiElements;

    public override void Initialize()
    {
        base.Initialize();
        foreach (var element in uiElements)
        {
            element.SetUIManager(GameManager, this);
        }

        GameManager.AddGameStateEnterAction(GameManager.GameState.GameStop, () =>
        {
            ShowUIElement(UIElementEnums.PausePanel);
        });

        GameManager.AddGameStateExitAction(GameManager.GameState.GameStop, () =>
        {
            HideUIElement(UIElementEnums.PausePanel);
            HideUIElement(UIElementEnums.SettingsPanel);
        });

        GameManager.AddGameStateEnterAction(GameManager.GameState.GameOver, () =>
        {
            ShowUIElement(UIElementEnums.GameOverPanel);
        });

        // 게임별 UI 연결 추가
    }

    public void InitializedUIElements()
    {
        foreach (var element in uiElements)
        {
            element.Initialize();
        }
    }

    public void ShowUIElement(UIElementEnums type)
    {
        uiElements[(int)type].Show();
    }

    public void HideUIElement(UIElementEnums type)
    {
        uiElements[(int)type].Hide();
    }

    private IEnumerator ShowDelayed(UIElementEnums type, float delay)
    {
        yield return new WaitForSeconds(delay);
        ShowUIElement(type);
    }
}
