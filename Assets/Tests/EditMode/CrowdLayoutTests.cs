using System.Collections.Generic;
using System.IO;
using Game.Core.Data;
using Game.Core.League;
using Game.Core.Placement;
using NUnit.Framework;

// 경기장 4종 4단계(11-06): 관중 배치. 채운 좌석 수·같은 시드 = 같은 좌석·1,023개 호출 분할·면별 방향. 순수 함수라 dotnet에서도 돈다
public class CrowdLayoutTests
{
    private static List<StadiumSpec> Real()
    {
        return StadiumSpecParser.Parse(File.ReadAllText("Assets/Resources/Tables/Stadiums.csv"));
    }

    private static int CountFilled(StadiumSpec spec, uint seed)
    {
        int count = 0;
        int total = StadiumLayout.Seats(spec).Count;
        for (int i = 0; i < total; i++)
        {
            if (StadiumRules.IsFilled(seed, i, spec.FillPercent)) { count++; }
        }
        return count;
    }

    [Test]
    public void 관중_수는_채움_해시를_통과한_좌석_수와_같고_부마다_비율대로_늘어난다()
    {
        uint seed = StadiumRules.FillSeed(7, 1, 3);
        int previous = -1;
        List<StadiumSpec> specs = Real();
        for (int i = specs.Count - 1; i >= 0; i--)
        {
            StadiumSpec spec = specs[i];
            int expected = CountFilled(spec, seed);
            Assert.AreEqual(expected, CrowdLayout.FilledSeats(spec, seed).Count, $"{spec.Tier}부 관중 수");
            Assert.Greater(expected, previous, $"{spec.Tier}부가 아래 부보다 많다");
            previous = expected;
        }
    }

    [Test]
    public void 채운_좌석은_모두_StadiumLayout_좌석_중_하나이고_면별_합이_전체다()
    {
        StadiumSpec t1 = Real()[0];
        uint seed = StadiumRules.FillSeed(7, 1, 3);
        List<SeatSlot> filled = CrowdLayout.FilledSeats(t1, seed);
        var perSide = new Dictionary<StandSide, int>();
        var all = new HashSet<(float, float, float)>();
        foreach (SeatSlot seat in StadiumLayout.Seats(t1)) { all.Add((seat.X, seat.Y, seat.Z)); }
        foreach (SeatSlot seat in filled)
        {
            Assert.IsTrue(all.Contains((seat.X, seat.Y, seat.Z)), "좌석 슬롯에 없는 자리에 관중이 섰다");
            perSide[seat.Side] = perSide.TryGetValue(seat.Side, out int n) ? n + 1 : 1;
        }
        int sum = 0;
        foreach (int n in perSide.Values) { sum += n; }
        Assert.AreEqual(filled.Count, sum);
        Assert.AreEqual(t1.Stands.Count, perSide.Count, "스펙의 면마다 관중이 있다");
    }

