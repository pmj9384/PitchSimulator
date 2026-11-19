using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Viewing;
using NUnit.Framework;

// 하이라이트 구간 규칙(11-06 S4 설계 4절). 합성 사건 목록으로 모드별 선택·하한·앞뒤 여유·병합·경계를 잠근다.
// 1초 = 10틱으로 둔다(실제 길이와 무관한 식이라 숫자를 읽기 쉽게)
public class HighlightRulesTests
{
    private const float TicksPerSecond = 10f;
    private const int Total = 54000;   // 90분 × 60초 × 10틱

    private static MatchEvent Shot(int tick, int team, float p, ShotOutcome outcome, int resolveDelay = 10)
    {
        return new MatchEvent(MatchEventKind.Shot, tick, tick + resolveDelay, team, team * 11, p, outcome);
    }

    private static MatchEvent Won(int tick, int team)
    {
        return new MatchEvent(MatchEventKind.PossessionWon, tick, tick, team, team * 11, 0f, ShotOutcome.Goal);
    }

    private static int Ticks(float seconds)
    {
        return (int)System.Math.Ceiling(seconds * TicksPerSecond);
    }

    [Test]
    public void 전체_모드는_경기_전체_구간_하나다()
    {
        var events = new List<MatchEvent> { Shot(1000, 0, 0.5f, ShotOutcome.Goal) };

        IReadOnlyList<TickRange> ranges = HighlightRules.Build(events, ViewingMode.Full, Total, TicksPerSecond);

        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual(0, ranges[0].Start);
        Assert.AreEqual(Total, ranges[0].End);
    }

