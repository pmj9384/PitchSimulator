using System;

namespace Game.Core.Match
{
    // 슛 결정 시간 순수 함수(11-08 S3 1단계). 받자마자 차던 슛(받고 0.1초 안 79%, 수비가 반응할 틈이 없음)을 실제 분포로 바꾼다.
    // 소유를 얻을 때 한 번: 퍼스트 터치로 바로 찰 수 있나(거리별 확률) → 아니면 준비 시간 동안 슛을 판정에서 뺀다.
    // 근거 = StatsBomb PL 15/16 직접 계산(MatchTuning 슛 결정 시간 절). 주사위는 시뮬이 굴리고 여기는 확률·틱만 계산한다. 상태 없음, 엔진 없음
    public static class ShotDecisionRules
    {
        // 골 중심까지 거리에 해당하는 퍼스트 터치 슛 확률 × 배율. 구간 상한은 미포함(6m 정확히 = 6~11 구간).
        // edges는 구간 상한(오름차순), rates는 구간마다 비율이라 rates.Length = edges.Length + 1. 시뮬은 MatchTuning 표를 넘긴다
        public static float FirstTouchChance(float distanceToGoal, float[] edges, float[] rates, float scale)
        {
            int band = edges.Length;
            for (int i = 0; i < edges.Length; i++)
            {
                if (distanceToGoal < edges[i])
                {
                    band = i;
                    break;
                }
            }
            return Clamp01(rates[band] * scale);
        }

        // 준비 시간(초) → 틱. 볼 끌기와 같은 반올림
        public static int PrepTicks(float prepSeconds)
        {
            return (int)Math.Round(prepSeconds / MatchTuning.FixedStep);
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) { return 0f; }
            if (v > 1f) { return 1f; }
            return v;
        }
    }
}
