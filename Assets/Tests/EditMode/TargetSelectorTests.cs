using System.Collections.Generic;
using Game.Core.Match;
using NUnit.Framework;

// TargetSelector 검증. "최근접 하나 + 결정적 동률 처리"가 이 게임의 재현성(고정 스텝·멀티 전제)을 지킨다.
// 공 소유 판정(스펙 §5)이 이 함수 하나에 걸려 있다.
public class TargetSelectorTests
{
    [Test]
    public void 가장_가까운_후보의_스폰인덱스를_돌려준다()
    {
        var candidates = new List<TargetInfo>
        {
            new TargetInfo(spawnIndex: 10, x: 5f, z: 0f),    // 거리 5
            new TargetInfo(spawnIndex: 11, x: 3f, z: 0f),    // 거리 3 ← 최근접
            new TargetInfo(spawnIndex: 12, x: 0f, z: 4f),    // 거리 4
        };

        Assert.AreEqual(11, TargetSelector.SelectNearest(0f, 0f, candidates));
    }

    [Test]
    public void 거리가_같으면_스폰인덱스_작은_쪽이다()
    {
        // 일부러 큰 인덱스를 목록 앞에 둔다. "먼저 온 쪽"이 아니라 "인덱스 작은 쪽"임을 증명
        var candidates = new List<TargetInfo>
        {
            new TargetInfo(spawnIndex: 7, x: 3f, z: 0f),   // 거리 3
            new TargetInfo(spawnIndex: 2, x: 0f, z: 3f),   // 거리 3 (동률)
        };

        Assert.AreEqual(2, TargetSelector.SelectNearest(0f, 0f, candidates));

        // 목록 순서를 뒤집어도 같은 답. 실행마다 결과가 같아야 하는 재현성의 핵심
        candidates.Reverse();
        Assert.AreEqual(2, TargetSelector.SelectNearest(0f, 0f, candidates));
    }

    [Test]
    public void 후보가_없으면_마이너스1이다()
    {
        Assert.AreEqual(-1, TargetSelector.SelectNearest(0f, 0f, new List<TargetInfo>()));
        Assert.AreEqual(-1, TargetSelector.SelectNearest(0f, 0f, null));
    }

    [Test]
    public void 후보가_하나면_그_선수다()
    {
        var one = new List<TargetInfo> { new TargetInfo(spawnIndex: 42, x: 100f, z: 100f) };
        Assert.AreEqual(42, TargetSelector.SelectNearest(0f, 0f, one), "아무리 멀어도 후보가 하나면 그 선수. 거리 제한은 여기 몫이 아님");
    }
}