    [Test]
    public void 주요_장면은_골과_큰_찬스만_고르고_하이라이트는_유효_슛과_위험한_블록을_더한다()
    {
        var events = new List<MatchEvent>
        {
            Shot(2000, 0, 0.05f, ShotOutcome.Goal),      // 0 골(낮은 p여도 주요 장면)
            Shot(6000, 1, 0.40f, ShotOutcome.Missed),    // 1 큰 찬스 빗나감 → 주요 장면
            Shot(10000, 0, 0.32f, ShotOutcome.Caught),   // 2 큰 찬스 세이브 → 주요 장면
            Shot(14000, 1, 0.10f, ShotOutcome.Parried),  // 3 유효 슛 → 하이라이트
            Shot(18000, 0, 0.20f, ShotOutcome.Blocked),  // 4 위험한 블록 → 하이라이트
            Shot(22000, 1, 0.10f, ShotOutcome.Blocked),  // 5 약한 블록 → 제외
            Shot(26000, 0, 0.20f, ShotOutcome.Missed),   // 6 약한 빗나감 → 제외
        };

        List<int> key = HighlightRules.ChooseShots(events, ViewingMode.Key);
        List<int> extended = HighlightRules.ChooseShots(events, ViewingMode.Extended);
        key.Sort();
        extended.Sort();

        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, key);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, extended);
    }

    [Test]
    public void 주요_장면이_하한보다_적으면_p_높은_순으로_채운다()
    {
        var events = new List<MatchEvent>
        {
            Shot(2000, 0, 0.10f, ShotOutcome.Missed),
            Shot(6000, 1, 0.25f, ShotOutcome.Missed),
            Shot(10000, 0, 0.05f, ShotOutcome.Blocked),
            Shot(14000, 1, 0.20f, ShotOutcome.Caught),
        };

        List<int> key = HighlightRules.ChooseShots(events, ViewingMode.Key);
        key.Sort();

        Assert.AreEqual(ViewingTuning.KeyMinimumShots, key.Count);
        CollectionAssert.AreEqual(new[] { 0, 1, 3 }, key, "p 0.25·0.20·0.10을 고르고 0.05는 뺀다");
    }

    [Test]
    public void 시작은_슛_팀이_공을_얻은_마지막_틱에서_여유를_뺀_자리다()
    {
        var events = new List<MatchEvent>
        {
            Won(9700, 1),   // 상대가 얻음(무시)
            Won(9800, 0),   // 슛 팀이 얻음 = 전개 시작
            Shot(10000, 0, 0.5f, ShotOutcome.Goal),
        };

        IReadOnlyList<TickRange> ranges = HighlightRules.Build(events, ViewingMode.Key, Total, TicksPerSecond);

        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual(9800 - Ticks(ViewingTuning.BuildUpLeadSec), ranges[0].Start);
        Assert.AreEqual(10010 + Ticks(ViewingTuning.AfterGoalSec), ranges[0].End, "골 결과 틱 뒤 3초");
    }

    [Test]
    public void 시작은_찬_순간_기준_최소_8초_최대_45초_앞으로_자른다()
    {
        var shortBuild = new List<MatchEvent> { Won(9990, 0), Shot(10000, 0, 0.5f, ShotOutcome.Missed) };
        var longBuild = new List<MatchEvent> { Won(1000, 0), Shot(10000, 0, 0.5f, ShotOutcome.Missed) };
        var noBuild = new List<MatchEvent> { Shot(10000, 0, 0.5f, ShotOutcome.Missed) };

        int shortStart = HighlightRules.Build(shortBuild, ViewingMode.Key, Total, TicksPerSecond)[0].Start;
        int longStart = HighlightRules.Build(longBuild, ViewingMode.Key, Total, TicksPerSecond)[0].Start;
        int noStart = HighlightRules.Build(noBuild, ViewingMode.Key, Total, TicksPerSecond)[0].Start;

        Assert.AreEqual(10000 - Ticks(ViewingTuning.MinLeadSec), shortStart);
        Assert.AreEqual(10000 - Ticks(ViewingTuning.MaxLeadSec), longStart);
        Assert.AreEqual(10000 - Ticks(ViewingTuning.MaxLeadSec), noStart, "소유 사건 없이 시작한 공격(킥오프·골킥)은 최대 여유");
    }

    [Test]
    public void 앞_슛보다_이전의_소유는_이번_공격의_시작이_아니다()
    {
        var events = new List<MatchEvent>
        {
            Won(9800, 0),
            Shot(9850, 1, 0.1f, ShotOutcome.Blocked),   // 그 사이 상대 슛(주요 장면이 아니라 안 골라진다): 9,800 소유의 공격은 여기서 끝났다
            Shot(10000, 0, 0.5f, ShotOutcome.Goal),
            Shot(30000, 1, 0.5f, ShotOutcome.Goal),     // 하한(3개)을 골로 채워 블록 슛이 안 골라지게
            Shot(40000, 0, 0.5f, ShotOutcome.Goal),
        };

        IReadOnlyList<TickRange> ranges = HighlightRules.Build(events, ViewingMode.Key, Total, TicksPerSecond);

        Assert.AreEqual(3, ranges.Count);
        Assert.AreEqual(10000 - Ticks(ViewingTuning.MaxLeadSec), ranges[0].Start, "9,800 소유를 쓰면 9,780에서 시작했을 것이다");
    }

    [Test]
    public void 끝은_결과별_여유를_더한다()
    {
        Assert.AreEqual(10010 + Ticks(ViewingTuning.AfterSaveSec), EndOf(ShotOutcome.Caught));
        Assert.AreEqual(10010 + Ticks(ViewingTuning.AfterSaveSec), EndOf(ShotOutcome.Parried));
        Assert.AreEqual(10010 + Ticks(ViewingTuning.AfterMissSec), EndOf(ShotOutcome.Missed));
        Assert.AreEqual(10010 + Ticks(ViewingTuning.AfterBlockSec), EndOf(ShotOutcome.Blocked));
    }

    private static int EndOf(ShotOutcome outcome)
    {
        var events = new List<MatchEvent> { Shot(10000, 0, 0.5f, outcome) };
        return HighlightRules.Build(events, ViewingMode.Key, Total, TicksPerSecond)[0].End;
    }

    [Test]
    public void 간격이_20초_안이면_합치고_넘으면_따로_둔다()
    {
        int gapStart = 10010 + Ticks(ViewingTuning.AfterGoalSec);   // 첫 구간 끝
        int nearShot = gapStart + Ticks(ViewingTuning.MergeGapSec) + Ticks(ViewingTuning.MaxLeadSec);       // 다음 구간 시작 = 첫 끝 + 20초
        int farShot = gapStart + Ticks(ViewingTuning.MergeGapSec) + Ticks(ViewingTuning.MaxLeadSec) + 1;

        var near = new List<MatchEvent> { Shot(10000, 0, 0.5f, ShotOutcome.Goal), Shot(nearShot, 1, 0.5f, ShotOutcome.Goal) };
        var far = new List<MatchEvent> { Shot(10000, 0, 0.5f, ShotOutcome.Goal), Shot(farShot, 1, 0.5f, ShotOutcome.Goal) };

        Assert.AreEqual(1, HighlightRules.Build(near, ViewingMode.Key, Total, TicksPerSecond).Count);
        Assert.AreEqual(2, HighlightRules.Build(far, ViewingMode.Key, Total, TicksPerSecond).Count);
    }

    [Test]
    public void 경기_처음과_끝을_넘는_구간은_자른다()
    {
        var events = new List<MatchEvent>
        {
            Shot(30, 0, 0.5f, ShotOutcome.Goal),
            Shot(Total - 5, 1, 0.5f, ShotOutcome.Goal, resolveDelay: 5),   // 마지막 틱 골(휘슬 마감)
        };

        IReadOnlyList<TickRange> ranges = HighlightRules.Build(events, ViewingMode.Key, Total, TicksPerSecond);

        Assert.AreEqual(0, ranges[0].Start);
        Assert.AreEqual(Total, ranges[ranges.Count - 1].End);
    }

    [Test]
    public void 구간은_시작_순으로_정렬되고_겹치지_않는다()
    {
        var events = new List<MatchEvent>();
        for (int i = 0; i < 40; i++)
        {
            int tick = 50000 - i * 1200;   // 뒤에서부터 넣어도
            events.Add(Shot(tick, i % 2, 0.35f, i % 3 == 0 ? ShotOutcome.Goal : ShotOutcome.Caught));
        }

        IReadOnlyList<TickRange> ranges = HighlightRules.Build(events, ViewingMode.Extended, Total, TicksPerSecond);

        for (int i = 1; i < ranges.Count; i++)
        {
            Assert.Greater(ranges[i].Start, ranges[i - 1].End, $"{i}번 구간");
        }
    }

    [Test]
    public void 고를_슛이_없으면_킥오프와_종료_장면을_보여_준다()
    {
        var events = new List<MatchEvent> { Won(500, 0), Won(900, 1) };

        IReadOnlyList<TickRange> key = HighlightRules.Build(events, ViewingMode.Key, Total, TicksPerSecond);
        IReadOnlyList<TickRange> extended = HighlightRules.Build(events, ViewingMode.Extended, Total, TicksPerSecond);

        int length = Ticks(ViewingTuning.EmptyMatchSec);
        Assert.AreEqual(2, key.Count);
        Assert.AreEqual(0, key[0].Start);
        Assert.AreEqual(length, key[0].End);
        Assert.AreEqual(Total - length, key[1].Start);
        Assert.AreEqual(Total, key[1].End);
        Assert.AreEqual(2, extended.Count);
    }

    // 구간 초는 화면 시간: 1초 = 고정 스텝 수(50틱). 실제 90분(추가시간 포함)은 경기 시계 기준과 같은 값이라 그 길이에서는 바뀌지 않는다
    [Test]
    public void 화면_1초는_50틱이고_실제_90분의_경기_시계와_같다()
    {
        Assert.AreEqual(50f, HighlightRules.ScreenTicksPerSecond);
        var added = new AddedTime(2, 5);
        float realTimeClock = (float)MatchLength.RealTime.TotalTicks(added) / MatchClock.TotalDisplaySeconds(added);
        Assert.AreEqual(realTimeClock, HighlightRules.ScreenTicksPerSecond);
    }
}
