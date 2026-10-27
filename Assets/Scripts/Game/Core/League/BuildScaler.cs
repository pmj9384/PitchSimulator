using System;
using Game.Core.Data;

namespace Game.Core.League
{
    // 빌드 9개 합계를 목표 총점으로 비율 조정한 복사본(스펙 §4-1 "리그 로드 때 비율로 축소"). 반올림 오차는 가장 큰 스탯에 몰아 합계를 정확히 맞춘다.
    // 분모는 현재 합계라 줄이기(표 300 → 부 총점)와 늘리기(스카우트한 260점 선수 → 내 300, 10-12)에 같이 쓴다.
    // 다이얼·역할 문자열은 그대로 복사. 1부 "재분배"(rebuild)는 10-06 칸(3~1부 규칙)에서
    public static class BuildScaler
    {
        public const int RebuildShift = 5;   // 1부 빌드 재분배 폭(10-08 [가정], Rebuild 주석)
        public const int StatMax = 100;      // 능력치 한 칸 상한(PlayerTable 값 범위 0~100). 재분배가 이걸 넘기지 않는다

        public static PlayerStats Scale(PlayerStats src, int totalPoints)
        {
            int currentTotal = src.BuildTotal;
            if (totalPoints == currentTotal) { return src; }
            if (currentTotal <= 0) { throw new InvalidOperationException($"[BuildScaler] {src.VariantId}의 합계가 {currentTotal}이라 비율을 낼 수 없다"); }

            float ratio = (float)totalPoints / currentTotal;
            PlayerStats p = Copy(src);
            p.Speed = Round(src.Speed * ratio);
            p.Stamina = Round(src.Stamina * ratio);
            p.Pass = Round(src.Pass * ratio);
            p.Shot = Round(src.Shot * ratio);
            p.Tackle = Round(src.Tackle * ratio);
            p.Positioning = Round(src.Positioning * ratio);
            p.Reflexes = Round(src.Reflexes * ratio);
            p.Handling = Round(src.Handling * ratio);
            p.Diving = Round(src.Diving * ratio);

            int diff = totalPoints - p.BuildTotal;
            if (diff != 0) { AddToLargest(p, diff); }
            if (p.Speed < 0 || p.Stamina < 0 || p.Pass < 0 || p.Shot < 0 || p.Tackle < 0 || p.Positioning < 0 || p.Reflexes < 0 || p.Handling < 0 || p.Diving < 0)
            {
                throw new InvalidOperationException($"[BuildScaler] {src.VariantId}를 {totalPoints}로 줄이니 음수 스탯이 생긴다");   // 지금 표(260~300)론 안 나지만 방어
            }
            if (Largest(p) > StatMax)
            {
                throw new InvalidOperationException($"[BuildScaler] {src.VariantId}를 {totalPoints}로 늘리니 {StatMax}를 넘는 스탯이 생긴다");   // 표 최댓값 90이라 안 나지만 방어
            }
            return p;
        }

        // 빌드 재분배(스펙 §10 1부 "피지컬 CB·기술 ST", 10-08 [가정]): 총점은 그대로 두고 역할 묶음별로 RebuildShift씩 옮긴다.
        // 수비(CB·FB·DM)는 pass·shot → tackle·speed, 공격(ST·W·AM)은 tackle·positioning → shot·pass. GK·CM은 그대로. 옮길 값이 모자라면 있는 만큼만
        public static PlayerStats Rebuild(PlayerStats src, string roleId)
        {
            PlayerStats p = Copy(src);
            if (IsDefender(roleId))
            {
                int fromPass = MoveAmount(p.Pass, p.Tackle);
                int fromShot = MoveAmount(p.Shot, p.Speed);
                p.Pass -= fromPass;
                p.Shot -= fromShot;
                p.Tackle += fromPass;
                p.Speed += fromShot;
                return p;
            }
            if (IsAttacker(roleId))
            {
                int fromTackle = MoveAmount(p.Tackle, p.Shot);
                int fromPositioning = MoveAmount(p.Positioning, p.Pass);
                p.Tackle -= fromTackle;
                p.Positioning -= fromPositioning;
                p.Shot += fromTackle;
                p.Pass += fromPositioning;
                return p;
            }
            return p;
        }

        // 옮길 양: 주는 쪽에 있는 만큼, 받는 쪽 상한(StatMax)까지만
        private static int MoveAmount(int source, int destination)
        {
            return Math.Max(0, Math.Min(Math.Min(RebuildShift, source), StatMax - destination));
        }

        // 재분배가 실제로 빌드를 옮기는 자리인가(GK·CM은 아니다). 스카우트 별 판정이 쓴다
        public static bool RebuildApplies(string roleId)
        {
            return IsDefender(roleId) || IsAttacker(roleId);
        }

        private static bool IsDefender(string roleId)
        {
            return IsRole(roleId, "CB") || IsRole(roleId, "FB") || IsRole(roleId, "DM");
        }

        private static bool IsAttacker(string roleId)
        {
            return IsRole(roleId, "ST") || IsRole(roleId, "W") || IsRole(roleId, "AM");
        }

        private static bool IsRole(string roleId, string expected)
        {
            return string.Equals(roleId, expected, StringComparison.OrdinalIgnoreCase);
        }

        // 전 필드 복사(PlayerTable 행은 공유 객체라 빌드를 바꾸기 전에 복사한다)
        public static PlayerStats Copy(PlayerStats src)
        {
            return src.Clone();   // 필드를 하나씩 나열하던 것을 PlayerStats.Clone으로 모았다(다이얼을 더할 때 빠뜨릴 자리가 둘이 되지 않게)
        }

        // 빌드 9개만 복사(다이얼·문자열은 그대로). 슬롯 변형에 선수 빌드를 얹을 때(LineupComposer)와 역할 이월이 쓴다
        public static void CopyBuild(PlayerStats from, PlayerStats to)
        {
            to.Speed = from.Speed;
            to.Stamina = from.Stamina;
            to.Pass = from.Pass;
            to.Shot = from.Shot;
            to.Tackle = from.Tackle;
            to.Positioning = from.Positioning;
            to.Reflexes = from.Reflexes;
            to.Handling = from.Handling;
            to.Diving = from.Diving;
        }

        private static int Round(float v)
        {
            return (int)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        private static int Largest(PlayerStats p)
        {
            int[] values = { p.Speed, p.Stamina, p.Pass, p.Shot, p.Tackle, p.Positioning, p.Reflexes, p.Handling, p.Diving };
            int best = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                best = Math.Max(best, values[i]);
            }
            return best;
        }

        // 가장 큰 스탯에 오차를 더한다(음수도). 큰 스탯이라 몇 점 차이가 비율로 가장 작다
        private static void AddToLargest(PlayerStats p, int diff)
        {
            int best = p.Speed;
            int which = 0;
            int[] values = { p.Speed, p.Stamina, p.Pass, p.Shot, p.Tackle, p.Positioning, p.Reflexes, p.Handling, p.Diving };
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] > best)
                {
                    best = values[i];
                    which = i;
                }
            }
            switch (which)
            {
                case 0: p.Speed += diff; break;
                case 1: p.Stamina += diff; break;
                case 2: p.Pass += diff; break;
                case 3: p.Shot += diff; break;
                case 4: p.Tackle += diff; break;
                case 5: p.Positioning += diff; break;
                case 6: p.Reflexes += diff; break;
                case 7: p.Handling += diff; break;
                default: p.Diving += diff; break;
            }
        }
    }
}
