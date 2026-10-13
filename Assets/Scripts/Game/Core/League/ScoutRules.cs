using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Tactics;

namespace Game.Core.League
{
    // 스카우트 한 번의 결과. 미리보기(Roll 전)는 Roll 0·Success false
    public readonly struct ScoutVerdict
    {
        public readonly int Stars;
        public readonly float Chance;
        public readonly float Roll;
        public readonly bool Success;

        public ScoutVerdict(int stars, float chance, float roll, bool success)
        {
            Stars = stars;
            Chance = chance;
            Roll = roll;
            Success = success;
        }
    }

    // 스카우트 판정 순수 함수(스펙 §4-5, 10-12 구현 설계. 레퍼런스 프로야구 스피리츠 A의 "확률 공개·누적 보정").
    // 상태·엔진·난수 없음: 별(난이도)·성공 확률·주사위 값만 계산한다. 영입(교체)은 SeasonState, 화면은 UI 몫
    public static class ScoutRules
    {
        public const int MinStars = 1;
        public const int MaxStars = 5;
        public const int AttemptsPerSeason = 2;        // [가정] 시즌당 기본 시도
        public const int ExtraAttemptCoinPrice = 100;  // [가정] 코인으로 시도 +1
        public const int WinCoins = 30;                // [가정] 경기 결과 코인 수입(코인 용처가 시도 횟수뿐이라 수입도 여기 둔다)
        public const int DrawCoins = 10;
        public const int LossCoins = 0;

        public const float TopHalfBonus = 0.1f;        // [가정] 내 순위가 상위 절반이면 +, 하위면 −
        public const float UnmetFactor = 0.5f;         // [가정] 상대해 보지 않은 팀은 절반
        public const float RetryBonus = 0.1f;          // [가정] 같은 시즌 같은 선수 재시도마다 +
        public const float MinChance = 0.05f;
        public const float MaxChance = 0.95f;
        public const float BigDialDistanceCells = 6f;  // [가정] 기본 변형에서 다이얼이 이만큼(칸 합) 벗어나면 ★3

        // [가정] 별 등급별 기본 확률(★1~★5). 출처 없음(프로스피A 공개 확률은 최저 등급 4%부터지만 그 게임은 구매 누적으로 오르는 구조)
        private static readonly float[] BaseChance = { 0.8f, 0.6f, 0.4f, 0.25f, 0.15f };

        // 별 = 조합의 특이성 + 부 차이(스펙 §4-5). 생성기가 노출 변형만 뽑아(10-08) 변형 희귀도로는 안 갈린다:
        // 기본 변형이 아닌가(★2, 다이얼 거리가 크면 ★3) / 자리 2쌍이 실제로 다른가(★3) / 빌드 재분배가 적용됐나(★4) → 최댓값, 둘 이상 겹치면 +1, 상위 부면 +1, 상한 5
        // 상위 부 +1은 지금 앱에선 안 일어난다: 스카우트 풀이 내 부의 상대 팀뿐이라 SeasonSystem이 늘 같은 부를 넘긴다(10-12 리뷰). 다른 부 풀을 열 때를 위해 규칙만 둔다
        public static int Stars(PlayerStats target, StageEntry targetRow, TierRule targetTier, int myTier, IReadOnlyList<PlayerStats> table)
        {
            int stars = MinStars;
            int traits = 0;

            PlayerStats baseline = TeamGenerator.DefaultVariant(table, target.RoleId);
            if (!string.Equals(baseline.VariantId, target.VariantId, StringComparison.OrdinalIgnoreCase))
            {
                traits++;
                stars = Math.Max(stars, DialDistanceCells(target, baseline) >= BigDialDistanceCells ? 3 : 2);
            }
            if (targetTier.DualPositions && HasDistinctDefendPosition(targetRow))
            {
                traits++;
                stars = Math.Max(stars, 3);
            }
            if (targetTier.Rebuild && BuildScaler.RebuildApplies(target.RoleId))
            {
                traits++;
                stars = Math.Max(stars, 4);
            }
            if (traits >= 2) { stars++; }
            if (targetTier.Tier < myTier) { stars++; }   // 상위 부(번호가 작을수록 위)
            return ClampStars(stars);
        }

        // 이 부의 상대가 전부 ★1인가: 변형 변주·자리 2쌍·재분배가 다 꺼진 부(지금은 4부)는 Stars의 세 항목이 전부 안 걸린다. 화면 안내가 부 번호 대신 이걸 묻는다(10-12 리뷰)
        public static bool AllStarsAreOne(TierRule tier)
        {
            if (tier.DialVariance > 0) { return false; }
            if (tier.DualPositions) { return false; }
            return !tier.Rebuild;
        }

