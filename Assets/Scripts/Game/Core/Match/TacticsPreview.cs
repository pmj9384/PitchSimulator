using System;
using System.Collections.Generic;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    // 팀 전술 모달의 미니 피치(10-29 ③d, FM26 Visualiser: 공 위치 9칸을 눌러 모양 변화 확인, 공격/수비 전환).
    // 공이 어느 칸에 있을 때 11명이 어디에 서는지를 경기와 같은 함수(PositionRules.AttackStand·DefendStand, 수비 이동량은 LineRules)로 계산한다. 그래서 보이는 것 = 실제.
    // 보이지 않는 것: 압박·패스·온사이드 클램프 같은 행동(상대 위치가 필요). 화면은 "자리만 보인다"고 한 줄로 적는다.
    // 좌표는 팀 0(+X 공격) 기준 미터. 화면이 앵커로 바꾼다. 상태 없음, 엔진 없음
    public static class TacticsPreview
    {
        public const int Cells = 3;   // 열(우리 진영·중원·상대 진영) · 행(왼쪽·중앙·오른쪽) 각 3
        private const int MyTeamSign = +1;   // 전술판은 내 팀만 보이고 내 팀은 늘 팀 0(+X 공격, FieldBounds). 상대 미리보기는 없다

        // [가정] 칸 중심 = 필드 3등분 중심. 열은 서드 중심 x(−35·0·35 = 서드 경계 17.5와 골라인 52.5의 가운데), 행은 폭 3분할 중심 z(−22.7·0·22.7). FM26은 칸 좌표를 공개하지 않는다
        private const float ThirdCenterX = (FieldBounds.HalfLength + MatchTuning.ThirdBoundary) / 2f;
        private const float LateralCenterZ = FieldBounds.HalfWidth * 2f / 3f;

        public static (float x, float z) BallCell(int column, int row)
        {
            if (column < 0 || column >= Cells) { throw new ArgumentOutOfRangeException(nameof(column), column, "공 칸 열은 0~2"); }
            if (row < 0 || row >= Cells) { throw new ArgumentOutOfRangeException(nameof(row), row, "공 칸 행은 0~2"); }
            return ((column - 1) * ThirdCenterX, (row - 1) * LateralCenterZ);
        }

        // 라인업 순서대로 자리(선발 11명을 넘기면 11개). defending = 상대 소유(수비 시 자리 기준), 아니면 아군 소유(공격 시 자리 기준).
        // 수비 이동량 Δ는 경기와 같은 LineRules 식으로 라인업에서 직접 계산한다(11-08 D2): 편성 뒷줄 깊이·GK 라인은 라인업이 정하고, 공 깊이는 칸이 정한다
        public static (float x, float z)[] Positions(IReadOnlyList<LineupSlot> lineup, TeamTactics tactics, float ballX, float ballZ, bool defending)
        {
            var result = new (float x, float z)[lineup.Count];
            float lineShift = 0f;
            float depthCap = float.MaxValue;
            if (defending)
            {
                float keeperLineDepth = KeeperLineDepthOf(lineup);
                float ballDepth = LineRules.HomeDepth(ballX, MyTeamSign);
                lineShift = LineRules.TeamLineShift(ballDepth, tactics.DefenseLine, keeperLineDepth, RestDepthOf(lineup));
                depthCap = LineRules.TeamStandDepthCap(ballDepth, keeperLineDepth);
            }
            for (int i = 0; i < lineup.Count; i++)
            {
                LineupSlot slot = lineup[i];
                if (defending)
                {
                    result[i] = PositionRules.DefendStand(slot.DefendX, slot.DefendZ, MyTeamSign, slot.Stats, ballZ, lineShift, depthCap, slot.Stats.IsKeeper);
                }
                else
                {
                    result[i] = PositionRules.AttackStand(slot.AttackX, slot.AttackZ, MyTeamSign, tactics, slot.Stats, ballX, ballZ, slot.Stats.IsKeeper);
                }
            }
            return result;
        }

        // 시뮬(MatchSimulation.FillLineDepth)과 같은 입력: GK 라인 깊이(GK가 없으면 기본값)
        private static float KeeperLineDepthOf(IReadOnlyList<LineupSlot> lineup)
        {
            for (int i = 0; i < lineup.Count; i++)
            {
                LineupSlot slot = lineup[i];
                if (!slot.Stats.IsKeeper) { continue; }
                float keeperX = PositionRules.KeeperLineX(slot.DefendX, slot.DefendZ, MyTeamSign, slot.Stats.LineHeight);
                return LineRules.HomeDepth(keeperX, MyTeamSign);
            }
            return MatchTuning.DefaultKeeperLineDepth;
        }

        // 시뮬(MatchSimulation.DialedRestDepth)과 같은 입력: 필드 선수 다이얼 얹은 수비 시 자리 깊이 최솟값. 필드 선수가 없으면 float.MaxValue
        private static float RestDepthOf(IReadOnlyList<LineupSlot> lineup)
        {
            float restDepth = float.MaxValue;
            for (int i = 0; i < lineup.Count; i++)
            {
                LineupSlot slot = lineup[i];
                if (slot.Stats.IsKeeper) { continue; }
                restDepth = Math.Min(restDepth, LineRules.DialedHomeDepth(slot.DefendX, MyTeamSign, slot.Stats.LineHeight));
            }
            return restDepth;
        }
    }
}
