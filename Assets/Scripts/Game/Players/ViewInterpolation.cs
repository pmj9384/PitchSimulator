using UnityEngine;

// 고정 스텝(50Hz) 시뮬 값을 화면 프레임(60~120Hz)에 맞춰 보간하는 계수. Unity 매뉴얼 "Fixed Timestep" 권장 방식:
// 이번 프레임이 마지막 고정 스텝에서 얼마나 지났나(0~1). 뷰는 FixedUpdate 뒤에 이전·현재 값을 받아 두고 Update에서 이 계수로 섞는다.
// 09-26 Play: 위치를 FixedUpdate에서 바로 대입하니 20ms 계단으로 튀고 공이 안 굴러 보였다
public static class ViewInterpolation
{
    public static float Alpha()
    {
        float step = Time.fixedDeltaTime;
        if (step <= 0f) { return 1f; }
        return Mathf.Clamp01((Time.time - Time.fixedTime) / step);
    }
}