        // 기본 변형과의 다이얼 거리를 개인 지시 칸 단위로 합산한다(단위가 다른 다이얼을 같은 자로 재기 위해 PlayerInstructions.Step으로 나눈다)
        public static float DialDistanceCells(PlayerStats a, PlayerStats b)
        {
            float cells = 0f;
            for (int i = 0; i < PlayerInstructions.DialCount; i++)
            {
                var dial = (PlayerDial)i;
                float step = Math.Abs(PlayerInstructions.StepOf(dial));
                if (step <= 0f) { continue; }
                cells += Math.Abs(PlayerInstructions.ValueOf(a, dial) - PlayerInstructions.ValueOf(b, dial)) / step;
            }
            return cells;
        }

        public static bool HasDistinctDefendPosition(StageEntry row)
        {
            return row.PosX != row.PosX2 || row.PosZ != row.PosZ2;
        }

        // 성공 확률: 별 기준값 × 내 팀 위상 × 상대해 봤는지 + 재시도 누적. myRank는 0부터(0 = 1위)
        public static float SuccessChance(int stars, int myRank, int teamCount, bool metBefore, int retries)
        {
            int index = ClampStars(stars) - 1;
            float chance = BaseChance[index];
            chance += myRank < teamCount / 2 ? TopHalfBonus : -TopHalfBonus;
            if (!metBefore) { chance *= UnmetFactor; }
            chance += RetryBonus * Math.Max(0, retries);
            return Math.Max(MinChance, Math.Min(MaxChance, chance));
        }

        // 그 팀과 정규 시즌에서 붙어 봤는가
        public static bool MetBefore(IReadOnlyList<MatchResult> results, int myTeamId, int teamId)
        {
            for (int i = 0; i < results.Count; i++)
            {
                MatchResult r = results[i];
                bool mine = r.HomeTeamId == myTeamId || r.AwayTeamId == myTeamId;
                bool theirs = r.HomeTeamId == teamId || r.AwayTeamId == teamId;
                if (mine && theirs) { return true; }
            }
            return false;
        }

        // 주사위 하나: (시즌 시드, 팀, 선수 번호, 시도 번호)에서만 나온다. 같은 세팅 = 같은 결과(경기 시드와 같은 원칙).
        // System.Random을 쓰지 않는다: 구형 .NET Random은 가까운 시드의 첫 출력이 거의 선형이라(시도 번호 +1마다 약 +0.195, 10-12 리뷰에서 측정)
        // 재시도 결과가 서로 예측 가능하게 묶인다. 네 값을 정수 해시(murmur3 fmix 계열)로 섞어 24비트를 [0, 1)로 편다. 정수 연산만이라 기기 간에 같다
        public static float Roll(int seasonSeed, int teamId, int playerIndex, int attemptIndex)
        {
            uint h = Mix((uint)seasonSeed);
            h = Mix(h ^ (uint)teamId);
            h = Mix(h ^ (uint)playerIndex);
            h = Mix(h ^ (uint)attemptIndex);
            return (h >> 8) / 16777216f;   // 2^24 → float 가수에 딱 맞는 균등 눈금
        }

        private static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }

        public static bool Succeeds(float chance, float roll)
        {
            return roll < chance;
        }

        // 경기 결과 → 코인 수입. 내 팀 기준 골
        public static int CoinsFor(int myGoals, int theirGoals)
        {
            if (myGoals > theirGoals) { return WinCoins; }
            if (myGoals == theirGoals) { return DrawCoins; }
            return LossCoins;
        }

        // 홈·원정 어느 쪽이 나인지 골라 CoinsFor에 넘긴다(10-12 리뷰: 이 분기가 매니저에 있으면 테스트가 못 잠근다)
        public static int CoinsFor(MatchResult result, int myTeamId)
        {
            if (result.HomeTeamId == myTeamId) { return CoinsFor(result.HomeGoals, result.AwayGoals); }
            if (result.AwayTeamId == myTeamId) { return CoinsFor(result.AwayGoals, result.HomeGoals); }
            throw new ArgumentException($"[ScoutRules] 팀 {myTeamId}의 경기가 아니다({result.HomeTeamId} vs {result.AwayTeamId})", nameof(result));
        }

        private static int ClampStars(int stars)
        {
            return Math.Min(MaxStars, Math.Max(MinStars, stars));
        }
    }
}
