using System;

namespace Game.Core.Match
{
    // 가속이 있는 이동(09-29 전술 상성 ③④ 1단계 F2b). 전엔 선수가 즉시 최고 속도로 뛰고 즉시 멈춰서 수비수가 순간적으로 뒤돌아 7m/s로 뛰었다.
    // 그래서 같은 속도의 공격수가 라인 뒤로 뛰어도 늘 수비가 먼저 닿아 스루패스·침투가 애초에 성립하지 않았다(침투-런-레퍼런스.md Q1·Q4).
    // 레퍼런스: Spearman 2017·2018(가속 7m/s², 방향 전환은 "멈췄다 다시 가속"으로 자연히 비용), Shaw pitch control(반응 지연 = 최고 속도 ÷ 가속).
    // 실측 대조: 정지 출발 10m 현실 2.04초(PMC9667937). 이 모델(가속 7, 최고 7)은 1.93초. 상태 없음, 난수 없음
    public static class MovementRules
    {
        // 한 틱 이동. 현재 속도는 a·dt만큼만 바뀐다. arrive(자리 잡기)면 원하는 속도 = min(최고 속도, 목표에서 멈출 수 있는 속도 √(2·a·거리))이고
        // 목표를 이번 틱 안에 지나치면 목표에 세우고 멈춘다. arrive가 아니면(공 쫓기·공 몰기) 최고 속도로 계속 간다.
        // Simple Soccer(Buckland, Reynolds 조향)의 Arrive와 Seek·Pursuit. 09-29 계측: 쫓기까지 감속하면 달아나는 공 운반자를 못 따라가 태클이 36.8 → 9.9회로 줄고,
        // 자리 잡기까지 감속 없이 가면 공 따라 조금씩 움직이는 자리를 매 틱 지나쳐 섰다 다시 가속해 슛이 12.5 → 7.1로 줄었다
        public static (float x, float z, float vx, float vz) Step(float x, float z, float vx, float vz, float targetX, float targetZ, float maxSpeed, float accel, float dt, bool arrive)
        {
            float dx = targetX - x;
            float dz = targetZ - z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist <= 0f) { return Accelerate(x, z, vx, vz, 0f, 0f, accel, dt); }

            float speed = arrive ? Math.Min(maxSpeed, (float)Math.Sqrt(2f * accel * dist)) : maxSpeed;
            (float x, float z, float vx, float vz) next = Accelerate(x, z, vx, vz, dx / dist * speed, dz / dist * speed, accel, dt);
            if (!arrive || !PassedTarget(x, z, next.x, next.z, targetX, targetZ)) { return next; }
            return (targetX, targetZ, 0f, 0f);
        }

        // 원하는 이동이 없을 때: 가속 한도 안에서 멈춘다
        public static (float x, float z, float vx, float vz) Brake(float x, float z, float vx, float vz, float accel, float dt)
        {
            return Accelerate(x, z, vx, vz, 0f, 0f, accel, dt);
        }

        // 정지한 선수가 가속 a로 최고 속도 v까지 가는 동안 잃는 거리는 "최고 속도로 v ÷ 2a초 늦게 출발한 것"과 같다.
        // 판단 쪽(가로채기 위험·돌파 방향)의 상대 도달 시간에 이 값을 더하면 실제 이동과 같은 전제가 된다(Shaw는 최고 속도 5에서 0.7초)
        public static float ReactionEquivalent(float maxSpeed, float accel)
        {
            return maxSpeed / (2f * accel);
        }

        // 개인 가속(10-29): 반응 등가가 모든 선수에게 reaction으로 같아지는 가속 = 최고 속도 ÷ (2 × reaction). 최고 속도까지 2 × reaction초.
        // 전원 7로 같으면 짧은 거리에서 속도 스탯 차이가 묻혀 4부 승률이 59 → 39%로 떨어졌고(속도 비례면 55%),
        // 판정의 상수 반응 지연(가로채기·돌파)이 speed 50 선수에게만 맞았다. 기록 실험/2026-10-29-가속-설계-스펙.md 8절
        public static float AccelerationFor(float maxSpeed, float reaction)
        {
            return maxSpeed / (2f * reaction);
        }

        // 자리 잡기 속도 상한(10-29 가속 C): 자리까지 lateDistance 안이면 조깅(최고 속도 × jogFraction), 그보다 늦었으면 전력으로 복귀.
        // 전엔 공 따라 틱마다 움직이는 자리를 전원이 최고 속도로 쫓아 스프린트가 현실의 70배였다(움직임-현실성-대조 #1·#3)
        public static float PositioningSpeed(float maxSpeed, float distanceToSpot, float jogFraction, float lateDistance)
        {
            if (distanceToSpot > lateDistance) { return maxSpeed; }
            return maxSpeed * jogFraction;
        }

        // 속도 크기를 상한으로 자른다. 방향은 그대로
        public static (float vx, float vz) ClampSpeed(float vx, float vz, float maxSpeed)
        {
            float speed = (float)Math.Sqrt(vx * vx + vz * vz);
            if (speed <= maxSpeed) { return (vx, vz); }
            return (vx / speed * maxSpeed, vz / speed * maxSpeed);
        }

        private static (float x, float z, float vx, float vz) Accelerate(float x, float z, float vx, float vz, float wantVx, float wantVz, float accel, float dt)
        {
            float dvx = wantVx - vx;
            float dvz = wantVz - vz;
            float dv = (float)Math.Sqrt(dvx * dvx + dvz * dvz);
            float maxDv = accel * dt;
            if (dv > maxDv)
            {
                dvx = dvx / dv * maxDv;
                dvz = dvz / dv * maxDv;
            }
            float nvx = vx + dvx;
            float nvz = vz + dvz;
            return (x + nvx * dt, z + nvz * dt, nvx, nvz);
        }

        // 이번 틱에 목표를 지나쳤나: 이동 전 목표 방향과 이동 후 목표 방향이 반대면 지나친 것
        private static bool PassedTarget(float x0, float z0, float x1, float z1, float tx, float tz)
        {
            float before = (tx - x0) * (tx - x1) + (tz - z0) * (tz - z1);
            return before <= 0f;
        }
    }
}
