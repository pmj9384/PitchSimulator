using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;

// 새 시즌의 재료를 모아 SeasonState를 만든다(09-27 확정 스펙 ①). 바뀌는 이유 = "내 기본 11명이 어디서 오나".
// 지금은 StageComposition 1번 편성의 player 쪽. 스카우트(§4-5)가 오면 이전 시즌 로스터를 이어받는 입구가 여기 생긴다
public static class SeasonFactory
{
    public const int DefaultRosterStage = 1;

    public static SeasonState NewSeason(int tier, int seasonSeed, string presetId)
    {
        IReadOnlyList<StageEntry> rows = StageCompositionRepository.RowsFor(DefaultRosterStage);
        return SeasonState.NewSeason(tier, seasonSeed, rows, PlayerTableRepository.All, presetId);
    }
}
