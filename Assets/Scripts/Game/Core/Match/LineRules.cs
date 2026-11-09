using System;
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
        // x의 우리 골라인에서 깊이(m). 수비 시 자리(DefendHomeX)와 공 x(박스 앞 전진)가 같은 식을 쓴다.
        // attackSign은 그 팀의 공격 방향이라 우리 골라인은 x = −attackSign × HalfLength
        public static float HomeDepth(float x, int attackSign)
        {
            return FieldBounds.HalfLength + x * attackSign;
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

        // 깊이(우리 골라인에서 m)를 x로 되돌린다. HomeDepth의 역함수
        public static float XAtDepth(float depth, int attackSign)
        {
            return (depth - FieldBounds.HalfLength) * attackSign;
        }

        // ── 라인 깊이(11-08 D2, 설계-수비-구조.md 8절). 팀 뒷줄이 서는 깊이 L을 공 깊이로 정하고, 모든 필드 선수가 같은 Δ만큼 평행이동한다.
        // 옛 모양(편성 자리 + 공 x × 0.3 세로 슬라이드, 상한 10m + 계단 하한 10/7.5m)은 공이 우리 서드에 있으면 CB를 GK 옆까지 끌어내렸다(11-05·11-06 유저 Play)

        // 기본 라인 깊이 L₀ = Metrica 트래킹 앵커 3점(공 깊이 17.5·52.5·87.5 → 라인 15·32·47m) 사이 보간. 앵커 밖은 끝 구간 기울기로 늘린다
        public static float RestLineDepth(float ballDepth)
        {
            float[] ballAnchors = MatchTuning.LineAnchorBallDepth;
            float[] lineAnchors = MatchTuning.LineAnchorDepth;
            int segment = 0;
            while (segment < ballAnchors.Length - 2 && ballDepth > ballAnchors[segment + 1])
            {
                segment++;
            }
            float t = (ballDepth - ballAnchors[segment]) / (ballAnchors[segment + 1] - ballAnchors[segment]);
            return lineAnchors[segment] + (lineAnchors[segment + 1] - lineAnchors[segment]) * t;
        }

        // 하한 = GK 라인 깊이 + FloorMargin. 뒷줄이 GK와 한 줄에 서지 않게 한다
        public static float LineFloor(float keeperLineDepth)
        {
            return keeperLineDepth + MatchTuning.FloorMargin;
        }

        // L = L₀ + 팀 수비 라인 오프셋(깊게·표준·높게), 하한과 상한(LineCap) 사이로 자른다
        public static float LineDepth(float ballDepth, int defenseLine, float keeperLineDepth)
        {
            float depth = RestLineDepth(ballDepth) + MatchTuning.DefenseLineOffset[defenseLine];
            float floor = LineFloor(keeperLineDepth);
            if (depth < floor) { return floor; }
            if (depth > MatchTuning.LineCap) { return MatchTuning.LineCap; }
            return depth;
        }

        // 팀 이동량 Δ = L − 편성 뒷줄 깊이(restDepth = 필드 선수 수비 시 자리 깊이 최솟값). 시뮬(팀마다 틱 1회)과 전술판 미니 피치가 이 값을 수비 자리에 더한다
        public static float LineShift(float lineDepth, float restDepth)
        {
            return lineDepth - restDepth;
        }

        // 필드 선수 수비 자리 깊이의 상한 = 공 깊이 − GoalSideMargin(수비는 공보다 골 쪽, 11-09). 하한(GK 라인 + 4m)이 우선한다: 공이 하한보다 깊으면 하한에 선다
        public static float StandDepthCap(float ballDepth, float floorDepth)
        {
            return Math.Max(ballDepth - MatchTuning.GoalSideMargin, floorDepth);
        }

        // 마크 지점이 내려갈 수 있는 가장 깊은 곳 = max(하한, min(L, 공 깊이)). 마커는 라인 뒤로 안 빠지고, 공이 라인보다 깊을 때만 공 깊이까지 따라간다
        public static float MarkFloorDepth(float lineDepth, float ballDepth, float floorDepth)
        {
            float depth = Math.Min(lineDepth, ballDepth);
            return Math.Max(floorDepth, depth);
        }

        // x가 floorDepth보다 우리 골 쪽이면 그 깊이로 올린다
        public static float AboveDepth(float x, float floorDepth, int attackSign)
        {
            if (HomeDepth(x, attackSign) >= floorDepth) { return x; }
            return XAtDepth(floorDepth, attackSign);
        }
    }
}
