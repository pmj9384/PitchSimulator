using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core.AI;
using Game.Core.AutoMatch;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 22명 3분 경기를 엔진 없이 끝까지 돌린다(09-21 태스크 ③). 조립은 MatchAssembler(러너·매니저와 같은 재료: 실제 CSV 3개 + Build() 트리 + 시드).
// 여기 Summary는 규칙을 고칠 때 보는 진단 지표(접촉·최소 거리·|z| 등)다. 밸런스용 결과 한 줄은 MatchProbe/MatchSummary가 따로 낸다.
// ①같은 시드 = 같은 경기(CLAUDE.md "기기가 달라도 결과가 같아야 한다"의 첫 실증, 09-23 자동 대전 러너의 전제)
// ②9,000틱(3분 ÷ 0.02)이 예외 없이 돈다 ③결과 요약을 출력해 Play 없이 기준선 로그를 본다
public class FullMatchTests
{
    private const float Dt = 0.02f;
    private const int ThreeMinutesTicks = MatchTuning.MatchTicks;

    private sealed class Summary
    {
        public int Ticks;
        public int Shots;
        public int Goals;
        public int ShotsClose;   // xG ≥ 0.3
        public int ShotsMid;     // 0.1 ≤ xG < 0.3
        public int ShotsLong;    // xG < 0.1
        public int ReboundShots; // 파링 뒤 100틱(2초) 안의 슛
        public int Parries;
        public int LastParryTick = -1000;
        public int TickNow;
        public int GoalsFromRebound;
        public readonly Dictionary<PossessionChange, int> Possession = new Dictionary<PossessionChange, int>();
        public int Team1Possessions;    // 팀1이 공을 가진 횟수(0이면 팀0이 독점)
        public int InterceptedBySameTeam;   // 가로채기 중 같은 팀(리시버 아닌 아군)이 주운 수
        public int InterceptedByOpponent;
        public int FlightTicksSum;          // 패스 킥부터 잡힐 때까지 틱 합(평균 = ÷ 소유 변경 수)
        public int FlightSamples;
        public int TicksBallOppThird;       // 공이 소유 팀 기준 상대 서드에 있던 틱(팀0: x ≥ 17.5, 팀1: x ≤ −17.5)
        public float MaxBallXTeam0 = -60f;  // 팀0 소유 중 공 X 최대(공격 방향 +X)
        public float MinBallXTeam1 = 60f;   // 팀1 소유 중 공 X 최소
        public int ForwardPasses;           // 킥 원점 → 다음 소유 지점이 공격 방향이면 앞
        public int BackPasses;
        public float MaxShotChance;         // 소유자 위치에서의 xG 최대(ST shotBias 0.2 이상이면 슛이 나야 함)
        public int TicksOwnerNearGoal;      // 소유자가 상대 골라인 20m 안에 있던 틱
        public int DribbleTicks;            // 소유 중 이동한 틱(패스 대기 아님)
        public int ContactTicks;            // 상대가 소유자 태클 사거리 안에 있던 틱
        public int ContactTicksNotImmune;   // 그중 면역이 아닌 틱(= 태클 시도가 가능했던 틱)
        public int OwnedTicks;
        public float MinOppDist = 999f;     // 소유 중 상대와의 최소 거리
        public int Within3mTicks;           // 상대가 3m 안에 있던 소유 틱
        public float NearGoalAbsZSum;       // 골라인 20m 안 소유 틱의 |z| 합(평균 = ÷ TicksOwnerNearGoal). 측면에 머무는지
    }

