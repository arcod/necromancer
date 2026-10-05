using Unity.Entities;
using UnityEngine;

/// <summary>Marks an entity as an enemy (a Church of the First Flame unit). StopDistance = how close it gets to a minion before stopping.</summary>
public struct Enemy : IComponentData
{
    public float StopDistance;
}

/// <summary>
/// Put this on the enemy PREFAB (a prefab asset in the Project window, not an object in a scene).
/// RaidAuthoring references the prefab, and the baker turns it into an entity prefab the
/// spawner can copy thousands of times.
/// </summary>
public class EnemyAuthoring : MonoBehaviour
{
    public float MoveSpeed = 3f;
    [Tooltip("How close an enemy gets to a minion before it stops and attacks.")]
    public float StopDistance = 1.2f;

    [Header("Combat")]
    public float MaxHealth = 50f;
    public float AttackDamage = 5f;
    [Tooltip("Must be a bit more than StopDistance, or enemies will stop just out of reach.")]
    public float AttackRange = 1.6f;
    [Tooltip("Seconds between attacks.")]
    public float AttackCooldown = 1f;

    class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Enemy { StopDistance = authoring.StopDistance });
            // MoveSpeed is shared with minions. Enemies have no MoveTarget,
            // so MinionMoveSystem leaves them alone; EnemyMoveSystem moves them instead.
            AddComponent(entity, new MoveSpeed { Value = authoring.MoveSpeed });

            // Combat (see CombatSystems.cs).
            AddComponent(entity, new Health { Current = authoring.MaxHealth, Max = authoring.MaxHealth });
            AddComponent(entity, new Attack
            {
                Damage = authoring.AttackDamage,
                Range = authoring.AttackRange,
                Cooldown = authoring.AttackCooldown,
            });
        }
    }
}
