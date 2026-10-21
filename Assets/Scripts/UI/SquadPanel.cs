using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 스쿼드 패널(스펙 §4-5 「10-19 스쿼드」). 전술 화면(TacticsBoardView)의 왼쪽, [스쿼드] 탭.
// 벤치 선수를 한 줄씩 보여 주고 ①필드에서 고른 선발과 같은 자리면 [교체] ②[방출 +코인](★×SquadRules.ReleaseCoinsPerStar)을 낸다. 방출은 두 번 눌러 확정한다(실수로 선수를 잃지 않게).
// 규칙(같은 자리만 교체, 벤치만 방출, 코인 = ★×30)은 전부 코어(SeasonState·SquadRules), 여기는 화면과 입력만(PlayerTacticsPanel과 같은 꼴).
// 이 패널은 필드도 다른 패널도 모른다. 값이 바뀌면 Changed, 선발이 바뀌면 StarterSwapped만 쏜다
public class SquadPanel : MonoBehaviour
{
    private const int NoRow = -1;

    [SerializeField] private TMP_Text headerText;          // "선발: ST 포처 · 벤치 3/7"
    [Header("벤치 행 (SquadRules.MaxBench개, 쓰는 만큼만 켠다)")]
    [SerializeField] private GameObject[] rowRoots;
    [SerializeField] private Image[] rowIcons;             // 선수 초상(10-22)
    [SerializeField] private TMP_Text[] rowLabels;         // "ST 타깃맨 ★3"
    [SerializeField] private Button[] swapButtons;
    [SerializeField] private Button[] releaseButtons;
    [SerializeField] private TMP_Text[] releaseLabels;     // "방출 90" → 한 번 누르면 "정말? 90"

    public event Action Changed;              // 교체·방출 뒤. 필드가 다시 그리려고 듣는다
    public event Action<int> StarterSwapped;  // 교체로 선발이 된 선수 id. 필드가 그 선수를 고르게

    private readonly List<RosterPlayer> bench = new List<RosterPlayer>();   // 행 번호 → 벤치 선수
    private int starterId = PitchView.NoPlayer;
    private int pendingRelease = NoRow;   // 두 번 눌러 확정할 행
    private bool initialized;

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 필드에서 선수를 고르거나 화면을 열 때 부른다
    public void Show(int selectedStarterId)
    {
        EnsureInitialized();
        starterId = selectedStarterId;
        pendingRelease = NoRow;
        Render();
    }

    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;
        if (rowRoots.Length != SquadRules.MaxBench || swapButtons.Length != rowRoots.Length || releaseButtons.Length != rowRoots.Length)
        {
            Debug.LogError($"[SquadPanel] 행 {rowRoots.Length}·교체 {swapButtons.Length}·방출 {releaseButtons.Length}개가 벤치 {SquadRules.MaxBench}명과 안 맞는다. 프리팹 배선을 확인한다");
            return;
        }
        for (int i = 0; i < rowRoots.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 행 번호를 잡게 한다
            swapButtons[i].onClick.AddListener(() => OnSwapClicked(index));
            releaseButtons[i].onClick.AddListener(() => OnReleaseClicked(index));
        }
    }

    #region 입력
    private void OnSwapClicked(int index)
    {
        if (index >= bench.Count) { return; }
        if (starterId == PitchView.NoPlayer) { return; }
        int incoming = bench[index].PlayerId;
        Season.SwapStarter(starterId, incoming);
        starterId = incoming;   // 교체 들어온 선수가 이제 선발. 필드도 같은 선수를 고른다
        pendingRelease = NoRow;
        Render();
        StarterSwapped?.Invoke(incoming);
        Changed?.Invoke();
    }

    // 첫 탭은 확인 요청, 같은 행을 다시 누르면 방출
    private void OnReleaseClicked(int index)
    {
        if (index >= bench.Count) { return; }
        if (pendingRelease != index)
        {
            pendingRelease = index;
            Render();
            return;
        }
        Season.ReleasePlayer(bench[index].PlayerId);
        pendingRelease = NoRow;
        Render();
        Changed?.Invoke();
    }
    #endregion

    #region 화면 채우기
    private void Render()
    {
        SeasonState state = Season.State;
        bench.Clear();
        bench.AddRange(state.Bench());
        headerText.text = HeaderLine(state);

        string starterRole = starterId == PitchView.NoPlayer ? string.Empty : state.FindRosterPlayer(starterId).Stats.RoleId;
        for (int i = 0; i < rowRoots.Length; i++)
        {
            bool hasPlayer = i < bench.Count;
            rowRoots[i].SetActive(hasPlayer);
            if (!hasPlayer) { continue; }
            RosterPlayer player = bench[i];
            Sprite portrait = PortraitRepository.Get(player.Portrait);
            rowIcons[i].sprite = portrait;
            rowIcons[i].enabled = portrait != null;   // 에셋이 빠졌으면 흰 네모 대신 빈 칸(ScoutEntryButton과 같은 규칙)
            rowLabels[i].text = $"{player.Stats.RoleId} {player.Stats.DisplayName}  {new string('★', player.Stars)}";
            swapButtons[i].interactable = PlayerTableLookup.IsSamePosition(player.Stats, starterRole);
            int coins = SquadRules.ReleaseCoins(player.Stars);
            releaseLabels[i].text = pendingRelease == i ? $"정말? +{coins}" : $"방출 +{coins}";
        }
    }

    private string HeaderLine(SeasonState state)
    {
        string benchCount = $"벤치 {state.BenchCount}/{SquadRules.MaxBench}";
        if (starterId == PitchView.NoPlayer) { return $"필드에서 선발을 고르면 같은 자리 벤치와 바꿀 수 있다 · {benchCount}"; }
        PlayerStats starter = state.FindRosterPlayer(starterId).Stats;
        return $"선발: {starter.RoleId} {starter.DisplayName} · {benchCount}";
    }
    #endregion
}
