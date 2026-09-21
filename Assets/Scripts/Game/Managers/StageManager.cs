using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Placement;
using UnityEngine;

// "이번 판이 어떤 판인가". 스테이지 데이터를 필드로 옮기는 담당.
// 1차 이식(09-16): 양 팀 행을 전부 CSV에서 스폰한다. 플레이어 팀은 배치 UI(2차)가 붙으면 PlacementManager 몫이 된다.
public class StageManager : InGameManager
{
    [SerializeField] private int stageNumber = 1;   // 아웃게임(3주차)이 붙기 전까지 인스펙터로 고른다

    public int StageNumber => stageNumber;
    public string DisplayName { get; private set; }
    public string OpponentName { get; private set; }

    private bool loaded;

    public override void Initialize()
    {
        GameManager.AddGameStateEnterAction(GameManager.GameState.GameReady, Load);   // 판이 차려질 때 스폰
    }

    // 스테이지 데이터 → 선수 스폰. 씬 로드당 한 번. 두 번 돌면 선수가 두 배가 된다
    private void Load()
    {
        if (loaded) { return; }
        loaded = true;

        StageInfo info = StageTableRepository.Get(stageNumber);
        if (info == null)
        {
            // 조용히 넘기면 빈 필드로 경기가 시작된다. 데이터 오류는 크게 터뜨린다
            throw new InvalidOperationException($"[StageManager] StageTable에 stage {stageNumber} 행이 없다");
        }

        DisplayName = info.DisplayName;
        OpponentName = info.OpponentName;

        IReadOnlyList<StageEntry> rows = StageCompositionRepository.RowsFor(stageNumber);
        if (rows.Count == 0)
        {
            throw new InvalidOperationException($"[StageManager] StageComposition에 stage {stageNumber} 행이 없다");
        }

        for (int i = 0; i < rows.Count; i++)
        {
            SpawnRow(rows[i]);
        }
    }

    // count가 1보다 크면 폭(Z) 방향으로 최소 간격씩 벌려 세운다. 공격 시·수비 시 자리를 같은 간격으로 벌린다
    private void SpawnRow(StageEntry row)
    {
        float spread = FieldBounds.MinSpacing;
        float attackStartZ = row.PosZ - (row.Count - 1) * spread * 0.5f;
        float defendStartZ = row.DefendZ - (row.Count - 1) * spread * 0.5f;

        for (int i = 0; i < row.Count; i++)
        {
            Vector3 attackHome = new Vector3(row.PosX, 0f, attackStartZ + i * spread);
            Vector3 defendHome = new Vector3(row.DefendX, 0f, defendStartZ + i * spread);
            GameManager.Players.Spawn(row.Id, row.Team, attackHome, defendHome);
        }
    }

    public override void Clear()
    {
        GameManager.RemoveGameStateEnterAction(GameManager.GameState.GameReady, Load);
        loaded = false;
        DisplayName = null;
        OpponentName = null;
    }
}
