using System;

namespace Game.Core.Match
{
    // 이동 애니의 재생 배율(10-29). 뷰(PlayerController)와 상태기 빌더(PlayerLocomotionBuilder)가 같은 값을 읽는다.
    // 유저 Play "가속과 조깅 차이가 안 느껴짐": 조깅 4.2m/s와 전력 7m/s가 달리기 클립 1배라 같아 보였다. 달리기 기준 속도를 넘으면 다리 주기를 속도에 비례시킨다.
    // 순수 계산이라 Game.Core에 두고 EditMode로 잠근다(10-29 리뷰). 상태 없음.
    // 11-14 모델 교체 2단계(결정 B11): 클립이 UAL 걷기·조깅·전력 3개가 되어 기준 속도를 클립의 자연 속도로 바꿨다.
    // 값 = 루트 모션판(UAL1_Standard_RM.fbx, 루트 회전 Body Orientation·XZ Center of Mass) 클립의 AnimationClip.averageSpeed 앞 방향 성분. 옆 성분 0.07 이하·위 성분 0 [측정]. 축 변환 굽기(Bake Axis Conversion)는 UBC·UAL 둘 다 끈다(0단계와 같음. 켜면 몸이 누웠다)
    public static class LocomotionCadence
    {
        public const float WalkAnimSpeed = 0.95f;    // 걷기 클립(Walk_Loop)이 100%인 속도(m/s). 측정 0.955
        public const float JogAnimSpeed = 5.2f;      // 조깅 클립(Jog_Fwd_Loop)이 100%인 속도(m/s). 측정 5.161
        public const float SprintAnimSpeed = 8f;     // 전력 클립(Sprint_Loop)이 100%·1배 재생인 속도(m/s). 측정 8.013. 이보다 빠르면 다리 주기를 속도에 비례시킨다(선수 최고 속도가 이보다 낮아 지금은 거의 안 쓰인다)
        public const float Max = 2f;                 // 재생 배율 상한 [가정]

        public static float For(float speed)
        {
            if (speed <= SprintAnimSpeed) { return 1f; }
            return Math.Min(speed / SprintAnimSpeed, Max);
        }
    }
}
