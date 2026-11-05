using System;
using System.Collections.Generic;
using Game.Core.Match;
using Game.Core.Placement;
using NUnit.Framework;

// 경기장 표시(11-05): 화면에 그리는 선이 시뮬이 판정하는 박스·골대와 같은 자리에 놓이는지(IFAB 1조 규격)
public class PitchLayoutTests
{
    private static bool HasVerticalLine(List<PitchLine> lines, float x, float halfWidth)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            PitchLine l = lines[i];
            if (Math.Abs(l.FromX - x) > 1e-4f || Math.Abs(l.ToX - x) > 1e-4f) { continue; }
            if (Math.Abs(Math.Abs(l.FromZ - l.ToZ) - 2f * halfWidth) < 1e-4f) { return true; }
        }
        return false;
    }

    [Test]
    public void 페널티_박스_앞_선은_시뮬의_박스_판정_경계와_같은_자리다()
    {
        List<PitchLine> lines = PitchLayout.Lines();
        float frontX = FieldBounds.HalfLength - FieldBounds.PenaltyBoxDepth;
        Assert.IsTrue(HasVerticalLine(lines, frontX, FieldBounds.PenaltyBoxHalfWidth), "오른쪽 박스 앞 선 x = 36");
        Assert.IsTrue(HasVerticalLine(lines, -frontX, FieldBounds.PenaltyBoxHalfWidth), "왼쪽 박스 앞 선 x = −36");

        // 선 바로 안쪽은 박스 안, 바로 바깥은 박스 밖으로 판정된다(팀 1이 +X 골을 지킨다: AttackSign −1)
        Assert.IsTrue(MatchRules.IsInOwnPenaltyBox(frontX + 0.1f, 0f, -1));
        Assert.IsFalse(MatchRules.IsInOwnPenaltyBox(frontX - 0.1f, 0f, -1));
    }

    [Test]
    public void 골_에어리어는_골라인에서_5점5m_폭_18점32m다()
    {
        List<PitchLine> lines = PitchLayout.Lines();
        float frontX = FieldBounds.HalfLength - FieldBounds.GoalAreaDepth;
        Assert.IsTrue(HasVerticalLine(lines, frontX, FieldBounds.GoalAreaHalfWidth));
        Assert.IsTrue(HasVerticalLine(lines, -frontX, FieldBounds.GoalAreaHalfWidth));
        Assert.IsTrue(HasVerticalLine(lines, 0f, FieldBounds.HalfWidth), "하프라인");
    }

    [Test]
    public void 페널티_아크는_마크에서_9점15m이고_박스_밖에만_있다()
    {
        for (int side = -1; side <= 1; side += 2)
        {
            float spotX = side * (FieldBounds.HalfLength - FieldBounds.PenaltySpotDistance);
            float frontX = side * (FieldBounds.HalfLength - FieldBounds.PenaltyBoxDepth);
            int arcPoints = 0;
            List<PitchLine> lines = PitchLayout.Lines();
            for (int i = 0; i < lines.Count; i++)
            {
                PitchLine l = lines[i];
                float d = (float)Math.Sqrt((l.FromX - spotX) * (l.FromX - spotX) + l.FromZ * l.FromZ);
                if (Math.Abs(d - FieldBounds.PenaltyArcRadius) > 1e-3f) { continue; }
                arcPoints++;
                Assert.LessOrEqual(l.FromX * side, frontX * side + 1e-3f, $"아크는 박스 앞 선 바깥(중앙 쪽) side {side}");
            }
            Assert.Greater(arcPoints, 3, $"아크가 그려진다 side {side}");
        }
    }

    [Test]
    public void 센터서클은_반지름_9점15m_한_바퀴이고_골대_포스트는_골라인_위_7점32m_간격이다()
    {
        int circle = 0;
        List<PitchLine> lines = PitchLayout.Lines();
        for (int i = 0; i < lines.Count; i++)
        {
            float d = (float)Math.Sqrt(lines[i].FromX * lines[i].FromX + lines[i].FromZ * lines[i].FromZ);
            if (Math.Abs(d - FieldBounds.CenterCircleRadius) < 1e-3f) { circle++; }
        }
        Assert.AreEqual(PitchLayout.CircleSegments, circle);

        List<(float x, float z)> posts = PitchLayout.GoalPosts();
        Assert.AreEqual(4, posts.Count);
        Assert.AreEqual(2f * FieldBounds.GoalHalfWidth, posts[1].z - posts[0].z - FieldBounds.GoalPostDiameter, 1e-4f, "포스트 안쪽 면 사이 7.32m");
        Assert.AreEqual(-FieldBounds.HalfLength, posts[0].x, 1e-4f);
        Assert.AreEqual(3, PitchLayout.Spots().Count, "센터 마크 + 페널티 마크 2");
    }
}