    private static MatchSimulation FullMatch(int seed, Summary summary)
    {
        List<PlayerStats> table = PlayerTableParser.Parse(File.ReadAllText("Assets/Resources/Tables/PlayerTable.csv"));
        List<StageEntry> rows = StageCompositionParser.Parse(File.ReadAllText("Assets/Resources/Tables/StageComposition.csv")).FindAll(r => r.Stage == 1);
        TeamTactics balanced = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv")).Find(t => t.PresetId == "balanced");
        Assert.IsNotNull(balanced, "TacticPresets에 balanced");

        MatchSimulation sim = MatchAssembler.Create(table, rows, balanced, balanced, seed);   // 러너·매니저와 같은 조립(09-23)

        sim.ShotResolved += r =>
        {
            summary.Shots++;
            bool rebound = summary.TickNow - summary.LastParryTick <= 100;
            if (rebound) { summary.ReboundShots++; }
            if (r.Outcome == ShotOutcome.Goal) { summary.Goals++; if (rebound) { summary.GoalsFromRebound++; } }
            if (r.Outcome == ShotOutcome.Parried) { summary.Parries++; summary.LastParryTick = summary.TickNow; }
            if (r.Probability >= 0.3f) { summary.ShotsClose++; } else if (r.Probability >= 0.1f) { summary.ShotsMid++; } else { summary.ShotsLong++; }
        };
        sim.PossessionChanged += r =>
        {
            summary.Possession.TryGetValue(r.Kind, out int n);
            summary.Possession[r.Kind] = n + 1;
            if (r.NewOwnerTeam == 1) { summary.Team1Possessions++; }
            if (r.Kind == PossessionChange.Intercepted && r.PreviousOwnerId >= 0)
            {
                int prevTeam = sim.Players[r.PreviousOwnerId].Team;
                if (prevTeam == r.NewOwnerTeam) { summary.InterceptedBySameTeam++; } else { summary.InterceptedByOpponent++; }
            }
        };
        return sim;
    }

    private static void RunThreeMinutes(MatchSimulation sim, Summary summary)
    {
        int flightTicks = 0;
        int kickTeam = -1; float kickX = 0f; bool prevOwned = false; float prevBallX = 0f; int prevOwnerId = -1; float prevOwnerX = 0f;
        for (int i = 0; i < ThreeMinutesTicks; i++)
        {
            summary.TickNow = i;
            sim.Tick(Dt);
            summary.Ticks++;
            bool owned = sim.Ball.Phase == BallPhase.Owned;
            if (prevOwned && sim.Ball.Phase == BallPhase.Flight) { kickTeam = sim.Players[prevOwnerId].Team; kickX = prevBallX; }
            if (owned && kickTeam != -1) { int sign = kickTeam == 0 ? 1 : -1; if ((sim.Ball.X - kickX) * sign > 0f) { summary.ForwardPasses++; } else { summary.BackPasses++; } kickTeam = -1; }
            if (owned)
            {
                PlayerState o = sim.Players[sim.Ball.OwnerId];
                int sign = o.Team == 0 ? 1 : -1;
                PlayerState keeper = sim.Players.First(p => p.Team != o.Team && p.IsGoalkeeper);
                float chance = MatchRules.ShotProbability(o.X, o.Z, sign, o.Stats.Shot, keeper.Stats.Reflexes, keeper.Stats.Diving);
                if (chance > summary.MaxShotChance) { summary.MaxShotChance = chance; }
                if (52.5f - o.X * sign <= 20f) { summary.TicksOwnerNearGoal++; summary.NearGoalAbsZSum += Math.Abs(o.Z); }
                if (prevOwned && prevOwnerId == o.PlayerId && Math.Abs(o.X - prevOwnerX) > 0.01f) { summary.DribbleTicks++; }
                summary.OwnedTicks++;
                float r2 = MatchTuning.TackleRange * MatchTuning.TackleRange;
                float minD2 = sim.Players.Where(q => q.Team != o.Team).Min(q => (q.X - o.X) * (q.X - o.X) + (q.Z - o.Z) * (q.Z - o.Z));
                if (minD2 < summary.MinOppDist * summary.MinOppDist) { summary.MinOppDist = (float)Math.Sqrt(minD2); }
                if (minD2 <= 9f) { summary.Within3mTicks++; }
                bool contact = minD2 <= r2;
                if (contact) { summary.ContactTicks++; if (!sim.OwnerImmune) { summary.ContactTicksNotImmune++; } }
                prevOwnerId = o.PlayerId; prevOwnerX = o.X;
            }
            prevOwned = owned; prevBallX = sim.Ball.X;
            if (sim.Ball.Phase == BallPhase.Flight) { flightTicks++; }
            else if (flightTicks > 0) { summary.FlightTicksSum += flightTicks; summary.FlightSamples++; flightTicks = 0; }
            int ownerTeam = sim.OwnerTeam();
            if (ownerTeam == 0) { if (sim.Ball.X > summary.MaxBallXTeam0) { summary.MaxBallXTeam0 = sim.Ball.X; } if (sim.Ball.X >= 17.5f) { summary.TicksBallOppThird++; } }
            if (ownerTeam == 1) { if (sim.Ball.X < summary.MinBallXTeam1) { summary.MinBallXTeam1 = sim.Ball.X; } if (sim.Ball.X <= -17.5f) { summary.TicksBallOppThird++; } }
        }
    }

