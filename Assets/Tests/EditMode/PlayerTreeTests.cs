using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;
using NUnit.Framework;

// 4국면 트리 진입 조건(09-18). 가짜 컨텍스트로 "어느 분기가 어떤 의도를 남기나"만 본다. 판정 수치는 각 Rules 테스트가 잠근다
public class PlayerTreeTests
{
    private sealed class Fake : IPlayerContext
    {
        public int PlayerId { get; set; }
        public int Team { get; set; }
        public int AttackSign => Team == 0 ? +1 : -1;
        public float X { get; set; }
        public float Z { get; set; }
        public PlayerStats Stats { get; set; } = new PlayerStats { Speed = 50, Shot = 50, ShotBias = 0.3f, PassLength = 15f, PressRange = 8f, Dribble = 0.5f, GkRushRadius = 12f };
        public bool IsGoalkeeper { get; set; }
        public BallPhase BallPhase { get; set; }
        public bool OwnsBall { get; set; }
        public float BallX { get; set; }
        public float BallZ { get; set; }
        public int BallOwnerTeam { get; set; } = -1;
        public TeamTactics Tactics { get; set; } = new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 1, 1, 1 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, Mentality = 1 };
        public Third BallThird { get; set; } = Third.Middle;
        public bool IsCountering { get; set; }
        public bool IsCounterPressing { get; set; }
        public IReadOnlyList<TargetInfo> Teammates { get; set; } = new List<TargetInfo>();
        public IReadOnlyList<TargetInfo> Opponents { get; set; } = new List<TargetInfo>();
        public int OpponentKeeperId { get; set; } = -1;
        public PlayerStats OpponentKeeper { get; set; } = null;
        public float AttackHomeX { get; set; }
        public float AttackHomeZ { get; set; }
        public float DefendHomeX { get; set; }
        public float DefendHomeZ { get; set; }
        public bool IsPassTarget { get; set; }
        public float PassTargetX { get; set; }
        public float PassTargetZ { get; set; }

