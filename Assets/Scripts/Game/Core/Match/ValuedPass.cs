namespace Game.Core.Match
{
    // 값이 매겨진 패스 후보 하나(11-03 P1). 없으면 None(Pass.Found가 거짓). ④가 이 후보를 그대로 실행하고 ②c는 같은 값을 운반 값과 견준다(판정과 실행이 같은 후보)
    public readonly struct ValuedPass
    {
        public static readonly ValuedPass None = new ValuedPass(0f, PassChoice.None);

        public readonly float Value;
        public readonly PassChoice Pass;

        public ValuedPass(float value, PassChoice pass)
        {
            Value = value;
            Pass = pass;
        }

        public bool Found => Pass.Found;
    }
}
