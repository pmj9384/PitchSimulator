using System;
using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Placement;

namespace Game.Core.Tactics
{
    // 개인 지시로 고칠 수 있는 다이얼. 판정에 실제로 쓰이는 것만 있다(자리 이탈 반경은 판정이 없어 뺐다. 스펙 §4-2 "판정 없는 슬라이더는 뺀다").
    // 순서가 곧 세이브의 칸 순서다(SeasonSave.Player.instructions). 중간에 끼워 넣지 않고 끝에 붙인다
    public enum PlayerDial
    {
        PushUp,        // 전진 폭
        Width,         // 측면 쏠림
        PressRange,    // 압박 거리
        LineHeight,    // 라인 높이
        ShotBias,      // 슛 성향
        PassLength,    // 패스 길이
        PassRisk,      // 패스 리스크
        Dribble,       // 드리블 성향
        HoldUp,        // 볼 끌기
        GkRushRadius,  // GK 출격 반경(GK만)
    }

    // 개인 지시(09-30, 스펙 §4-2 개인 전술·전술-기획 3-2)의 순수 판정. 선수는 역할(다이얼 묶음)을 갖고, 개인 지시는 그 역할 값에서 다이얼마다 −2~+2칸 옮긴다.
    // 값 하나를 직접 고르게 하지 않고 "역할보다 더/덜"로 둔 이유: 팀 슬라이더(카드 기준 오프셋)와 같은 구조라 읽기 쉽고,
    // 역할 표(PlayerTable)의 값을 고쳐도 세이브의 지시가 상대값이라 그대로 뜻이 통한다. FC 온라인 개인 전술도 항목마다 단계 선택이다.
    // 한 칸의 크기는 출처가 없다(FM·FC는 "더/덜"만 주고 수치를 안 준다). ±2칸이 역할 간 차이(PlayerTable의 다이얼 범위)와 비슷한 폭이 되게 잡은 출발값이다
    public static class PlayerInstructions
    {
        public const int OffsetMin = -2;
        public const int OffsetMax = 2;
        public const int DialCount = 10;

        // PlayerDial 순서. 슛 성향은 부호가 반대다: ShotBias는 "이 값 × 0.5 이상의 xG면 쏜다"는 문턱이라 낮을수록 자주 쏜다. 지시의 +를 "더 자주"로 맞추려고 음수 칸
        private static readonly float[] Step = { 5f, 4f, 2f, 4f, -0.05f, 5f, 0.1f, 0.15f, 0.15f, 2f };
        private static readonly float[] Min = { 0f, 0f, 0f, 0f, 0.05f, 5f, 0f, 0f, 0f, 6f };
        private static readonly float[] Max = { 40f, 30f, 22f, 30f, 1f, 45f, 1f, 1f, 1.5f, FieldBounds.PenaltyBoxDepth };   // GK 출격은 박스 깊이가 상한(스펙 §5)

        // 역할 값 + 지시 → 경기에 들어가는 값. 원본은 바꾸지 않는다
        public static PlayerStats Apply(PlayerStats roleStats, IReadOnlyList<int> offsets)
        {
            Validate(offsets, roleStats);

            PlayerStats s = roleStats.Clone();
            for (int i = 0; i < DialCount; i++)
            {
                if (offsets[i] == 0) { continue; }
                var dial = (PlayerDial)i;
                float shifted = ValueOf(roleStats, dial) + offsets[i] * Step[i];
                SetValue(s, dial, Math.Clamp(shifted, Min[i], Max[i]));
            }
            return s;
        }

        // 세이브·입력의 관문. 칸 수, 범위, 그 선수에게 해당 없는 다이얼(골키퍼의 필드 지시 등)
        public static void Validate(IReadOnlyList<int> offsets, PlayerStats roleStats)
        {
            if (offsets == null || offsets.Count != DialCount) { throw new InvalidOperationException($"[PlayerInstructions] 개인 지시는 {DialCount}칸이어야 한다"); }
            for (int i = 0; i < DialCount; i++)
            {
                if (offsets[i] < OffsetMin || offsets[i] > OffsetMax) { throw new InvalidOperationException($"[PlayerInstructions] {(PlayerDial)i} 지시는 {OffsetMin}~{OffsetMax} ({offsets[i]})"); }
                if (offsets[i] != 0 && !AppliesTo((PlayerDial)i, roleStats)) { throw new InvalidOperationException($"[PlayerInstructions] {roleStats.RoleId}에게 {(PlayerDial)i} 지시는 해당 없다"); }
            }
        }

        // 골키퍼는 출격 반경만, 필드 선수는 출격 반경만 빼고. 골키퍼의 배급은 팀 전술(GK 배급)이 정한다
        public static bool AppliesTo(PlayerDial dial, PlayerStats roleStats)
        {
            bool isKeeper = roleStats.IsKeeper;
            bool keeperDial = dial == PlayerDial.GkRushRadius;
            return isKeeper == keeperDial;
        }

        public static float ValueOf(PlayerStats s, PlayerDial dial)
        {
            switch (dial)
            {
                case PlayerDial.PushUp: return s.PushUp;
                case PlayerDial.Width: return s.Width;
                case PlayerDial.PressRange: return s.PressRange;
                case PlayerDial.LineHeight: return s.LineHeight;
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
                case PlayerDial.LineHeight: s.LineHeight = value; break;
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