    [Test]
    public void 같은_시드는_같은_좌석이고_홈_팀이_다르면_빈자리가_다르다()
    {
        StadiumSpec t3 = Real()[2];
        uint seed = StadiumRules.FillSeed(7, 3, 3);
        List<SeatSlot> a = CrowdLayout.FilledSeats(t3, seed);
        List<SeatSlot> b = CrowdLayout.FilledSeats(t3, seed);
        Assert.AreEqual(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].X, b[i].X);
            Assert.AreEqual(a[i].Z, b[i].Z);
        }
        List<SeatSlot> other = CrowdLayout.FilledSeats(t3, StadiumRules.FillSeed(7, 3, 4));
        bool same = other.Count == a.Count;
        for (int i = 0; same && i < a.Count; i++)
        {
            same = a[i].X == other[i].X && a[i].Z == other[i].Z && a[i].Y == other[i].Y;
        }
        Assert.IsFalse(same, "홈 팀이 다르면 빈자리도 달라야 한다");
    }

    [Test]
    public void 호출은_1023개씩_나뉘고_마지막만_나머지다()
    {
        Assert.AreEqual(1023, CrowdLayout.MaxInstancesPerCall);
        Assert.AreEqual(0, CrowdLayout.CallCount(0));
        Assert.AreEqual(1, CrowdLayout.CallCount(1));
        Assert.AreEqual(1, CrowdLayout.CallCount(1023));
        Assert.AreEqual(2, CrowdLayout.CallCount(1024));
        Assert.AreEqual(3, CrowdLayout.CallCount(2047));
        Assert.AreEqual(1023, CrowdLayout.CallSize(2047, 0));
        Assert.AreEqual(1023, CrowdLayout.CallSize(2047, 1));
        Assert.AreEqual(1, CrowdLayout.CallSize(2047, 2));
        Assert.AreEqual(1023, CrowdLayout.CallSize(1023, 0));
    }

    [Test]
    public void 호출별_크기를_더하면_인스턴스_수이고_1부_전체가_30호출_안이다()
    {
        StadiumSpec t1 = Real()[0];
        List<SeatSlot> filled = CrowdLayout.FilledSeats(t1, StadiumRules.FillSeed(7, 1, 3));
        var perSide = new Dictionary<StandSide, int>();
        foreach (SeatSlot seat in filled) { perSide[seat.Side] = perSide.TryGetValue(seat.Side, out int n) ? n + 1 : 1; }
        int calls = 0;
        foreach (int count in perSide.Values)
        {
            int sum = 0;
            for (int c = 0; c < CrowdLayout.CallCount(count); c++) { sum += CrowdLayout.CallSize(count, c); }
            Assert.AreEqual(count, sum);
            calls += CrowdLayout.CallCount(count);
        }
        Assert.LessOrEqual(calls, 30, "1부 관중 드로우 30 이하(설계 §7 S4)");
    }

    [Test]
    public void 면마다_쿼드가_경기장_가운데를_본다()
    {
        // 쿼드의 보이는 면은 로컬 -Z. Y축 yaw(도) 회전을 적용한 앞 방향 = (-sin, -cos). 좌석에서 경기장 중심(0, 0)으로 가는 방향과 맞아야 한다
        StadiumSpec t1 = Real()[0];
        List<SeatSlot> filled = CrowdLayout.FilledSeats(t1, StadiumRules.FillSeed(7, 1, 3));
        var checkedSides = new HashSet<StandSide>();
        foreach (SeatSlot seat in filled)
        {
            float yaw = CrowdLayout.FacingYaw(seat.Side) * (float)System.Math.PI / 180f;
            float frontX = -(float)System.Math.Sin(yaw);
            float frontZ = -(float)System.Math.Cos(yaw);
            float toX = -seat.X;
            float toZ = -seat.Z;
            float length = (float)System.Math.Sqrt(toX * toX + toZ * toZ);
            float dot = (frontX * toX + frontZ * toZ) / length;
            checkedSides.Add(seat.Side);
            Assert.Greater(dot, 0f, $"{seat.Side} 좌석 ({seat.X}, {seat.Z})의 쿼드가 가운데 쪽을 본다");
        }
        Assert.AreEqual(t1.Stands.Count, checkedSides.Count);
    }

    [Test]
    public void 면별_상자는_채운_좌석의_바닥점과_머리_높이를_전부_덮는다()
    {
        const float quadHeight = 1.1f;
        const float padding = 1f;
        foreach (StadiumSpec spec in Real())
        {
            List<SeatSlot> filled = CrowdLayout.FilledSeats(spec, StadiumRules.FillSeed(7, spec.Tier, 3));
            foreach (StandSide side in spec.Stands)
            {
                CrowdLayout.SeatBox box = CrowdLayout.SideBox(filled, side, quadHeight, padding);
                int count = 0;
                foreach (SeatSlot seat in filled)
                {
                    if (seat.Side != side) { continue; }

                    count++;
                    Assert.IsTrue(box.Contains(seat.X, seat.Y, seat.Z), $"{spec.Tier}부 {side} 바닥점");
                    Assert.IsTrue(box.Contains(seat.X, seat.Y + quadHeight, seat.Z), $"{spec.Tier}부 {side} 머리 높이");
                }
                Assert.Greater(count, 0, $"{spec.Tier}부 {side} 좌석");
            }
        }
    }
}
