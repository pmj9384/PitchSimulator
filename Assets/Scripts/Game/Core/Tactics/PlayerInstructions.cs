using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Placement;

namespace Game.Core.Tactics
{
    // 개인 지시로 고칠 수 있는 다이얼. 판정에 실제로 쓰이는 것만 있다(자리 이탈 반경은 판정이 없어 뺐다. 스펙 §4-2 "판정 없는 슬라이더는 뺀다").
    // 순서가 곧 세이브의 칸 순서다(SlotTacticsSave.instructions). 중간에 끼워 넣지 않고 끝에 붙인다.
    // 10-27: 라인 높이 다이얼을 뺐다. 수비 역할(DefenseRoles.csv lineHeightOffset)이 같은 값을 정해 두 곳이 됐다(스펙 §4-2). 옛 세이브의 10칸은 FromLegacy가 3번 칸을 버린다
    public enum PlayerDial
    {
        PushUp,        // 전진 폭
        Width,         // 측면 쏠림
        PressRange,    // 압박 거리
        ShotBias,      // 슛 성향
        PassLength,    // 패스 길이
        PassRisk,      // 패스 리스크
        Dribble,       // 드리블 성향
        HoldUp,        // 볼 끌기
        GkRushRadius,  // GK 출격 반경(GK만)
    }

    // 개인 지시(09-30, 스펙 §4-2 개인 전술·전술-기획 3-2)의 순수 판정. 슬롯은 역할(다이얼 묶음)을 갖고, 개인 지시는 그 역할 값에서 다이얼마다 −2~+2칸 옮긴다.
    // 값 하나를 직접 고르게 하지 않고 "역할보다 더/덜"로 둔 이유: 팀 슬라이더(카드 기준 오프셋)와 같은 구조라 읽기 쉽고,
    // 역할 표(PlayerTable)의 값을 고쳐도 세이브의 지시가 상대값이라 그대로 뜻이 통한다. FC 온라인 개인 전술도 항목마다 단계 선택이다.
    // 한 칸의 크기는 출처가 없다(FM·FC는 "더/덜"만 주고 수치를 안 준다). ±2칸이 역할 간 차이(PlayerTable의 다이얼 범위)와 비슷한 폭이 되게 잡은 출발값이다.
    // 10-27 슬롯 중심: 지시는 선수가 아니라 자리(슬롯)의 것이다. 어느 선수가 서든 같은 오프셋이 얹히고, 그 선수의 역할 값 범위를 넘으면 잘라낸다(Apply는 던지지 않는다)
    public static class PlayerInstructions
    {
        public const int OffsetMin = -2;
        public const int OffsetMax = 2;
        public const int DialCount = 9;
        public const int LegacyDialCount = 10;        // 10-27 전 세이브의 칸 수
        private const int LegacyLineHeightIndex = 3;  // 그중 버리는 칸

        // PlayerDial 순서. 슛 성향은 부호가 반대다: ShotBias는 "이 값 × 0.5 이상의 xG면 쏜다"는 문턱이라 낮을수록 자주 쏜다. 지시의 +를 "더 자주"로 맞추려고 음수 칸
        private static readonly float[] Step = { 5f, 4f, 2f, -0.05f, 5f, 0.1f, 0.15f, 0.15f, 2f };
        private static readonly float[] Min = { 0f, 0f, 0f, 0.05f, 5f, 0f, 0f, 0f, 6f };
        private static readonly float[] Max = { 40f, 30f, 22f, 1f, 45f, 1f, 1f, 1.5f, FieldBounds.PenaltyBoxDepth };   // GK 출격은 박스 깊이가 상한(스펙 §5)

        public static int[] Empty()
        {
            return new int[DialCount];
        }

        // 역할 값 + 지시 → 경기에 들어가는 값. 원본은 바꾸지 않는다. 범위 밖은 잘라내고 그 자리에 해당 없는 다이얼은 무시한다(슬롯 지시가 다른 변형 선수에게 얹힐 때)
        public static PlayerStats Apply(PlayerStats roleStats, IReadOnlyList<int> offsets)
        {
            if (offsets == null || offsets.Count != DialCount) { throw new InvalidOperationException($"[PlayerInstructions] 개인 지시는 {DialCount}칸이어야 한다"); }

            PlayerStats s = roleStats.Clone();
            for (int i = 0; i < DialCount; i++)
            {
                if (offsets[i] == 0) { continue; }
                var dial = (PlayerDial)i;
                if (!AppliesTo(dial, roleStats.RoleId)) { continue; }
                int offset = Math.Clamp(offsets[i], OffsetMin, OffsetMax);
                float shifted = ValueOf(roleStats, dial) + offset * Step[i];
                SetValue(s, dial, Math.Clamp(shifted, Min[i], Max[i]));
            }
            return s;
        }

