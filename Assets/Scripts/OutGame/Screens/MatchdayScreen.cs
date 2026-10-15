using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 매치데이 허브(스펙 §2-1, 10-13 확정. 레퍼런스 FM Pre-Match Hub). 경기 전에 만지는 것이 전부 이 한 화면에 모인다.
// 다음 경기(라운드·상대·홈/원정·상대 전술)와 내 전술 요약을 보여 주고 [전술판]·[스카우트]·[경기 시작] 세 길을 낸다.
// 인게임 GameReady 전술판을 없앤 자리다: 상대를 보고 고치는 일은 여기 [전술판]이 맡고, 인게임은 들어가는 순간 킥오프다(SkipTitle).
// 시즌이 끝났으면 [경기 시작] 자리가 [다음 시즌 시작]이 된다. 다음 시즌 열기는 여기가 부르고, 인게임의 PrepareNextMatch는 안전장치로만 남는다.
// 값은 전부 시즌(SeasonSystem)에서 읽기만 한다. 열 때마다 다시 채운다: 전술판·스카우트에서 돌아올 때 바뀐 값이 보여야 한다
public class MatchdayScreen : UIScreen
{
    // 인게임 → 로비로 돌아올 때 첫 화면을 허브로. 결과 화면이 켜고 OutGameManager가 읽고 끈다(GameManager.SkipTitle과 같은 꼴의 씬 간 한 번짜리 신호)
    public static bool OpenOnLobbyLoad;

    [SerializeField] private TMP_Text roundText;       // "4부 3/6 라운드" 또는 승강전 단계, 시즌 끝이면 승강 판정
    [SerializeField] private TMP_Text matchupText;     // 상대 이름·홈/원정·상대 전술
    [SerializeField] private TMP_Text myTacticsText;   // 내 카드 이름 + 조정 여부
    [SerializeField] private Button tacticsButton;
    [SerializeField] private Button scoutButton;
    [SerializeField] private TMP_Text scoutButtonLabel;
    [SerializeField] private Button startButton;
    [SerializeField] private TMP_Text startButtonLabel;

    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    public override void Open()
    {
        base.Open();
        if (!initialized)
        {
            initialized = true;
            tacticsButton.onClick.AddListener(OnTactics);
            scoutButton.onClick.AddListener(OnScout);
            startButton.onClick.AddListener(OnStart);
        }
        Refresh();
    }

    // 보고 중(IsReporting)엔 CurrentMatch가 던진다. 지금은 결과 화면이 보고가 끝나야 [다음 경기]를 풀어 여기 올 길이 없지만, 새 진입 경로가 생겨도 조용히 틀리지 않게 잠근다
    private void Refresh()
    {
        SeasonSystem season = Season;
        bool ready = !season.IsReporting;
        tacticsButton.interactable = ready;
        startButton.interactable = ready;
        if (!ready)
        {
            roundText.text = "다른 경기 결과 계산 중…";
            matchupText.text = string.Empty;
            myTacticsText.text = string.Empty;
            scoutButton.interactable = false;
            return;
        }
        roundText.text = season.NextMatchLine();
        matchupText.text = season.IsOver ? "시즌이 끝났다. 다음 시즌은 새 부의 상대와 다시 시작한다" : MatchupLine(season.CurrentMatch);
        myTacticsText.text = MyTacticsLine(season.State);
        scoutButtonLabel.text = $"스카우트 (시도 {season.State.RemainingScoutAttempts})";
        scoutButton.interactable = !season.LastReportFailed;   // 저장이 실패한 라운드 위에서 시도를 쓰면 다음 로드 때 어긋난다
        startButtonLabel.text = season.IsOver ? "다음 시즌 시작" : "경기 시작";
    }

    private static string MatchupLine(MatchSetup setup)
    {
        string venue = setup.MyTeamIsHome ? "홈" : "원정";
        return $"상대 {setup.OpponentName} ({venue}) · 상대 전술: {setup.Tactics1.DisplayName}";
    }

    // 카드 이름만. 슬라이더·세부 표로 바꿨으면 "(조정)"을 붙여 전술판에서 확인하게 한다(세부 값은 여기 안 적는다, [가정] ③)
    private static string MyTacticsLine(SeasonState state)
    {
        string card = state.MyTactics(TeamTacticsRepository.All).DisplayName;
        string adjusted = state.MyCustomTactics != null ? " (조정)" : string.Empty;
        return $"내 전술: {card}{adjusted}";
    }

    private void OnTactics()
    {
        uiManager.OpenScreen<TacticsScreen>();
    }

    private void OnScout()
    {
        uiManager.OpenScreen<ScoutScreen>();
    }

    // 시즌이 끝났으면 다음 시즌을 열고 이 화면을 다시 채운다(StartNextSeason이 저장까지 한다). 아니면 저장하고 인게임으로.
    // SkipTitle: 인게임 GameReady에 화면이 없으므로 바로 GamePlay로 보낸다(GameManager.Start가 읽고 끈다)
    private void OnStart()
    {
        if (Season.IsOver)
        {
            Season.StartNextSeason();
            Refresh();
            return;
        }
        SaveLoadSystem.Instance.Save();
        GameManager.SkipTitle = true;
        SceneManager.LoadScene("InGameScene");
    }
}
