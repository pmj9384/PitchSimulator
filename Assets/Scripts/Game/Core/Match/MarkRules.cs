using System.Collections.Generic;

namespace Game.Core.Match
{
    // 마커 1명과 그가 맡은 상대 1명. 시뮬이 팀별로 틱마다 다시 정하고, 직전 틱 목록을 히스테리시스 기준으로 넘긴다
    public readonly struct MarkAssignment
    {
        public readonly int MarkerId;
        public readonly int TargetId;

        public MarkAssignment(int markerId, int targetId)
        {
            MarkerId = markerId;
            TargetId = targetId;
        }
    }

    // 리시버 마크 판정 순수 함수(11-06 S3 ①, 설계 2-1 A안).
    // 상대 소유이고 공이 우리 골 중심에서 MaxShotRange 안이면, 압박하는 1명(⑧) 말고 우리 골에 가까운 상대 필드 선수 MaxMarkers명에게
    // 가장 가까운 우리 수비 1명씩이 붙어 그 선수의 골 쪽(상대 → 우리 골 중심 선 위) MarkDistance에 선다.
    // 지역 방어(공 지향 슬라이드)는 그대로 두고 슛 사거리 안에서만 사람을 본다: 90분 깔때기에서 슛의 96%가 슛 선 15m 안에 수비가 없었고
    // 압박받는 슛이 1.2%(StatsBomb 약 65%)였다. 수비는 골 쪽에 있되 공만 따라 움직여 리시버에서 6m 떨어져 있었다(설계 1-2).
    // 배정은 압박 순위(PressRules.PressRank)와 같은 방식: 거리순, 동률은 PlayerId 작은 쪽, 지금 맡은 쌍은 margin만큼 가깝게 친다(ChaserSwitchMargin).
    public static class MarkRules
    {
        // 마크 발동: 공이 우리 골 중심(x = −attackSign × HalfLength, z = 0)에서 MaxShotRange 안. 경계 포함.
        // 슛 확률이 0이 되는 거리(MatchRules.ShotProbability)와 같은 상수라 "슛이 나올 수 있는 구역 = 마크하는 구역"이다
        public static bool IsActive(float ballX, float ballZ, int attackSign)
        {
            return DistanceToOwnGoal(ballX, ballZ, attackSign) <= MatchTuning.MaxShotRange;
        }

        // 마크 대상: 상대 필드 선수 중 우리 골 중심에서 MaxShotRange 안인 선수를 골에 가까운 순으로 max명까지.
        // 공을 가진 선수(압박 몫)와 상대 GK는 뺀다. 날아가는 패스의 리시버는 소유자가 아니라 대상에 들어간다(착지 전 마크)
        public static void SelectTargets(IReadOnlyList<TargetInfo> opponents, int ownerId, int keeperId, int attackSign, int max, List<TargetInfo> result)
        {
            result.Clear();
            for (int i = 0; i < opponents.Count; i++)
            {
                TargetInfo o = opponents[i];
                if (o.PlayerId == ownerId || o.PlayerId == keeperId) { continue; }
                if (DistanceToOwnGoal(o.X, o.Z, attackSign) > MatchTuning.MaxShotRange) { continue; }
                InsertByGoalDistance(result, o, attackSign);
            }
            if (result.Count > max) { result.RemoveRange(max, result.Count - max); }
        }

        // 배정: 대상 순서(골에 가까운 순)대로, 아직 안 맡은 마커 후보 중 대상에게 가장 가까운 1명을 붙인다.
        // 직전 틱에 이 대상을 맡던 마커는 거리를 margin만큼 짧게 친다. 후보가 바닥나면 남은 대상은 안 맡는다
        public static void Assign(IReadOnlyList<TargetInfo> targets, IReadOnlyList<TargetInfo> markers, IReadOnlyList<MarkAssignment> previous, float margin, List<MarkAssignment> result)
        {
            result.Clear();
            for (int t = 0; t < targets.Count; t++)
            {
                int incumbent = PreviousMarkerOf(previous, targets[t].PlayerId);
                int best = -1;
                float bestDist = float.MaxValue;
                for (int m = 0; m < markers.Count; m++)
                {
                    int id = markers[m].PlayerId;
                    if (IsAssigned(result, id)) { continue; }
                    float d = EffectiveDistance(markers[m], targets[t], incumbent, margin);
                    if (!IsCloser(d, id, bestDist, best)) { continue; }
                    bestDist = d;
                    best = id;
                }
                if (best == -1) { return; }
                result.Add(new MarkAssignment(best, targets[t].PlayerId));
            }
        }

