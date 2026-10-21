using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 스카우트 화면(스펙 §4-5 「10-19 일괄 제시」). 로비 하단 탭 [스카우트]로 열린다(10-15, §2-1).
// 로비에선 결과 보고가 늘 끝나 있어 "보고가 끝날 때까지 잠금"이 없다. 저장이 실패한 라운드 위에서 시도를 쓰면 다음 로드 때 어긋나므로 그때만 이 화면이 판정·구매를 잠근다(탭 입구라 밖에서 막아 줄 버튼이 없다)
// 두 열: ①후보 4명(소속 부·팀·자리·역할·★·성공 %. 뒤 두 자리는 위 부) → ②[영입 시도]. 누르는 순간 판정이다(미리보기와 같은 확률로 주사위 하나). 성공 = 벤치에 추가(스펙 §4-5 10-19 스쿼드), 벤치가 차면 잠긴다
// 10-12 설계의 "팀 → 11명" 3열은 4부에서 전원 ★1이라 변별이 없어 접었다. 후보는 코어(ScoutPool)가 시드로 정하고 성공한 자리만 바뀐다. 실패한 후보는 남아 재시도 +10%가 숫자에 보인다
// 레퍼런스 프로야구 스피리츠 A처럼 확률을 숨기지 않는다: 안 만난 팀은 절반(내 부 후보만), 재시도는 +10%가 목록 숫자에 그대로 보인다. 보정값은 ScoutVerdict가 실어 준다.
// 규칙(후보·별·확률·주사위·영입)은 전부 코어(ScoutPool·ScoutRules·SeasonState), 시도 소모·저장은 SeasonSystem. 여기는 목록을 그리고 누른 것을 넘긴다.
// 목록은 버튼 배열을 미리 깔아 두고 쓰는 만큼만 켠다(PlayerTacticsPanel의 역할 버튼과 같은 꼴): 후보 4. ②열은 버튼 하나만 쓴다(나머지는 숨김. 10-19 전엔 같은 자리의 내 선수 목록이었다)
// 안내 글자(verdictText)는 누른 쪽 핸들러만 쓴다. 채우기 함수가 같이 쓰면 마지막 시도의 판정이 "시도가 없다"에 덮인다(10-12 리뷰 🔴)
// 판정 연출은 ScoutRevealPopup(카드 뒤집기, 10-21)이 맡고 verdictText는 그 아래 글자 기록이자 팝업이 없는 씬의 예비다
// 버튼 배선은 처음 열릴 때 한 번(TacticsBoardView와 같은 꼴). UIManager가 시작 때 Close만 부르므로 Awake에 두면 열리기 전엔 안 돈다
public class ScoutScreen : UIScreen
{
    private const int NoSelection = -1;   // 아직 안 골랐다(후보 자리 번호는 0 이상)

    [Header("상단 (남은 시도·코인·구매·닫기)")]
    [SerializeField] private TMP_Text infoText;
    [SerializeField] private TMP_Text noticeText;    // 4부 안내 한 줄
    [SerializeField] private Button buyButton;       // 코인으로 시도 +1
    [SerializeField] private TMP_Text buyButtonLabel;
    [SerializeField] private Button closeButton;

    [Header("① 후보 ② 영입 시도 (버튼은 미리 깔아 두고 쓰는 만큼만 켠다)")]
    [SerializeField] private ScoutEntryButton[] candidateButtons;
    [SerializeField] private ScoutEntryButton[] myButtons;   // [0]만 [영입 시도]로 쓴다
    [SerializeField] private TMP_Text verdictText;   // 다음에 할 일·미리보기·판정 결과

    private IReadOnlyList<ScoutCandidate> candidates = Array.Empty<ScoutCandidate>();   // ①열 버튼 번호 = 후보 자리 번호
    private int selectedSlot = NoSelection;
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;
    private static int Coins => GameDataManager.Instance.PlayerAccountData.Coins;

