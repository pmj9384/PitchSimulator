using Game.Core.AI;
using Game.Core.Data;
using Game.Core.Match;
using NUnit.Framework;

// 루즈볼 추격자 히스테리시스(09-28 F1b)를 시뮬 배선째 잠근다. 선택 규칙 자체는 PressRulesTests.
// 틱마다 선수 위치를 다시 놓아 이동의 영향을 없애고 "공과의 거리 차이"만으로 담당이 바뀌는지 본다
public class ChaserHysteresisTests
{
    private const float Dt = 0.02f;

    private static PlayerStats Field()
    {
        return new PlayerStats { RoleId = "CM", VariantId = "cm_test", Speed = 50, PressRange = 8f, ShotBias = 1f, PassLength = 15f };
    }

    private static PlayerStats Keeper()
    {
        return new PlayerStats { RoleId = "GK", VariantId = "gk_test", Speed = 30, PressRange = 3f, ShotBias = 1f, PassLength = 25f, GkRushRadius = 12f };
    }

    private static void Place(PlayerState p, float x, float z)
    {
        p.X = x;
        p.Z = z;
    }

    [Test]
    public void 루즈볼_담당은_도전자가_2m_넘게_가까워질_때만_바뀐다()
    {
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.Build());
        PlayerState a = sim.AddPlayer(new PlayerState(playerId: 0, team: 0, Field(), x: -10f, z: 0f));
        PlayerState b = sim.AddPlayer(new PlayerState(playerId: 1, team: 0, Field(), x: -10.5f, z: 0f));
        sim.Ball = BallState.FreeAt(0f, 0f);

        sim.Tick(Dt);
        Assert.IsTrue(a.IsLooseBallChaser, "처음엔 최근접(10m 대 10.5m)");
        Assert.IsFalse(b.IsLooseBallChaser);

        Place(a, -10f, 0f); Place(b, -9f, 0f);   // 도전자 1m 더 가까움
        sim.Tick(Dt);
        Assert.IsTrue(a.IsLooseBallChaser, "1m 차이로는 담당 유지(붙다 말다 방지)");

        Place(a, -10f, 0f); Place(b, -7.5f, 0f); // 2.5m 더 가까움
        sim.Tick(Dt);
        Assert.IsTrue(b.IsLooseBallChaser, "2m 넘으면 넘긴다");
        Assert.IsFalse(a.IsLooseBallChaser);
    }

    [Test]
    public void 출격_못_하는_GK가_최근접이어도_필드_플레이어가_쫓는다()
    {
        // 09-23 R3 회귀: 공 -33, GK -48(15m > 출격 12m), 필드 -20(13m). GK는 루즈볼 후보에서 빠진다
        var sim = new MatchSimulation(() => 0.5f, PlayerTreeBuilder.Build());
        PlayerState gk = sim.AddPlayer(new PlayerState(playerId: 0, team: 0, Keeper(), x: -48f, z: 0f));
        PlayerState field = sim.AddPlayer(new PlayerState(playerId: 1, team: 0, Field(), x: -20f, z: 0f));
        sim.Ball = BallState.FreeAt(-33f, 0f);

        sim.Tick(Dt);
        Assert.IsTrue(field.IsLooseBallChaser, "GK를 뺀 최근접");
        Assert.IsFalse(gk.IsLooseBallChaser);
        Assert.Less(field.X, -20f, "필드 플레이어가 공 쪽으로 움직였다");
    }
}
