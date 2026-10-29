using System;

namespace Game.Core.Match
{
    // 이동 애니의 재생 배율(10-29). 뷰(PlayerController)와 상태기 빌더(PlayerLocomotionBuilder)가 같은 값을 읽는다.
    // 유저 Play "가속과 조깅 차이가 안 느껴짐": 조깅 4.2m/s와 전력 7m/s가 달리기 클립 1배라 같아 보였다. 달리기 기준 속도를 넘으면 다리 주기를 속도에 비례시킨다.
    // 순수 계산이라 Game.Core에 두고 EditMode로 잠근다(10-29 리뷰). 상태 없음
    public static class LocomotionCadence
    {
        public const float WalkAnimSpeed = 1.5f;   // 블렌드 트리에서 걷기 클립이 100%인 속도(m/s). [가정] 사람 걷기 1.3~1.5m/s
        public const float RunAnimSpeed = 4f;      // 달리기 클립이 100%·1배 재생인 속도(m/s). 옛 걷기 → 달리기 문턱. [가정]
        public const float Max = 2f;               // 재생 배율 상한. GK 다이빙 버스트(8m/s)도 2배 [가정]

        public static float For(float speed)
        {
            if (speed <= RunAnimSpeed) { return 1f; }
            return Math.Min(speed / RunAnimSpeed, Max);
        }
    }
}