    // 열 때마다 처음부터: 후보만 보이고 내 선수 열은 비어 있다
    public override void Open()
    {
        base.Open();
        if (!initialized)
        {
            initialized = true;
            buyButton.onClick.AddListener(OnBuy);
            closeButton.onClick.AddListener(OnClose);
            buyButtonLabel.text = $"시도 +1 ({ScoutRules.ExtraAttemptCoinPrice}코인)";
            InitializeList(candidateButtons, OnCandidateClicked);
            InitializeList(myButtons, OnRecruitClicked);
        }
        selectedSlot = NoSelection;
        verdictText.text = "영입할 후보를 고른다";
        noticeText.text = NoticeLine(Season);
        ShowHeader();
        ShowCandidates();
        ShowRecruitButton();
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
    private void OnCandidateClicked(ScoutEntryButton button)
    {
        selectedSlot = Array.IndexOf(candidateButtons, button);
        ScoutCandidate picked = candidates[selectedSlot];
        ScoutVerdict preview = Season.PreviewScout(picked.TeamId, picked.PlayerIndex);
        verdictText.text = PreviewLine(preview);
        ShowCandidates();
        ShowRecruitButton();
    }

    // 여기서 판정한다. 성공이면 벤치에 들어가고 저장까지 끝난 뒤 돌아온다(SeasonSystem.TryScout).
    // 성공하면 그 자리의 후보가 다음 선수로 바뀌므로 선택을 푼다. 실패하면 같은 후보가 남아 재시도 +10%가 보이게 선택을 둔다.
    // 목록은 팝업 뒤에서 먼저 갱신하고, 팝업(카드 뒤집기)이 닫히면 바뀐 목록이 보인다(10-21 연출)
    private void OnRecruitClicked(ScoutEntryButton button)
    {
        ScoutCandidate picked = candidates[selectedSlot];
        PlayerStats target = TargetPlayer(picked);
        Sprite portrait = PortraitRepository.Get(PortraitRules.Of(picked.TeamId, picked.PlayerIndex));
        ScoutVerdict verdict = Season.TryScout(picked.TeamId, picked.PlayerIndex);
        string line = VerdictLine(verdict, target);
        verdictText.text = line;
        if (verdict.Success) { selectedSlot = NoSelection; }
        ShowHeader();
        ShowCandidates();      // 성공한 자리는 다음 후보로, 실패한 자리는 확률이 +10%
        ShowRecruitButton();   // 성공했으면 숨고, 시도가 0이거나 벤치가 차면 잠긴다
        ShowReveal(target, portrait, verdict, line);
    }

    // 팝업이 씬에 없으면(옛 씬) 글자 판정만 남는다
    private void ShowReveal(PlayerStats target, Sprite portrait, ScoutVerdict verdict, string line)
    {
        ScoutRevealPopup popup = uiManager.GetPopup<ScoutRevealPopup>();
        if (popup == null) { return; }
        popup.Show(target, portrait, verdict, line);
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
        ShowRecruitButton();   // 시도가 0이라 잠겨 있던 버튼이 풀린다
    }

    // 닫기 = 홈. 탭으로 들어왔으니 홈으로 돌아가야 하단 바 상태와 맞는다
    private void OnClose()
    {
        uiManager.OpenScreen<LobbyScreen>();
    }
    #endregion

    #region 화면 채우기
    // 저장 실패 상태면 구매도 잠근다: 산 시도가 저장 안 된 라운드 위에 얹힌다
    private void ShowHeader()
    {
        infoText.text = $"남은 시도 {Season.State.RemainingScoutAttempts}  ·  코인 {Coins:N0}";
        buyButton.interactable = CanSpend() && Coins >= ScoutRules.ExtraAttemptCoinPrice;
    }

    // 지난 결과 저장이 실패한 라운드 위에서는 시도를 쓰지 않는다. 다음 로드 때 시즌과 시도 기록이 어긋난다
    private static bool CanSpend()
    {
        return !Season.LastReportFailed;
    }

    // ①열: 후보 4명. 별·성공 확률·재시도 보정을 같이 적는다(확률 공개). 위 부 팀 이름엔 코어가 "(3부)"를 붙여 준다
    private void ShowCandidates()
    {
        candidates = Season.ScoutCandidates();
        if (candidates.Count > candidateButtons.Length)
        {
            Debug.LogError($"[ScoutScreen] 후보 {candidates.Count}명 > 버튼 {candidateButtons.Length}개. 뒤쪽 후보가 안 보인다");
        }
        for (int i = 0; i < candidateButtons.Length; i++)
        {
            if (i >= candidates.Count)
            {
                candidateButtons[i].Hide();
                continue;
            }
            Sprite portrait = PortraitRepository.Get(PortraitRules.Of(candidates[i].TeamId, candidates[i].PlayerIndex));   // 영입되면 이 번호가 선수를 따라간다(SeasonSystem.TryScout)
            candidateButtons[i].Show(CandidateLine(candidates[i]), portrait, i == selectedSlot, interactable: true);
        }
    }

    private static string CandidateLine(ScoutCandidate picked)
    {
        GeneratedTeam team = Season.ScoutTeam(picked.TeamId);
        PlayerStats player = team.Players[picked.PlayerIndex];
        ScoutVerdict preview = Season.PreviewScout(picked.TeamId, picked.PlayerIndex);
        string text = $"{team.Name} · {player.RoleId} {player.DisplayName}   {Stars(preview.Stars)}  {Percent(preview.Chance)}";
        if (preview.Retries > 0) { text += $"  (재시도 +{Percent(ScoutRules.RetryBonus * preview.Retries)})"; }
        if (preview.Unmet) { text += $"  (아직 안 붙어 봄 ×{ScoutRules.UnmetFactor:0.##})"; }
        return text;
    }

    // ②열: [영입 시도] 버튼 하나. 후보를 골라야 보이고, 시도가 없거나 벤치가 차면 보이되 잠긴다
    private void ShowRecruitButton()
    {
        HideAll(myButtons);
        if (selectedSlot == NoSelection) { return; }
        myButtons[0].Show($"영입 시도  (벤치 {Season.State.BenchCount}/{SquadRules.MaxBench})", selected: false, interactable: CanTryRecruit());
    }

    private static bool CanTryRecruit()
    {
        if (!CanSpend()) { return false; }
        if (Season.State.RemainingScoutAttempts <= 0) { return false; }
        return Season.CanRecruit;
    }

    private static PlayerStats TargetPlayer(ScoutCandidate picked)
    {
        return Season.ScoutTeam(picked.TeamId).Players[picked.PlayerIndex];
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
        if (season.LastReportFailed) { return "지난 경기 결과를 저장하지 못했다. 다음 경기를 치른 뒤 영입할 수 있다"; }
        if (season.IsOver) { return "시즌이 끝났다. 지금 영입하면 역할은 다음 시즌으로 가지만 능력치는 기본으로 돌아간다"; }
        if (!season.CanRecruit) { return $"벤치가 찼다({SquadRules.MaxBench}명). 전술 탭에서 방출해야 영입할 수 있다"; }
        if (ScoutRules.AllStarsAreOne(season.Tier)) { return "내 부의 상대는 모두 기본 역할이라 ★1뿐이다. 역할이 다른 선수는 뒤 두 자리의 위 부 후보에서 온다"; }
        return string.Empty;
    }

    private static string PreviewLine(ScoutVerdict preview)
    {
        if (Season.State.RemainingScoutAttempts <= 0) { return $"{Stars(preview.Stars)} · 성공 {Percent(preview.Chance)} · 남은 시도가 없다. [시도 +1]로 산다"; }
        if (!Season.CanRecruit) { return $"{Stars(preview.Stars)} · 성공 {Percent(preview.Chance)} · 벤치가 차서 영입할 수 없다"; }
        return $"{Stars(preview.Stars)} · 성공 {Percent(preview.Chance)} · [영입 시도]를 누르면 시도 1회를 쓴다";
    }

    private static string VerdictLine(ScoutVerdict verdict, PlayerStats target)
    {
        string tail = Season.State.RemainingScoutAttempts <= 0 ? " · 남은 시도가 없다" : string.Empty;
        if (verdict.Success)
        {
            return $"영입 성공: {target.RoleId} {target.DisplayName} → 벤치. 능력치는 내 팀 총점에 맞춰 조정했다. 선발로 쓰려면 전술 탭에서 바꾼다{tail}";
        }
        return $"실패 (주사위 {PercentFine(verdict.Roll)} ≥ 확률 {Percent(verdict.Chance)}). 같은 후보에게 다시 도전하면 확률 +{Percent(ScoutRules.RetryBonus)}{tail}";
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
