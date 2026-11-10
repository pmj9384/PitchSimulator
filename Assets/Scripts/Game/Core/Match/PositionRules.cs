using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Placement;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    // 자리·구역 판정 순수 함수(09-18 확정 스펙 §6). 상태 없음, 엔진 없음.
    // 서드는 콜라이더가 아니라 공 X 비교 한 줄이다(잡기 판정과 같은 이유: PhysX 콜백 순서·EditMode 불가).
    // 공은 하나라 서드도 팀당 하나. MatchSimulation이 틱마다 한 번 계산해 컨텍스트에 넣는다. AttackStand는 컨텍스트 없이 쓰이는 전술판용이라 같은 식(ThirdOf)으로 직접 센다(10-29).
    public static class PositionRules
    {
        // 공이 있는 세로 구역. attackSign = +1(팀 0, +X 공격)이면 x ≤ −17.5가 우리 진영. 경계는 진영 쪽에 포함
        public static Third ThirdOf(float ballX, int attackSign)
        {
            float forward = ballX * attackSign;   // 우리 골에서 상대 골 쪽으로 잰 값
            if (forward <= -MatchTuning.ThirdBoundary) { return Third.Own; }
            if (forward >= MatchTuning.ThirdBoundary) { return Third.Opponent; }
            return Third.Middle;
        }

        // 역습 판정의 재료: 공보다 상대 골 쪽에 있는 상대 필드 플레이어 수. GK는 뺀다(늘 뒤에 있어 조직 붕괴와 무관)
        public static int CountDefendersAhead(float ballX, IReadOnlyList<TargetInfo> opponents, int attackSign, int keeperId)
        {
            int count = 0;
            float ballForward = ballX * attackSign;
            for (int i = 0; i < opponents.Count; i++)
            {
                if (opponents[i].PlayerId == keeperId) { continue; }
                if (opponents[i].X * attackSign > ballForward) { count++; }
            }
            return count;
        }

        // 아군 소유 때 서는 자리. 배치(공격 시 자리) + X 전진(팀 전진 정도 + 개인 전진 폭) + Z 폭(서드별 팀 폭 배율 × 개인 측면 쏠림 × 다이얼 배율).
        // 다이얼 배율(PositionDialScale 0.3, 09-26)은 측면 쏠림·라인 높이에만: 선수표 값을 미터로 그대로 더하면 폭이 필드 밖(FB 42·W 49)으로 나가고 수비 라인이 겹친다
        // 폭은 선수가 선 쪽(z 부호)으로 벌린다. 중앙(z=0)은 안 벌린다. 필드 안으로만 자른다.
        // 이탈 반경(roamRadius)으로는 안 자른다: 전진 폭은 공격 자리를 정의하는 값이고 이탈 반경은 그 자리에서 벗어나는 허용치라 다른 축이다.
        // 09-18 첫 구현이 둘을 묶어 ST(전진 30, 이탈 3)가 3m만 올라가 공격 형태가 자기 진영에 갇혔고 3분 동안 슛이 0이었다
        public static (float x, float z) AttackHome(float baseX, float baseZ, int attackSign, int mentality, float pushUp, int widthLevel, float width)
        {
            float dx = (MatchTuning.MentalityOffset[mentality] + pushUp) * attackSign;   // 전진 폭은 미터 그대로: 편성 posX가 이 전제로 맞춰져 있다. 0.3을 걸면 공격이 상대 진영에 못 간다(09-26 100판 슛 0.1)
            float dz = width * MatchTuning.PositionDialScale * MatchTuning.WidthScale[widthLevel] * Math.Sign(baseZ);
            return Offset(baseX, baseZ, dx, dz);
        }

        // 상대 소유 때 서는 자리. 배치(수비 시 자리)에서 개인 라인 높이 × 다이얼 배율 + 팀 이동량(lineShift)만큼 앞으로. 라인 간격 자체는 편성 posX2가 정한다(스펙 §6).
        // lineShift = 팀 라인 깊이 Δ(LineRules.LineShift, 11-08 D2). GK는 0을 넘긴다: GK 라인(KeeperLineX)은 슛 대응의 기준이라 팀 라인을 따라 움직이지 않는다(10-27)
        public static (float x, float z) DefendHome(float baseX, float baseZ, int attackSign, float lineHeight, float lineShift)
        {
            return Offset(baseX, baseZ, (lineHeight * MatchTuning.PositionDialScale + lineShift) * attackSign, 0f);
        }

        // GK가 지키는 라인 x = 수비 시 자리의 x. 시뮬의 닿는 범위 판정(PullAimIntoReach)과 트리의 이동(MoveToShotLine)이 이 한 함수를 써서 같은 점을 본다(10-25)
        public static float KeeperLineX(float defendHomeX, float defendHomeZ, int attackSign, float lineHeight)
        {
            return DefendHome(defendHomeX, defendHomeZ, attackSign, lineHeight, 0f).x;   // GK엔 팀 이동량이 없다(DefendStand가 GK면 0을 넘기는 것과 같은 점)
        }

        // 실제로 서는 자리 = 홈 + 공 지향 슬라이드. 트리(MoveToAttackHome·MoveToDefendHome)와 전술판 미니 피치(TacticsPreview)가 이 두 함수를 같이 부른다(10-29 ③d).
        // 합성을 한 곳에 둔 이유: 미니 피치가 "보이는 것 = 실제"를 약속하는데, 합성이 트리 안에만 있으면 둘이 따로 바뀔 수 있다.
        // 서드는 공 x로 여기서 센다(시뮬이 컨텍스트에 넣는 BallThird와 같은 식 ThirdOf). 온사이드 클램프는 상대 위치가 필요해 트리에 남는다.
        // 읽는 값: 공격은 팀 전술 Mentality·Width[서드]와 선수 PushUp·Width, 수비는 팀 이동량 lineShift(팀 수비 라인 높이는 그 안에 들어 있다)·깊이 상한 depthCap과 선수 LineHeight.
        // GK 슛 대응 라인(KeeperLineX)은 이동 목표가 아니라 닿음 판정의 기준점이라 여기 안 넣는다.
        // goalkeeper는 Slide처럼 따로 받는다: 시뮬의 GK 여부(PlayerState.IsGoalkeeper, 명부 keeperIds와 짝)가 기준이고 stats.IsKeeper는 그 출처일 뿐이다
        public static (float x, float z) AttackStand(float baseX, float baseZ, int attackSign, TeamTactics tactics, PlayerStats stats, float ballX, float ballZ, bool goalkeeper)
        {
            int third = (int)ThirdOf(ballX, attackSign);
            (float x, float z) home = AttackHome(baseX, baseZ, attackSign, tactics.Mentality, stats.PushUp, tactics.Width[third], stats.Width);
            return SlideAttack(home.x, home.z, ballX, ballZ, goalkeeper);
        }

        // depthCap = 필드 선수가 설 수 있는 가장 앞 깊이(LineRules.StandDepthCap: 공보다 골 쪽, 11-09). 개인 라인 높이 다이얼이 Δ 위에 얹혀 뒷줄이 공보다 앞에 서지 않게 자른다. GK는 안 자른다
        public static (float x, float z) DefendStand(float baseX, float baseZ, int attackSign, PlayerStats stats, float ballZ, float lineShift, float depthCap, bool goalkeeper)
        {
            if (goalkeeper)
            {
                (float x, float z) keeperHome = DefendHome(baseX, baseZ, attackSign, stats.LineHeight, 0f);
                return SlideDefend(keeperHome.x, keeperHome.z, ballZ, goalkeeper: true);
            }
            (float x, float z) home = DefendHome(baseX, baseZ, attackSign, stats.LineHeight, lineShift);
            float x = home.x;
            if (LineRules.HomeDepth(x, attackSign) > depthCap) { x = LineRules.XAtDepth(depthCap, attackSign); }
            return SlideDefend(x, home.z, ballZ, goalkeeper: false);
        }

        // 공 지향 슬라이드 두 가지(09-21 유저 "자리가 완전 고정은 아닌 것 같다"). 공 좌표 기준이라 팀 부호가 필요 없다. 결과는 필드 안으로 클램프.
        // 식의 근거·버린 안·수치는 실험/2026-10-25-슬라이드-수축-격자.md. 비소유 폭·라인 높이는 ③ 가속·F3·④ 공 높이가 함께 다룬다.

        // 아군 소유 때: 세로는 공 x × 계수 평행이동(상한), 가로는 (공 z − 자리 z) × 계수 수축. 공에서 먼 선수가 더 들어와 소유 폭이 준다(10-25).
        // 전원이 같은 양을 더하는 평행이동은 폭을 못 줄였고, 공이 중앙이어도 당기므로 편성 자리 = 가장 벌어진 모양(전술판 PitchView도 같은 식).
        public static (float x, float z) SlideAttack(float homeX, float homeZ, float ballX, float ballZ, bool goalkeeper)
        {
            if (goalkeeper) { return SlideKeeper(homeX, homeZ, ballZ); }
            float dx = VerticalShift(ballX, MatchTuning.SlideVerticalAttack, MatchTuning.SlideVerticalMaxAttack);
            float dz = (ballZ - homeZ) * MatchTuning.SlideLateralAttack;
            return Offset(homeX, homeZ, dx, dz);
        }

        // 상대 소유 때: 가로만 공 z × 계수 평행이동. 블록이 공 쪽으로 옮기되 폭은 그대로다. 세로는 라인 깊이(LineRules, 11-08 D2)가 Δ로 맡는다.
        // 수비까지 수축하면 공격 수축과 겹쳐 내 빌드업 패스가 상대 ST 앞에서 끊겨 4부 승률이 무너졌다(10-25 격자)
        public static (float x, float z) SlideDefend(float homeX, float homeZ, float ballZ, bool goalkeeper)
        {
            if (goalkeeper) { return SlideKeeper(homeX, homeZ, ballZ); }
            return Offset(homeX, homeZ, 0f, ballZ * MatchTuning.SlideLateralDefend);
        }

        // GK는 가로만 조금(SlideLateralKeeper), 세로 0. 라인을 따라 나오면 안 된다
        private static (float x, float z) SlideKeeper(float homeX, float homeZ, float ballZ)
        {
            return Offset(homeX, homeZ, 0f, ballZ * MatchTuning.SlideLateralKeeper);
        }

        private static float VerticalShift(float ballX, float coefficient, float max)
        {
            float dx = ballX * coefficient;
            if (dx > max) { return max; }
            if (dx < -max) { return -max; }
            return dx;
        }

        private static (float x, float z) Offset(float baseX, float baseZ, float dx, float dz)
        {
            // 세로는 필드 끝까지, 가로는 터치라인에서 TouchlineMargin 안쪽까지(공 쪽 윙어가 라인 위에 붙어 빌드업하던 것)
            return (Clamp(baseX + dx, FieldBounds.HalfLength, FieldBounds.EdgeMargin), Clamp(baseZ + dz, FieldBounds.HalfWidth, MatchTuning.TouchlineMargin));
        }

        private static float Clamp(float v, float half, float margin)
        {
            float limit = half - margin;
            if (v > limit) { return limit; }
            if (v < -limit) { return -limit; }
            return v;
        }
    }
}
