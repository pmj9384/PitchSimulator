using System;
using Game.Core.Data;

namespace Game.Core.Tactics
{
    // 수비 시 역할(10-25 슬롯 격자, FM26의 Out of Possession 역할을 우리 수비 다이얼 3개로 옮긴 것: 실험/2026-10-25-FM26-공수-포메이션-레퍼런스.md).
    // 공격 역할은 PlayerTable 변형(전진 폭·측면 쏠림·슛 성향·패스 길이…)이 그대로 맡고, 수비 역할은 그 위에 압박 거리·라인 높이·자리 이탈 반경만 덮는다.
    // 값은 DefenseRoles.csv. [가정] 배율·오프셋과 생성자 범위(배율 0.3~2, 오프셋 ±10m)는 FM 역할 이름의 뜻에서 옮긴 추정이라 출처가 없다. 러너로 맞춘다
    public sealed class DefenseRole
    {
        public const string DefaultId = "balanced";   // 역할 값 그대로(배율 1, 오프셋 0). 옛 세이브·새 시즌의 기본

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public float PressRangeScale { get; }    // 압박 거리 배율
        public float LineHeightOffset { get; }   // 라인 높이에 더하는 m(+면 앞으로)
        public float RoamRadiusScale { get; }    // 자리 이탈 반경 배율

        public DefenseRole(string id, string displayName, string description, float pressRangeScale, float lineHeightOffset, float roamRadiusScale)
        {
            if (string.IsNullOrEmpty(id)) { throw new ArgumentException("[DefenseRole] id가 비었다"); }
            if (pressRangeScale < 0.3f || pressRangeScale > 2f) { throw new ArgumentException($"[DefenseRole] {id}: 압박 거리 배율은 0.3~2 ({pressRangeScale})"); }
            if (lineHeightOffset < -10f || lineHeightOffset > 10f) { throw new ArgumentException($"[DefenseRole] {id}: 라인 높이 오프셋은 ±10m ({lineHeightOffset})"); }
            if (roamRadiusScale < 0.3f || roamRadiusScale > 2f) { throw new ArgumentException($"[DefenseRole] {id}: 이탈 반경 배율은 0.3~2 ({roamRadiusScale})"); }
            Id = id;
            DisplayName = displayName;
            Description = description;
            PressRangeScale = pressRangeScale;
            LineHeightOffset = lineHeightOffset;
            RoamRadiusScale = roamRadiusScale;
        }

        // 경기에 들어가는 값: 역할 값(+개인 지시) 위에 수비 다이얼 3개만 덮은 복사본. 원본은 안 바뀐다.
        // 라인 높이는 0에서 자른다: PlayerTable 불변식이 lineHeight ≥ 0이고(라인 높이 0인 변형이 23개), 음수면 DefendHome이 자리를 뒤로 밀어 "앞으로 서는 폭"이라는 뜻이 깨진다
        public PlayerStats Apply(PlayerStats stats)
        {
            PlayerStats copy = stats.Clone();
            copy.PressRange = stats.PressRange * PressRangeScale;
            copy.LineHeight = Math.Max(0f, stats.LineHeight + LineHeightOffset);
            copy.RoamRadius = stats.RoamRadius * RoamRadiusScale;
            return copy;
        }
    }
}
