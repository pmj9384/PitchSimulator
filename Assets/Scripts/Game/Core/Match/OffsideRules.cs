using System.Collections.Generic;

namespace Game.Core.Match
{
    // 오프사이드 "위치"(FIFA 규칙 11)를 순수 함수로. 1차 출시엔 휘슬(반칙 판정)이 없다(스펙 §5). 대신 트리가 오프사이드 위치의
    // 아군에겐 패스를 안 주고, 공격 자리도 온사이드 선 뒤로 잡는다(09-23). 그전엔 ST가 상대 수비 라인 뒤에 서서 앞 패스를 받아
    // 3분에 xG 0.3 이상 슛이 16~19회, 경기당 10골이 났다.
    // 온사이드 선 = 상대 중 골 쪽에서 2번째 선수(보통 GK 다음 최종 수비수), 공, 하프라인 중 가장 공격 쪽. 선과 나란히는 온사이드
    public static class OffsideRules
    {
        public const float NoLine = float.MaxValue;   // 상대가 2명 미만이면 오프사이드 없음(리트머스·소인원 테스트)

        // 공격 방향 좌표(forward = x × attackSign)로 돌려준다
        public static float OnsideLine(IReadOnlyList<TargetInfo> opponents, int attackSign, float ballX)
        {
            if (opponents.Count < 2) { return NoLine; }

            float first = float.MinValue;    // 가장 골 쪽
            float second = float.MinValue;
            for (int i = 0; i < opponents.Count; i++)
            {
                float f = opponents[i].X * attackSign;
                if (f > first) { second = first; first = f; }
                else if (f > second) { second = f; }
            }

            float line = second;
            float ballForward = ballX * attackSign;
            if (ballForward > line) { line = ballForward; }
            if (line < 0f) { line = 0f; }   // 자기 진영에선 오프사이드 없음
            return line;
        }

        public static bool IsOffsidePosition(float x, int attackSign, float onsideLine)
        {
            return x * attackSign > onsideLine;
        }

        // 자리 X를 온사이드 선에서 margin 뒤로 자른다. 선이 없으면 그대로
        public static float ClampOnside(float x, int attackSign, float onsideLine, float margin)
        {
            if (onsideLine == NoLine) { return x; }
            float forward = x * attackSign;
            float limit = onsideLine - margin;
            if (forward <= limit) { return x; }
            return limit * attackSign;
        }
    }
}
