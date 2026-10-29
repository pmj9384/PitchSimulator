using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 명단 패널(10-27 ③b, 스펙 §8 화면 확정. 10-19 스쿼드 패널을 넓힘). 전술 화면 왼쪽의 기본 상태: 아무 칩도 고르지 않았을 때 보인다.
// 왼쪽 열 = 선발 11(누르면 그 선수가 선 슬롯을 고른다 → 자리 패널), 오른쪽 열 = 벤치 ≤ 7(보기만. 방출은 10-29부터 스카우트 화면의 내 벤치에서: 유저 "사고팔기는 스카우트 창에서").
// 선발 ↔ 벤치 교체는 여기 없다: 자리 패널의 후보 목록이 한다(FM26 Touch "같은 자리 벤치가 안 보인다" 불만을 거기서 푼다).
// 여기는 화면과 입력만. 선발을 고르면 StarterSelected만 쏜다
public class RosterPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text headerText;          // "선발 11 · 벤치 3/7"
    [Header("선발 열 (FormationTemplate.SlotCount개, 라인업 순서)")]
    [SerializeField] private GameObject[] starterRoots;
    [SerializeField] private Image[] starterIcons;
    [SerializeField] private TMP_Text[] starterLabels;     // "ST 포처 ★"
    [SerializeField] private Button[] starterButtons;
    [Header("벤치 열 (SquadRules.MaxBench개, 쓰는 만큼만 켠다)")]
    [SerializeField] private GameObject[] benchRoots;
    [SerializeField] private Image[] benchIcons;
    [SerializeField] private TMP_Text[] benchLabels;

    public event Action<int> StarterSelected; // 선발 행을 눌렀을 때 그 선수 id

    private readonly List<RosterPlayer> bench = new List<RosterPlayer>();   // 벤치 행 번호 → 선수
    private bool initialized;
    private bool wired;   // 프리팹 배열이 코어 상수와 맞을 때만 그린다

    private static SeasonSystem Season => GameDataManager.Instance.Season;

    // 화면을 열 때·명단으로 돌아올 때·세팅이 바뀔 때 부른다. 명단은 고른 선수가 없을 때만 보이므로 선택 상태를 받지 않는다
    public void Show()
    {
        EnsureInitialized();
        if (wired) { Render(); }
    }

    private void EnsureInitialized()
    {
        if (initialized) { return; }
        initialized = true;
        if (!SameLength(FormationTemplate.SlotCount, starterRoots, starterIcons, starterLabels, starterButtons))
        {
            Debug.LogError($"[RosterPanel] 선발 행 {starterRoots.Length}개 ≠ 슬롯 {FormationTemplate.SlotCount}. 프리팹 배선을 확인한다");
            return;
        }
        if (!SameLength(SquadRules.MaxBench, benchRoots, benchIcons, benchLabels))
        {
            Debug.LogError($"[RosterPanel] 벤치 행 {benchRoots.Length}개 ≠ 벤치 {SquadRules.MaxBench}명. 프리팹 배선을 확인한다");
            return;
        }
        wired = true;
        for (int i = 0; i < starterButtons.Length; i++)
        {
            int index = i;   // 클로저가 루프 변수 대신 이 행 번호를 잡게 한다
            starterButtons[i].onClick.AddListener(() => OnStarterClicked(index));
        }
    }

    private static bool SameLength(int expected, params Array[] arrays)
    {
        for (int i = 0; i < arrays.Length; i++)
        {
            if (arrays[i].Length != expected) { return false; }
        }
        return true;
    }

    #region 입력
    private void OnStarterClicked(int index)
    {
        IReadOnlyList<LineupEntry> lineup = Season.State.Lineup;
        if (index >= lineup.Count) { return; }
        StarterSelected?.Invoke(lineup[index].PlayerId);
    }
    #endregion

    #region 화면 채우기
    private void Render()
    {
        SeasonState state = Season.State;
        IReadOnlyList<LineupEntry> lineup = state.Lineup;
        bench.Clear();
        bench.AddRange(state.Bench());
        headerText.text = $"선발 {lineup.Count} · 벤치 {state.BenchCount}/{SquadRules.MaxBench}";

        for (int i = 0; i < starterRoots.Length; i++)
        {
            bool hasPlayer = i < lineup.Count;
            starterRoots[i].SetActive(hasPlayer);
            if (!hasPlayer) { continue; }
            RosterPlayer player = state.FindRosterPlayer(lineup[i].PlayerId);
            ShowPortrait(starterIcons[i], player);
            starterLabels[i].text = Line(player);
        }
        for (int i = 0; i < benchRoots.Length; i++)
        {
            bool hasPlayer = i < bench.Count;
            benchRoots[i].SetActive(hasPlayer);
            if (!hasPlayer) { continue; }
            RosterPlayer player = bench[i];
            ShowPortrait(benchIcons[i], player);
            benchLabels[i].text = Line(player);
        }
    }

    // 선수 한 줄: 자연 포지션 · 자연 변형 · ★. 역할·지시는 자리(슬롯) 것이라 여기 안 적는다(10-27 슬롯 중심)
    private static string Line(RosterPlayer player)
    {
        return $"{player.Stats.RoleId} {player.Stats.DisplayName} {new string('★', player.Stars)}";
    }

    private static void ShowPortrait(Image icon, RosterPlayer player)
    {
        Sprite portrait = PortraitRepository.Get(player.Portrait);
        icon.sprite = portrait;
        icon.enabled = portrait != null;   // 에셋이 빠졌으면 흰 네모 대신 빈 칸(ScoutEntryButton과 같은 규칙)
    }
    #endregion
}
