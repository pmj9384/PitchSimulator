using System;
using System.Collections.Generic;
using Game.Core.Data;

namespace Game.Core.League
{
    // 스카우트 후보 한 명(스펙 §4-5 10-19 일괄 제시). Slot은 화면의 행 번호이자 "그 자리가 몇 번째 후보인가"의 열쇠
    public readonly struct ScoutCandidate
    {
        public readonly int Slot;
        public readonly int TeamId;
        public readonly int PlayerIndex;
        public readonly bool UpperTier;

        public ScoutCandidate(int slot, int teamId, int playerIndex, bool upperTier)
        {
            Slot = slot;
            TeamId = teamId;
            PlayerIndex = playerIndex;
            UpperTier = upperTier;
        }

        public bool Is(int teamId, int playerIndex)
        {
            return TeamId == teamId && PlayerIndex == playerIndex;
        }
    }

    // 후보 선정 순수 함수. 상태·난수 없음: (시즌 시드, 내 로스터 자리, 풀, 시도 기록)에서 매번 같은 후보가 나오므로 세이브에 안 둔다.
    // 4부의 변별은 위 부 후보에서 나온다(TierRules 4부는 전원 ★1). 자리마다 서로 겹치지 않는 부분 풀(인덱스 나머지)에서 뽑아
    // 한 자리가 바뀌어도 다른 자리는 그대로다("성공한 자리만 바뀐다", 스펙 §4-5 10-19). 영입한 선수는 그 부분 풀에서 건너뛰므로 다시 뜨지 않는다(10-19 리뷰 🟡)
    public static class ScoutPool
    {
        public const int CandidateCount = 4;   // [가정] 출처 없음. ★1(내 부)과 ★3(위 부)가 2개씩은 나란히 보여야 비교가 된다
        public const int UpperTierSlots = 2;   // [가정] 뒤 2자리가 위 부. 위 부가 없거나(1부) 들어갈 선수가 모자라면 전부 내 부로 채운다

        // history = 이번 시즌 시도 기록(실패 포함). 성공한 기록의 선수는 후보에서 빠지고 그 자리가 다음 선수로 넘어간다. 실패 기록은 후보를 바꾸지 않는다(재시도 보정은 ScoutRetriesOn)
        public static List<ScoutCandidate> Pick(int seasonSeed, IReadOnlyList<RosterPlayer> myRoster, IReadOnlyList<GeneratedTeam> ownTier, GeneratedTeam? upper,
            IReadOnlyList<ScoutTarget> history)
        {
            List<ScoutCandidate> ownPool = Eligible(ownTier, myRoster, upperTier: false);
            List<ScoutCandidate> upperPool = upper == null ? new List<ScoutCandidate>() : Eligible(new[] { upper }, myRoster, upperTier: true);
            int upperSlots = upperPool.Count >= UpperTierSlots ? UpperTierSlots : 0;
            int ownSlots = CandidateCount - upperSlots;
            if (ownPool.Count < ownSlots) { throw new InvalidOperationException($"[ScoutPool] 내 부 풀이 {ownPool.Count}명이라 후보 {ownSlots}자리를 못 채운다"); }

            var picked = new List<ScoutCandidate>(CandidateCount);
            for (int slot = 0; slot < CandidateCount; slot++)
            {
                bool fromUpper = slot >= ownSlots;
                List<ScoutCandidate> pool = fromUpper ? upperPool : ownPool;
                int stride = fromUpper ? upperSlots : ownSlots;
                int offset = fromUpper ? slot - ownSlots : slot;
                ScoutCandidate source = PickFromSubPool(seasonSeed, slot, pool, offset, stride, history);
                picked.Add(new ScoutCandidate(slot, source.TeamId, source.PlayerIndex, source.UpperTier));
            }
            return picked;
        }

        // 자리의 부분 풀 = pool[offset], pool[offset + stride], … 해시로 시작 칸을 정하고 영입한 선수는 건너뛴다. 전원 영입했으면(시도를 그만큼 샀을 때) 시작 칸을 그대로 쓴다
        private static ScoutCandidate PickFromSubPool(int seasonSeed, int slot, List<ScoutCandidate> pool, int offset, int stride, IReadOnlyList<ScoutTarget> history)
        {
            int subCount = (pool.Count - offset + stride - 1) / stride;
            int start = (int)(ScoutRules.Hash((uint)seasonSeed, (uint)slot, (uint)subCount) % (uint)subCount);
            for (int k = 0; k < subCount; k++)
            {
                ScoutCandidate source = pool[offset + ((start + k) % subCount) * stride];
                if (!Recruited(history, source)) { return source; }
            }
            return pool[offset + start * stride];
        }

        private static bool Recruited(IReadOnlyList<ScoutTarget> history, ScoutCandidate source)
        {
            for (int i = 0; i < history.Count; i++)
            {
                if (!history[i].Success) { continue; }
                if (source.Is(history[i].TeamId, history[i].PlayerIndex)) { return true; }
            }
            return false;
        }

        // 내 로스터의 어느 자리에든 들어갈 수 있는 선수만(교체는 같은 자리라서, SeasonState.CanReplacePlayer와 같은 기준). 팀 순서·선수 번호 순서라 결정적
        public static List<ScoutCandidate> Eligible(IReadOnlyList<GeneratedTeam> teams, IReadOnlyList<RosterPlayer> myRoster, bool upperTier)
        {
            var pool = new List<ScoutCandidate>();
            for (int t = 0; t < teams.Count; t++)
            {
                GeneratedTeam team = teams[t];
                for (int p = 0; p < team.Players.Count; p++)
                {
                    if (!FitsAnyRosterSlot(team.Players[p], myRoster)) { continue; }
                    pool.Add(new ScoutCandidate(-1, team.TeamId, p, upperTier));
                }
            }
            return pool;
        }

        private static bool FitsAnyRosterSlot(PlayerStats scouted, IReadOnlyList<RosterPlayer> myRoster)
        {
            for (int i = 0; i < myRoster.Count; i++)
            {
                if (PlayerTableLookup.IsSamePosition(scouted, myRoster[i].Stats.RoleId)) { return true; }
            }
            return false;
        }
    }
}
