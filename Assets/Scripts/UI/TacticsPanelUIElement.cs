using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 경기 직전 세팅 화면(09-30). GameReady(선수 세팅 국면)에 뜨고 킥오프 버튼이 GamePlay로 넘긴다.
// 전술 편집 자체는 부품(TacticsEditorView)이 맡는다. 로비 전술 화면과 같은 프리팹이라 로비에서 세팅한 값이 그대로 보이고, 여기서는 상대를 보고 고칠 수 있다.
// 이 화면의 몫은 이번 경기다: 상대 이름·홈/원정·상대 전술(상성이 있어도 상대를 모르면 고르는 게 찍기가 된다)과 킥오프.
// 경기는 킥오프 순간에 시즌의 내 전술을 읽는다(MatchManager.StartMatch). 경기 도중엔 못 바꾼다(스펙 축: 레버는 전부 킥오프 전).
// 내용은 Show가 아니라 첫 Update에서 채운다: GameUIManager가 MatchManager보다 먼저 등록돼 GameReady 훅도 먼저 돈다.
// Show에서 이번 경기를 읽으면 시즌이 끝난 직후엔 ResetMatch가 다음 시즌을 열기 전이라 CurrentMatch가 던진다(결과 화면과 같은 Dirty Flag 꼴)
public class TacticsPanelUIElement : UIElement
{
    [SerializeField] private TacticsEditorView editor;
    [SerializeField] private TMP_Text matchupText;
    [SerializeField] private Button kickoffButton;

    private bool needsRefresh;

    public override void Initialize()
    {
        gameObject.SetActive(false);
        kickoffButton.onClick.AddListener(OnKickoff);
    }

    public override void Show()
    {
        gameObject.SetActive(true);
        needsRefresh = true;
    }

    public override void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!needsRefresh) { return; }
        needsRefresh = false;
        editor.Refresh();
        FillMatchup();
    }

    private void FillMatchup()
    {
        MatchSetup setup = GameDataManager.Instance.Season.CurrentMatch;
        string venue = setup.MyTeamIsHome ? "홈" : "원정";
        matchupText.text = $"상대 {setup.OpponentName} ({venue}) · 상대 전술: {setup.Tactics1.DisplayName}";
    }

    private void OnKickoff()
    {
        gameManager.SetGameState(GameManager.GameState.GamePlay);
    }
}