        // 입력·세이브 검증(SlotTactics가 부른다). 칸 수, 범위, 그 자리에 해당 없는 다이얼
        public static void Validate(IReadOnlyList<int> offsets, string slotRoleId)
        {
            if (offsets == null || offsets.Count != DialCount) { throw new InvalidOperationException($"[PlayerInstructions] 개인 지시는 {DialCount}칸이어야 한다"); }
            for (int i = 0; i < DialCount; i++)
            {
                if (offsets[i] < OffsetMin || offsets[i] > OffsetMax) { throw new InvalidOperationException($"[PlayerInstructions] {(PlayerDial)i} 지시는 {OffsetMin}~{OffsetMax} ({offsets[i]})"); }
                if (offsets[i] != 0 && !AppliesTo((PlayerDial)i, slotRoleId)) { throw new InvalidOperationException($"[PlayerInstructions] {slotRoleId} 자리에 {(PlayerDial)i} 지시는 해당 없다"); }
            }
        }

        // 자리(포지션)마다 보이는 다이얼(10-27, 근거 실험/2026-10-26-FM26-슬롯-역할-개인지시-레퍼런스.md 7절 (b). 값은 추정).
        // FM처럼 목록은 하나고 자리에 안 맞는 항목을 숨긴다. GK는 출격 반경만(배급은 팀 전술 GK 배급). 수비수는 슛·드리블·볼 끌기 없음. CB는 측면 쏠림도 없음(커버 자리)
        public static bool AppliesTo(PlayerDial dial, string roleId)
        {
            if (string.Equals(roleId, "GK", StringComparison.OrdinalIgnoreCase)) { return dial == PlayerDial.GkRushRadius; }
            if (dial == PlayerDial.GkRushRadius) { return false; }
            switch (RoleGroups.Of(roleId))
            {
                case RoleGroup.Defender: return DefenderDial(dial, roleId);
                default: return true;
            }
        }

        private static bool DefenderDial(PlayerDial dial, string roleId)
        {
            switch (dial)
            {
                case PlayerDial.ShotBias:
                case PlayerDial.Dribble:
                case PlayerDial.HoldUp:
                    return false;
                case PlayerDial.Width:
                    return !string.Equals(roleId, "CB", StringComparison.OrdinalIgnoreCase);
                default:
                    return true;
            }
        }

        // 세이브·이행에서 온 지시를 그 자리에 맞게 다듬는다: 범위 밖은 잘라내고 해당 없는 칸은 0. Apply가 무시하는 값이 남아 있으면 나중에 다른 칸을 만질 때 Validate가 던진다(10-27 리뷰 🟡)
        public static int[] Normalize(IReadOnlyList<int> offsets, string slotRoleId)
        {
            if (offsets == null || offsets.Count != DialCount) { throw new InvalidOperationException($"[PlayerInstructions] 개인 지시는 {DialCount}칸이어야 한다"); }
            int[] next = Empty();
            for (int i = 0; i < DialCount; i++)
            {
                if (!AppliesTo((PlayerDial)i, slotRoleId)) { continue; }
                next[i] = Math.Clamp(offsets[i], OffsetMin, OffsetMax);
            }
            return next;
        }

        // 10-27 전 세이브의 10칸(라인 높이 포함)을 9칸으로. 이미 9칸이면 그대로, null이면 0
        public static int[] FromLegacy(int[]? saved)
        {
            if (saved == null) { return Empty(); }
            if (saved.Length == DialCount) { return (int[])saved.Clone(); }
            if (saved.Length != LegacyDialCount) { throw new InvalidOperationException($"[PlayerInstructions] 세이브의 지시가 {saved.Length}칸이다({DialCount} 또는 {LegacyDialCount})"); }
            int[] next = Empty();
            int j = 0;
            for (int i = 0; i < LegacyDialCount; i++)
            {
                if (i == LegacyLineHeightIndex) { continue; }
                next[j] = saved[i];
                j++;
            }
            return next;
        }

        // 다이얼 한 칸의 크기(부호 포함). 스카우트 별 판정이 변형 간 거리를 칸으로 재는 데 쓴다
        public static float StepOf(PlayerDial dial)
        {
            return Step[(int)dial];
        }

        public static float ValueOf(PlayerStats s, PlayerDial dial)
        {
            switch (dial)
            {
                case PlayerDial.PushUp: return s.PushUp;
                case PlayerDial.Width: return s.Width;
                case PlayerDial.PressRange: return s.PressRange;
                case PlayerDial.ShotBias: return s.ShotBias;
                case PlayerDial.PassLength: return s.PassLength;
                case PlayerDial.PassRisk: return s.PassRisk;
                case PlayerDial.Dribble: return s.Dribble;
                case PlayerDial.HoldUp: return s.HoldUp;
                case PlayerDial.GkRushRadius: return s.GkRushRadius;
                default: throw new ArgumentOutOfRangeException(nameof(dial), dial, "없는 다이얼");
            }
        }

        private static void SetValue(PlayerStats s, PlayerDial dial, float value)
        {
            switch (dial)
            {
                case PlayerDial.PushUp: s.PushUp = value; break;
                case PlayerDial.Width: s.Width = value; break;
                case PlayerDial.PressRange: s.PressRange = value; break;
                case PlayerDial.ShotBias: s.ShotBias = value; break;
                case PlayerDial.PassLength: s.PassLength = value; break;
                case PlayerDial.PassRisk: s.PassRisk = value; break;
                case PlayerDial.Dribble: s.Dribble = value; break;
                case PlayerDial.HoldUp: s.HoldUp = value; break;
                case PlayerDial.GkRushRadius: s.GkRushRadius = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(dial), dial, "없는 다이얼");
            }
        }
    }
}
