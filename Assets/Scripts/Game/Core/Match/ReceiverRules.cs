namespace Game.Core.Match
{
    // 패스 후보의 종류(11-03 ⑤ A1). 게이트를 리시버의 현재 위치가 아니라 공이 실제로 가는 점(착지점)으로 정한다.
    // 지금까지는 "리시버가 패서보다 뒤 + 압박 없음"이면 탈락이라, 패서 뒤에 있는 윙어가 앞 공간으로 달려가도 후보가 못 됐다(W 후보의 81%).
    // ⑥ 되돌림 강등과 ⑦ 컷백 리시버가 같은 줄을 건드리므로 분류를 한 함수에 둔다(컷백형은 ⑦이 한 칸 추가한다)
    public enum PassKind
    {
        Forward,   // 착지가 패서보다 앞: 압박과 무관하게 후보
        Sideways,  // 착지가 옆·뒤: 압박받을 때만 후보
        Return,    // 방금 나에게 준 선수에게 뒤로: 압박받을 때만, 다른 후보가 없을 때만 쓴다
        Gated,     // 후보가 아니다(옆·뒤인데 압박이 없다)
    }

    public static class ReceiverRules
    {
        // 좌표는 공격 방향으로 잰 값(x × attackSign). 착지가 패서와 같은 깊이면 옆으로 본다(옛 규칙 "리시버가 앞이 아니면 뒤"와 같은 경계)
        public static PassKind Classify(float passerForward, float aimForward, bool pressed, bool isLastPasser)
        {
            if (aimForward > passerForward) { return PassKind.Forward; }
            if (!pressed) { return PassKind.Gated; }
            if (isLastPasser) { return PassKind.Return; }
            return PassKind.Sideways;
        }

        // ②c 운반 대 침투 패스(11-03 ⑤ xT): 위협 증가(ThreatRules.CarryGain·PassGain)가 패스 쪽이 커야 운반을 접는다. 같으면 운반(10-08 결정 7을 안 바꾼다)
        public static bool PassBeatsCarry(float carryGain, float passGain)
        {
            return passGain > carryGain;
        }
    }
}