        public string Did = "";
        public float MoveX, MoveZ; public int PassedTo = -1;
        public void MoveToward(float x, float z) { Did = "move"; MoveX = x; MoveZ = z; }
        public void Shoot() { Did = "shoot"; }
        public void Pass(int receiverId) { Did = "pass"; PassedTo = receiverId; }
    }

    private static readonly BehaviorNode Tree = PlayerTreeBuilder.Build();

    [Test]
    public void GK가_잡으면_배급_패스다()
    {
        var gk = new Fake { PlayerId = 0, IsGoalkeeper = true, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = -48f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, -36f, 7f), new TargetInfo(2, -8f, 0f) } };
        Tree.Tick(gk);
        Assert.AreEqual("pass", gk.Did);
        Assert.AreEqual(1, gk.PassedTo, "짧게(기본 0) = 가장 가까운 아군");
    }

    [Test]
    public void 슛_확률이_성향_이상이면_쏜다()
    {
        var st = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 41.5f, Z = 0f };
        st.Stats.Shot = 90; st.Stats.ShotBias = 0.1f;
        Tree.Tick(st);
        Assert.AreEqual("shoot", st.Did);
    }

    [Test]
    public void 역습_중이면_안전_검사_없이_최전방_아군에게()
    {
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, IsCountering = true, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 10f, 0f), new TargetInfo(2, 30f, 5f) },
            Opponents = new List<TargetInfo> { new TargetInfo(11, 15f, 0.2f) } };   // 경로 위 상대가 있어도
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(2, mid.PassedTo);
    }

    [Test]
    public void 안전한_앞선_아군이_있으면_패스_없으면_드리블()
    {
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 15f, 0f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(1, mid.PassedTo);

        mid.Opponents = new List<TargetInfo> { new TargetInfo(11, 7f, 0.2f) };   // 경로 위 상대 → 위험도 1
        mid.Did = "";
        Tree.Tick(mid);
        Assert.AreEqual("move", mid.Did, "드리블");
        Assert.Greater(mid.MoveX, 0f, "골 쪽으로");
    }

    [Test]
    public void 드리블_성향이_낮으면_가까운_아군_쪽으로_몬다()
    {
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, -5f, 10f) },   // 뒤에 있어 패스 후보 아님
            Opponents = new List<TargetInfo>() };
        mid.Stats.ShotBias = 1f; mid.Stats.Dribble = 0.1f;
        Tree.Tick(mid);
        Assert.AreEqual("move", mid.Did);
        Assert.AreEqual(-5f, mid.MoveX, 1e-4f, "가까운 아군 쪽");
    }

    [Test]
    public void 나한테_오는_패스면_도착점으로_마중_나간다()
    {
        var w = new Fake { PlayerId = 3, BallPhase = BallPhase.Flight, BallOwnerTeam = -1, IsPassTarget = true, PassTargetX = 20f, PassTargetZ = -10f };
        Tree.Tick(w);
        Assert.AreEqual("move", w.Did);
        Assert.AreEqual(20f, w.MoveX);
        Assert.AreEqual(-10f, w.MoveZ);
    }

    [Test]
    public void 아군_소유면_공격_시_자리로_전진_오프셋을_더해_간다()
    {
        var cm = new Fake { PlayerId = 4, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, AttackHomeX = -20f, AttackHomeZ = 8f };
        cm.Stats.PushUp = 15f; cm.Stats.RoamRadius = 100f;
        Tree.Tick(cm);
        Assert.AreEqual("move", cm.Did);
        Assert.AreEqual(-5f, cm.MoveX, 1e-4f, "-20 + 0(균형) + 15");
    }

    [Test]
    public void 상대_소유면_압박_거리_안이면_공으로_아니면_수비_자리로()
    {
        var st = new Fake { PlayerId = 9, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = 0f, BallX = 10f, BallZ = 0f, DefendHomeX = -10f, DefendHomeZ = 0f };
        st.Stats.PressRange = 12f;
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did);
        Assert.AreEqual(10f, st.MoveX, "압박: 공으로");

        var cb = new Fake { PlayerId = 2, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -36f, BallX = 10f, BallZ = 0f, DefendHomeX = -36f, DefendHomeZ = 7f };
        cb.Stats.PressRange = 3f; cb.Stats.LineHeight = 6f;
        Tree.Tick(cb);
        Assert.AreEqual(-30f, cb.MoveX, 1e-4f, "수비 자리 + 라인 높이");
    }

    [Test]
    public void 팀_압박_시작이_안_감이면_아무도_안_간다()
    {
        var st = new Fake { PlayerId = 9, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = 9f, BallX = 10f, DefendHomeX = -10f };
        st.Tactics.PressStart = new[] { 0, 0, 0 };
        Tree.Tick(st);
        Assert.AreEqual(-10f, st.MoveX, "1m 앞인데도 자리로");
    }

    [Test]
    public void 자유_공은_가장_가까운_선수만_쫓고_GK는_출격_반경_안만()
    {
        var near = new Fake { PlayerId = 9, BallPhase = BallPhase.Free, X = 2f, BallX = 0f, Opponents = new List<TargetInfo> { new TargetInfo(20, 8f, 0f) } };
        Tree.Tick(near);
        Assert.AreEqual(0f, near.MoveX, "공으로");

        var far = new Fake { PlayerId = 20, Team = 1, BallPhase = BallPhase.Free, X = 8f, BallX = 0f, AttackHomeX = 8f, Opponents = new List<TargetInfo> { new TargetInfo(9, 2f, 0f) } };
        Tree.Tick(far);
        Assert.AreEqual(8f, far.MoveX, 1e-4f, "가장 가깝지 않으면 자리 유지");

        var gk = new Fake { PlayerId = 0, IsGoalkeeper = true, BallPhase = BallPhase.Free, X = -48f, BallX = 0f, AttackHomeX = -48f };
        Tree.Tick(gk);
        Assert.AreEqual(-48f, gk.MoveX, 1e-4f, "GK는 반경 밖 공은 안 쫓음");
    }
}