        // 마커 반응 지연(초) = ReactionTime × (1 + MarkReactionSpread × (50 − positioning) / 50).
        // 폭 0.5면 50 = 0.5초(판정의 반응 등가와 같음), 100 = 0.25초, 0 = 0.75초(설계 2-1의 0.5 × (1.5 − pos/100)).
        // 죽은 스탯이던 Positioning의 첫 자리(설계 결정 3, FM "Positioning·Anticipation" 1층). 범위 밖 스탯은 0~100으로 자른다
        public static float ReactionDelay(int positioning)
        {
            int clamped = System.Math.Max(0, System.Math.Min(100, positioning));
            return MatchTuning.ReactionTime * (1f + MatchTuning.MarkReactionSpread * (50 - clamped) / 50f);
        }

        // 마크 지점: 상대가 delay초 전에 있던 자리(지금 위치 − 속도 × delay)에서 우리 골 중심 쪽으로 distance.
        // 반응이 늦은 마커일수록 달리는 상대의 뒤를 본다(지연된 지각). 골까지가 distance보다 가까우면 골 중심에 선다. 필드 안으로 자른다
        public static (float x, float z) MarkPoint(float targetX, float targetZ, float targetVelX, float targetVelZ, float delay, int attackSign, float distance)
        {
            float seenX = targetX - targetVelX * delay;
            float seenZ = targetZ - targetVelZ * delay;
            float goalX = -attackSign * Placement.FieldBounds.HalfLength;
            float dx = goalX - seenX;
            float dz = -seenZ;
            float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (len <= distance) { return (goalX, 0f); }

            float x = seenX + dx / len * distance;
            float z = seenZ + dz / len * distance;
            float limitX = Placement.FieldBounds.HalfLength - Placement.FieldBounds.EdgeMargin;
            float limitZ = Placement.FieldBounds.HalfWidth - Placement.FieldBounds.EdgeMargin;
            float clampedX = System.Math.Max(-limitX, System.Math.Min(limitX, x));
            float clampedZ = System.Math.Max(-limitZ, System.Math.Min(limitZ, z));
            return (clampedX, clampedZ);
        }

        // 우리 골 중심까지 거리. attackSign은 "우리 팀"의 공격 방향이라 우리 골은 그 반대쪽 골라인이다
        private static float DistanceToOwnGoal(float x, float z, int attackSign)
        {
            float dx = x + attackSign * Placement.FieldBounds.HalfLength;
            return (float)System.Math.Sqrt(dx * dx + z * z);
        }

        // 골에 가까운 순으로 끼워 넣는다. 동률은 PlayerId 작은 쪽이 앞(목록 순서에 기대지 않는다)
        private static void InsertByGoalDistance(List<TargetInfo> sorted, TargetInfo item, int attackSign)
        {
            float d = DistanceToOwnGoal(item.X, item.Z, attackSign);
            int at = sorted.Count;
            for (int i = 0; i < sorted.Count; i++)
            {
                float other = DistanceToOwnGoal(sorted[i].X, sorted[i].Z, attackSign);
                if (d < other || (d == other && item.PlayerId < sorted[i].PlayerId))
                {
                    at = i;
                    break;
                }
            }
            sorted.Insert(at, item);
        }

        private static int PreviousMarkerOf(IReadOnlyList<MarkAssignment> previous, int targetId)
        {
            for (int i = 0; i < previous.Count; i++)
            {
                if (previous[i].TargetId == targetId) { return previous[i].MarkerId; }
            }
            return -1;
        }

        private static bool IsAssigned(List<MarkAssignment> assigned, int markerId)
        {
            for (int i = 0; i < assigned.Count; i++)
            {
                if (assigned[i].MarkerId == markerId) { return true; }
            }
            return false;
        }

        // 대상까지 거리. 직전 틱에 이 대상을 맡던 마커는 margin만큼 짧게(0에서 자름)
        private static float EffectiveDistance(TargetInfo marker, TargetInfo target, int incumbentId, float margin)
        {
            float dx = marker.X - target.X;
            float dz = marker.Z - target.Z;
            float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (marker.PlayerId != incumbentId) { return d; }
            return System.Math.Max(0f, d - margin);
        }

        private static bool IsCloser(float d, int playerId, float bestDist, int bestId)
        {
            if (d < bestDist) { return true; }
            return d == bestDist && playerId < bestId;
        }
    }
}
