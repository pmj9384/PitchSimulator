using System;
using Game.Core.Tactics;

namespace Game.Core.Match
{
    // 로비 홈의 "지난 경기 설정 대 실측" 한 줄 재료(스펙 §2-1, 10-15). 실측(TacticsReadout)은 인게임이 휘슬 때 만들고 결과 화면이 쓴 뒤 버리므로,
    // 로비가 읽을 숫자만 떼어 세이브에 남긴다. 그때의 설정 3칸을 같이 담는 이유: 홈에 올 때쯤 전술을 바꿨을 수 있어 "지금 설정 대 지난 실측"이 되면 거짓말이다
    public sealed class TacticsReadoutSummary
    {
        private const int Thirds = 3;

        private readonly int[] passCounts;
        private readonly float[] meanPassLengths;
        private readonly int[] regainCounts;

        public int Counters { get; }
        public int CounterShots { get; }
        public int Shots { get; }
        public int SettingPassStyle { get; }    // 그 경기의 중원 패스 방식(TacticLevelNames.PassStyle 번호)
        public int SettingPressStart { get; }   // 그 경기의 상대 진영 압박 시작(TacticLevelNames.PressStart 번호)
        public int SettingCounter { get; }      // 그 경기의 역습 성향

        public TacticsReadoutSummary(int[] passCounts, float[] meanPassLengths, int[] regainCounts, int counters, int counterShots, int shots,
            int settingPassStyle, int settingPressStart, int settingCounter)
        {
            if (passCounts.Length != Thirds || meanPassLengths.Length != Thirds || regainCounts.Length != Thirds)
            {
                throw new ArgumentException($"[TacticsReadoutSummary] 서드 배열은 {Thirds}칸이어야 한다");
            }
            // 설정 3칸은 홈 한 줄이 이름 배열(TacticLevelNames)에 바로 넣으므로 여기서 범위를 본다. 세이브가 깨졌거나 단계 수가 바뀐 옛 세이브를 걸러 준다
            if (!IsLevel(settingPassStyle) || !IsLevel(settingPressStart) || !IsLevel(settingCounter))
            {
                throw new ArgumentException("[TacticsReadoutSummary] 설정 값은 0~" + (TeamTactics.Levels - 1) + " 범위여야 한다");
            }
            if (counters < 0 || counterShots < 0 || shots < 0)
            {
                throw new ArgumentException("[TacticsReadoutSummary] 횟수는 음수일 수 없다");
            }
            this.passCounts = passCounts;
            this.meanPassLengths = meanPassLengths;
            this.regainCounts = regainCounts;
            Counters = counters;
            CounterShots = counterShots;
            Shots = shots;
            SettingPassStyle = settingPassStyle;
            SettingPressStart = settingPressStart;
            SettingCounter = settingCounter;
        }

        private static bool IsLevel(int value)
        {
            return value >= 0 && value < TeamTactics.Levels;
        }

        public static TacticsReadoutSummary From(TacticsReadout readout, TeamTactics setting)
        {
            var passCounts = new int[Thirds];
            var lengths = new float[Thirds];
            var regains = new int[Thirds];
            for (int i = 0; i < Thirds; i++)
            {
                var third = (Third)i;
                passCounts[i] = readout.PassCount(third);
                lengths[i] = readout.MeanPassLength(third);
                regains[i] = readout.RegainCount(third);
            }
            return new TacticsReadoutSummary(passCounts, lengths, regains, readout.Counters, readout.CounterShots, readout.Shots,
                setting.PassStyle[(int)Third.Middle], setting.PressStart[(int)Third.Opponent], setting.Counter);
        }

        public int PassCount(Third third)
        {
            return passCounts[(int)third];
        }

        public float MeanPassLength(Third third)
        {
            return meanPassLengths[(int)third];
        }

        public int RegainCount(Third third)
        {
            return regainCounts[(int)third];
        }

        // 세이브용 복사본. 내부 배열을 그대로 내주면 DTO가 같은 배열을 들고 있다가 한쪽이 바뀌면 같이 바뀐다
        public int[] CopyPassCounts()
        {
            return (int[])passCounts.Clone();
        }

        public float[] CopyMeanPassLengths()
        {
            return (float[])meanPassLengths.Clone();
        }

        public int[] CopyRegainCounts()
        {
            return (int[])regainCounts.Clone();
        }
    }
}
