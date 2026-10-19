using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 매치데이 화면(스펙 §2-1, 10-15 재편. 레퍼런스 FM Pre-Match Hub). 홈의 [일정 진행]이 연다.
// 다음 경기(라운드·상대·홈/원정·상대 전술)와 내 전술 요약을 보여 주고 [전술판]·[경기 시작] 두 길을 낸다. 스카우트는 하단 탭이 입구라 여기 버튼이 없다.
// 인게임 GameReady 전술판을 없앤 자리다: 상대를 보고 고치는 일은 여기 [전술판]이 맡고, 인게임은 들어가는 순간 킥오프다(SkipTitle).
// 다음 시즌 열기는 홈의 [다음 시즌]이 맡는다. 끝난 시즌으로 여기 들어오면 안내만 하고 [경기 시작]을 잠근다.
// 값은 전부 시즌(SeasonSystem)에서 읽기만 한다. 열 때마다 다시 채운다: 전술판에서 돌아올 때 바뀐 값이 보여야 한다
public class MatchdayScreen : UIScreen
{
    [SerializeField] private TMP_Text roundText;       // "4부 3/6 라운드" 또는 승강전 단계, 시즌 끝이면 승강 판정
    [SerializeField] private TMP_Text matchupText;     // 상대 이름·홈/원정·상대 전술
    [SerializeField] private TMP_Text myTacticsText;   // 내 카드 이름 + 조정 여부
    [SerializeField] private Button tacticsButton;
    [SerializeField] private Button backButton;        // 홈으로
    [SerializeField] private Button startButton;   // 글자는 프리팹의 "경기 시작" 고정. 시즌 끝 분기는 홈이 맡아 여기서 바꿀 일이 없다

    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    public override void Open()
    {
        base.Open();
        if (!initialized)
        {
            initialized = true;
            tacticsButton.onClick.AddListener(OnTactics);
            backButton.onClick.AddListener(OnBack);
            startButton.onClick.AddListener(OnStart);
        }
        Refresh();
    }

    // 보고 중(IsReporting)엔 CurrentMatch가 던진다. 홈이 보고 중엔 [일정 진행]을 잠가 여기 올 길이 없지만, 새 진입 경로가 생겨도 조용히 틀리지 않게 잠근다
    private void Refresh()
    {
        SeasonSystem season = Season;
        bool ready = !season.IsReporting;
        tacticsButton.interactable = ready;
        startButton.interactable = ready && !season.IsOver;
        if (!ready)
        {
            roundText.text = "다른 경기 결과 계산 중…";
            matchupText.text = string.Empty;
            myTacticsText.text = string.Empty;
            return;
        }
        roundText.text = season.NextMatchLine();
        matchupText.text = season.IsOver ? MatchdayLines.SeasonOverNotice : MatchdayLines.Matchup(season.CurrentMatch);
        myTacticsText.text = MatchdayLines.MyTactics(season.State, TeamTacticsRepository.All);
    }

    private void OnTactics()
    {
        uiManager.OpenScreen<TacticsScreen>();
    }

    private void OnBack()
    {
        uiManager.OpenScreen<LobbyScreen>();
    }

    // 저장하고 인게임으로. SkipTitle: 인게임 GameReady에 화면이 없으므로 바로 GamePlay로 보낸다(GameManager.Start가 읽고 끈다)
    private void OnStart()
    {
        if (Season.IsOver) { return; }   // 버튼이 잠겨 있어 안 오지만, 끝난 시즌으로 인게임에 들어가면 안전장치(PrepareNextMatch)가 다음 시즌을 열어 버린다
        SaveLoadSystem.Instance.Save();
        GameManager.SkipTitle = true;
        SceneManager.LoadScene("InGameScene");
    }
}
