using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.League;
using Game.Core.Tactics;
using NUnit.Framework;

// 팀 전술 설정창(09-30, 스펙 §4-2)의 순수 판정: 카드 → "앞으로" 슬라이더(카드 기준 오프셋) → 국면 탭의 칸. 속도·압박 슬라이더는 10-29에 지웠다(칸마다 좌우 조절이 생겨 중복).
// 기준 카드 balanced(TacticPresets.csv): 패스 방식 0/1/1 · 리스크 0/1/2 · 속도 0/1/2 · 폭 2/1/1 · 압박 시작 2/1/0 · 역습 1 · 역압박 1 · GK 1 · 전진 1 · 수비 라인 1
public class TacticsEditingTests
{
    private static List<TeamTactics> presets;

    [OneTimeSetUp]
    public void Load()
    {
        presets = TeamTacticsParser.Parse(File.ReadAllText("Assets/Resources/Tables/TacticPresets.csv"));
    }

    private static TeamTactics Card(string presetId)
    {
        return presets.Find(p => p.PresetId == presetId);
    }

    [Test]
    public void 슬라이더_앞으로는_전진_수비_라인_폭_패스_리스크를_함께_옮기고_0에서_자른다()
    {
        TeamTactics card = Card("balanced");
        TeamTactics t = TacticsEditing.ApplyForward(card, card, -1);
        Assert.AreEqual(0, t.Mentality);
        Assert.AreEqual(0, t.DefenseLine, "수비 라인도 같이 내려간다(10-27, FM 멘탈리티)");
        Assert.AreEqual(2, TacticsEditing.ApplyForward(card, card, 1).DefenseLine);
        Assert.AreEqual(0, TacticsEditing.ApplyForward(Card("counter"), Card("counter"), -1).DefenseLine, "역습 카드는 이미 깊게라 0에서 자른다");
        CollectionAssert.AreEqual(new[] { 1, 0, 0 }, t.Width, "폭 2/1/1 − 1");
        CollectionAssert.AreEqual(new[] { 0, 0, 1 }, t.PassRisk, "리스크 0/1/2 − 1, 0 아래는 0");
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, t.Tempo, "속도는 안 움직인다");
        CollectionAssert.AreEqual(new[] { 2, 1, 0 }, t.PressStart, "압박은 안 움직인다");
    }

    [Test]
    public void 슬라이더는_한_칸이_한_단이고_카드_값이_끝이면_실제로_바뀌는_칸까지만_센다()
    {
        // 10-13: ±2였을 때 카드 값 1에서 "더"와 "훨씬 더"가 같은 값이라 유저가 "분배가 안 된다"고 느꼈다. ±1로 줄이고 포화는 EffectiveForwardOffset이 알린다
        Assert.AreEqual(1, TacticsEditing.SliderMax);
        TeamTactics balanced = Card("balanced");   // 전진 1 · 수비 라인 1 · 폭 2/1/1 · 리스크 0/1/2
        Assert.AreEqual(1, TacticsEditing.EffectiveForwardOffset(balanced, 1), "전진 1 → 2로 바뀐다");
        Assert.AreEqual(-1, TacticsEditing.EffectiveForwardOffset(balanced, -1));
        var all = TacticsEditing.Copy(balanced);
        all.Mentality = 2;
        all.DefenseLine = 2;
        all.Width = new[] { 2, 2, 2 };
        all.PassRisk = new[] { 2, 2, 2 };
        Assert.AreEqual(0, TacticsEditing.EffectiveForwardOffset(all, 1), "맡은 칸이 전부 2면 +1은 아무것도 안 바꾼다 → 0");
        Assert.AreEqual(-1, TacticsEditing.EffectiveForwardOffset(all, -1));
        Assert.AreEqual(0, TacticsEditing.EffectiveForwardOffset(balanced, 0));
    }

    [Test]
    public void 슬라이더는_자기_담당_칸만_다시_쓰고_표에서_바꾼_다른_칸은_지킨다()
    {
        // 09-30 재검증: 카드를 통째로 복사해 다시 계산하면 슬라이더와 상관없는 패스 방식·전환과 다른 슬라이더의 칸까지 카드 값으로 돌아갔다
        TeamTactics card = Card("balanced");
        TeamTactics current = TacticsEditing.Copy(card);
        current.PassStyle[1] = 2;   // 탭: 중원 롱볼(슬라이더가 안 맡는 칸)
        current.Counter = 2;        // 탭: 역습 적극
        current.Tempo[0] = 2;       // 탭: 우리 진영 속도 빠르게(슬라이더가 안 맡는 칸)
        current.Width[0] = 0;       // 탭: 우리 진영 폭 좁게("앞으로"가 맡는 칸)

        TeamTactics t = TacticsEditing.ApplyForward(current, card, 1);
        Assert.AreEqual(2, t.PassStyle[1], "패스 방식은 그대로");
        Assert.AreEqual(2, t.Counter, "전환은 그대로");
        Assert.AreEqual(2, t.Tempo[0], "속도도 그대로(10-29부터 슬라이더가 안 맡는다)");
        CollectionAssert.AreEqual(new[] { 2, 2, 2 }, t.Width, "자기 칸은 카드 2/1/1 + 1로 다시 쓴다(탭에서 바꾼 값을 덮는다)");
    }

    [Test]
    public void 슬라이더는_원본을_바꾸지_않고_범위_밖_값은_거부한다()
    {
        TeamTactics card = Card("balanced");
        TeamTactics current = TacticsEditing.Copy(card);
        TacticsEditing.ApplyForward(current, card, 1);
        CollectionAssert.AreEqual(new[] { 2, 1, 1 }, card.Width, "카드는 그대로");
        CollectionAssert.AreEqual(new[] { 2, 1, 1 }, current.Width, "지금 값 원본도 그대로");
        Assert.Throws<ArgumentOutOfRangeException>(() => TacticsEditing.ApplyForward(current, card, 2));
    }

    [Test]
    public void 카드_값_그대로면_그_카드로_알아보고_한_칸만_달라도_사용자_설정이다()
    {
        TeamTactics copy = TacticsEditing.Copy(Card("pressing"));
        Assert.AreEqual("pressing", TacticsEditing.MatchingPreset(copy, presets));
        copy.Tempo[1] = copy.Tempo[1] == 0 ? 1 : 0;
        Assert.IsNull(TacticsEditing.MatchingPreset(copy, presets));
    }

    [Test]
    public void 범위_밖_값이나_서드가_3칸이_아니면_검증에서_던진다()
    {
        TeamTactics bad = TacticsEditing.Copy(Card("balanced"));
        bad.PassStyle[0] = 3;
        Assert.Throws<InvalidOperationException>(() => TacticsEditing.Validate(bad));

        TeamTactics shortArray = TacticsEditing.Copy(Card("balanced"));
        shortArray.Width = new int[2];
        Assert.Throws<InvalidOperationException>(() => TacticsEditing.Validate(shortArray));

        TeamTactics badCounter = TacticsEditing.Copy(Card("balanced"));
        badCounter.Counter = -1;
        Assert.Throws<InvalidOperationException>(() => TacticsEditing.Validate(badCounter));
    }

    [Test]
    public void 복사본을_고쳐도_원본은_그대로다()
    {
        TeamTactics card = Card("counter");
        TeamTactics copy = TacticsEditing.Copy(card);
        copy.PassStyle[0] = card.PassStyle[0] == 0 ? 1 : 0;
        Assert.AreNotEqual(copy.PassStyle[0], card.PassStyle[0]);
        Assert.AreEqual(card.PresetId, copy.PresetId);
    }

    [Test]
    public void 수비_라인은_같음_검증_복사_세이브에_들어가고_세이브_DTO_초기값은_표준이다()
    {
        TeamTactics a = Card("balanced");
        TeamTactics b = TacticsEditing.Copy(a);
        Assert.AreEqual(a.DefenseLine, b.DefenseLine, "복사");
        b.DefenseLine = 2;
        Assert.IsFalse(TacticsEditing.SameValues(a, b), "수비 라인만 달라도 다른 값");
        b.DefenseLine = 3;
        Assert.Throws<InvalidOperationException>(() => TacticsEditing.Validate(b));

        var saved = SeasonSave.TacticsValues.From(a);
        Assert.AreEqual(1, saved.defenseLine);
        var old = new SeasonSave.TacticsValues { passStyle = (int[])a.PassStyle.Clone(), passRisk = (int[])a.PassRisk.Clone(), tempo = (int[])a.Tempo.Clone(), width = (int[])a.Width.Clone(), pressStart = (int[])a.PressStart.Clone() };
        Assert.AreEqual(1, old.ToTactics("balanced").DefenseLine, "DTO 초기값이 표준. 옛 세이브(필드 없음)는 Json.NET이 기본 생성자 뒤 있는 필드만 채우므로 같은 값(SaveLoadSystem 설정은 Formatting뿐). Json 경로 자체는 테스트 어셈블리가 Newtonsoft를 참조하지 않아 여기선 안 거친다");
    }
}
