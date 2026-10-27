using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.League;

namespace Game.Core.Tactics
{
    // 선수 + 슬롯 설정 → 경기에 들어가는 PlayerStats, 한 곳(10-27 슬롯 중심, 스펙 §4-2).
    // 빌드 9개는 선수 것, 다이얼은 슬롯의 공격 변형 것, 그 위에 슬롯의 개인 지시(잘라내기), 그 위에 수비 슬롯의 수비 역할 배율.
    // 적성: 슬롯 변형이 선수의 자연 변형(선수가 데려올 때 들고 온 변형)과 다르면 다이얼을 그 변형과 자리 기본 변형의 중간값으로 [가정, 값 출처 없음].
    // 포처를 데려온 이유가 여기서 생긴다: 4부는 빌드가 자리마다 같아(PlayerTable.csv) 변형이 유일한 변별이다
    public static class LineupComposer
    {
        public static PlayerStats Compose(PlayerStats player, PlayerStats slotVariant, IReadOnlyList<int> offsets, DefenseRole? defenseRole, IReadOnlyList<PlayerStats> table)
        {
            PlayerStats stats = BuildScaler.Copy(slotVariant);
            BuildScaler.CopyBuild(player, stats);
            if (!IsNatural(player, slotVariant))
            {
                PlayerStats baseline = TeamGenerator.DefaultVariant(table, slotVariant.RoleId);
                MidpointDials(stats, slotVariant, baseline);
            }
            stats = PlayerInstructions.Apply(stats, offsets);
            if (defenseRole != null) { stats = defenseRole.Apply(stats); }
            return stats;
        }

        // 선수가 그 변형을 자연 변형으로 갖는가(= 적성 100%)
        public static bool IsNatural(PlayerStats player, PlayerStats slotVariant)
        {
            return string.Equals(player.VariantId, slotVariant.VariantId, StringComparison.OrdinalIgnoreCase);
        }

        // 다이얼 11개를 변형과 기본 변형의 중간으로. 포커스(정수 3단)는 변형 것을 둔다
        private static void MidpointDials(PlayerStats target, PlayerStats variant, PlayerStats baseline)
        {
            target.PushUp = Mid(variant.PushUp, baseline.PushUp);
            target.PressRange = Mid(variant.PressRange, baseline.PressRange);
            target.ShotBias = Mid(variant.ShotBias, baseline.ShotBias);
            target.PassLength = Mid(variant.PassLength, baseline.PassLength);
            target.Width = Mid(variant.Width, baseline.Width);
            target.LineHeight = Mid(variant.LineHeight, baseline.LineHeight);
            target.RoamRadius = Mid(variant.RoamRadius, baseline.RoamRadius);
            target.PassRisk = Mid(variant.PassRisk, baseline.PassRisk);
            target.Dribble = Mid(variant.Dribble, baseline.Dribble);
            target.HoldUp = Mid(variant.HoldUp, baseline.HoldUp);
            target.GkRushRadius = Mid(variant.GkRushRadius, baseline.GkRushRadius);
        }

        private static float Mid(float a, float b)
        {
            return (a + b) * 0.5f;
        }
    }
}
