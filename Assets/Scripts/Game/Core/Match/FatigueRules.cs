using System;

namespace Game.Core.Match
{
    // 체력(09-29 전술 상성 ⑥). 압박이 공짜라 압박 프리셋이 빌드업·균형을 90% 안팎으로 이겼다. 현실에서 압박의 대가 중 하나가 체력이다.
    // 레퍼런스: 경기 끝 15분 고강도 주행 -15%(포지션별 -8~-21%, UEFA CL 트래킹 PMC9598698), 가장 힘든 5분 직후 3~5분 안에 회복(J Sports Sci 2024),
    // 최고 속도 자체는 거의 안 떨어진다(줄어드는 건 고속 주행 거리·횟수). 구조는 RoboCup 2D 서버의 stamina → effort(실효 출력 배율).
    // 이 시뮬은 자리 이동도 늘 전력이라(09-28 대조: 스프린트 29% 대 현실 0.4%) 거리로 깎으면 모두가 똑같이 지친다. 그래서 행동으로 가른다:
    // 현실에서 전력인 행동(압박·루즈볼 추격·공 몰기·패스 마중)만 깎고 나머지 틱은 회복한다(조깅 = 회복). 전력 행동 중이어도 그 틱에 움직이지 않았으면
    // (볼 끌기 대기·킥·태클 실패 정지) 회복 쪽이다.
    // 두 층: 단기(행동마다 줄고 반감기로 회복)와 장기 상한(전력 행동이 쌓일수록 회복 상한이 내려간다, 전후반 차이). 상태 없음, 난수 없음
    public static class FatigueRules
    {
        // stamina 스탯 50이 기준(배율 1). 100이면 절반만 줄고 0이면 1.5배
        public static float DrainScale(int staminaStat)
        {
            return Math.Max(0.5f, 1.5f - staminaStat / 100f);
        }

        // 전력 행동 한 틱: 단기 체력이 줄고 장기 상한도 조금 내려간다
        public static (float stamina, float cap) Exert(float stamina, float cap, int staminaStat, float drainPerTick, float capLossPerTick, float capMin)
        {
            float scale = DrainScale(staminaStat);
            float nextCap = Math.Max(capMin, cap - capLossPerTick * scale);
            float next = Math.Max(0f, stamina - drainPerTick * scale);
            return (Math.Min(next, nextCap), nextCap);
        }

        // 그 밖의 한 틱: 상한을 향해 반감기(틱)로 다가간다
        public static float Recover(float stamina, float cap, float halfLifeTicks)
        {
            if (stamina >= cap) { return cap; }
            float keep = (float)Math.Pow(0.5, 1.0 / halfLifeTicks);
            return cap - (cap - stamina) * keep;
        }

        // 실효 속도 배율. 체력이 문턱 이상이면 1, 아래면 0에서 effortMin까지 선형으로 내려간다
        public static float Effort(float stamina, float threshold, float effortMin)
        {
            if (stamina >= threshold) { return 1f; }
            return effortMin + (1f - effortMin) * Math.Max(0f, stamina) / threshold;
        }
    }
}
