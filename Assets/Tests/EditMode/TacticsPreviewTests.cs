using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Match;
using Game.Core.Placement;
using Game.Core.Tactics;
using NUnit.Framework;

// 팀 전술 미니 피치(10-29 ③d, FM26 Visualiser): 공 위치 9칸 × 공수 국면 → 11명이 서는 자리.
// PositionRules.AttackStand·DefendStand(트리와 같은 합성 함수, 그 동치는 PositionRulesTests)를 그대로 부르는지와 팀 전술 항목(전진 정도·수비 라인·폭)이 자리에 보이는지를 잠근다.
// 트리가 그 함수를 부른다는 것 자체는 값 테스트로 못 잠근다(PlayerTreeTests가 같은 값을 확인할 뿐). 러너 대시보드 전 지표 동일(10-29)이 그 증거다
public class TacticsPreviewTests
{
    private static List<PlayerStats> table;
    private static List<DefenseRole> roles;
    private static List<StageEntry> myRows;
    private static TeamTactics balanced;

    [OneTimeSetUp]
    public void Load()
    {
        table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        roles = DefenseRoleParser.Parse(File.ReadAllText("Assets/Resources/Tables/DefenseRoles.csv"));
        myRows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1 && r.Team == 0);
        balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(t => t.PresetId == "balanced");
    }

    // 새 시즌 기본 11명(4-4-2 슬롯 순서: 0 GK · 1~4 FB·CB·CB·FB · 5~8 W·CM·CM·W · 9~10 ST)
    private static IReadOnlyList<LineupSlot> Lineup()
    {
        return SeasonState.NewSeason(4, 42, myRows, table).MyLineup(table, roles);
    }

    [Test]
    public void 공_칸_9개의_중심은_서드_중앙과_좌중우_3분할_중앙이다()
    {
        // [가정] 열 = 우리 진영·중원·상대 진영 중심 x(−35·0·35), 행 = 왼쪽·중앙·오른쪽 z(−22.7·0·22.7). FM26은 칸 좌표를 공개하지 않아 필드 3등분 중심으로
        float lateral = FieldBounds.HalfWidth * 2f / 3f;
        (float x, float z) ownLeft = TacticsPreview.BallCell(column: 0, row: 0);
        Assert.AreEqual(-35f, ownLeft.x, 1e-3f, "우리 진영 서드 중심 = (−52.5 + −17.5) / 2");
        Assert.AreEqual(-lateral, ownLeft.z, 1e-3f);
        Assert.AreEqual((0f, 0f), TacticsPreview.BallCell(1, 1));
        (float x, float z) oppRight = TacticsPreview.BallCell(2, 2);
        Assert.AreEqual(35f, oppRight.x, 1e-3f);
        Assert.AreEqual(lateral, oppRight.z, 1e-3f);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => TacticsPreview.BallCell(3, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => TacticsPreview.BallCell(0, -1));
    }

    [Test]
    public void 자리는_Stand_함수를_팀_0_좌표로_그대로_부른다()
    {
        IReadOnlyList<LineupSlot> lineup = Lineup();
        (float x, float z) ball = TacticsPreview.BallCell(2, 0);
        (float x, float z)[] attack = TacticsPreview.Positions(lineup, balanced, ball.x, ball.z, defending: false);
        (float x, float z)[] defend = TacticsPreview.Positions(lineup, balanced, ball.x, ball.z, defending: true);

        Assert.AreEqual(lineup.Count, attack.Length);
        Assert.AreEqual(lineup.Count, defend.Length);
        float shift = ExpectedLineShift(lineup, balanced, ball.x);
        float cap = LineRules.StandDepthCap(LineRules.HomeDepth(ball.x, +1), LineRules.LineFloor(KeeperDepth(lineup)));
        for (int i = 0; i < lineup.Count; i++)
        {
            LineupSlot slot = lineup[i];
            Assert.AreEqual(PositionRules.AttackStand(slot.AttackX, slot.AttackZ, +1, balanced, slot.Stats, ball.x, ball.z, slot.Stats.IsKeeper), attack[i], $"공격 자리 {i}");
            Assert.AreEqual(PositionRules.DefendStand(slot.DefendX, slot.DefendZ, +1, slot.Stats, ball.z, shift, cap, slot.Stats.IsKeeper), defend[i], $"수비 자리 {i}");
        }
        Assert.Less(attack[0].x, -40f, "GK는 우리 골 앞(팀 0 = −X가 우리 골)");
    }

    [Test]
    public void 전진_정도를_올리면_공격_자리가_11명_모두_앞으로_간다()
    {
        // 지금 엔진은 GK도 전진 정도를 따른다(AttackHome이 GK를 가리지 않음). 미니 피치는 그 사실을 그대로 보인다(10-29 결정: 보이는 것 = 실제)
        IReadOnlyList<LineupSlot> lineup = Lineup();
        TeamTactics defensive = TacticsEditing.Copy(balanced);
        defensive.Mentality = 0;
        TeamTactics offensive = TacticsEditing.Copy(balanced);
        offensive.Mentality = 2;

        (float x, float z)[] defensivePositions = TacticsPreview.Positions(lineup, defensive, 0f, 0f, defending: false);
        (float x, float z)[] offensivePositions = TacticsPreview.Positions(lineup, offensive, 0f, 0f, defending: false);
        float expected = MatchTuning.MentalityOffset[2] - MatchTuning.MentalityOffset[0];
        for (int i = 1; i < lineup.Count; i++)
        {
            Assert.AreEqual(expected, offensivePositions[i].x - defensivePositions[i].x, 1e-4f, $"필드 선수 {i}: 수비적 → 공격적 = +{expected}m");
            Assert.AreEqual(defensivePositions[i].z, offensivePositions[i].z, 1e-4f, "전진 정도는 가로를 안 바꾼다");
        }
        // GK(x −48)는 수비적 −5면 골라인 여백(−52)에 걸려 4m만 물러나고, 공격적이면 +5 나온다. 미니 피치는 이 클램프까지 그대로 보인다
        Assert.AreEqual(-(FieldBounds.HalfLength - FieldBounds.EdgeMargin), defensivePositions[0].x, 1e-4f, "GK 수비적 = 골라인 여백에 클램프");
        Assert.AreEqual(lineup[0].AttackX + MatchTuning.MentalityOffset[2], offensivePositions[0].x, 1e-4f, "GK 공격적 = 배치 + 5");
    }

    [Test]
    public void 수비_라인을_올리면_필드_선수만_앞으로_가고_GK는_그대로다()
    {
        IReadOnlyList<LineupSlot> lineup = Lineup();
        TeamTactics deep = TacticsEditing.Copy(balanced);
        deep.DefenseLine = 0;
        TeamTactics high = TacticsEditing.Copy(balanced);
        high.DefenseLine = 2;

        // 공은 상대 진영 왼쪽 칸(라인 L₀ 47m, 깊게 42·높게 52로 상한 55 안): 가로 슬라이드 0.3이 들어가도 라인 높이 차이는 그대로여야 한다. 공이 중앙이면 슬라이드가 0이라 좌표 전달 오류를 못 잡는다(리뷰)
        (float x, float z) ball = TacticsPreview.BallCell(2, 0);
        (float x, float z)[] deepPositions = TacticsPreview.Positions(lineup, deep, ball.x, ball.z, defending: true);
        (float x, float z)[] highPositions = TacticsPreview.Positions(lineup, high, ball.x, ball.z, defending: true);
        float expected = MatchTuning.DefenseLineOffset[2] - MatchTuning.DefenseLineOffset[0];
        Assert.IsTrue(lineup[0].Stats.IsKeeper, "0번은 GK");
        Assert.AreEqual(deepPositions[0].x, highPositions[0].x, 1e-4f, "GK 라인은 슛 대응 기준이라 팀 수비 라인을 안 따른다");
        Assert.AreEqual(ball.z * MatchTuning.SlideLateralKeeper, highPositions[0].z, 1e-4f, "GK는 공 쪽으로 가로만 조금(SlideKeeper)");
        for (int i = 1; i < lineup.Count; i++)
        {
            Assert.AreEqual(expected, highPositions[i].x - deepPositions[i].x, 1e-4f, $"필드 선수 {i}: 깊게 → 높게 = +{expected}m");
            Assert.AreEqual(lineup[i].DefendZ + ball.z * MatchTuning.SlideLateralDefend, highPositions[i].z, 1e-4f, $"필드 선수 {i}: 가로는 공 z × 계수 평행이동");
        }
    }

    [TestCase(0, 15f)]
    [TestCase(1, 32f)]
    [TestCase(2, 47f)]
    public void 수비_때_뒷줄은_공_칸의_서드별_라인_깊이에_선다(int column, float anchor)
    {
        // 11-08 D2: 경기와 같은 LineRules 식. 편성에서 가장 깊은 필드 선수가 L(Metrica 앵커)에 서고 개인 라인 높이 다이얼(× 0.3)만큼 더 앞이다.
        // 단 공보다 골 쪽(공 깊이 − GoalSideMargin)을 넘지 않는다(11-09): 우리 진영 칸(공 깊이 17.5)에서는 이 상한이 다이얼을 자른다
        IReadOnlyList<LineupSlot> lineup = Lineup();
        (float x, float z) ball = TacticsPreview.BallCell(column, 1);
        (float x, float z)[] defend = TacticsPreview.Positions(lineup, balanced, ball.x, ball.z, defending: true);
        int deepest = DeepestFieldSlot(lineup);
        float dial = lineup[deepest].Stats.LineHeight * MatchTuning.PositionDialScale;
        float cap = LineRules.HomeDepth(ball.x, +1) - MatchTuning.GoalSideMargin;
        Assert.AreEqual(System.Math.Min(anchor + dial, cap), LineRules.HomeDepth(defend[deepest].x, +1), 1e-3f, $"열 {column}");
    }

    // 시뮬(MatchSimulation.FillLineDepth)과 같은 입력을 테스트에서 따로 센다: 필드 선수 수비 시 자리 깊이 최솟값, GK 라인 깊이, 공 깊이
    private static float ExpectedLineShift(IReadOnlyList<LineupSlot> lineup, TeamTactics tactics, float ballX)
    {
        float keeperDepth = KeeperDepth(lineup);
        float restDepth = LineRules.HomeDepth(lineup[DeepestFieldSlot(lineup)].DefendX, +1);
        return LineRules.LineShift(LineRules.LineDepth(LineRules.HomeDepth(ballX, +1), tactics.DefenseLine, keeperDepth), restDepth);
    }

    private static float KeeperDepth(IReadOnlyList<LineupSlot> lineup)
    {
        LineupSlot keeper = lineup[0];
        Assert.IsTrue(keeper.Stats.IsKeeper, "0번은 GK");
        return LineRules.HomeDepth(PositionRules.KeeperLineX(keeper.DefendX, keeper.DefendZ, +1, keeper.Stats.LineHeight), +1);
    }

    private static int DeepestFieldSlot(IReadOnlyList<LineupSlot> lineup)
    {
        int deepest = 1;
        for (int i = 2; i < lineup.Count; i++)
        {
            if (lineup[i].DefendX < lineup[deepest].DefendX) { deepest = i; }
        }
        return deepest;
    }

    [Test]
    public void 공격_때는_좌우_윙어_간격이_수축_계수만큼_줄고_수비_때는_그대로다()
    {
        // 10-25 수축 격자: 공격 가로만 (공 z − 자리 z) × 0.2 수축, 수비는 평행이동. 4-4-2 W 두 명(5번 z −24, 8번 z +24. 공격 홈은 측면 쏠림 25 × 0.3을 더해 31.5 → 터치라인 여백 31에 클램프), 공은 중앙
        IReadOnlyList<LineupSlot> lineup = Lineup();
        (float x, float z)[] attack = TacticsPreview.Positions(lineup, balanced, 0f, 0f, defending: false);
        (float x, float z)[] defend = TacticsPreview.Positions(lineup, balanced, 0f, 0f, defending: true);

        LineupSlot left = lineup[5];
        LineupSlot right = lineup[8];
        (float x, float z) leftHome = PositionRules.AttackHome(left.AttackX, left.AttackZ, +1, balanced.Mentality, left.Stats.PushUp, balanced.Width[(int)Third.Middle], left.Stats.Width);
        (float x, float z) rightHome = PositionRules.AttackHome(right.AttackX, right.AttackZ, +1, balanced.Mentality, right.Stats.PushUp, balanced.Width[(int)Third.Middle], right.Stats.Width);
        float homeSpread = rightHome.z - leftHome.z;
        Assert.AreEqual(homeSpread * (1f - MatchTuning.SlideLateralAttack), attack[8].z - attack[5].z, 1e-4f, "공격 간격은 (1 − k)배");
        Assert.AreEqual(right.DefendZ - left.DefendZ, defend[8].z - defend[5].z, 1e-4f, "수비 간격은 편성 그대로(공이 중앙이면 평행이동 0)");
    }

    [Test]
    public void 폭을_넓게_하면_측면_선수의_z가_바깥으로_가고_중앙_선수는_그대로다()
    {
        IReadOnlyList<LineupSlot> lineup = Lineup();
        TeamTactics narrow = TacticsEditing.Copy(balanced);
        TeamTactics wide = TacticsEditing.Copy(balanced);
        for (int t = 0; t < 3; t++)
        {
            narrow.Width[t] = 0;
            wide.Width[t] = 2;
        }

        (float x, float z)[] narrowPositions = TacticsPreview.Positions(lineup, narrow, 0f, 0f, defending: false);
        (float x, float z)[] widePositions = TacticsPreview.Positions(lineup, wide, 0f, 0f, defending: false);
        Assert.AreEqual(0f, lineup[0].AttackZ, "GK는 중앙(z = 0)");
        Assert.AreEqual(narrowPositions[0].z, widePositions[0].z, 1e-4f, "중앙 선수는 폭 오프셋이 없다");
        Assert.Greater(lineup[1].Stats.Width, 0f, "FB 측면 쏠림 다이얼이 0이면 이 테스트는 의미가 없다");
        Assert.Less(widePositions[1].z, narrowPositions[1].z, "왼쪽 FB(z −22)는 넓게면 더 왼쪽(더 작은 z)");
        Assert.Greater(widePositions[4].z, narrowPositions[4].z, "오른쪽 FB(z +22)는 넓게면 더 오른쪽");
    }
}
