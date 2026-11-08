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
    // 상대 소유이고 공이 우리 골 중심에서 MarkZoneRadius 안이면, 압박하는 1명(⑧) 말고 우리 골에 가까운 상대 필드 선수(뒷줄 수까지)에게
    // 뒷줄(LineRules.IsBackLine) 중 가장 가까운 우리 수비 1명씩이 붙어 그 선수의 골 쪽(상대 → 우리 골 중심 선 위) MarkDistance에 선다.
    // 11-06 D1: 후보가 전 필드 선수였을 땐 내려오던 수미가 박스 앞 상대를 먼저 잡고 CB가 10m 뒤에 남았다(중앙 대상 마커 DM 49%·CB 27%, 설계 1-2). 뒷줄만 후보면 CB 95%.
    // 뒷줄이 모자라면 둘째 줄(LineRules.IsSecondLine)에서 수비 역할이 tracking인 선수가 2차로 든다(AssignTiered).
    // 지역 방어(공 지향 슬라이드)는 그대로 두고 마크 구역(MarkZoneRadius) 안에서만 사람을 본다: 90분 깔때기에서 슛의 96%가 슛 선 15m 안에 수비가 없었고
    // 압박받는 슛이 1.2%(StatsBomb 약 65%)였다. 수비는 골 쪽에 있되 공만 따라 움직여 리시버에서 6m 떨어져 있었다(설계 1-2).
    // 배정은 압박 순위(PressRules.PressRank)와 같은 방식: 거리순, 동률은 PlayerId 작은 쪽, 지금 맡은 쌍은 margin만큼 가깝게 친다(ChaserSwitchMargin).
    public static class MarkRules
    {
        // 마크 발동: 공이 우리 골 중심(x = −attackSign × HalfLength, z = 0)에서 MarkZoneRadius 안. 경계 포함.
        // 11-06엔 슛 사거리(MaxShotRange 40)를 같이 읽었다. 11-08 S3 1단계에서 사거리만 30으로 줄이며 갈랐다: 마크 구역까지 줄이면 D1 행동이 바뀌어 원인이 섞인다
        public static bool IsActive(float ballX, float ballZ, int attackSign)
        {
            return DistanceToOwnGoal(ballX, ballZ, attackSign) <= MatchTuning.MarkZoneRadius;
        }

        // 마크 대상: 상대 필드 선수 중 우리 골 중심에서 MarkZoneRadius 안인 선수를 골에 가까운 순으로 max명까지.
        // 공을 가진 선수(압박 몫)와 상대 GK는 뺀다. 날아가는 패스의 리시버는 소유자가 아니라 대상에 들어간다(착지 전 마크, 위치는 WithReceiverAt)
        // 히스테리시스: 직전 틱 대상(previous)은 골 거리를 margin만큼 짧게 쳐서 순서를 매긴다. 골 거리가 비슷한 3명이 상한 2명 경계에서
        // 틱마다 들고 나지 않게 한다(배정 Assign의 마커 여유와 같은 꼴). 마크 구역(MarkZoneRadius) 판정은 실제 거리 그대로다
        public static void SelectTargets(IReadOnlyList<TargetInfo> opponents, int ownerId, int keeperId, int attackSign, int max, IReadOnlyList<MarkAssignment> previous, float margin, List<TargetInfo> result)
        {
            result.Clear();
            for (int i = 0; i < opponents.Count; i++)
            {
                TargetInfo o = opponents[i];
                if (o.PlayerId == ownerId || o.PlayerId == keeperId) { continue; }
                if (DistanceToOwnGoal(o.X, o.Z, attackSign) > MatchTuning.MarkZoneRadius) { continue; }
                InsertByGoalDistance(result, o, attackSign, previous, margin);
            }
            if (result.Count > max) { result.RemoveRange(max, result.Count - max); }
        }

        // 상대 명부를 result에 복사하되 날아오는 패스의 리시버는 그 패스의 도착점에 둔다. 대상 선정·배정·마크 지점이 같은 점을 본다:
        // 선정만 지금 자리로 보면 40m 밖에서 달려드는 리시버가 착지 직전까지 대상에서 빠졌다(11-06 리뷰)
        public static void WithReceiverAt(IReadOnlyList<TargetInfo> opponents, int receiverId, float landingX, float landingZ, List<TargetInfo> result)
        {
            result.Clear();
            for (int i = 0; i < opponents.Count; i++)
            {
                TargetInfo o = opponents[i];
                if (o.PlayerId == receiverId)
                {
                    result.Add(new TargetInfo(o.PlayerId, landingX, landingZ));
                    continue;
                }
                result.Add(o);
            }
        }

        // 배정: 대상 순서(골에 가까운 순)대로, 아직 안 맡은 마커 후보 중 대상에게 가장 가까운 1명을 붙인다.
        // 직전 틱에 이 대상을 맡던 마커는 거리를 margin만큼 짧게 친다. 후보가 바닥나면 남은 대상은 안 맡는다
        public static void Assign(IReadOnlyList<TargetInfo> targets, IReadOnlyList<TargetInfo> markers, IReadOnlyList<MarkAssignment> previous, float margin, List<MarkAssignment> result)
        {
            result.Clear();
            AssignInto(targets, markers, previous, margin, result);
        }

        // 2단 배정(11-06 D1): 1차 후보(뒷줄)로 먼저 채우고, 남은 대상이 있으면 2차 후보(둘째 줄 + tracking)로 채운다.
        // 2차는 1차가 바닥났을 때만 들어온다: 뒷줄 CB가 박스 앞 전진으로 압박자가 되거나 얼어 있어 대상보다 적을 때. 뒷줄이 충분하면 미드필더는 자기 줄에 남는다(FIFA D1 지역 수비)
        public static void AssignTiered(IReadOnlyList<TargetInfo> targets, IReadOnlyList<TargetInfo> primary, IReadOnlyList<TargetInfo> secondary, IReadOnlyList<MarkAssignment> previous, float margin, List<MarkAssignment> result)
        {
            result.Clear();
            AssignInto(targets, primary, previous, margin, result);
            if (result.Count == targets.Count) { return; }
            AssignInto(targets, secondary, previous, margin, result);
        }

        // 아직 안 맡은 대상마다 result에 없는 마커 후보 중 최근접 1명을 붙여 result에 더한다. 후보가 바닥나면 멈춘다
        private static void AssignInto(IReadOnlyList<TargetInfo> targets, IReadOnlyList<TargetInfo> markers, IReadOnlyList<MarkAssignment> previous, float margin, List<MarkAssignment> result)
        {
            for (int t = 0; t < targets.Count; t++)
            {
                if (IsTargetAssigned(result, targets[t].PlayerId)) { continue; }
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
        // 반응이 늦은 마커일수록 달리는 상대의 뒤를 본다(지연된 지각). 다만 본 자리가 실제 상대보다 골에서 멀면(골 쪽으로 달리는 상대) 그 차이만큼 더 골 쪽으로 가서,
        // 마크 지점이 실제 상대보다 공 쪽으로 넘어가지 않게 한다(골 쪽 선 전제. 늦은 마커는 상대 옆까지만 따라붙는다, 11-06 리뷰).
        // 골까지가 그보다 가까우면 골 중심에 선다. 필드 안으로 자른다. 이 점은 하한 전이다: 시뮬(MatchSimulation.MarkPointFor)이 수비 하한(PositionRules.AboveDefendFloor) 위로 올려
        // GK 옆 상대를 따라 GK 선까지 내려가지 않게 한다(11-06)
        public static (float x, float z) MarkPoint(float targetX, float targetZ, float targetVelX, float targetVelZ, float delay, int attackSign, float distance)
        {
            float seenX = targetX - targetVelX * delay;
            float seenZ = targetZ - targetVelZ * delay;
            float goalX = -attackSign * Placement.FieldBounds.HalfLength;
            float dx = goalX - seenX;
            float dz = -seenZ;
            float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
            float behind = len - DistanceToOwnGoal(targetX, targetZ, attackSign);
            float advance = System.Math.Max(distance, behind);
            if (len <= advance) { return (goalX, 0f); }

            float x = seenX + dx / len * advance;
            float z = seenZ + dz / len * advance;
            float limitX = Placement.FieldBounds.HalfLength - Placement.FieldBounds.EdgeMargin;
            float limitZ = Placement.FieldBounds.HalfWidth - Placement.FieldBounds.EdgeMargin;
            float clampedX = System.Math.Max(-limitX, System.Math.Min(limitX, x));
            float clampedZ = System.Math.Max(-limitZ, System.Math.Min(limitZ, z));
            return (clampedX, clampedZ);
        }

        // 계측: 이번 배정에서 직전 틱에 없던 (마커, 대상) 쌍의 수. 직전 틱이 비었으면(막 발동) 0. 러너가 "마커 교대"로 센다
        public static int NewPairs(IReadOnlyList<MarkAssignment> previous, IReadOnlyList<MarkAssignment> current)
        {
            if (previous.Count == 0) { return 0; }

            int count = 0;
            for (int i = 0; i < current.Count; i++)
            {
                if (PreviousMarkerOf(previous, current[i].TargetId) != current[i].MarkerId) { count++; }
            }
            return count;
        }

        // 우리 골 중심까지 거리. attackSign은 "우리 팀"의 공격 방향이라 우리 골은 그 반대쪽 골라인이다
        private static float DistanceToOwnGoal(float x, float z, int attackSign)
        {
            float dx = x + attackSign * Placement.FieldBounds.HalfLength;
            return (float)System.Math.Sqrt(dx * dx + z * z);
        }

        // 골에 가까운 순으로 끼워 넣는다(직전 틱 대상은 margin만큼 가깝게). 동률은 PlayerId 작은 쪽이 앞(목록 순서에 기대지 않는다)
        private static void InsertByGoalDistance(List<TargetInfo> sorted, TargetInfo item, int attackSign, IReadOnlyList<MarkAssignment> previous, float margin)
        {
            float d = RankedGoalDistance(item, attackSign, previous, margin);
            int at = sorted.Count;
            for (int i = 0; i < sorted.Count; i++)
            {
                float other = RankedGoalDistance(sorted[i], attackSign, previous, margin);
                if (d < other || (d == other && item.PlayerId < sorted[i].PlayerId))
                {
                    at = i;
                    break;
                }
            }
            sorted.Insert(at, item);
        }

        private static float RankedGoalDistance(TargetInfo t, int attackSign, IReadOnlyList<MarkAssignment> previous, float margin)
        {
            float d = DistanceToOwnGoal(t.X, t.Z, attackSign);
            if (PreviousMarkerOf(previous, t.PlayerId) == -1) { return d; }
            return System.Math.Max(0f, d - margin);
        }

        private static int PreviousMarkerOf(IReadOnlyList<MarkAssignment> previous, int targetId)
        {
            for (int i = 0; i < previous.Count; i++)
            {
                if (previous[i].TargetId == targetId) { return previous[i].MarkerId; }
            }
            return -1;
        }

        private static bool IsTargetAssigned(List<MarkAssignment> assigned, int targetId)
        {
            for (int i = 0; i < assigned.Count; i++)
            {
                if (assigned[i].TargetId == targetId) { return true; }
            }
            return false;
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
