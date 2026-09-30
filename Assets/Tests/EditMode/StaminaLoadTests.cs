using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 체력 부담 게이지(09-30, 유저 제안 + 방식 A 승인: 실측 표). ①표 파서: 81칸(압박 시작 3구역 × 3단 × 전진 정도 3단)이 빠짐없이 한 번씩
// ②조회: 내 전술의 압박 시작·전진 정도로 칸을 찾는다 ③막대 칸 수·단계 ④표가 지금 엔진과 맞는지(러너로 다시 재서 대조)
public class StaminaLoadTests
{
    private const string Header = "pressStartOwn,pressStartMid,pressStartOpp,mentality,slowedPercent";

    private static List<StaminaLoadEntry> RealTable()
    {
        return StaminaLoadTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/StaminaLoadTable.csv"));
    }

    private static TeamTactics Tactics(int own, int mid, int opp, int mentality)
    {
        return new TeamTactics { PressStart = new[] { own, mid, opp }, Mentality = mentality };
    }

    [Test]
    public void 실제_표는_81칸이고_조합마다_한_칸이다()
    {
        List<StaminaLoadEntry> table = RealTable();
        Assert.AreEqual(81, table.Count);
        for (int own = 0; own < TeamTactics.Levels; own++)
        {
            for (int mid = 0; mid < TeamTactics.Levels; mid++)
            {
                for (int opp = 0; opp < TeamTactics.Levels; opp++)
                {
                    for (int mentality = 0; mentality < TeamTactics.Levels; mentality++)
                    {
                        Assert.DoesNotThrow(() => StaminaLoad.Find(table, Tactics(own, mid, opp, mentality)), $"{own},{mid},{opp},{mentality}");
                    }
                }
            }
        }
    }

    [Test]
    public void 칸이_빠지거나_겹치거나_범위_밖이면_거부한다()
    {
        Assert.Throws<FormatException>(() => StaminaLoadTableParser.Parse(Header + "\n0,0,0,0,1.5\n"), "81칸이 아니다");
        Assert.Throws<FormatException>(() => StaminaLoadTableParser.Parse(Header + "\n0,0,0,0,1.5\n0,0,0,0,2.0\n"), "같은 조합이 두 번");
        Assert.Throws<FormatException>(() => StaminaLoadTableParser.Parse(Header + "\n3,0,0,0,1.5\n"), "단계는 0~2");
        Assert.Throws<FormatException>(() => StaminaLoadTableParser.Parse(Header + "\n0,0,0,0,-1\n"), "비율은 0 이상");
        Assert.Throws<FormatException>(() => StaminaLoadTableParser.Parse(""));
    }

    [Test]
    public void 조회는_압박_시작_3구역과_전진_정도만_본다()
    {
        List<StaminaLoadEntry> table = RealTable();
        TeamTactics plain = Tactics(2, 1, 0, 1);
        TeamTactics other = Tactics(2, 1, 0, 1);
        other.Tempo = new[] { 2, 2, 2 };
        other.CounterPress = 2;
        Assert.AreEqual(StaminaLoad.Find(table, plain), StaminaLoad.Find(table, other), "속도·역압박은 체력 부담에 영향이 없어 표에 없다(09-30 계측)");
        Assert.Less(StaminaLoad.Find(table, Tactics(0, 0, 0, 1)), StaminaLoad.Find(table, Tactics(2, 2, 2, 1)), "전 구역 안 감 < 전 구역 적극");
    }

    [Test]
    public void 막대는_표의_최소가_1칸_최대가_8칸이고_그_사이는_고르게_나눈다()
    {
        Assert.AreEqual(1, StaminaLoad.FilledSegments(2f, min: 2f, max: 9f));
        Assert.AreEqual(8, StaminaLoad.FilledSegments(9f, 2f, 9f));
        Assert.AreEqual(5, StaminaLoad.FilledSegments(6f, 2f, 9f), "2~9를 7등분: 6은 1 + 4칸");
        Assert.AreEqual(1, StaminaLoad.FilledSegments(1f, 2f, 9f), "범위 밖은 끝 칸으로");
        Assert.AreEqual(8, StaminaLoad.FilledSegments(10f, 2f, 9f));
        Assert.AreEqual(1, StaminaLoad.FilledSegments(5f, 5f, 5f), "표가 한 값뿐이면 1칸");
        Assert.AreEqual(4, StaminaLoad.FilledSegments(2.5f, 0f, 7f), "딱 절반(2.5칸)은 올린다");
        Assert.AreEqual(5, StaminaLoad.FilledSegments(3.5f, 0f, 7f), "3.5칸도 올린다(짝수 쪽 반올림이면 4칸 + 1이 된다)");
    }

