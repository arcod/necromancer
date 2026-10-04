using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// ---------------------------------------------------------------------------
// COMPONENTS
// ---------------------------------------------------------------------------

/// <summary>Singleton with the horde's settings. Baked from HordeAuthoring.</summary>
public struct HordeConfig : IComponentData
{
    public Entity EnemyPrefab;
    public int PerWave;
    public float WaveInterval;
    public int MaxEnemies;
    public float SpawnRadius;
    public float SpawnRingWidth;
    public float SeparationRadius;
    public float SeparationStrength;
}

/// <summary>Spawner state that changes while playing (kept apart from the fixed settings).</summary>
public struct HordeSpawnState : IComponentData
{
    public float Timer;
    public Unity.Mathematics.Random Random;
}

// ---------------------------------------------------------------------------
// AUTHORING
// ---------------------------------------------------------------------------

/// <summary>
/// Spawns waves of enemies on a ring around the map center; they chase the nearest unit.
/// Add to an empty GameObject INSIDE the SubScene and assign EnemyPrefab
/// (a prefab asset with EnemyAuthoring on it).
/// </summary>
public class HordeAuthoring : MonoBehaviour
{
    public GameObject EnemyPrefab;

    [Header("Waves")]
    public int PerWave = 200;
    [Tooltip("Seconds between waves. The first wave spawns right away.")]
    public float WaveInterval = 5f;
    [Tooltip("Stop spawning once this many enemies exist. Raise it to stress-test.")]
    public int MaxEnemies = 5000;
    [Tooltip("Enemies spawn this far from the map center...")]
    public float SpawnRadius = 40f;
    [Tooltip("...plus a random extra distance up to this much.")]
    public float SpawnRingWidth = 5f;
    public uint Seed = 1;

    [Header("Crowding")]
    [Tooltip("Enemies closer than this push each other apart. About the enemy's width.")]
    public float SeparationRadius = 1f;
    [Tooltip("How hard they push apart. Higher = more spread out, but more jittery.")]
    public float SeparationStrength = 4f;

    class Baker : Baker<HordeAuthoring>
    {
        public override void Bake(HordeAuthoring authoring)
        {
            // None = this entity has no position; it only holds settings.
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new HordeConfig
            {
                // Referencing a prefab here makes the baker convert it into an entity prefab.
                EnemyPrefab = GetEntity(authoring.EnemyPrefab, TransformUsageFlags.Dynamic),
                PerWave = authoring.PerWave,
                WaveInterval = authoring.WaveInterval,
                MaxEnemies = authoring.MaxEnemies,
                SpawnRadius = authoring.SpawnRadius,
                SpawnRingWidth = authoring.SpawnRingWidth,
                SeparationRadius = authoring.SeparationRadius,
                SeparationStrength = authoring.SeparationStrength,
            });
            AddComponent(entity, new HordeSpawnState
            {
                Timer = 0f,
                Random = Unity.Mathematics.Random.CreateFromIndex(authoring.Seed),
            });
        }
    }
}

// ---------------------------------------------------------------------------
// SPAWNING
// ---------------------------------------------------------------------------

/// <summary>Every WaveInterval seconds, copies the enemy prefab PerWave times onto the spawn ring.</summary>
[BurstCompile]
public partial struct HordeSpawnSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Don't run until the SubScene with the horde settings has loaded.
        state.RequireForUpdate<HordeConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        Entity hordeEntity = SystemAPI.GetSingletonEntity<HordeConfig>();
        HordeConfig config = entityManager.GetComponentData<HordeConfig>(hordeEntity);
        HordeSpawnState spawn = entityManager.GetComponentData<HordeSpawnState>(hordeEntity);

        spawn.Timer -= SystemAPI.Time.DeltaTime;
        if (spawn.Timer <= 0f)
        {
            spawn.Timer = config.WaveInterval;

            int existing = SystemAPI.QueryBuilder().WithAll<Enemy>().Build().CalculateEntityCount();
            int count = math.min(config.PerWave, config.MaxEnemies - existing);
            if (count > 0)
            {
                LocalTransform prefabTransform = entityManager.GetComponentData<LocalTransform>(config.EnemyPrefab);
                NativeArray<Entity> enemies = entityManager.Instantiate(config.EnemyPrefab, count, Allocator.Temp);

                for (int i = 0; i < count; i++)
                {
                    float angle = spawn.Random.NextFloat(0f, 2f * math.PI);
                    float radius = config.SpawnRadius + spawn.Random.NextFloat(0f, config.SpawnRingWidth);

                    LocalTransform transform = prefabTransform; // keeps the prefab's height and scale
                    transform.Position = new float3(math.cos(angle) * radius, prefabTransform.Position.y, math.sin(angle) * radius);
                    entityManager.SetComponentData(enemies[i], transform);
                }
            }
        }

        entityManager.SetComponentData(hordeEntity, spawn);
    }
}

