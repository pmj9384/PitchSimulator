using System;
using Game.Core.Data;
using Game.Core.Tactics;
using NUnit.Framework;

// 개인 지시(09-30, 스펙 §4-2 개인 전술·전술-기획 3-2)의 순수 판정: 역할 값 기준 5단(−2~+2) 오프셋.
// 팀 슬라이더(카드 기준 오프셋)와 같은 구조다. 값 하나를 직접 고르지 않고 "역할보다 더/덜"을 고른다.
// 10-27 슬롯 중심: 지시는 자리(슬롯)의 것이라 Apply는 던지지 않고 잘라내며, 자리별 보이는 다이얼 표(AppliesTo)와 옛 10칸 이행(FromLegacy)이 있다
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
        PlayerStats shortPress = Winger();
        shortPress.PressRange = 2f;
        Assert.AreEqual(0f, PlayerInstructions.Apply(shortPress, Offsets(PlayerDial.PressRange, -2)).PressRange, 1e-4f, "2 − 4는 0에서 자른다");
        Assert.AreEqual(0.9f, role.Dribble, "역할 값 원본은 그대로");
    }

    [Test]
    public void 칸_수가_다르거나_범위_밖이면_거부한다()
    {
        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Validate(new int[3], "W"));
        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Validate(Offsets(PlayerDial.PushUp, 3), "W"));
        Assert.DoesNotThrow(() => PlayerInstructions.Validate(Offsets(PlayerDial.PushUp, -2), "W"));
        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Apply(Winger(), new int[3]), "Apply도 칸 수는 본다");
        Assert.AreEqual(35f, PlayerInstructions.Apply(Winger(), Offsets(PlayerDial.PushUp, 7)).PushUp, 1e-4f, "Apply는 범위 밖을 +2로 잘라 쓴다(슬롯 지시가 다른 선수에게 얹힐 때 던지지 않는다)");
    }

    [Test]
    public void 골키퍼는_출격_반경만_필드_선수는_그것만_빼고_지시할_수_있다()
    {
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.GkRushRadius, "GK"));
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.PushUp, "GK"));
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.PushUp, "W"));
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.GkRushRadius, "W"));

        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Validate(Offsets(PlayerDial.PushUp, 1), "GK"));
        Assert.AreEqual(12f, PlayerInstructions.Apply(Keeper(), Offsets(PlayerDial.PushUp, 2)).GkRushRadius, 1e-4f, "해당 없는 다이얼은 Apply가 무시한다");
        Assert.AreEqual(14f, PlayerInstructions.Apply(Keeper(), Offsets(PlayerDial.GkRushRadius, 1)).GkRushRadius, 1e-4f, "출격 반경 한 칸 2m");
        PlayerStats sweeper = Keeper();
        sweeper.GkRushRadius = 16f;
        Assert.AreEqual(16.5f, PlayerInstructions.Apply(sweeper, Offsets(PlayerDial.GkRushRadius, 1)).GkRushRadius, 1e-4f, "16 + 2는 박스 깊이(16.5m)에서 자른다");
    }

    [Test]
    public void 수비수는_슛_드리블_볼끌기가_없고_CB는_측면_쏠림도_없다()
    {
        // 레퍼런스 7절 (b): FM은 목록 하나에서 자리에 안 맞는 항목을 숨긴다. 값은 추정
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.ShotBias, "CB"));
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.Dribble, "FB"));
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.HoldUp, "CB"));
        Assert.IsFalse(PlayerInstructions.AppliesTo(PlayerDial.Width, "CB"), "CB는 커버 자리라 측면 쏠림 없음");
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.Width, "FB"));
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.PressRange, "CB"));
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.ShotBias, "CM"));
        Assert.IsTrue(PlayerInstructions.AppliesTo(PlayerDial.HoldUp, "ST"));
        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.Validate(Offsets(PlayerDial.ShotBias, 1), "CB"));
    }

    [Test]
    public void 옛_10칸_지시는_라인_높이_칸을_버리고_9칸이_된다()
    {
        int[] legacy = { 1, 2, -1, 2, -2, 1, 0, 1, -1, 2 };   // 3번 칸(라인 높이 +2)을 버린다
        CollectionAssert.AreEqual(new[] { 1, 2, -1, -2, 1, 0, 1, -1, 2 }, PlayerInstructions.FromLegacy(legacy));
        CollectionAssert.AreEqual(new int[PlayerInstructions.DialCount], PlayerInstructions.FromLegacy(null), "없으면 0");
        int[] current = Offsets(PlayerDial.HoldUp, 1);
        CollectionAssert.AreEqual(current, PlayerInstructions.FromLegacy(current), "이미 9칸이면 그대로");
        Assert.Throws<InvalidOperationException>(() => PlayerInstructions.FromLegacy(new int[4]));
    }
}
