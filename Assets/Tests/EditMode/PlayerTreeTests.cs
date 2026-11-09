using System.Collections.Generic;
using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Placement;
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
        public bool IsLooseBallChaser { get; set; }
        public bool IsMarker { get; set; }
        public float MarkX { get; set; }
        public float MarkZ { get; set; }
        public bool IsKickoffTaker { get; set; }
        public bool WantsTakeOn { get; set; }
        public bool ShotReady { get; set; } = true;
        public bool IsKeeperAlternate { get; set; }
        public int LastPasserId { get; set; } = -1;
        public PlayerStats OpponentKeeper { get; set; } = null;
        public int OpponentKeeperId { get; set; } = -1;
        public float AttackHomeX { get; set; }
        public float AttackHomeZ { get; set; }
        public float DefendHomeX { get; set; }
        public float DefendHomeZ { get; set; }
        public float DefendLineShift { get; set; }
        public float DefendDepthCap { get; set; } = float.MaxValue;
        public IReadOnlyList<RunOffer> RunOffers { get; set; } = new List<RunOffer>();
        public ThreatGrid Threat { get; set; } = TestThreat.Plateau();   // 높은 평지: 슛은 지고 운반은 위험이 있으면 음수, 안전한 패스는 적합도 가산만큼 양수라 후보가 있으면 패스가 이긴다(11-03 P1+). xT 차이가 필요한 테스트는 격자를 직접 준다
        public bool IsPassTarget { get; set; }
        public bool IsRunPass { get; set; }
        public float PassTargetX { get; set; }
        public float PassTargetZ { get; set; }
        public bool WaitingForShot { get; set; }
        public bool IncomingShot { get; set; }
        public float IncomingShotTargetZ { get; set; }

        public string Did = "";
        public string Gait = "";   // 이동 종류: arrive(감속·최고 속도) / run(전력) / jog(자리 잡기). 10-29 가속 C
        public float MoveX, MoveZ; public int PassedTo = -1; public float PassX, PassZ;
        public void ArriveToward(float x, float z)
        {
            Did = "move";
            Gait = "arrive";
            MoveX = x;
            MoveZ = z;
        }
        public void RunToward(float x, float z)   // 트리 테스트는 "어디로 가나"와 이동 종류만 본다(속도 계산은 MovementRules 몫)
        {
            Did = "move";
            Gait = "run";
            MoveX = x;
            MoveZ = z;
        }
        public void JogToward(float x, float z)   // 자리 잡기(조깅 상한은 MovementRules 몫)
        {
            Did = "move";
            Gait = "jog";
            MoveX = x;
            MoveZ = z;
        }
        public void Shoot() { Did = "shoot"; }
        public bool Lob;   // 마지막 패스가 로빙이었나(10-29 ④-1)
        public void Pass(int receiverId, float landingX, float landingZ)
        {
            Did = "pass";
            Lob = false;
            Run = false;
            PassedTo = receiverId;
            PassX = landingX;
            PassZ = landingZ;
        }
        public void PassLob(int receiverId, float landingX, float landingZ)
        {
            Pass(receiverId, landingX, landingZ);
            Lob = true;
        }
        public bool Run;   // 마지막 패스가 런 제안 점으로 찬 것이었나(11-03 ⑤)
        public void PassRun(int receiverId, float landingX, float landingZ)
        {
            Pass(receiverId, landingX, landingZ);
            Run = true;
        }
    }

    private static readonly BehaviorNode Tree = PlayerTreeBuilder.Build();

    [Test]
    public void 땅볼_길이_막히고_착지점이_비면_띄워_준다()
    {
        // 10-29 ④-1: 패서 (0,0) → 앞선 리시버 (24,0). 경로 한가운데 (12,0)에 상대 1명. 리시버 주변은 비었다
        var cm = new Fake
        {
            PlayerId = 4, BallPhase = BallPhase.Owned, OwnsBall = true, BallOwnerTeam = 0, X = 0f, Z = 0f, Threat = TestThreat.Flat(),   // 골에서 52m라 슛이 없고, 평평한 격자에선 안전하지 않은 패스도 적합도 가산만큼 양수다
            Teammates = new List<TargetInfo> { new TargetInfo(9, 24f, 0f) },
            Opponents = new List<TargetInfo> { new TargetInfo(15, 12f, 0f), new TargetInfo(16, 40f, -15f), new TargetInfo(17, 42f, 15f), new TargetInfo(18, 50f, 0f) },   // 깊은 수비 2명 + GK 자리: 오프사이드 선 42
        };
        cm.Stats.PassLength = 25f;
        cm.Tactics.PassStyle = new[] { 1, 1, 1 };   // 직접. 짧게(기본 0)는 안 띄운다
        Tree.Tick(cm);
        Assert.AreEqual("pass", cm.Did);
        Assert.AreEqual(9, cm.PassedTo);
        Assert.IsTrue(cm.Lob, "땅볼은 (12,0) 수비에 끊긴다");

        cm.Tactics.PassStyle = new[] { 0, 0, 0 };
        cm.Did = "";
        cm.Lob = false;
        Tree.Tick(cm);
        Assert.IsFalse(cm.Lob, "짧게 서드에선 막혀도 안 띄운다(11-02)");
    }

    [Test]
    public void 땅볼_길이_비어_있으면_띄우지_않는다()
    {
        // 위와 같은 자리에서 경로 위 상대만 뺐다. 땅볼과 로빙이 둘 다 안전하면(가중 동률) 땅볼
        var cm = new Fake
        {
            PlayerId = 4, BallPhase = BallPhase.Owned, OwnsBall = true, BallOwnerTeam = 0, X = 0f, Z = 0f, Threat = TestThreat.Flat(),   // 골에서 52m라 슛이 없고, 평평한 격자에선 안전하지 않은 패스도 적합도 가산만큼 양수다
            Teammates = new List<TargetInfo> { new TargetInfo(9, 24f, 0f) },
            Opponents = new List<TargetInfo> { new TargetInfo(16, 40f, -15f), new TargetInfo(17, 42f, 15f), new TargetInfo(18, 50f, 0f) },
        };
        cm.Stats.PassLength = 25f;
        cm.Tactics.PassStyle = new[] { 1, 1, 1 };
        Tree.Tick(cm);
        Assert.AreEqual("pass", cm.Did);
        Assert.IsFalse(cm.Lob);
    }

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
        Assert.IsFalse(gk.Lob, "GK 배급은 로빙 판정을 안 탄다(PassUnchecked, 땅볼만)");
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
    public void 슛_준비_시간_중이면_같은_자리에서도_슛을_고르지_않는다()
    {
        // 11-08 S3 1단계: 위 테스트와 같은 자리·스탯인데 시뮬이 ShotReady 거짓을 주면 운반·패스 값 비교로 넘어간다
        var st = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 41.5f, Z = 0f, ShotReady = false };
        st.Stats.Shot = 90;
        st.Stats.ShotBias = 0.1f;
        Tree.Tick(st);
        Assert.That(st.Did, Is.EqualTo("move").Or.EqualTo("pass"), "운반이나 패스로 넘어간다");
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
    public void 드리블_성향이_낮아도_패스_후보가_있으면_패스한다_가까운_아군_쪽_드리블은_후보가_없을_때뿐이다()
    {
        // 11-03 P1+: 보유 값 0 기준선이 없어 필드 선수는 운반이나 패스 중 하나를 늘 실행한다. ⑤ 드리블은 닿지 않는 안전망으로 남았다 [가정 P-G13]
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, -30f, 10f) },
            Opponents = new List<TargetInfo>() };
        mid.Stats.ShotBias = 1f; mid.Stats.Dribble = 0.1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(1, mid.PassedTo);
    }

    [Test]
    public void 받는_선수_앞_착지점이_막히면_발밑으로_주고_발밑도_막히면_몬다()
    {
        // 09-21: 받는 선수(1)는 (15,0), 상대는 그 너머 (19, 0.3). 8m 앞(23)으로 차면 그 상대가 먹는다.
        // 09-30: 그때는 드리블로 떨어졌다. 이제 3.5m 앞(18.5, 그 상대 0.6m 옆)도 버리고 발밑(15)으로 준다
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 15f, 0f) },
            Opponents = new List<TargetInfo> { new TargetInfo(11, 19f, 0.3f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(15f, mid.PassX, 1e-4f, "착지점은 받는 선수 발밑");

        // 상대가 받는 선수에게 붙으면(15.5, 0.3) 발밑도 닿는다 → 드리블
        mid.Opponents = new List<TargetInfo> { new TargetInfo(11, 15.5f, 0.3f) };
        mid.Did = "";
        Tree.Tick(mid);
        Assert.AreEqual("move", mid.Did, "패스 대신 드리블");
    }

    [Test]
    public void 앞이_비면_받는_선수_앞_긴_리드_착지점으로_찬다()
    {
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 15f, 4f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(15f + MatchTuning.PassLeadOptions[0], mid.PassX, 1e-4f);
        Assert.AreEqual(4f, mid.PassZ, 1e-4f);
    }

    [Test]
    public void 방금_나에게_준_선수에게_뒤로_되돌리는_패스는_감점이라_비슷한_값이면_다른_후보가_이긴다()
    {
        // 11-03 P1 [가정 P-G2]: 옆 레인 A(18,28)와 B(18,-28)는 값이 같다. A가 직전 패서면 되돌림 감점으로 B가 이긴다. 후보가 A뿐이면 감점을 넘는 값이라 받는다
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 20f, BallX = 20f, LastPasserId = 1, Threat = WideLaneGrid(),
            Teammates = new List<TargetInfo> { new TargetInfo(1, 18f, 28f), new TargetInfo(2, 18f, -28f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(2, mid.PassedTo, "직전 패서(1)가 아닌 B(2)");

        mid.Teammates = new List<TargetInfo> { new TargetInfo(1, 18f, 28f) };
        mid.Did = "";
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did);
        Assert.AreEqual(1, mid.PassedTo, "다른 후보가 없으면 되돌림도 값이 양수면 허용");
    }

    [Test]
    public void 오프사이드_위치_아군에겐_안_주고_공격_자리는_온사이드_선_뒤로_잡는다()
    {
        // 팀0 패서 (10,0). 상대 GK 48, CB 31, CB 30 → 온사이드 선 31. 아군 A (35,0)은 오프사이드 위치, B (25,0)은 온사이드.
        // CB는 경로에서 12m 옆(가로채기 판정 밖: 09-24 상대 속도 7·평균 공 속도 9.7이면 8m 옆은 위험도 0.55라 안전 판정에 걸린다. 이 테스트는 오프사이드만 본다)
        var opp = new List<TargetInfo> { new TargetInfo(20, 48f, 0f), new TargetInfo(21, 31f, 12f), new TargetInfo(22, 30f, -12f) };
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

    // 앞으로 갈수록 오르는 단조 격자: 뒤·옆 후보의 도착 xT가 항상 낮은 장면(실제 격자는 레인마다 요철이 있어 경계 값이 흔들린다)
    private static ThreatGrid SlopeGrid()
    {
        return TestThreat.Slope();
    }

    // 폭 레인(행 0·7)이 중앙보다 위협이 큰 격자: 옆 레인으로 돌리는 값이 양수가 되는 장면을 만든다
    private static ThreatGrid WideLaneGrid()
    {
        var cells = new float[ThreatGrid.Rows, ThreatGrid.Columns];
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++) { cells[r, c] = c * 0.01f + ((r == 0 || r == 7) ? 0.15f : 0f); }
        }
        return new ThreatGrid(cells);
    }

    [Test]
    public void 옆_뒤_아군도_후보라서_앞이_막혀도_값이_음수면_돌리지_않고_양수면_압박과_무관하게_돌린다()
    {
        // 11-03 P1: 게이트(옆·뒤는 압박받을 때만)를 없앴다. 후보 전원의 값(성공 확률 × 도착 xT − 지금 xT)을 비교하고 0을 못 넘으면 드리블한다.
        // 앞 아군 A(15,0)는 경로 위 상대(7,0.2)에 막혀 값이 음수, 뒤 아군 B(-6,8)는 도착 xT가 지금보다 낮아 음수 → 드리블
        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f, Threat = SlopeGrid(),
            Teammates = new List<TargetInfo> { new TargetInfo(1, 15f, 0f), new TargetInfo(2, -6f, 8f) },
            Opponents = new List<TargetInfo> { new TargetInfo(11, 7f, 0.2f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual("move", mid.Did, "돌려도 xT가 오르지 않으면 몬다");
        Assert.Greater(mid.MoveX, 0f, "골 쪽으로");

        // 옆 레인의 xT가 높은 격자: 같은 깊이의 옆 아군 C(0,28)는 압박이 없어도 값이 양수라 받는다
        mid.Threat = WideLaneGrid();
        mid.Teammates = new List<TargetInfo> { new TargetInfo(1, 15f, 0f), new TargetInfo(3, 0f, 28f) };
        mid.Did = "";
        Tree.Tick(mid);
        Assert.AreEqual("pass", mid.Did, "압박이 없어도 옆 레인이 더 위협적이면 돌린다");
        Assert.AreEqual(3, mid.PassedTo);
    }

    [Test]
    public void 나한테_오는_패스면_도착점으로_마중_나간다()
    {
        var w = new Fake { PlayerId = 3, BallPhase = BallPhase.Flight, BallOwnerTeam = -1, IsPassTarget = true, PassTargetX = 20f, PassTargetZ = -10f };
        Tree.Tick(w);
        Assert.AreEqual("move", w.Did);
        Assert.AreEqual(20f, w.MoveX);
        Assert.AreEqual(-10f, w.MoveZ);
        Assert.AreEqual("arrive", w.Gait, "일반 패스는 도착점에서 멈춰 받는다");
    }

    [Test]
    public void 런_패스를_받는_선수는_감속_없이_공의_앞을_쫓아_달려_들어간다()
    {
        // 11-06 W 침투: 런 패스 수신은 고정 도착점이 아니라 공 추격(PressRules.PursuitPoint). 판정 롤아웃(RunRules.RunnerMeetsBall)과 같은 식
        var w = new Fake { PlayerId = 3, X = 15f, Z = -12f, BallPhase = BallPhase.Flight, BallOwnerTeam = -1, IsPassTarget = true, IsRunPass = true,
            PassTargetX = 20f, PassTargetZ = -10f, BallX = 5f, BallZ = -5f, BallVelX = 10f, BallVelZ = -3f };
        Tree.Tick(w);
        (float x, float z) aim = PressRules.PursuitPoint(15f, -12f, MatchRules.SpeedMps(w.Stats.Speed), 5f, -5f, 10f, -3f);
        Assert.AreEqual("move", w.Did);
        Assert.AreEqual("run", w.Gait);
        Assert.AreEqual(aim.x, w.MoveX, 1e-4f);
        Assert.AreEqual(aim.z, w.MoveZ, 1e-4f);
        Assert.AreNotEqual(20f, w.MoveX, "고정 도착점이 아니다");
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

        var cb = new Fake { PlayerId = 2, BallPhase = BallPhase.Flight, BallOwnerTeam = 1, X = -36f, BallX = 10f, BallZ = 0f, DefendHomeX = -36f, DefendLineShift = 7f, PressRank = int.MaxValue };   // 압박 거리 밖 = 시뮬이 순위를 안 준다
        Tree.Tick(cb);
        Assert.AreEqual(-36f + 7f, cb.MoveX, 1e-4f, "수비 자리 + 팀 이동량 Δ. 비행 중이라고 공격 자리(⑪)로 안 간다");
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
    public void 아군_소유에_공격_자리가_앞이면_전진_런이라_조깅이_아니다()
    {
        var cm = new Fake { PlayerId = 4, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = -30f, AttackHomeX = -20f, AttackHomeZ = 8f };
        cm.Stats.RoamRadius = 100f;
        Tree.Tick(cm);
        Assert.AreEqual("arrive", cm.Gait, "자리 -20이 지금 -30보다 10m 앞");
    }

    [Test]
    public void 아군_소유라도_공격_자리가_뒤면_조깅으로_돌아온다()
    {
        var cm = new Fake { PlayerId = 4, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = -10f, AttackHomeX = -20f, AttackHomeZ = 8f };
        cm.Stats.RoamRadius = 100f;
        Tree.Tick(cm);
        Assert.AreEqual("jog", cm.Gait);
    }

    [Test]
    public void 상대_소유에_압박_밖이면_수비_자리로_조깅한다()
    {
        var cb = new Fake { PlayerId = 3, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -10f, BallX = 30f, BallZ = 0f, DefendHomeX = -30f, DefendHomeZ = 0f, PressRank = MatchTuning.MaxPressers };   // 압박 순위 밖
        Tree.Tick(cb);
        Assert.AreEqual("jog", cb.Gait);
    }

    [Test]
    public void 상대_소유면_압박_거리_안이면_공으로_아니면_수비_자리로()
    {
        var st = new Fake { PlayerId = 9, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = 0f, BallX = 10f, BallZ = 0f, DefendHomeX = -10f, DefendHomeZ = 0f };
        st.Stats.PressRange = 12f;
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did);
        Assert.AreEqual(10f, st.MoveX, "압박: 공으로");

        var cb = new Fake { PlayerId = 2, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -36f, BallX = 10f, BallZ = 0f, DefendHomeX = -36f, DefendHomeZ = 7f, DefendLineShift = 7f, PressRank = int.MaxValue };   // 압박 거리 밖 = 시뮬이 순위를 안 준다
        cb.Stats.LineHeight = 6f;
        Tree.Tick(cb);
        Assert.AreEqual(-36f + 6f * MatchTuning.PositionDialScale + 7f, cb.MoveX, 1e-4f, "수비 자리 + 라인 높이 × 0.3 + 팀 이동량 Δ(세로 슬라이드는 11-08 D2에서 삭제)");
    }

    [Test]
    public void 수비_자리는_시뮬이_준_팀_이동량을_더하고_GK는_안_더한다()
    {
        // 11-08 D2: 팀 수비 라인 높이는 시뮬이 LineRules로 계산한 Δ(DefendLineShift) 안에 들어 있다. 트리가 ctx.DefendLineShift와 IsGoalkeeper를 넘기는 배선을 잠근다(순수 함수 테스트만으론 배선을 빼도 통과한다, 10-27 리뷰 🟡)
        var still = new Fake { PlayerId = 2, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -36f, BallX = 10f, BallZ = 0f, DefendHomeX = -36f, DefendHomeZ = 7f, PressRank = int.MaxValue };
        var shifted = new Fake { PlayerId = 2, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -36f, BallX = 10f, BallZ = 0f, DefendHomeX = -36f, DefendHomeZ = 7f, DefendLineShift = 12f, PressRank = int.MaxValue };
        Tree.Tick(still);
        Tree.Tick(shifted);
        Assert.AreEqual("move", shifted.Did);
        Assert.AreEqual(still.MoveX + 12f, shifted.MoveX, 1e-4f, "필드 선수는 Δ만큼 앞");

        var gkStill = new Fake { PlayerId = 0, IsGoalkeeper = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -48f, BallX = 10f, BallZ = 0f, DefendHomeX = -48f, DefendHomeZ = 0f, PressRank = int.MaxValue };
        var gkShifted = new Fake { PlayerId = 0, IsGoalkeeper = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -48f, BallX = 10f, BallZ = 0f, DefendHomeX = -48f, DefendHomeZ = 0f, DefendLineShift = 12f, PressRank = int.MaxValue };
        Tree.Tick(gkStill);
        Tree.Tick(gkShifted);
        Assert.AreEqual(gkStill.MoveX, gkShifted.MoveX, 1e-4f, "GK는 팀 이동량을 안 따른다(GK 라인은 슛 대응 기준)");
    }

    [Test]
    public void 압박_거리_안이어도_팀_안_순위가_상한_밖이면_수비_자리로_간다()
    {
        // 09-23 뭉침: 인원 제한이 없으면 압박 거리 안 4~6명이 동시에 공으로 갔다. 순위 1(두 번째)은 상한 1 밖이라 자리로
        var cm = new Fake { PlayerId = 6, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = 0f, BallX = 10f, BallZ = 0f, DefendHomeX = -20f, DefendLineShift = 7f, PressRank = 1 };
        cm.Stats.PressRange = 12f;
        Tree.Tick(cm);
        Assert.AreEqual(-20f + 7f, cm.MoveX, 1e-4f, "수비 자리 + 팀 이동량 Δ. 공(10)으로 안 간다");
    }

    [Test]
    public void 시뮬이_압박_후보에서_뺐으면_공_1m_앞이어도_자리로_간다()
    {
        // 09-29: 압박 후보(팀 압박 시작 "안 감" 포함)는 시뮬이 정하고 트리는 순위만 본다. "안 감"이면 거리와 무관하게 후보가 아니다(PressRulesTests)
        var st = new Fake { PlayerId = 9, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = 9f, BallX = 10f, DefendHomeX = -10f, DefendLineShift = 7f, PressRank = int.MaxValue };
        Tree.Tick(st);
        Assert.AreEqual(-10f + 7f, st.MoveX, 1e-4f, "1m 앞인데도 자리로(+ 팀 이동량 Δ)");
    }

    [Test]
    public void 자유_공은_시뮬이_정한_팀_추격자가_쫓고_GK는_출격_반경_안만()
    {
        // 누가 추격자인지(팀별 최근접 + 히스테리시스)는 시뮬이 정해 스냅샷으로 준다(09-28 F1b). 트리는 그 표시만 본다. 선택 규칙은 PressRulesTests·ChaserHysteresisTests
        var chaser = new Fake { PlayerId = 9, BallPhase = BallPhase.Free, X = 2f, BallX = 0f, IsLooseBallChaser = true };
        Tree.Tick(chaser);
        Assert.AreEqual(0f, chaser.MoveX, "공으로");

        var other = new Fake { PlayerId = 21, Team = 1, BallPhase = BallPhase.Free, X = 8f, BallX = 0f, AttackHomeX = 8f, IsLooseBallChaser = false };
        Tree.Tick(other);
        Assert.AreEqual(8f, other.MoveX, 1e-4f, "추격자가 아니면 자리 유지");

        var gk = new Fake { PlayerId = 0, IsGoalkeeper = true, BallPhase = BallPhase.Free, X = -48f, BallX = 0f, AttackHomeX = -48f };
        Tree.Tick(gk);
        Assert.AreEqual(-48f, gk.MoveX, 1e-4f, "GK는 반경 밖 공은 안 쫓음");
    }

    [Test]
    public void GK는_내_골로_오는_슛_차례면_자리_규칙_대신_수비_자리_x와_조준_z로_간다()
    {
        // ⑨b(10-23). 시뮬이 IncomingShot·조준점을 주면 GK만 그쪽으로. 자기 x(라인)는 지킨다 — 시뮬의 닿음 판정(KeeperCanReachShot)도 같은 점까지의 거리를 본다
        var gk = new Fake { PlayerId = 0, IsGoalkeeper = true, BallPhase = BallPhase.Flight, BallOwnerTeam = -1, X = -48f, Z = 0f, BallX = 20f, BallZ = 1f,
            DefendHomeX = -48f, DefendHomeZ = 0f, IncomingShot = true, IncomingShotTargetZ = 3f };
        Tree.Tick(gk);
        Assert.AreEqual("move", gk.Did);
        Assert.AreEqual(-48f + gk.Stats.LineHeight * MatchTuning.PositionDialScale, gk.MoveX, 1e-4f, "수비 자리 x(라인)");
        Assert.AreEqual(3f, gk.MoveZ, 1e-4f, "조준점 z");

        var fieldPlayer = new Fake { PlayerId = 5, BallPhase = BallPhase.Flight, BallOwnerTeam = -1, X = -30f, BallX = 20f, DefendHomeX = -30f, IncomingShot = true, IncomingShotTargetZ = 3f, IsLooseBallChaser = false };
        Tree.Tick(fieldPlayer);
        Assert.AreNotEqual(3f, fieldPlayer.MoveZ, "필드 플레이어는 ⑨b를 안 탄다");
    }

    [Test]
    public void GK는_슛_대기_차례면_움직이지_않는다()
    {
        // ⑨a(10-25). 시뮬이 WaitingForShot을 주면 GK는 자리 규칙(수비 자리·공 추적)으로도 안 간다
        var gk = new Fake { PlayerId = 0, IsGoalkeeper = true, BallPhase = BallPhase.Flight, BallOwnerTeam = -1, X = -48f, Z = 1.5f, BallX = 20f, BallZ = 0f,
            DefendHomeX = -48f, DefendHomeZ = 0f, WaitingForShot = true };
        Tree.Tick(gk);
        Assert.AreNotEqual("move", gk.Did, "대기 중엔 이동 의도 없음");
    }

    [Test]
    public void 킥오프_키커는_앞선_동료가_있어도_옆_뒤_가장_가까운_동료에게_내준다()
    {
        // 09-29 유저 Play: 킥오프가 센터 킥 없이 바로 시작됐다. 키커 표시(시뮬이 정함)가 있으면 슛·드리블보다 먼저 옆·뒤로 짧게
        var kicker = new Fake { PlayerId = 9, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f, Z = 0f, IsKickoffTaker = true,
            Teammates = new List<TargetInfo> { new TargetInfo(10, 5f, 1f), new TargetInfo(7, -8f, 6f), new TargetInfo(6, -20f, 0f) } };
        Tree.Tick(kicker);
        Assert.AreEqual("pass", kicker.Did);
        Assert.AreEqual(7, kicker.PassedTo, "앞선 10번(5m)이 더 가까워도 뒤의 7번(10m)");
        Assert.IsFalse(kicker.Lob, "킥오프는 땅볼만");
    }

    [Test]
    public void 앞이_비면_운반이_패스보다_값이_커서_역할과_위치와_무관하게_골_쪽으로_몬다()
    {
        // 11-03 P1+: 운반 자격(ST·AM·W, 슛 사거리 안, 우리 진영 밖)을 없앴다. 앞으로 갈수록 오르는 격자에서 앞이 비면 운반이 패스를 이긴다
        var st = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 20f, Z = 5f, BallThird = Third.Opponent, Threat = SlopeGrid(),
            Teammates = new List<TargetInfo> { new TargetInfo(1, 30f, -8f) },
            Opponents = new List<TargetInfo> { new TargetInfo(11, 32f, 20f) } };
        st.Stats.RoleId = "ST"; st.Stats.ShotBias = 0f;
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did, "운반");
        Assert.AreEqual(FieldBounds.HalfLength, st.MoveX, 0.01f, "골 쪽으로");

        st.Opponents = new List<TargetInfo> { new TargetInfo(11, 22f, 6f) };   // 정면 2.2m: 운반 위험이 커서 운반 값이 음수 → 패스
        st.Did = "";
        Tree.Tick(st);
        Assert.AreEqual("pass", st.Did);

        st.Opponents = new List<TargetInfo> { new TargetInfo(11, 32f, 20f) };
        st.Stats.RoleId = "CM"; st.Did = "";
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did, "미드필더도 운반 후보다");

        st.X = -20f; st.BallThird = Third.Own; st.Did = "";   // 우리 진영 서드·골에서 72m도 후보다
        st.Teammates = new List<TargetInfo> { new TargetInfo(1, -5f, -20f) };
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did, "자격 조건이 없다");
    }

    [Test]
    public void 운반은_반대_진영_팀도_같은_규칙이고_골_앞에선_운반보다_슛이_먼저다()
    {
        // 팀 1은 -X 골을 노린다. 앞 = -X 쪽. 상대가 +X 쪽(뒤)에 있으면 앞이 빈 것
        var st = new Fake { PlayerId = 11, Team = 1, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -20f, Z = 5f, BallThird = Third.Opponent, Threat = SlopeGrid(),
            Teammates = new List<TargetInfo> { new TargetInfo(12, -35f, -20f) },
            Opponents = new List<TargetInfo> { new TargetInfo(1, -17f, 5f) } };   // 뒤 3m: 앞이 아니다
        st.Stats.RoleId = "ST"; st.Stats.ShotBias = 1f;
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did);
        Assert.AreEqual(-FieldBounds.HalfLength, st.MoveX, 0.01f, "-X 골 쪽으로");

        // 골 앞 4m 정면: 표의 최대 슛 성향(0.9 × 0.5 = 문턱 0.45)도 xG 0.53(shot 50)이 넘어 슛. 운반 목표가 골 중심이라 다가갈수록 각이 넓어져
        // 슛이 먼저 걸리고, 골라인을 소유한 채 넘는 일은 없다(10-08 러너 150판 골라인 너머 소유 틱 0)
        st.X = -(FieldBounds.HalfLength - 4f); st.Z = 0f; st.Did = ""; st.Stats.ShotBias = 0.9f;
        st.Opponents = new List<TargetInfo> { new TargetInfo(1, 0f, 20f) };
        Tree.Tick(st);
        Assert.AreEqual("shoot", st.Did, "골 앞에선 슛");
    }

    [Test]
    public void 돌파_의도가_있고_앞에_수비수가_있으면_안전한_패스가_있어도_몰고_간다()
    {
        // 09-29: 돌파 의도(시뮬이 드리블 성향으로 굴림)가 서면 ④ 패스보다 먼저 제치러 간다. 앞에 수비수가 없으면 평소대로 패스
        var w = new Fake { PlayerId = 7, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 10f, Z = 20f, WantsTakeOn = true,
            Teammates = new List<TargetInfo> { new TargetInfo(9, 22f, 5f) },
            Opponents = new List<TargetInfo> { new TargetInfo(20, 15f, 20f) } };
        w.Stats.ShotBias = 1f;
        Tree.Tick(w);
        Assert.AreEqual("move", w.Did, "수비수 정면 5m → 돌파");

        var free = new Fake { PlayerId = 7, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 10f, Z = 20f, WantsTakeOn = true,
            Teammates = new List<TargetInfo> { new TargetInfo(9, 22f, 5f) },
            Opponents = new List<TargetInfo> { new TargetInfo(20, 40f, -20f) } };
        free.Stats.ShotBias = 1f;
        Tree.Tick(free);
        Assert.AreEqual("pass", free.Did, "앞 8m에 상대가 없으면(이미 제침) 평소 판단");
    }

    // ── 사전 런·착지점(11-03 ⑤. 후보 게이트는 11-03 P1에서 지웠다)

    // 패서 (20,0)가 슛 안 하는 설정. 수비 라인 42(깊은 수비 2 + GK). 러너 9는 패서보다 8m 뒤 (12,10)
    private static Fake CarrierWithRunnerBehind(RunOffer offer, bool pressed = false)
    {
        var cm = new Fake
        {
            PlayerId = 4, BallPhase = BallPhase.Owned, OwnsBall = true, BallOwnerTeam = 0, X = 20f, Z = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(9, 12f, 10f) },
            Opponents = new List<TargetInfo> { new TargetInfo(16, 40f, -15f), new TargetInfo(17, 42f, 15f), new TargetInfo(18, 50f, 0f) },
        };
        cm.Stats.ShotBias = 1f;
        if (offer.RunnerId != RunOffer.NoRunner) { cm.RunOffers = new List<RunOffer> { offer }; }
        if (pressed) { cm.Opponents = new List<TargetInfo> { new TargetInfo(16, 40f, -15f), new TargetInfo(17, 42f, 15f), new TargetInfo(18, 50f, 0f), new TargetInfo(19, 20.5f, 0f) }; }
        return cm;
    }

    [Test]
    public void 패서_뒤의_러너도_런_목표가_앞이면_후보고_착지점은_그_목표점이다()
    {
        var cm = CarrierWithRunnerBehind(new RunOffer(9, 45f, 10f, 0f, 0f));
        Tree.Tick(cm);
        Assert.AreEqual("pass", cm.Did);
        Assert.AreEqual(9, cm.PassedTo);
        Assert.AreEqual(45f, cm.PassX, 1e-4f, "착지점 = 러너의 런 목표점(판정과 실행이 같은 점)");
        Assert.AreEqual(10f, cm.PassZ, 1e-4f);
        Assert.IsFalse(cm.Lob);
    }

    [Test]
    public void 런_제안이_없으면_패서보다_한참_뒤의_아군은_도착_xT가_낮아_후보_값이_음수다()
    {
        var cm = CarrierWithRunnerBehind(RunOffer.None);
        cm.Teammates = new List<TargetInfo> { new TargetInfo(9, 0f, 10f) };   // 20m 뒤
        cm.Threat = SlopeGrid();
        Tree.Tick(cm);
        Assert.AreEqual("move", cm.Did, "운반: 20m 뒤 패스는 값이 더 낮다");
    }

    [Test]
    public void 런_제안의_위험이_허용치를_넘으면_패스하지_않는다()
    {
        var cm = CarrierWithRunnerBehind(RunOffer.None);
        cm.Teammates = new List<TargetInfo> { new TargetInfo(9, 0f, 10f) };
        cm.RunOffers = new List<RunOffer> { new RunOffer(9, 45f, 10f, 1f, 1f) };
        Tree.Tick(cm);
        Assert.AreEqual("move", cm.Did, "러너가 아직 못 붙었다(위험 1)");
    }

    [Test]
    public void 방금_나에게_준_선수가_앞_공간으로_달리면_되돌림이_아니라_전진형이다()
    {
        var cm = CarrierWithRunnerBehind(new RunOffer(9, 45f, 10f, 0f, 0f));
        cm.LastPasserId = 9;
        Tree.Tick(cm);
        Assert.AreEqual("pass", cm.Did);
        Assert.AreEqual(9, cm.PassedTo, "원투: 뒤에 있어도 착지가 앞이면 전진형");
    }

    [Test]
    public void 러너는_런_목표점으로_가되_온사이드_선_뒤_여유까지만_간다()
    {
        // 러너 9(팀 0)가 아군 소유 중. 수비 라인 42 → 목표 x 45는 41.5로 잘린다. z는 목표 그대로, 이동 종류는 감속 도착(선에서 멈춘다)
        var runner = new Fake
        {
            PlayerId = 9, Team = 0, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 12f, Z = 10f,
            Opponents = new List<TargetInfo> { new TargetInfo(16, 40f, -15f), new TargetInfo(17, 42f, 15f), new TargetInfo(18, 50f, 0f) },
            RunOffers = new List<RunOffer> { new RunOffer(9, 45f, 10f, 0.3f, 0.3f) },
        };
        Tree.Tick(runner);
        Assert.AreEqual("move", runner.Did);
        Assert.AreEqual("arrive", runner.Gait);
        Assert.AreEqual(42f - MatchTuning.OnsideMargin, runner.MoveX, 1e-4f);
        Assert.AreEqual(10f, runner.MoveZ, 1e-4f);

        var inside = new Fake
        {
            PlayerId = 9, Team = 0, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 12f, Z = 10f,
            Opponents = runner.Opponents,
            RunOffers = new List<RunOffer> { new RunOffer(9, 36f, -8f, 0.3f, 0.3f) },
        };
        Tree.Tick(inside);
        Assert.AreEqual(36f, inside.MoveX, 1e-4f, "선 안쪽 목표는 그대로");
        Assert.AreEqual(-8f, inside.MoveZ, 1e-4f);
    }

    [Test]
    public void 팀1_러너도_공격_방향_온사이드_선_뒤로_자른다()
    {
        var runner = new Fake
        {
            PlayerId = 9, Team = 1, BallPhase = BallPhase.Owned, BallOwnerTeam = 1, X = -12f, Z = 10f,
            Opponents = new List<TargetInfo> { new TargetInfo(16, -40f, -15f), new TargetInfo(17, -42f, 15f), new TargetInfo(18, -50f, 0f) },
            RunOffers = new List<RunOffer> { new RunOffer(9, -45f, 10f, 0.3f, 0.3f) },
        };
        Tree.Tick(runner);
        Assert.AreEqual(-(42f - MatchTuning.OnsideMargin), runner.MoveX, 1e-4f);
    }

    [Test]
    public void 다른_선수의_런_제안은_내_자리_이동에_영향이_없다()
    {
        var teammate = new Fake
        {
            PlayerId = 5, Team = 0, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 12f, Z = 10f, AttackHomeX = 20f, AttackHomeZ = 10f,
            Opponents = new List<TargetInfo> { new TargetInfo(16, 40f, -15f), new TargetInfo(17, 42f, 15f), new TargetInfo(18, 50f, 0f) },
            RunOffers = new List<RunOffer> { new RunOffer(9, 45f, 10f, 0.3f, 0.3f) },
        };
        Tree.Tick(teammate);
        Assert.AreEqual("move", teammate.Did);
        Assert.Less(teammate.MoveX, 41f, "자리(공격 시 자리 + 오프셋)로 간다. 러너 목표(41.5)가 아니다");
    }

    [Test]
    public void GK_배급과_역습_패스는_런_제안이_있어도_스루를_만들지_않는다()
    {
        var gk = new Fake { PlayerId = 0, IsGoalkeeper = true, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = -48f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, -36f, 7f), new TargetInfo(2, -8f, 0f) },
            RunOffers = new List<RunOffer> { new RunOffer(2, 30f, 5f, 0f, 0f) } };
        Tree.Tick(gk);
        Assert.AreEqual("pass", gk.Did);
        Assert.AreEqual(1, gk.PassedTo, "GK 배급은 PassUnchecked 경로");
        Assert.AreNotEqual(30f, gk.PassX);

        var mid = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, IsCountering = true, X = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 10f, 0f), new TargetInfo(2, 30f, 5f) },
            RunOffers = new List<RunOffer> { new RunOffer(1, 40f, 20f, 0f, 0f) } };
        mid.Stats.ShotBias = 1f;
        Tree.Tick(mid);
        Assert.AreEqual(2, mid.PassedTo, "역습 패스는 최전방 아군에게, 착지는 PassLeadMax 리드");
        Assert.LessOrEqual(mid.PassX, 30f + MatchTuning.PassLeadMax + 1e-3f);
    }

    [Test]
    public void 런_제안이_있어도_운반_길이_비었으면_운반_값이_커서_운반하고_앞을_막으면_패스한다()
    {
        // ST(공격 자리 밖 중앙 22m)가 슛 사거리 안에서 공을 쥠. 러너 9는 뒤 (12,10), 런 지점은 박스 앞 (40,8). 격자는 앞으로 갈수록 오른다
        var cells = new float[ThreatGrid.Rows, ThreatGrid.Columns];
        for (int r = 0; r < ThreatGrid.Rows; r++)
        {
            for (int c = 0; c < ThreatGrid.Columns; c++) { cells[r, c] = c * 0.04f; }
        }
        var blocker = new TargetInfo(19, 24f, 0.5f);   // 운반 방향 정면 2m

        Fake st = CarrierSt();
        st.Threat = new ThreatGrid(cells);
        Tree.Tick(st);
        Assert.AreEqual("move", st.Did, "앞이 비면 운반이 CarryHorizon 앞의 xT를 얻어 런 지점보다 크다");

        st = CarrierSt();
        st.Threat = new ThreatGrid(cells);
        st.Opponents = new List<TargetInfo>(st.Opponents) { blocker };
        Tree.Tick(st);
        Assert.AreEqual("pass", st.Did, "정면을 막으면 운반 위험이 커서 값이 음수: 런 지점 패스");
        Assert.AreEqual(9, st.PassedTo);

        st = CarrierSt(risk: 1f);
        st.X = 23f;   // 골 중심 29.5m. 11-08 사거리 30 뒤로 22(30.5m)는 슛 후보 밖
        st.Threat = TestThreat.Plateau();
        st.Opponents = new List<TargetInfo>(st.Opponents) { blocker };
        Tree.Tick(st);
        Assert.AreEqual("shoot", st.Did, "운반도 패스도 음수여도 가장 큰 행동을 실행한다: 슛 값(xG × 배율 − 지금 값)이 가장 덜 나쁘다");
    }

    private static Fake CarrierSt(float risk = 0f)
    {
        var st = new Fake
        {
            PlayerId = 4, BallPhase = BallPhase.Owned, OwnsBall = true, BallOwnerTeam = 0, X = 22f, Z = 0f,
            Teammates = new List<TargetInfo> { new TargetInfo(9, 12f, 10f) },
            Opponents = new List<TargetInfo> { new TargetInfo(16, 49f, -15f), new TargetInfo(17, 50f, 15f), new TargetInfo(18, 52f, 0f) },
            RunOffers = new List<RunOffer> { new RunOffer(9, 40f, 8f, risk, risk) },
        };
        st.Stats.RoleId = "ST";
        st.Stats.Shot = 50;
        st.Stats.ShotBias = 1f;
        return st;
    }

    // ── 값 비교(11-03 P1+, 11-05 리뷰 반영)

    // 같은 장면에서 xG가 0.14~0.18인 자리를 찾는다. 배율 1 + 4 × (1 − 성향)이라 성향 0.2는 4.2(xG 0.12 넘으면 쏨), 0.8은 1.8(xG 0.28 넘어야 쏨)이다(높은 평지 격자 V = 0.5, 운반 값 0)
    private static float ShotBoundaryX()
    {
        for (float x = 20f; x < 50f; x += 0.25f)
        {
            float chance = MatchRules.ShotProbability(x, 0f, +1, 50, 50, 50);
            if (chance > 0.14f && chance < 0.18f) { return x; }
        }
        Assert.Fail("xG 0.14~0.18 자리가 없다");
        return 0f;
    }

    private static Fake ShooterAt(float x, float shotBias)
    {
        var st = new Fake { PlayerId = 9, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = x, Z = 0f, BallThird = Third.Opponent };
        st.Stats.RoleId = "ST"; st.Stats.Shot = 50; st.Stats.ShotBias = shotBias;
        return st;
    }

    [Test]
    public void 슛_성향이_낮을수록_자주_쏜다_같은_장면에서_0점2는_쏘고_0점8은_안_쏜다()
    {
        float x = ShotBoundaryX();
        Fake poacher = ShooterAt(x, 0.2f);
        Tree.Tick(poacher);
        Assert.AreEqual("shoot", poacher.Did, "포처(0.2): 배율 4.2 × xG가 지금 값 0.5를 넘는다");

        Fake defender = ShooterAt(x, 0.8f);
        Tree.Tick(defender);
        Assert.AreNotEqual("shoot", defender.Did, "수비수(0.8): 배율 1.8 × xG가 지금 값 0.5에 못 미쳐 운반(값 0)이 이긴다");
    }

    [Test]
    public void 슛_성향을_올리면_같은_자리에서_슛이_단조로_줄어든다()
    {
        float x = ShotBoundaryX();
        bool stopped = false;
        int shots = 0;
        for (int i = 0; i <= 10; i++)
        {
            Fake st = ShooterAt(x, i / 10f);
            Tree.Tick(st);
            bool shoots = st.Did == "shoot";
            Assert.IsFalse(stopped && shoots, $"성향 {i / 10f}에서 안 쏘다가 다시 쏜다");
            if (shoots) { shots++; }
            else { stopped = true; }
        }
        Assert.Greater(shots, 0, "낮은 성향은 쏜다");
        Assert.Less(shots, 11, "높은 성향은 안 쏜다");
    }

    [Test]
    public void 값_메모는_한_틱의_한_선수_것이라_같은_선수의_다음_틱과_다른_선수에게_이어지지_않는다()
    {
        var a = new Fake { PlayerId = 1, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f, Threat = TestThreat.Flat(),
            Teammates = new List<TargetInfo> { new TargetInfo(2, 15f, 0f) } };
        Tree.Tick(a);
        Assert.AreEqual("pass", a.Did);

        a.Teammates = new List<TargetInfo>();   // 같은 선수, 다음 틱: 후보가 사라졌다
        a.Did = "";
        Tree.Tick(a);
        Assert.AreEqual("move", a.Did, "지난 틱의 패스 결과를 이어받지 않는다");

        var b = new Fake { PlayerId = 3, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 0f, Threat = TestThreat.Flat(),
            Teammates = new List<TargetInfo> { new TargetInfo(4, 15f, 0f) } };
        Tree.Tick(b);
        Assert.AreEqual("pass", b.Did);
        Assert.AreEqual(4, b.PassedTo);
        Tree.Tick(a);   // a는 여전히 후보가 없다
        Assert.AreEqual("move", a.Did, "다른 선수의 값 메모를 이어받지 않는다");
    }

    [Test]
    public void 후보_상한_가지치기를_끄고_켜도_고르는_행동이_같다_무작위_장면_300개()
    {
        // 패스 방식(짧게·직접·길게)과 폭을 섞어 로빙 후보를, 3장면에 1번 런 제안을 넣어 상한의 런 제안 분기도 탄다(11-05 리뷰)
        var random = new System.Random(20261105);
        for (int scene = 0; scene < 300; scene++)
        {
            var st = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, Threat = TestThreat.Real,
                X = Next(random, -30f, 40f), Z = Next(random, -25f, 25f), LastPasserId = random.Next(0, 4) == 0 ? 1 : -1 };
            st.Stats.RoleId = "CM"; st.Stats.Shot = 50; st.Stats.ShotBias = (float)random.NextDouble();
            var mates = new List<TargetInfo>();
            for (int i = 1; i <= random.Next(2, 10); i++) { mates.Add(new TargetInfo(i, Next(random, -40f, 50f), Next(random, -30f, 30f))); }
            var opponents = new List<TargetInfo> { new TargetInfo(50, 48f, 0f) };
            for (int i = 0; i < random.Next(3, 10); i++) { opponents.Add(new TargetInfo(51 + i, Next(random, -20f, 50f), Next(random, -30f, 30f))); }
            st.Teammates = mates; st.Opponents = opponents;
            int passStyle = random.Next(0, 3);
            int width = random.Next(0, 3);
            st.Tactics.PassStyle = new[] { passStyle, passStyle, passStyle };
            st.Tactics.Width = new[] { width, width, width };
            st.RunOffers = new List<RunOffer>();
            if (random.Next(0, 3) == 0)
            {
                st.RunOffers = new List<RunOffer> { new RunOffer(mates[0].PlayerId, Next(random, 0f, 45f), Next(random, -25f, 25f), Next(random, 0f, 0.6f), Next(random, 0f, 0.6f)) };
            }

            PlayerTreeBuilder.PruneByCeiling = true;
            Tree.Tick(st);
            string pruned = $"{st.Did}/{st.PassedTo}/{st.PassX:0.000}/{st.PassZ:0.000}/{st.MoveX:0.000}/{st.MoveZ:0.000}";
            st.Did = ""; st.PassedTo = -1;
            PlayerTreeBuilder.PruneByCeiling = false;
            Tree.Tick(st);
            string full = $"{st.Did}/{st.PassedTo}/{st.PassX:0.000}/{st.PassZ:0.000}/{st.MoveX:0.000}/{st.MoveZ:0.000}";
            PlayerTreeBuilder.PruneByCeiling = true;
            Assert.AreEqual(full, pruned, $"장면 {scene}");
        }
    }

    private static float Next(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    [Test]
    public void 실제_격자에서_운반이_역습과_돌파보다_먼저_정해진다_현재_순서_잠금()
    {
        // 11-05 리뷰 Y2: ②c(운반)가 ③ 역습·③b 돌파보다 앞이라 운반 ≥ 패스면 둘이 안 나온다. 순서가 정해지면 이 테스트를 바꾼다.
        // 앞이 빈 중원: 운반 값이 패스 값보다 크다
        var counter = new Fake { PlayerId = 0, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, IsCountering = true, X = 0f, Z = 0f, Threat = TestThreat.Real,
            Teammates = new List<TargetInfo> { new TargetInfo(1, 4f, 0f) },   // 역습 대상(패서보다 CounterForwardMargin 이상 앞)이지만 5m도 안 되는 짧은 패스
            Opponents = new List<TargetInfo> { new TargetInfo(11, 45f, -20f), new TargetInfo(12, 48f, 15f) } };
        counter.Stats.RoleId = "CM"; counter.Stats.ShotBias = 1f;
        Tree.Tick(counter);
        Assert.AreEqual("move", counter.Did, "역습 중이어도 운반이 먼저(③ 역습 패스가 아니다)");
        Assert.AreEqual(FieldBounds.HalfLength, counter.MoveX, 0.01f);

        var takeOn = new Fake { PlayerId = 7, OwnsBall = true, BallPhase = BallPhase.Owned, BallOwnerTeam = 0, X = 10f, Z = 20f, WantsTakeOn = true, Threat = TestThreat.Real,
            Teammates = new List<TargetInfo> { new TargetInfo(9, 22f, 5f) },
            Opponents = new List<TargetInfo> { new TargetInfo(20, 17.5f, 20f), new TargetInfo(21, 48f, 0f) } };   // 정면 7.5m: 돌파 사거리(8m) 안, 운반 위험 존(6m) 밖
        takeOn.Stats.RoleId = "W"; takeOn.Stats.ShotBias = 1f;
        Tree.Tick(takeOn);
        Assert.AreEqual("move", takeOn.Did);
        Assert.AreEqual(FieldBounds.HalfLength, takeOn.MoveX, 0.01f, "돌파 목표(앞 4m)가 아니라 골 중심으로 간다: ②c가 ③b보다 먼저");
    }

    [Test]
    public void 측면_가중만으로는_xT가_더_높은_중앙_후보를_못_이긴다()
    {
        // 지운 계약(옛 "라인 위 윙어가 중앙 앞 후보를 측면 가중만으로 이기지 않는다")을 값 식으로 다시 잠근다.
        // 패서 (0, 20). 라인 위 윙어 (10, 31)과 5m 더 앞선 중앙 (15, 20). 격자는 앞으로 오른다
        ThreatGrid grid = TestThreat.Slope();
        float wingerFit = PassRules.ReceiverFit(0f, 20f, 10f, 31f, passStyle: 1, passLength: 15f, widthLevel: 2);
        float centralFit = PassRules.ReceiverFit(0f, 20f, 15f, 20f, passStyle: 1, passLength: 15f, widthLevel: 2);
        float winger = ThreatRules.PassValue(grid, 0f, 20f, 10f, 31f, +1, 1f, MatchTuning.ReceiverFitWeight * wingerFit);
        float central = ThreatRules.PassValue(grid, 0f, 20f, 15f, 20f, +1, 1f, MatchTuning.ReceiverFitWeight * centralFit);
        Assert.Less(winger, central, "전진 5m의 xT 이득이 측면 가중을 이긴다");
    }
}
