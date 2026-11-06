using Game.Core.Placement;

namespace Game.Core.Match
{
    // 수비 줄 판정 순수 함수(11-06 수비 구조 D1, 실험/2026-11-06-설계-수비-구조.md 3-1-①).
    // 뒷줄 = 수비 시 자리(편성 posX2 + 다이얼)의 깊이가 팀 필드 선수 중 가장 깊은 값에서 BackLineBand 안인 선수.
    // 역할 이름(CB·FB)을 보지 않고 슬롯에서 파생한다(10-27 "역할은 자리에 고정"). 그래서 3-5-2 윙백(−31, 깊이 차 10m)은 뒷줄이 아니고 5-3-2 FB(−38, 4m)는 뒷줄이다.
    // 쓰는 곳: 리시버 마크(⑧b) 1차 후보·상한, 박스 앞 전진(옛 StepOutCenterBack). 라인 깊이(D2)·상태(D3)도 여기에 모인다
    public static class LineRules
    {
        // 수비 시 자리의 우리 골라인에서 깊이(m). attackSign은 그 선수 팀의 공격 방향이라 우리 골라인은 x = −attackSign × HalfLength
        public static float HomeDepth(float defendHomeX, int attackSign)
        {
            return FieldBounds.HalfLength + defendHomeX * attackSign;
        }

        // 가장 깊은 수비 자리에서 BackLineBand 안이면 뒷줄. 경계 포함
        public static bool IsBackLine(float homeDepth, float deepestHomeDepth)
        {
            return homeDepth <= deepestHomeDepth + MatchTuning.BackLineBand;
        }
    }
}
