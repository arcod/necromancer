using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// ---------------------------------------------------------------------------
// COMPONENTS (baked by MinionAuthoring and EnemyAuthoring)
// ---------------------------------------------------------------------------

public struct Health : IComponentData
{
    public float Current;
    public float Max;
}

/// <summary>Auto-attack: hits the nearest enemy-side target within Range every Cooldown seconds.</summary>
public struct Attack : IComponentData
{
    public float Damage;
    public float Range;
    public float Cooldown;
    public float Timer; // counts down; can attack when it reaches 0
}

/// <summary>"Target should lose Amount health." Collected from many attackers, applied in one place.</summary>
public struct DamageEvent
{
    public Entity Target;
    public float Amount;
}

// ---------------------------------------------------------------------------
// COMBAT
// Minions attack enemies, enemies attack minions. Thousands of enemies may hit the
// same minion in one frame, and parallel jobs can't safely all write to one
// minion's Health at once. So attackers only *queue* damage events in parallel,
// and a single job then applies them all.
// ---------------------------------------------------------------------------

[BurstCompile]
[UpdateAfter(typeof(MinionMoveSystem))]
[UpdateAfter(typeof(EnemyMoveSystem))]
public partial struct CombatSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityQuery minionQuery = SystemAPI.QueryBuilder().WithAll<Minion, LocalTransform, Health>().Build();
        EntityQuery enemyQuery = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform, Health>().Build();

        // Snapshot of who can be hit, and where they are.
        NativeArray<Entity> minions = minionQuery.ToEntityArray(state.WorldUpdateAllocator);
        NativeArray<LocalTransform> minionTransforms = minionQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);
        NativeArray<Entity> enemies = enemyQuery.ToEntityArray(state.WorldUpdateAllocator);
        NativeArray<LocalTransform> enemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);

        var damage = new NativeQueue<DamageEvent>(Allocator.TempJob);
        float deltaTime = SystemAPI.Time.DeltaTime;

        // The same AttackJob runs twice, on two different sets of attackers.
        EntityQuery minionAttackers = SystemAPI.QueryBuilder().WithAll<Minion, LocalTransform>().WithAllRW<Attack>().Build();
        EntityQuery enemyAttackers = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform>().WithAllRW<Attack>().Build();

        state.Dependency = new AttackJob
        {
            Targets = enemies,
            TargetTransforms = enemyTransforms,
            Damage = damage.AsParallelWriter(),
            DeltaTime = deltaTime,
        }.ScheduleParallel(minionAttackers, state.Dependency);

        state.Dependency = new AttackJob
        {
            Targets = minions,
            TargetTransforms = minionTransforms,
            Damage = damage.AsParallelWriter(),
            DeltaTime = deltaTime,
        }.ScheduleParallel(enemyAttackers, state.Dependency);

        state.Dependency = new ApplyDamageJob
        {
            Damage = damage,
            Health = SystemAPI.GetComponentLookup<Health>(),
        }.Schedule(state.Dependency);

        damage.Dispose(state.Dependency);
    }
}

[BurstCompile]
public partial struct AttackJob : IJobEntity
{
    [ReadOnly] public NativeArray<Entity> Targets;
    [ReadOnly] public NativeArray<LocalTransform> TargetTransforms;
    public NativeQueue<DamageEvent>.ParallelWriter Damage;
    public float DeltaTime;

    void Execute(ref Attack attack, in LocalTransform transform)
    {
        attack.Timer -= DeltaTime;
        if (attack.Timer > 0f)
            return;
        attack.Timer = 0f; // ready: keep looking each frame until something is in range

        // Find the nearest target within range.
        int nearest = -1;
        float nearestDistanceSq = attack.Range * attack.Range;
        for (int i = 0; i < Targets.Length; i++)
        {
            float3 offset = TargetTransforms[i].Position - transform.Position;
            offset.y = 0f;
            float distanceSq = math.lengthsq(offset);
            if (distanceSq <= nearestDistanceSq)
            {
                nearestDistanceSq = distanceSq;
                nearest = i;
            }
        }

        if (nearest < 0)
            return;

        Damage.Enqueue(new DamageEvent { Target = Targets[nearest], Amount = attack.Damage });
        attack.Timer = attack.Cooldown;
    }
}

/// <summary>Runs on one thread, so it can safely apply many hits to the same target.</summary>
[BurstCompile]
public struct ApplyDamageJob : IJob
{
    public NativeQueue<DamageEvent> Damage;
    public ComponentLookup<Health> Health; // lets a job read/write a component on any entity

    public void Execute()
    {
        while (Damage.TryDequeue(out DamageEvent hit))
        {
            if (!Health.HasComponent(hit.Target))
                continue;
            Health target = Health[hit.Target];
            target.Current -= hit.Amount;
            Health[hit.Target] = target;
        }
    }
}

// ---------------------------------------------------------------------------
// DEATH
// Destroying an entity is a structural change, which can't happen inside a
// parallel job. Instead the job records "destroy this" in an EntityCommandBuffer,
// and Unity plays those commands back at the end of the frame.
// ---------------------------------------------------------------------------

[BurstCompile]
[UpdateAfter(typeof(CombatSystem))]
public partial struct DeathSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityCommandBuffer commands = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        state.Dependency = new DeathJob { Commands = commands.AsParallelWriter() }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct DeathJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter Commands;

    // ChunkIndexInQuery gives each parallel batch a sort key, so commands play back in a consistent order.
    void Execute([ChunkIndexInQuery] int sortKey, Entity entity, in Health health)
    {
        if (health.Current <= 0f)
            Commands.DestroyEntity(sortKey, entity);
    }
}
