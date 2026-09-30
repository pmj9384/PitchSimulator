using System;
using Game.Core.Data;
using Game.Core.Tactics;
using NUnit.Framework;

// 개인 지시(09-30, 스펙 §4-2 개인 전술·전술-기획 3-2)의 순수 판정: 역할 값 기준 5단(−2~+2) 오프셋.
// 팀 슬라이더(카드 기준 오프셋)와 같은 구조다. 값 하나를 직접 고르지 않고 "역할보다 더/덜"을 고른다
public class PlayerInstructionsTests
{
    private static PlayerStats Winger()
    {
        return new PlayerStats
        {
            RoleId = "W", VariantId = "w_winger", PushUp = 25f, Width = 25f, PressRange = 8f, LineHeight = 0f,
            ShotBias = 0.5f, PassLength = 15f, PassRisk = 0.5f, Dribble = 0.9f, HoldUp = 0.3f, Shot = 60,
        };
    }

    private static PlayerStats Keeper()
    {
        return new PlayerStats { RoleId = "GK", VariantId = "gk_standard", GkRushRadius = 12f, PressRange = 3f };
    }

    private static int[] Offsets(PlayerDial dial, int value)
    {
        var offsets = new int[PlayerInstructions.DialCount];
        offsets[(int)dial] = value;
        return offsets;
    }

    [Test]
    public void 지시가_없으면_역할_값_그대로다()
    {
        PlayerStats s = PlayerInstructions.Apply(Winger(), new int[PlayerInstructions.DialCount]);
        Assert.AreEqual(25f, s.PushUp);
        Assert.AreEqual(8f, s.PressRange);
        Assert.AreEqual(0.5f, s.ShotBias);
        Assert.AreEqual(60, s.Shot, "능력치는 지시와 무관");
    }

    [Test]
    public void 한_칸은_다이얼마다_정한_크기만큼_옮기고_다른_값은_그대로다()
    {
        PlayerStats forward = PlayerInstructions.Apply(Winger(), Offsets(PlayerDial.PushUp, 2));
        Assert.AreEqual(35f, forward.PushUp, 1e-4f, "전진 폭 한 칸 5m");
        Assert.AreEqual(8f, forward.PressRange, "다른 다이얼은 그대로");

        PlayerStats press = PlayerInstructions.Apply(Winger(), Offsets(PlayerDial.PressRange, -1));
        Assert.AreEqual(6f, press.PressRange, 1e-4f, "압박 거리 한 칸 2m");
    }

    [Test]
    public void 슛_성향을_올리면_더_낮은_확률에서도_쏜다()
    {
        // ShotBias는 "이 값 × 0.5 이상의 xG면 쏜다"는 문턱이라 값이 낮을수록 자주 쏜다. 지시의 +는 "더 자주"로 맞춘다
        PlayerStats more = PlayerInstructions.Apply(Winger(), Offsets(PlayerDial.ShotBias, 2));
        Assert.AreEqual(0.4f, more.ShotBias, 1e-4f);
        PlayerStats less = PlayerInstructions.Apply(Winger(), Offsets(PlayerDial.ShotBias, -2));
        Assert.AreEqual(0.6f, less.ShotBias, 1e-4f);
    }

    [Test]
    public void 범위_끝에서_자르고_원본은_바꾸지_않는다()
    {
        PlayerStats role = Winger();
        Assert.AreEqual(1f, PlayerInstructions.Apply(role, Offsets(PlayerDial.Dribble, 2)).Dribble, 1e-4f, "드리블 성향 0.9 + 0.3은 1에서 자른다");
        Assert.AreEqual(0f, PlayerInstructions.Apply(role, Offsets(PlayerDial.LineHeight, -2)).LineHeight, 1e-4f, "0 아래로 안 내려간다");
        Assert.AreEqual(0.9f, role.Dribble, "역할 값 원본은 그대로");
    }

    [Test]
    public void 칸_수가_다르거나_범위_밖이면_거부한다()
    {
        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Validate(new int[3], Winger()));
        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Validate(Offsets(PlayerDial.PushUp, 3), Winger()));
        Assert.DoesNotThrow(() => PlayerInstructions.Validate(Offsets(PlayerDial.PushUp, -2), Winger()));
    }

    [Test]
    public void 골키퍼는_출격_반경만_필드_선수는_그것만_빼고_지시할_수_있다()
    {
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.GkRushRadius, Keeper()));
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.PushUp, Keeper()));
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.PushUp, Winger()));
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.GkRushRadius, Winger()));

        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Validate(Offsets(PlayerDial.PushUp, 1), Keeper()));
        Assert.AreEqual(14f, PlayerInstructions.Apply(Keeper(), Offsets(PlayerDial.GkRushRadius, 1)).GkRushRadius, 1e-4f, "출격 반경 한 칸 2m");
        PlayerStats sweeper = Keeper();
        sweeper.GkRushRadius = 16f;
        Assert.AreEqual(16.5f, PlayerInstructions.Apply(sweeper, Offsets(PlayerDial.GkRushRadius, 1)).GkRushRadius, 1e-4f, "16 + 2는 박스 깊이(16.5m)에서 자른다");
    }
}
