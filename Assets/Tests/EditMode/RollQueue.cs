using System;
using System.Collections.Generic;

// 테스트용 주사위 수열. 정한 값을 차례로 준다.
// 나머지 값을 안 주면 다 쓴 뒤 예외: 슛이 예상보다 많이 굴리면 테스트가 알아챈다. 주면 다 쓴 뒤 그 값을 계속 준다
// (11-09 B2: 조준이 주사위 2개라 상수 주사위로는 골문 밖을 못 만든다. 11-08: 소유 때 퍼스트 터치 주사위만 바꾸고 싶을 때)
public sealed class RollQueue
{
    private readonly Queue<float> rolls;
    private readonly float? rest;

    public RollQueue(IEnumerable<float> values)
    {
        rolls = new Queue<float>(values);
    }

    public RollQueue(IEnumerable<float> values, float restRoll)
    {
        rolls = new Queue<float>(values);
        rest = restRoll;
    }

    public int Remaining => rolls.Count;

    public float Next()
    {
        if (rolls.Count > 0) { return rolls.Dequeue(); }
        if (!rest.HasValue) { throw new InvalidOperationException("정한 주사위를 다 썼다"); }
        return rest.Value;
    }
}
