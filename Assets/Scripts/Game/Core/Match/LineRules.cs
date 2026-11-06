using Game.Core.Placement;

namespace Game.Core.Match
{
    // 수비 줄 판정 순수 함수(11-06 수비 구조 D1, 실험/2026-11-06-설계-수비-구조.md 3-1-①).
    // 줄 판정의 깊이 = 편성 수비 시 자리(posX2 = PlayerState.DefendHomeX)만. 개인 라인 높이 다이얼(lineHeight × 0.3, DefendHome에서 얹힘)은 안 본다:
    // 다이얼 차는 최대 4.8m(PlayerTable lineHeight 10~26)로 밴드 6m보다 작아 넣어도 줄의 순서가 안 바뀌고, 슬롯이 정하는 "줄"이 선수 다이얼로 흔들리면 안 된다(10-27 슬롯 중심).
    // D2 라인 깊이의 RestDepth도 같은 정의(posX2 깊이 최솟값)를 쓰고 다이얼은 개인 오프셋으로 그 위에 얹는다.
    // 뒷줄 = 깊이가 팀 필드 선수 중 가장 깊은 값에서 BackLineBand 안인 선수. 둘째 줄 = 뒷줄이 아닌 선수 중 가장 깊은 값에서 같은 밴드 안.
    // 역할 이름(CB·FB)을 보지 않고 슬롯에서 파생한다(10-27 "역할은 자리에 고정"). 그래서 3-5-2 윙백(−31, 깊이 차 10m)은 뒷줄이 아니고 5-3-2 FB(−38, 4m)는 뒷줄이다.
    // 쓰는 곳: 리시버 마크(⑧b) 1차 후보(뒷줄)·2차 후보(둘째 줄 + marksAsSecond)·상한, 박스 앞 전진(옛 StepOutCenterBack). 라인 깊이(D2)·상태(D3)도 여기에 모인다
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

        // 뒷줄이 아니면서 "뒷줄이 아닌 선수 중 가장 깊은 자리"에서 BackLineBand 안이면 둘째 줄(뒷줄 바로 앞 한 줄). 4-2-3-1 DM(−29)은 둘째 줄, W·AM(−19)은 아니다.
        // 마크 2차 후보를 여기로 한정하는 이유: 공격수 슬롯에 tracking을 줘도 공격수가 내려와 마커가 되면 안 된다(11-07 리뷰 🟡)
        public static bool IsSecondLine(float homeDepth, float deepestHomeDepth, float secondLineDeepestDepth)
        {
            if (IsBackLine(homeDepth, deepestHomeDepth)) { return false; }
            return homeDepth <= secondLineDeepestDepth + MatchTuning.BackLineBand;
        }
    }
}
