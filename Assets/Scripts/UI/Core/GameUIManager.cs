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

        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, () =>
        {
            ShowUIElement(UIElementEnums.MatchHud);   // 판이 차려질 때부터 보인다. 결과 국면에서도 그대로(결과 화면이 위에 덮는다)
        });
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
