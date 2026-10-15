using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Data;
using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 스카우트 화면(10-12, 스펙 §4-5 「10-12 구현 설계」). 로비 매치데이 허브의 [스카우트]로 열린다(10-13, §2-1. 그 전엔 인게임 결과 화면 위에 떴다).
// 로비에선 결과 보고가 늘 끝나 있어 "보고가 끝날 때까지 잠금"이 없다. 저장이 실패한 라운드 위에서 시도를 쓰면 다음 로드 때 어긋나므로 그때만 허브가 버튼을 잠근다
// 세 열을 왼쪽부터 고른다: ①리그 상대 팀 → ②그 팀 11명(자리·역할·★·성공 %) → ③같은 자리의 내 선수. 내 선수를 누르는 순간 판정이다(미리보기와 같은 확률로 주사위 하나).
// 레퍼런스 프로야구 스피리츠 A처럼 확률을 숨기지 않는다: 안 만난 팀은 절반, 같은 선수 재시도는 +10%가 목록 숫자에 그대로 보인다.
// 규칙(별·확률·주사위·교체)은 전부 코어(ScoutRules·SeasonState), 시도 소모·저장은 SeasonSystem. 여기는 목록을 그리고 누른 것을 넘긴다.
// 목록은 버튼 배열을 미리 깔아 두고 쓰는 만큼만 켠다(PlayerTacticsPanel의 역할 버튼과 같은 꼴): 팀은 최대 11(1부 12팀), 선수 11,
// 같은 자리의 내 선수는 한 포메이션에 최대 3명(3백의 CB, 5-3-2의 CM. FormationTemplates.csv 실측)이라 5면 넉넉하다.
// 안내 글자(verdictText)는 누른 쪽 핸들러만 쓴다. 채우기 함수가 같이 쓰면 마지막 시도의 판정이 "시도가 없다"에 덮인다(10-12 리뷰 🔴)
// 버튼 배선은 처음 열릴 때 한 번(TacticsBoardView와 같은 꼴). UIManager가 시작 때 Close만 부르므로 Awake에 두면 열리기 전엔 안 돈다
public class ScoutScreen : UIScreen
{
    private const int NoSelection = -1;   // 아직 안 골랐다(팀 id·선수 번호 둘 다 0 이상이라 겹치지 않는다)

    [Header("상단 (남은 시도·코인·구매·닫기)")]
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private TMP_Text noticeText;    // 4부 안내 한 줄
    [SerializeField] private Button buyButton;       // 코인으로 시도 +1
    [SerializeField] private TMP_Text buyButtonLabel;
    [SerializeField] private Button closeButton;

    [Header("① 팀 ② 선수 ③ 내 선수 (버튼은 미리 깔아 두고 쓰는 만큼만 켠다)")]
    [SerializeField] private ScoutEntryButton[] teamButtons;
    [SerializeField] private ScoutEntryButton[] playerButtons;
    [SerializeField] private ScoutEntryButton[] myButtons;
    [SerializeField] private TMP_Text verdictText;   // 다음에 할 일·미리보기·판정 결과

    private readonly List<int> myCandidates = new List<int>();   // ③열 버튼 번호 → 내 선수 id
    private int selectedTeamId = NoSelection;
    private int selectedPlayerIndex = NoSelection;
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;
    private static int Coins => GameDataManager.Instance.PlayerAccountData.Coins;

    // 열 때마다 처음부터: 팀만 보이고 나머지는 비어 있다
    public override void Open()
    {
        base.Open();
        if (!initialized)
        {
            initialized = true;
            buyButton.onClick.AddListener(OnBuy);
            closeButton.onClick.AddListener(OnClose);
            buyButtonLabel.text = $"시도 +1 ({ScoutRules.ExtraAttemptCoinPrice}코인)";
            InitializeList(teamButtons, OnTeamClicked);
            InitializeList(playerButtons, OnPlayerClicked);
            InitializeList(myButtons, OnMyPlayerClicked);
        }
        selectedTeamId = NoSelection;
        selectedPlayerIndex = NoSelection;
        verdictText.text = "영입할 팀을 고른다";
        noticeText.text = NoticeLine(Season);
        ShowHeader();
        ShowTeams();
        ShowPlayers();
        ShowMyPlayers();
    }

