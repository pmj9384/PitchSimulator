using System.Collections.Generic;
using Game.Core.Data;
using Game.Core.Match;
using Game.Core.Tactics;

namespace Game.Core.AI
{
    // BT가 선수에게 묻고(읽기) 시키는(행동) 유일한 창구. 트리는 MonoBehaviour를 모르고 이 계약만 본다.
    // 가짜 구현만 있으면 엔진 없이 트리를 테스트한다(CompositeNodeTests). 실제 구현은 순수 PlayerState(MatchSimulation 안).
    // 09-18 확장: 4국면 트리(스펙 §6)가 팀 전술·서드·역습·아군/상대·상대 GK·자리 2쌍을 읽고 Pass를 시킨다.
    // 스냅샷은 시뮬이 틱마다 한 번 채운다. 22명이 같은 스냅샷을 보고 판단해야 결정성이 선다.
    public interface IPlayerContext
    {
        // ── 나
        int PlayerId { get; }
        int Team { get; }
        int AttackSign { get; }        // +1 = +X 골을 노림(팀 0), -1 = 반대(팀 1)
        float X { get; }
        float Z { get; }
        PlayerStats Stats { get; }
        bool IsGoalkeeper { get; }

        // ── 공(이번 틱 스냅샷)
        BallPhase BallPhase { get; }
        bool OwnsBall { get; }
        float BallX { get; }
        float BallZ { get; }
        float BallVelX { get; }        // 공 속도(m/s). 비행·굴림은 물리, 소유 중은 소유자 이동 속도(09-23). 추격 예측용
        float BallVelZ { get; }
        int BallOwnerTeam { get; }     // 소유 팀 0/1, 없으면 -1

        // ── 팀 전술·국면(시뮬이 계산해 넣음)
        TeamTactics Tactics { get; }
        Third BallThird { get; }       // 내 팀 기준 공이 있는 서드
        bool IsCountering { get; }     // 우리 팀이 역습 중(공 앞쪽 상대 수비 수 ≤ 문턱)
        bool IsCounterPressing { get; } // 우리 팀이 역압박 중(뺏긴 직후 창 안 + 뒤 수비 충분)
        int PressRank { get; }          // 압박 거리 안인 우리 팀 선수 중 공에 더 가까운 사람 수(0 = 첫 압박자, 거리 밖 = int.MaxValue). MatchTuning.MaxPressers 미만만 압박(09-23)

        // ── 동료·상대·상대 GK(위치 스냅샷, 판정 함수 입력)
        IReadOnlyList<TargetInfo> Teammates { get; }   // 나 제외
        IReadOnlyList<TargetInfo> Opponents { get; }
        bool IsLooseBallChaser { get; }                // 소유 팀 없는 공의 우리 팀 추격자(필드 플레이어). 시뮬이 팀별 최근접 + 히스테리시스로 정한다(09-28 F1b)
        bool KeeperAlternate { get; }                  // GK 배급 "섞어"의 교대 스위치(팀별, 시뮬이 GK 패스마다 뒤집음). 09-23 R2
        int LastPasserId { get; }                      // 방금 나에게 준 아군(패스로 받았을 때만, 아니면 -1). 되돌림 금지용(09-23)
        PlayerStats? OpponentKeeper { get; }

        // ── 자리 2쌍(배치 좌표. 오프셋은 PositionRules가 붙임)
        float AttackHomeX { get; }
        float AttackHomeZ { get; }
        float DefendHomeX { get; }
        float DefendHomeZ { get; }

        // ── 패스 받기
        bool IsPassTarget { get; }     // 지금 날아오는 패스의 리시버가 나인가
        float PassTargetX { get; }     // 그 패스의 도착점
        float PassTargetZ { get; }

        // ── 시키기. 실행(속도·판정)은 시뮬 몫, 트리는 의도만 남긴다
        void MoveToward(float x, float z);
        void Shoot();
        void Pass(int receiverId);
    }
}
