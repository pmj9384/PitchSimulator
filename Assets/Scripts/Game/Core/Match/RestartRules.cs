using System;
using Game.Core.Placement;

namespace Game.Core.Match
{
    // 재개 판정(누가·어디서·얼마나 멀리)을 순수 함수로(11-17 v1 기능 1 스로인). 실행(공 놓기·잡기 거르기·대기)은 MatchSimulation이 한다.
    // IFAB 15조: 공이 터치라인을 넘은 곳에서, 마지막에 닿은 선수의 상대 팀이 던진다. 상대는 던지는 자리에서 2m 떨어진다.
    // 코너킥(v1 기능 2)도 같은 "마지막 터치 팀"을 읽으므로 골라인 분기는 그때 여기에 붙인다
    public static class RestartRules
    {
        public const int NoTeam = -1;

        // 재개 팀 = 마지막에 닿은 팀의 상대. 마지막 터치를 모르면(킥오프 직후 아무도 안 닿은 공, 테스트가 직접 만든 공) NoTeam이고, 시뮬은 예전처럼 누구나 줍는 자유 공으로 둔다
        public static int ThrowInTeam(int lastTouchTeam)
        {
            if (lastTouchTeam < 0) { return NoTeam; }
            return 1 - lastTouchTeam;
        }

        // 던지는 자리 = 나간 x, 터치라인 안쪽 inset. 코너 근처에서 나간 공도 골라인 안쪽 inset까지만(필드 밖에 서서 던지지 않게)
        public static (float x, float z) ThrowInSpot(float outX, float outZ, float inset)
        {
            float maxX = FieldBounds.HalfLength - inset;
            float x = Math.Max(-maxX, Math.Min(maxX, outX));
            float sign = outZ < 0f ? -1f : 1f;
            return (x, sign * (FieldBounds.HalfWidth - inset));
        }

        // 스로인 후보 거리. 받는 선수의 지금 자리까지로 잰다(착지점 리드는 안 본다. 설계 "후보 거리 25m")
        public static bool IsWithinThrow(float fromX, float fromZ, float toX, float toZ, float maxDistance)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return dx * dx + dz * dz <= maxDistance * maxDistance;
        }

        // 자리에서 minDistance 안의 선수를 원 둘레로 민다. 자리 위에 정확히 서 있으면 필드 안쪽(터치라인 반대)으로. 밖의 선수는 입력을 그대로 돌려준다(시뮬이 같은 값인지로 밀었는지를 본다)
        public static (float x, float z) KeepAway(float px, float pz, float spotX, float spotZ, float minDistance)
        {
            float dx = px - spotX;
            float dz = pz - spotZ;
            float d2 = dx * dx + dz * dz;
            if (d2 >= minDistance * minDistance) { return (px, pz); }

            if (d2 == 0f)
            {
                float inward = spotZ > 0f ? -1f : 1f;
                return (spotX, spotZ + inward * minDistance);
            }
            float d = (float)Math.Sqrt(d2);
            return (spotX + dx / d * minDistance, spotZ + dz / d * minDistance);
        }
    }
}