    private static string Describe(MatchSimulation sim, Summary s)
    {
        var parts = new List<string>();
        foreach (KeyValuePair<PossessionChange, int> kv in s.Possession) { parts.Add($"{kv.Key} {kv.Value}"); }
        float avgFlight = s.FlightSamples == 0 ? 0f : (float)s.FlightTicksSum / s.FlightSamples;
        return $"스코어 {sim.HomeGoals}:{sim.AwayGoals}, 슛 {s.Shots}(골 {s.Goals}; xG≥0.3 {s.ShotsClose}·0.1~0.3 {s.ShotsMid}·<0.1 {s.ShotsLong}; 파링 {s.Parries}·리바운드 슛 {s.ReboundShots}·리바운드 골 {s.GoalsFromRebound}), 패스 {sim.PassCount}(앞 {s.ForwardPasses}·뒤 {s.BackPasses}), 가로채기 {sim.InterceptCount}, 태클 {sim.TackleAttemptCount}회(성공 {sim.TackleSuccessCount}), 턴오버 {sim.TurnoverCount}, 팀1 소유 {s.Team1Possessions}회, 소유 변경 [{string.Join(", ", parts)}], 비행 평균 {avgFlight:0.0}틱, 상대 서드 {s.TicksBallOppThird}틱, 골라인 20m 안 소유 {s.TicksOwnerNearGoal}틱(평균 |z| {(s.TicksOwnerNearGoal == 0 ? 0f : s.NearGoalAbsZSum / s.TicksOwnerNearGoal):0.0}), 드리블 {s.DribbleTicks}틱, 소유 {s.OwnedTicks}틱(3m 안 {s.Within3mTicks}·접촉 {s.ContactTicks}·면역 밖 {s.ContactTicksNotImmune}·최소 거리 {s.MinOppDist:0.0}), 최대 슛 확률 {s.MaxShotChance:0.000}, 팀0 최대 X {s.MaxBallXTeam0:0.0}, 팀1 최소 X {s.MinBallXTeam1:0.0}, 공 ({sim.Ball.X:0.0},{sim.Ball.Z:0.0}) {sim.Ball.Phase}";
    }

    [Test]
    public void 같은_시드면_3분_경기_결과가_같다()
    {
        var sa = new Summary();
        var sb = new Summary();
        MatchSimulation a = FullMatch(seed: 1, sa);
        MatchSimulation b = FullMatch(seed: 1, sb);
        RunThreeMinutes(a, sa);
        RunThreeMinutes(b, sb);

        Assert.AreEqual(a.HomeGoals, b.HomeGoals, "홈 골");
        Assert.AreEqual(a.AwayGoals, b.AwayGoals, "원정 골");
        Assert.AreEqual(a.PassCount, b.PassCount, "패스 수");
        Assert.AreEqual(a.InterceptCount, b.InterceptCount, "가로채기 수");
        Assert.AreEqual(sa.Shots, sb.Shots, "슛 수");
        Assert.AreEqual(a.Ball.X, b.Ball.X, "공 X");
        Assert.AreEqual(a.Ball.Z, b.Ball.Z, "공 Z");
        for (int i = 0; i < a.Players.Count; i++)
        {
            Assert.AreEqual(a.Players[i].X, b.Players[i].X, $"#{i} X");
            Assert.AreEqual(a.Players[i].Z, b.Players[i].Z, $"#{i} Z");
        }
    }

    [Test]
    public void 다른_시드면_주사위가_다르므로_결과가_달라질_수_있다_그리고_3분이_예외_없이_돈다()
    {
        // 결과가 반드시 달라야 한다고는 못 한다(슛이 0이면 주사위를 안 굴려 같은 궤도). 예외 0과 9,000틱 완주만 확정 기준
        for (int seed = 1; seed <= 3; seed++)
        {
            var s = new Summary();
            MatchSimulation sim = FullMatch(seed, s);
            RunThreeMinutes(sim, s);
            Assert.AreEqual(ThreeMinutesTicks, s.Ticks);
            UnityEngine.Debug.Log($"[FullMatch] seed {seed}: {Describe(sim, s)}");   // 파이프라인 결과 JSON엔 테스트 출력이 안 실려 콘솔로 남긴다
        }
    }
}
