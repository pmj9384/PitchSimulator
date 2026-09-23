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
        public float BallVelX { get; set; }
        public float BallVelZ { get; set; }
        public int BallOwnerTeam { get; set; } = -1;
        public TeamTactics Tactics { get; set; } = new TeamTactics { PassRisk = new[] { 1, 1, 1 }, PressStart = new[] { 1, 1, 1 }, Width = new[] { 1, 1, 1 }, Tempo = new[] { 1, 1, 1 }, Mentality = 1 };
        public Third BallThird { get; set; } = Third.Middle;
        public bool IsCountering { get; set; }
        public bool IsCounterPressing { get; set; }
        public int PressRank { get; set; }   // 기본 0 = 첫 압박자
        public IReadOnlyList<TargetInfo> Teammates { get; set; } = new List<TargetInfo>();
        public IReadOnlyList<TargetInfo> Opponents { get; set; } = new List<TargetInfo>();
        public int OpponentKeeperId { get; set; } = -1;
        public int TeamKeeperId { get; set; } = -1;
        public bool KeeperAlternate { get; set; }
        public int LastPasserId { get; set; } = -1;
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
    public void GK_배급은_발치에_상대가_붙어_있으면_짧게_대신_길게_찬다()
    {
        // 09-23 탐지 E: 짧은 배급이 릴리스 지점에서 붙은 ST에게 끊겨 GK-ST 핑퐁. 상대가 1.2m 안이면 가장 앞선 아군에게
        var gk = new Fake { PlayerId = 0, IsGoalkeeper = true, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = -48f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, -36f, 7f), new TargetInfo(2, -8f, 0f) },
            Opponents = new List<TargetInfo> { new TargetInfo(20, -47f, 0.5f) } };
        Tree.Tick(gk);
        Assert.AreEqual("pass", gk.Did);
        Assert.AreEqual(2, gk.PassedTo, "붙어 있으면 길게(가장 앞선 아군)");
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
            Teammates = new List<TargetInfo> { new TargetInfo(1, -30f, 10f) },  // 30m 뒤라 감점으로 패스 후보 아님(09-21 C)
            Opponents = new List<TargetInfo>() };
        mid.Stats.ShotBias = 1f; mid.Stats.Dribble = 0.1f;
        Tree.Tick(mid);
        Assert.AreEqual("move", mid.Did);
        Assert.AreEqual(-30f, mid.MoveX, 1e-4f, "가까운 아군 쪽");
    }

    [Test]
    public void 리시버_앞_착지점까지_수비수가_있으면_안전한_패스가_아니다()
    {
        // 09-21: 리시버(1)는 (15,0), 리드 목표는 그보다 앞. 상대는 리시버 너머 (19, 0.3)에 서 있다.
        // 리시버 위치까지만 보면 "리시버보다 멀어서 무관"이라 안전이지만, 공은 착지점까지 날아가 그 상대가 먹는다
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 15f, 0f) },
            Opponents = new List<TargetInfo> { new TargetInfo(11, 19f, 0.3f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("move", mid.Did, "패스 대신 드리블");
    }

    [Test]
    public void 방금_나에게_준_선수에게_곧바로_뒤로_되돌리지_않는다_다른_후보가_없을_때만()
    {
        // 압박(2m 안 상대) 중, 앞은 막힘. 뒤 후보 A(직전 패서)와 B. A는 되돌림이라 B로. B가 없으면 A로
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 20f, BallX = 20f, LastPasserId = 1,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 12f, 8f), new TargetInfo(2, 12f, -8f) },
            Opponents = new List<TargetInfo> { new TargetInfo(11, 21.5f, 0.3f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(2, mid.PassedTo, "직전 패서(1)가 아닌 B(2)");

        mid.Teammates = new List<TargetInfo> { new TargetInfo(1, 12f, 8f) };
        mid.Did = "";
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(1, mid.PassedTo, "다른 후보가 없으면 되돌림 허용");
    }

    [Test]
    public void 오프사이드_위치_아군에겐_안_주고_공격_자리는_온사이드_선_뒤로_잡는다()
    {
        // 팀0 패서 (10,0). 상대 GK 48, CB 31, CB 30 → 온사이드 선 31. 아군 A (35,0)은 오프사이드 위치, B (25,0)은 온사이드
        var opp = new List<TargetInfo> { new TargetInfo(20, 48f, 0f), new TargetInfo(21, 31f, 8f), new TargetInfo(22, 30f, -8f) };
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 10f, BallX = 10f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 35f, 0f), new TargetInfo(2, 25f, 0f) }, Opponents = opp };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(2, mid.PassedTo, "온사이드인 B에게. 더 앞선 A는 오프사이드 위치");

        // 아군 소유 중 ST의 공격 자리가 37이어도 선(31) − 0.5 = 30.5까지만
        var st = new Fake { PlayerId = 9, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 30f, BallX = 10f, AttackHomeX = 37f, AttackHomeZ = 0f, Opponents = opp };
        st.Stats.PushUp = 0f;
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did);
        Assert.LessOrEqual(st.MoveX, 30.5f + 1e-4f, "온사이드 선 뒤");
    }

    [Test]
    public void 앞_아군이_막히면_압박받을_때만_뒤_아군에게_돌리고_아니면_드리블한다()
    {
        // 09-18 Play 진단 C: 앞 후보가 전부 불안전하면 옆·뒤 후보로 돌린다. 09-21: 단 압박(상대 3m 안)받을 때만.
        // 앞 경로 위 상대(7, 0.2)는 앞 후보를 막지만 7m 떨어져 압박은 아니다 → 드리블
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 15f, 0f), new TargetInfo(2, -6f, 8f) },
            Opponents = new List<TargetInfo> { new TargetInfo(11, 7f, 0.2f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("move", mid.Did, "압박이 없으면 뒤로 안 돌리고 몬다");
        Assert.Greater(mid.MoveX, 0f, "골 쪽으로");

        // 상대가 1.5m 뒤에 붙으면(압박 = 태클 사거리 안) 뒤 아군에게 돌린다. 그 상대는 뒤 패스 경로 밖(z 반대, 발 뻗는 1.2m 밖)
        mid.Opponents = new List<TargetInfo> { new TargetInfo(11, 7f, 0.2f), new TargetInfo(12, -1.4f, -0.8f) };
        mid.Did = "";
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did, "압박받으면 돌린다");
        Assert.AreEqual(2, mid.PassedTo, "뒤 아군에게");
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
    public void 아군_패스_비행_중_리시버가_아니면_공을_안_쫓고_공격_시_자리로_간다()
    {
        // 09-23: 비행 중 소유 팀이 -1이던 때는 최근접 아군이 ⑩으로 날아가는 공을 쫓아 "아군끼리 가로채기"가 절반이었다
        var cm = new Fake { PlayerId = 4, BallPhase = BallPhase.Flight, BallOwnerTeam = 0, X = 10f, BallX = 11f, BallZ = 0f, AttackHomeX = -20f, AttackHomeZ = 8f };
        Tree.Tick(cm);
        Assert.AreEqual("move", cm.Did);
        Assert.AreEqual(-20f + 11f * MatchTuning.SlideVerticalAttack, cm.MoveX, 1e-4f, "공격 자리 + 슬라이드. 공(11)으로 가지 않는다");
    }

    [Test]
    public void 상대_패스_비행_중_압박_거리_안이면_공을_쫓고_아니면_수비_자리로_간다()
    {
        var st = new Fake { PlayerId = 9, BallPhase = BallPhase.Flight, BallOwnerTeam = 1, X = 0f, BallX = 10f, BallZ = 0f, DefendHomeX = -10f };
        st.Stats.PressRange = 12f;
        Tree.Tick(st);
        Assert.AreEqual(10f, st.MoveX, "압박: 공으로");

        var cb = new Fake { PlayerId = 2, BallPhase = BallPhase.Flight, BallOwnerTeam = 1, X = -36f, BallX = 10f, BallZ = 0f, DefendHomeX = -36f };
        cb.Stats.PressRange = 3f;
        Tree.Tick(cb);
        Assert.AreEqual(-36f + 10f * MatchTuning.SlideVerticalDefend, cb.MoveX, 1e-4f, "수비 자리 + 슬라이드. 비행 중이라고 공격 자리(⑪)로 안 간다");
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
        Assert.AreEqual(-27f, cb.MoveX, 1e-4f, "수비 자리 + 라인 높이 + 공 지향 슬라이드(공 X 10 × 0.3)");
    }

    [Test]
    public void 압박_거리_안이어도_팀_안_순위가_상한_밖이면_수비_자리로_간다()
    {
        // 09-23 뭉침: 인원 제한이 없으면 압박 거리 안 4~6명이 동시에 공으로 갔다. 순위 1(두 번째)은 상한 1 밖이라 자리로
        var cm = new Fake { PlayerId = 6, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = 0f, BallX = 10f, BallZ = 0f, DefendHomeX = -20f, PressRank = 1 };
        cm.Stats.PressRange = 12f;
        Tree.Tick(cm);
        Assert.AreEqual(-20f + 10f * MatchTuning.SlideVerticalDefend, cm.MoveX, 1e-4f, "수비 자리 + 슬라이드. 공(10)으로 안 간다");
    }

    [Test]
    public void 팀_압박_시작이_안_감이면_아무도_안_간다()
    {
        var st = new Fake { PlayerId = 9, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = 9f, BallX = 10f, DefendHomeX = -10f };
        st.Tactics.PressStart = new[] { 0, 0, 0 };
        Tree.Tick(st);
        Assert.AreEqual(-7f, st.MoveX, 1e-4f, "1m 앞인데도 자리로(+ 슬라이드 공 X 10 × 0.3)");
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

        // 09-23 R3: 최근접이 출격 못 하는 GK(공 -33, GK -48 → 15m > 출격 12m)여도 GK를 뺀 최근접 필드 플레이어(-20)가 쫓는다
        var field = new Fake { PlayerId = 2, BallPhase = BallPhase.Free, X = -20f, BallX = -33f, AttackHomeX = -20f, TeamKeeperId = 0,
            Teammates = new List<TargetInfo> { new TargetInfo(0, -48f, 0f) } };
        Tree.Tick(field);
        Assert.AreEqual(-33f, field.MoveX, 1e-4f, "GK는 최근접 경쟁에서 빠진다");
    }
}
