using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameUIManager : InGameManager
{
    public List<UIElement> uiElements;

    // 휘슬 뒤 결과 화면까지 기다리는 시간(09-28 결과 화면). 이 사이 HUD 자막이 경기 종료를 알린다. 나머지 경기 계산은 MatchEnded에서 이미 출발해 이 지연과 무관하다
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

        // GameReady = 선수 세팅 국면(09-30): 팀 전술 설정창만 보인다. HUD를 같이 켜 두면 반투명 패널 뒤로 글자가 비친다(09-29 결과 화면과 같은 원인)
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, ShowTacticsPanel);
        // 킥오프: 설정창을 닫고 HUD를 켠다. HUD는 휘슬 뒤 결과 화면이 뜰 때까지 경기 종료 자막을 보여 주고 그때 꺼진다
        GameManager.AddGameStateEnterAction(GameManager.GameState.GamePlay, ShowMatchHud);
    }

    private void ShowTacticsPanel()
    {
        ShowUIElement(UIElementEnums.TacticsPanel);
    }

    private void ShowMatchHud()
    {
        HideUIElement(UIElementEnums.TacticsPanel);
        ShowUIElement(UIElementEnums.MatchHud);
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
        StartCoroutine(ShowResultPanelAfterWait());
    }

    private IEnumerator ShowResultPanelAfterWait()
    {
        yield return resultPanelWait;
        // 결과 화면이 스코어를 다시 보여 주므로 HUD는 끈다. 배경이 반투명(알파 0.75)이라 HUD를 뒤로 보내도 글자가 비쳐 겹쳐 보였다(09-29 유저 Play)
        HideUIElement(UIElementEnums.MatchHud);
        ShowUIElement(UIElementEnums.ResultPanel);
    }
}
