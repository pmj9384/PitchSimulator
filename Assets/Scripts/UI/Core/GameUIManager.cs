using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameUIManager : InGameManager
{
    public List<UIElement> uiElements;

    // 휘슬 뒤 결과 화면까지 기다리는 시간(09-28 결과 화면). 이 사이 HUD 자막이 경기 종료를 알리고, 같은 라운드 나머지 경기 계산(백그라운드)이 먼저 출발한다
    [SerializeField] private float resultPanelDelaySec = 1.5f;
    private WaitForSeconds resultPanelWait;   // 모바일 가이드 "WaitForSeconds 캐시"(UNT0038). 값이 인스펙터 고정이라 Initialize에서 한 번 만든다

    public override void Initialize()
    {
        base.Initialize();
        resultPanelWait = new WaitForSeconds(resultPanelDelaySec);
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

        // 승리(GameClear)도 패배·무승부(GameOver)도 같은 결과 화면. 승리 쪽이 비어 있어 이긴 판에서 다음 경기로 못 갔다(09-28 결과 화면).
        // 코루틴은 이 매니저에 붙어 돌아 씬이 내려가면 같이 멈춘다. 대기 중 재시작·로비 이동이 있어도 사라진 씬에 패널을 띄우지 않는다
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameClear, ShowResultPanelDelayed);
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameOver, ShowResultPanelDelayed);

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

    private void ShowResultPanelDelayed()
    {
        StartCoroutine(ShowDelayed(UIElementEnums.ResultPanel, resultPanelWait));
    }

    private IEnumerator ShowDelayed(UIElementEnums type, WaitForSeconds wait)
    {
        yield return wait;
        ShowUIElement(type);
    }
}
