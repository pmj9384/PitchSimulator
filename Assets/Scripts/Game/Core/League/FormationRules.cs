using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;

namespace Game.Core.League
{
    // 포메이션 슬롯 판정(10-25 슬롯 격자, FM26 In/Out of Possession 모양 2장 모델). 상태 없음, 엔진 없음.
    // 공격 포메이션 A와 수비 포메이션 B를 따로 고르고 선수는 A의 슬롯 i·B의 슬롯 j에 선다. 경기 좌표는 템플릿 값 그대로(화면 격자와 미터를 분리)
    public static class FormationRules
    {
        // 슬롯 번호(1~11)의 자리. defending이면 수비 모양(posX2/posZ2), 아니면 공격 모양(posX/posZ)
        public static (float x, float z) Position(FormationTemplate template, int slot, bool defending)
        {
            FormationSlot s = SlotOf(template, slot);
            return defending ? (s.PosX2, s.PosZ2) : (s.PosX, s.PosZ);
        }

        public static FormationSlot SlotOf(FormationTemplate template, int slot)
        {
            if (slot < 1 || slot > FormationTemplate.SlotCount) { throw new ArgumentOutOfRangeException(nameof(slot), $"[FormationRules] 슬롯은 1~{FormationTemplate.SlotCount} ({slot})"); }
            for (int i = 0; i < template.Slots.Count; i++)
            {
                if (template.Slots[i].Slot == slot) { return template.Slots[i]; }
            }
            throw new InvalidOperationException($"[FormationRules] {template.Id}에 슬롯 {slot}이 없다");
        }

        public static FormationTemplate Find(IReadOnlyList<FormationTemplate> formations, string id)
        {
            for (int i = 0; i < formations.Count; i++)
            {
                if (string.Equals(formations[i].Id, id, StringComparison.OrdinalIgnoreCase)) { return formations[i]; }
            }
            throw new InvalidOperationException($"[FormationRules] FormationTemplates에 없는 포메이션: {id}");
        }

        // 선수를 그 슬롯에 둘 수 있나: 공격 역할(PlayerTable 변형)의 역할군과 슬롯 역할군이 같아야 한다
        public static bool CanOccupy(string playerRoleId, FormationSlot slot)
        {
            return RoleGroups.SameGroup(playerRoleId, slot.RoleId);
        }

        // 한 선수의 공격 자리 ↔ 수비 자리 거리. 전환 때 이만큼 달린다
        public static float TransitionDistance(FormationTemplate attack, int attackSlot, FormationTemplate defense, int defenseSlot)
        {
            (float ax, float az) = Position(attack, attackSlot, defending: false);
            (float dx, float dz) = Position(defense, defenseSlot, defending: true);
            return (float)Math.Sqrt((ax - dx) * (ax - dx) + (az - dz) * (az - dz));
        }

        // FM26 "둘 나란히" 보기의 경고처럼: 멀면 경고만, 막지 않는다. 결과(늦게 도착)는 경기가 보여 준다
        public static bool IsFarTransition(float distance)
        {
            return distance > MatchTuning.FormationTransitionWarnMeters;
        }

        // 공격 포메이션 A에 대해 수비 후보를 "슬롯 i ↔ i 평균 이동 거리"가 짧은 순으로. FM26의 어시스턴트 추천(IP를 고르면 OOP 3개)에 해당. 결과 길이는 후보 수 그대로
        public static List<FormationTemplate> RankDefenseFormations(FormationTemplate attack, IReadOnlyList<FormationTemplate> candidates)
        {
            var ranked = new List<(float distance, int order, FormationTemplate template)>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                ranked.Add((MeanTransitionDistance(attack, candidates[i]), i, candidates[i]));
            }
            ranked.Sort(CompareByDistanceThenOrder);
            var result = new List<FormationTemplate>(ranked.Count);
            for (int i = 0; i < ranked.Count; i++) { result.Add(ranked[i].template); }
            return result;
        }

        public static float MeanTransitionDistance(FormationTemplate attack, FormationTemplate defense)
        {
            float sum = 0f;
            for (int slot = 1; slot <= FormationTemplate.SlotCount; slot++)
            {
                sum += TransitionDistance(attack, slot, defense, slot);
            }
            return sum / FormationTemplate.SlotCount;
        }

        // 포메이션을 바꿀 때 선수를 새 템플릿 슬롯에 다시 앉힌다(10-26 리뷰 🟣: 슬롯 번호만 유지하면 4-4-2 슬롯 6 W가 4-3-3에선 DM이라 역할군이 어긋난다).
        // 세 번에 나눠 결정적으로: ①지금 번호의 새 슬롯이 같은 군이면 그대로 ②남은 선수는 같은 군의 빈 슬롯 중 번호 작은 것 ③그래도 남으면(4-4-2 미드 4명 → 4-3-3 미드 3칸) 빈 슬롯 중 번호 작은 것.
        // ③은 군이 어긋난 채 선다: 경기는 좌표만 쓰니 돌아가고, 유저가 교체·영입으로 푼다(FM도 적성 낮은 자리에 세울 수 있다). 결과 [i] = playerRoleIds[i]의 새 슬롯
        public static int[] ReseatSlots(IReadOnlyList<string> playerRoleIds, IReadOnlyList<int> currentSlots, FormationTemplate template)
        {
            int count = playerRoleIds.Count;
            var result = new int[count];
            var taken = new bool[FormationTemplate.SlotCount + 1];
            for (int i = 0; i < count; i++)
            {
                if (!CanOccupy(playerRoleIds[i], SlotOf(template, currentSlots[i]))) { continue; }
                result[i] = currentSlots[i];
                taken[currentSlots[i]] = true;
            }
            for (int i = 0; i < count; i++)
            {
                if (result[i] != 0) { continue; }
                result[i] = FirstFreeSlot(template, taken, playerRoleIds[i]);
                if (result[i] != 0) { taken[result[i]] = true; }
            }
            for (int i = 0; i < count; i++)
            {
                if (result[i] != 0) { continue; }
                result[i] = FirstFreeSlot(template, taken, null);
                taken[result[i]] = true;
            }
            return result;
        }

        // roleId를 주면 같은 군의 빈 슬롯, null이면 아무 빈 슬롯. 없으면 0
        private static int FirstFreeSlot(FormationTemplate template, bool[] taken, string? roleId)
        {
            for (int slot = 1; slot <= FormationTemplate.SlotCount; slot++)
            {
                if (taken[slot]) { continue; }
                if (roleId != null && !CanOccupy(roleId, SlotOf(template, slot))) { continue; }
                return slot;
            }
            return 0;
        }

        private static int CompareByDistanceThenOrder((float distance, int order, FormationTemplate template) a, (float distance, int order, FormationTemplate template) b)
        {
            int byDistance = a.distance.CompareTo(b.distance);
            return byDistance != 0 ? byDistance : a.order.CompareTo(b.order);
        }
    }
}
