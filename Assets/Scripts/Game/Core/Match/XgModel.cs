using System;
using Game.Core.Placement;

namespace Game.Core.Match
{
    // 공개 xG 모델(09-16 유저 결정: 공개 값 그대로). 출처: Soccermatics "Fitting the xG model"
    // https://soccermatics.readthedocs.io/en/latest/gallery/lesson2/plot_xGModelFit.html
    // 데이터 Wyscout 2017/18 프리미어리그, statsmodels GLM Binomial. 피처 정의도 원문 코드 그대로:
    //   X = 골라인까지 거리(m), C = 중앙선에서 옆으로 벌어진 거리(m), Distance = √(X²+C²),
    //   Angle = arctan(7.32·X / (X²+C²-3.66²)) (음수면 +π), X2 = X², C2 = C², AX = Angle·X
    // 검산(원문 계수): 정면 11m → 0.181, 20m → 0.062, 30m → 0.025, X=11·C=10 → 0.093. 현실 참고값과 맞는다.
    // 2변수(거리·각도)만 있는 완결된 공개 세트가 없어 7변수를 그대로 쓴다(databallpy는 절편 미공개, TDS는 계수 이미지).
    public static class XgModel
    {
        private const float Intercept = 0.5103f;
        private const float AngleCoef = 0.6338f;
        private const float DistanceCoef = -0.2798f;
        private const float XCoef = 0.1243f;
        private const float CCoef = -0.0300f;
        private const float X2Coef = 0.0014f;
        private const float C2Coef = 0.0041f;
        private const float AXCoef = -0.1251f;

        // 로짓 = 골 확률의 log-odds. MatchRules가 능력치 보정을 더한 뒤 Logistic으로 되돌린다
        public static float Logit(float xToGoalLine, float cFromCenter)
        {
            float x = xToGoalLine;
            float c = cFromCenter;
            float distance = (float)Math.Sqrt(x * x + c * c);
            float angle = Angle(x, c);
            return Intercept
                + AngleCoef * angle
                + DistanceCoef * distance
                + XCoef * x
                + CCoef * c
                + X2Coef * x * x
                + C2Coef * c * c
                + AXCoef * angle * x;
        }

        // 골문이 보이는 각(라디안). 원문 공식. 골라인 위(x = 0)는 0
        public static float Angle(float xToGoalLine, float cFromCenter)
        {
            if (xToGoalLine <= 0f) { return 0f; }

            float half = FieldBounds.GoalHalfWidth;
            float denom = xToGoalLine * xToGoalLine + cFromCenter * cFromCenter - half * half;
            float a = (float)Math.Atan(2f * half * xToGoalLine / denom);
            return a > 0f ? a : a + (float)Math.PI;
        }
    }
}