// ---------------------------------------------------------------------------
// MOVEMENT
// Two jobs per frame:
//  1. Drop every enemy into a "spatial hash": a map from grid cell -> enemies in that cell.
//  2. Each enemy walks toward the nearest unit, and pushes away from enemies in its own
//     and neighboring cells. Checking only nearby cells (not all enemies) is what keeps
//     this fast with thousands of enemies.
// ---------------------------------------------------------------------------

public static class HordeGrid
{
    public static int2 CellOf(float3 position, float cellSize) => (int2)math.floor(position.xz / cellSize);
    public static int Key(int2 cell) => (int)math.hash(cell);
}

[BurstCompile]
[UpdateAfter(typeof(HordeSpawnSystem))]
public partial struct EnemyMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<HordeConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        HordeConfig config = SystemAPI.GetSingleton<HordeConfig>();

        int enemyCount = SystemAPI.QueryBuilder().WithAll<Enemy, LocalTransform>().Build().CalculateEntityCount();
        if (enemyCount == 0)
            return;

        // Copy all unit positions into an array the jobs can read.
        // WorldUpdateAllocator memory is freed automatically after a couple of frames.
        NativeArray<LocalTransform> units = SystemAPI.QueryBuilder().WithAll<Unit, LocalTransform>().Build()
            .ToComponentDataArray<LocalTransform>(state.WorldUpdateAllocator);

        var grid = new NativeParallelMultiHashMap<int, float3>(enemyCount, Allocator.TempJob);

        // Each job waits for the previous one (state.Dependency) before running.
        state.Dependency = new BuildEnemyGridJob
        {
            Grid = grid.AsParallelWriter(),
            CellSize = config.SeparationRadius,
        }.ScheduleParallel(state.Dependency);

        state.Dependency = new EnemyMoveJob
        {
            Grid = grid,
            Units = units,
            CellSize = config.SeparationRadius,
            SeparationRadius = config.SeparationRadius,
            SeparationStrength = config.SeparationStrength,
            DeltaTime = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);

        grid.Dispose(state.Dependency); // free it once the move job is done
    }
}

[BurstCompile]
[WithAll(typeof(Enemy))]
public partial struct BuildEnemyGridJob : IJobEntity
{
    public NativeParallelMultiHashMap<int, float3>.ParallelWriter Grid;
    public float CellSize;

    void Execute(in LocalTransform transform)
    {
        Grid.Add(HordeGrid.Key(HordeGrid.CellOf(transform.Position, CellSize)), transform.Position);
    }
}

[BurstCompile]
public partial struct EnemyMoveJob : IJobEntity
{
    [ReadOnly] public NativeParallelMultiHashMap<int, float3> Grid;
    [ReadOnly] public NativeArray<LocalTransform> Units;
    public float CellSize;
    public float SeparationRadius;
    public float SeparationStrength;
    public float DeltaTime;

    void Execute(ref LocalTransform transform, in Enemy enemy, in MoveSpeed speed)
    {
        float3 position = transform.Position;

        // 1. Head for the nearest unit (unless already close enough).
        float3 toNearest = float3.zero;
        float nearestDistanceSq = float.MaxValue;
        for (int i = 0; i < Units.Length; i++)
        {
            float3 offset = Units[i].Position - position;
            offset.y = 0f;
            float distanceSq = math.lengthsq(offset);
            if (distanceSq < nearestDistanceSq)
            {
                nearestDistanceSq = distanceSq;
                toNearest = offset;
            }
        }

        float3 chase = float3.zero;
        if (Units.Length > 0 && nearestDistanceSq > enemy.StopDistance * enemy.StopDistance)
            chase = math.normalize(toNearest);

        // 2. Push away from enemies that are too close, checking this cell and the 8 around it.
        float3 push = float3.zero;
        float radiusSq = SeparationRadius * SeparationRadius;
        int2 cell = HordeGrid.CellOf(position, CellSize);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int key = HordeGrid.Key(cell + new int2(dx, dz));
                if (!Grid.TryGetFirstValue(key, out float3 other, out NativeParallelMultiHashMapIterator<int> iterator))
                    continue;
                do
                {
                    float3 away = position - other;
                    away.y = 0f;
                    float distanceSq = math.lengthsq(away);
                    // distanceSq ~0 is this enemy itself
                    if (distanceSq > 0.0001f && distanceSq < radiusSq)
                    {
                        float distance = math.sqrt(distanceSq);
                        push += away / distance * (1f - distance / SeparationRadius); // stronger when closer
                    }
                } while (Grid.TryGetNextValue(out other, ref iterator));
            }
        }

        // 3. Combine, cap the speed, and move.
        float3 velocity = chase * speed.Value + push * SeparationStrength;
        float maxSpeed = speed.Value * 1.5f;
        if (math.lengthsq(velocity) > maxSpeed * maxSpeed)
            velocity = math.normalize(velocity) * maxSpeed;

        transform.Position += velocity * DeltaTime;
        if (math.lengthsq(chase) > 0f)
            transform.Rotation = quaternion.LookRotationSafe(chase, math.up());
    }
}