    private static void InitializeList(ScoutEntryButton[] buttons, Action<ScoutEntryButton> onClicked)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Initialize();
            buttons[i].Clicked += onClicked;
        }
    }

    #region 입력
    private void OnTeamClicked(ScoutEntryButton button)
    {
        int index = Array.IndexOf(teamButtons, button);
        selectedTeamId = Season.Opponents[index].TeamId;
        selectedPlayerIndex = NoSelection;
        verdictText.text = "영입할 선수를 고른다";
        ShowTeams();
        ShowPlayers();
        ShowMyPlayers();
    }

    private void OnPlayerClicked(ScoutEntryButton button)
    {
        selectedPlayerIndex = Array.IndexOf(playerButtons, button);
        ScoutVerdict preview = Season.PreviewScout(selectedTeamId, selectedPlayerIndex);
        verdictText.text = PreviewLine(preview);
        ShowPlayers();
        ShowMyPlayers();
    }

    // 여기서 판정한다. 성공이면 내 선수가 바뀌고 저장까지 끝난 뒤 돌아온다(SeasonSystem.TryScout)
    private void OnMyPlayerClicked(ScoutEntryButton button)
    {
        int index = Array.IndexOf(myButtons, button);
        PlayerStats target = TargetPlayer();
        ScoutVerdict verdict = Season.TryScout(selectedTeamId, selectedPlayerIndex, myCandidates[index]);
        verdictText.text = VerdictLine(verdict, target);
        ShowHeader();
        ShowPlayers();      // 재시도 보정으로 그 선수의 확률이 달라졌다
        ShowMyPlayers();    // 성공했으면 그 자리의 역할 이름이 바뀌었다. 시도가 0이면 잠긴다
    }

    private void OnBuy()
    {
        if (!Season.BuyScoutAttempt())
        {
            verdictText.text = $"코인이 부족하다 ({Coins} / {ScoutRules.ExtraAttemptCoinPrice})";
            return;
        }
        verdictText.text = "시도가 1 늘었다";
        ShowHeader();
        ShowMyPlayers();   // 시도가 0이라 잠겨 있던 ③열이 풀린다
    }

    // 닫기 = 허브로 돌아간다(여기로 들어오는 길이 허브 하나다)
    private void OnClose()
    {
        uiManager.OpenScreen<MatchdayScreen>();
    }
    #endregion

    #region 화면 채우기
    private void ShowHeader()
    {
        infoText.text = $"남은 시도 {Season.State.RemainingScoutAttempts}  ·  코인 {Coins:N0}";
        buyButton.interactable = Coins >= ScoutRules.ExtraAttemptCoinPrice;
    }

    // ①열: 리그 상대 팀. 순위표 순서가 아니라 팀 id 순서(Opponents 순서)로 둔다: 버튼 번호 = Opponents 번호라 따로 매핑이 없다
    private void ShowTeams()
    {
        IReadOnlyList<GeneratedTeam> opponents = Season.Opponents;
        if (opponents.Count > teamButtons.Length)
        {
            Debug.LogError($"[ScoutScreen] 상대 팀 {opponents.Count}개 > 버튼 {teamButtons.Length}개. 뒤쪽 팀이 안 보인다");
        }
        for (int i = 0; i < teamButtons.Length; i++)
        {
            if (i >= opponents.Count)
            {
                teamButtons[i].Hide();
                continue;
            }
            GeneratedTeam team = opponents[i];
            bool met = ScoutRules.MetBefore(Season.State.Results, SeasonState.MyTeamId, team.TeamId);
            string text = met ? team.Name : $"{team.Name}  (아직 안 붙어 봄, 확률 ×{ScoutRules.UnmetFactor:0.##})";
            teamButtons[i].Show(text, team.TeamId == selectedTeamId, interactable: true);
        }
    }

    // ②열: 고른 팀의 11명. 별과 성공 확률을 같이 적는다(확률 공개). 팀을 안 골랐으면 비어 있다
    private void ShowPlayers()
    {
        if (selectedTeamId == NoSelection)
        {
            HideAll(playerButtons);
            return;
        }
        GeneratedTeam team = SeasonRunner.Find(Season.Opponents, selectedTeamId);
        for (int i = 0; i < playerButtons.Length; i++)
        {
            if (i >= team.Players.Count)
            {
                playerButtons[i].Hide();
                continue;
            }
            PlayerStats player = team.Players[i];
            ScoutVerdict preview = Season.PreviewScout(selectedTeamId, i);
            string text = $"{player.RoleId} {player.DisplayName}   {Stars(preview.Stars)}  {Percent(preview.Chance)}";
            playerButtons[i].Show(text, i == selectedPlayerIndex, interactable: true);
        }
    }

    // ③열: 고른 상대 선수와 같은 자리의 내 선수. 시도가 없으면 보이되 잠긴다(코인으로 +1 하면 풀린다)
    private void ShowMyPlayers()
    {
        myCandidates.Clear();
        if (selectedPlayerIndex == NoSelection)
        {
            HideAll(myButtons);
            return;
        }
        PlayerStats target = TargetPlayer();
        IReadOnlyList<RosterPlayer> roster = Season.State.Roster;
        for (int i = 0; i < roster.Count; i++)
        {
            int playerId = roster[i].PlayerId;
            if (Season.State.CanReplacePlayer(playerId, target)) { myCandidates.Add(playerId); }
        }
        if (myCandidates.Count > myButtons.Length)
        {
            Debug.LogError($"[ScoutScreen] {target.RoleId} 자리의 내 선수 {myCandidates.Count}명 > 버튼 {myButtons.Length}개");
        }

        bool canTry = Season.State.RemainingScoutAttempts > 0;
        for (int i = 0; i < myButtons.Length; i++)
        {
            if (i >= myCandidates.Count)
            {
                myButtons[i].Hide();
                continue;
            }
            PlayerStats mine = Season.State.FindRosterPlayer(myCandidates[i]).Stats;
            myButtons[i].Show($"{mine.RoleId} {mine.DisplayName}", selected: false, interactable: canTry);
        }
    }

    private PlayerStats TargetPlayer()
    {
        return SeasonRunner.Find(Season.Opponents, selectedTeamId).Players[selectedPlayerIndex];
    }

    private static void HideAll(ScoutEntryButton[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Hide();
        }
    }
    #endregion

    #region 글자
    // 안내 한 줄. 시즌이 끝난 뒤의 영입은 역할만 다음 시즌으로 가고 능력치는 기본으로 돌아간다(스펙 §4-5 [가정] ④, 10-13 재검증).
    // "이 부는 전부 ★1"인지는 코어가 판정한다(부 번호 4를 UI가 알 필요 없게)
    private static string NoticeLine(SeasonSystem season)
    {
        if (season.IsOver) { return "시즌이 끝났다. 지금 영입하면 역할은 다음 시즌으로 가지만 능력치는 기본으로 돌아간다"; }
        if (ScoutRules.AllStarsAreOne(season.Tier)) { return "이 부의 상대는 모두 기본 역할이라 ★1뿐이다. 역할이 다양한 선수는 상위 부부터 나온다"; }
        return string.Empty;
    }

    private static string PreviewLine(ScoutVerdict preview)
    {
        if (Season.State.RemainingScoutAttempts <= 0) { return $"{Stars(preview.Stars)} · 성공 {Percent(preview.Chance)} · 남은 시도가 없다. [시도 +1]로 산다"; }
        return $"{Stars(preview.Stars)} · 성공 {Percent(preview.Chance)} · 교체할 내 선수를 누르면 시도 1회를 쓴다";
    }

    private static string VerdictLine(ScoutVerdict verdict, PlayerStats target)
    {
        string tail = Season.State.RemainingScoutAttempts <= 0 ? " · 남은 시도가 없다" : string.Empty;
        if (verdict.Success)
        {
            return $"영입 성공: {target.RoleId} {target.DisplayName}. 능력치는 내 팀 총점에 맞춰 조정했고 개인 지시는 초기화했다{tail}";
        }
        return $"실패 (주사위 {PercentFine(verdict.Roll)} ≥ 확률 {Percent(verdict.Chance)}). 같은 선수에게 다시 도전하면 확률 +{Percent(ScoutRules.RetryBonus)}{tail}";
    }

    private static string Stars(int stars)
    {
        return new string('★', stars);
    }

    // 기기 로캘에 따라 "60 %"처럼 띄어지는 걸 막는다(한 곳에서 같은 모양으로)
    private static string Percent(float ratio)
    {
        return Mathf.RoundToInt(ratio * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    // 주사위는 소수 한 자리까지: 정수로 반올림하면 "60% ≥ 60%"처럼 같아 보이는 실패가 생긴다
    private static string PercentFine(float ratio)
    {
        return (ratio * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }
    #endregion
}
