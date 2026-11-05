namespace Game.Core.Match
{
    public static class ReceiverRules
    {
        // 방금 나에게 준 선수에게 곧바로 뒤로 되돌리는 패스의 감점(핑퐁 방지, 11-03 P1 [가정 P-G2]). 좌표는 공격 방향으로 잰 값(x × attackSign).
        // 착지가 패서보다 앞이면 원투(되돌려 받은 선수에게 앞 공간으로)라 감점이 없다. 같은 깊이는 옆으로 본다
        public static float ReturnPenaltyFor(float passerForward, float aimForward, bool isLastPasser)
        {
            if (!isLastPasser) { return 0f; }
            if (aimForward > passerForward) { return 0f; }
            return MatchTuning.ReturnPenalty;
        }
    }
}