    [Test]
    public void 단계는_3칸까지_낮음_6칸까지_보통_그_위는_높음이다()
    {
        Assert.AreEqual(StaminaLoadLevel.Low, StaminaLoad.LevelOf(1));
        Assert.AreEqual(StaminaLoadLevel.Low, StaminaLoad.LevelOf(3));
        Assert.AreEqual(StaminaLoadLevel.Medium, StaminaLoad.LevelOf(4));
        Assert.AreEqual(StaminaLoadLevel.Medium, StaminaLoad.LevelOf(6));
        Assert.AreEqual(StaminaLoadLevel.High, StaminaLoad.LevelOf(7));
        Assert.AreEqual(StaminaLoadLevel.High, StaminaLoad.LevelOf(8));
    }

    [Test]
    public void 카드_4장_중_압박_축구만_높음이고_나머지는_보통이다()
    {
        List<StaminaLoadEntry> table = RealTable();
        List<TeamTactics> cards = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
        for (int i = 0; i < cards.Count; i++)
        {
            StaminaLoadLevel expected = cards[i].PresetId == "pressing" ? StaminaLoadLevel.High : StaminaLoadLevel.Medium;
            Assert.AreEqual(expected, StaminaLoad.LevelOf(StaminaLoad.FilledSegments(table, cards[i])), cards[i].PresetId);
        }
    }

    [Test]
    public void 표의_범위는_실제_표에서_읽는다()
    {
        List<StaminaLoadEntry> table = RealTable();
        (float min, float max) range = StaminaLoad.Range(table);
        Assert.Less(range.min, range.max);
        Assert.AreEqual(1, StaminaLoad.FilledSegments(range.min, range.min, range.max));
        Assert.AreEqual(8, StaminaLoad.FilledSegments(range.max, range.min, range.max));
    }

    [Test]
    public void 압박을_안_가면_막대가_낮고_전_구역_적극이면_높다()
    {
        List<StaminaLoadEntry> table = RealTable();
        int none = StaminaLoad.FilledSegments(table, Tactics(0, 0, 0, 1));
        int balanced = StaminaLoad.FilledSegments(table, Tactics(2, 1, 0, 1));   // 균형 카드의 압박 시작
        int all = StaminaLoad.FilledSegments(table, Tactics(2, 2, 2, 1));
        Assert.AreEqual(StaminaLoadLevel.Low, StaminaLoad.LevelOf(none));
        Assert.Less(none, balanced);
        Assert.Less(balanced, all);
        Assert.AreEqual(StaminaLoadLevel.High, StaminaLoad.LevelOf(all));
    }

    // 표는 러너로 잰 값이라 체력·압박 규칙이 바뀌면 낡는다. 4칸을 20판씩 다시 재서 표와 1%p 넘게 벌어지면 깨진다.
    // 표는 100판(시드 101~200), 여기는 20판(시드 101~120)이다. 09-30에 81칸을 20판으로 다시 쟀을 때 100판 값과의 차이는 최대 0.4%p였다.
    // 깨지면 스크래치 LoadTable 하네스(Vault 실험/2026-09-30-LoadTable.cs.txt)로 표를 다시 만든다
    [Test]
    public void 표는_지금_엔진으로_다시_잰_값과_1퍼센트포인트_안에서_맞는다()
    {
        List<StaminaLoadEntry> table = RealTable();
        TeamTactics[] cells = { Tactics(0, 0, 0, 1), Tactics(2, 1, 0, 1), Tactics(2, 2, 2, 1), Tactics(2, 1, 0, 2) };   // 압박 3칸 + 전진 정도가 다른 1칸
        for (int i = 0; i < cells.Length; i++)
        {
            float measured = MeasureSlowedPercent(cells[i], matches: 20);
            Assert.AreEqual(StaminaLoad.Find(table, cells[i]), measured, 1f, $"압박 시작 {string.Join(",", cells[i].PressStart)}");
        }
    }

    // 표를 만든 하네스와 같은 식: 기본 4-4-2·균형 카드에서 압박 시작·전진 정도만 바꾸고, 내 필드 선수의 단기 체력이 문턱 아래인 표본 비율(25틱마다)
    private static float MeasureSlowedPercent(TeamTactics cell, int matches)
    {
        List<PlayerStats> players = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<StageEntry> rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1);
        TeamTactics balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(t => t.PresetId == "balanced");
        TeamTactics mine = TacticsEditing.Copy(balanced);
        mine.PressStart = (int[])cell.PressStart.Clone();
        mine.Mentality = cell.Mentality;

        const int FirstSeed = 101;
        const int SampleEvery = 25;
        long samples = 0;
        long slowed = 0;
        for (int seed = FirstSeed; seed < FirstSeed + matches; seed++)
        {
            MatchSimulation sim = MatchAssembler.Create(players, rows, mine, balanced, seed);
            for (int tick = 0; tick < MatchTuning.MatchTicks; tick++)
            {
                sim.Tick(MatchTuning.FixedStep);
                if (tick % SampleEvery != 0) { continue; }
                for (int i = 0; i < sim.Players.Count; i++)
                {
                    PlayerState p = sim.Players[i];
                    if (p.Team != 0 || p.IsGoalkeeper) { continue; }
                    samples++;
                    if (p.Stamina < MatchTuning.FatigueEffortThreshold) { slowed++; }
                }
            }
        }
        return 100f * slowed / samples;
    }
}
