using System.Text;
using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 로비 홈(스펙 §2-1, 10-15). 다음 경기 카드(라운드·상대·내 전술) + 지난 경기 설정 대 실측 한 줄 + [일정 진행] 하나.
// 설정 대 실측을 첫 화면에 두는 이유: "엔진이 내 설정을 들었다"가 이 게임이 FM과 갈리는 자리다(스펙 축 2번).
// [일정 진행]은 매치데이 화면을 연다. 시즌이 끝났으면 [다음 시즌]이 되어 여기서 다음 시즌을 열고 카드를 다시 채운다(매치데이로 안 간다).
// 값은 전부 시즌에서 읽기만 한다. 열 때마다 다시 채운다: 전술·스카우트 탭에서 돌아올 때 바뀐 값이 보여야 한다
public class LobbyScreen : UIScreen
{
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text playButtonLabel;
    [SerializeField] private Button optionsButton;
    [SerializeField] private TMP_Text roundText;       // "4부 3/6 라운드" 또는 승강전 단계, 시즌 끝이면 승강 판정
    [SerializeField] private TMP_Text matchupText;     // 상대 이름·홈/원정·상대 전술
    [SerializeField] private TMP_Text myTacticsText;   // 내 카드 이름 + 조정 여부
    [SerializeField] private TMP_Text readoutText;     // 지난 경기 설정 대 실측 한 줄

    private readonly StringBuilder readoutBuilder = new(128);
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    public override void Open()
    {
        base.Open();
        if (!initialized)
        {
            initialized = true;
            playButton.onClick.AddListener(OnPlay);
            optionsButton.onClick.AddListener(OnOptions);
        }
        Refresh();
    }

    // 보고 중(IsReporting)엔 CurrentMatch가 던진다. 로비에선 결과 화면이 보고를 끝내고 와서 안 걸리지만, 새 진입 경로가 생겨도 조용히 틀리지 않게 잠근다
    private void Refresh()
    {
        SeasonSystem season = Season;
        bool ready = !season.IsReporting;
        playButton.interactable = ready;
        if (!ready)
        {
            roundText.text = "다른 경기 결과 계산 중…";
            matchupText.text = string.Empty;
            myTacticsText.text = string.Empty;
            readoutText.text = string.Empty;
            return;
        }
        roundText.text = season.NextMatchLine();
        matchupText.text = season.IsOver ? MatchdayLines.SeasonOverNotice : MatchdayLines.Matchup(season.CurrentMatch);
        myTacticsText.text = MatchdayLines.MyTactics(season.State, TeamTacticsRepository.All);
        readoutText.text = ReadoutLine(season.State);
        playButtonLabel.text = season.IsOver ? "다음 시즌" : "일정 진행";
    }

    private string ReadoutLine(SeasonState state)
    {
        if (state.LastReadout == null) { return "아직 치른 경기가 없다"; }
        return "지난 경기 · " + TacticsReadoutText.HomeLine(state.LastReadout, readoutBuilder);
    }

    // 시즌이 끝났으면 다음 시즌을 열고 이 화면을 다시 채운다(StartNextSeason이 저장까지 한다). 아니면 매치데이로
    private void OnPlay()
    {
        if (Season.IsOver)
        {
            Season.StartNextSeason();
            Refresh();
            return;
        }
        uiManager.OpenScreen<MatchdayScreen>();
    }

    private void OnOptions()
    {
        uiManager.ShowPopup<OutGameSettingsPanel>();
    }
}
