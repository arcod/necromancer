using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

// ---------------------------------------------------------------------------
// COMPONENTS: plain data attached to an entity. No logic lives here.
// ---------------------------------------------------------------------------

/// <summary>Tag that marks an entity as a minion the player can command.</summary>
public struct Minion : IComponentData { }

/// <summary>How fast the entity moves, in world units per second. Shared by minions and enemies.</summary>
public struct MoveSpeed : IComponentData
{
    public float Value;
}

/// <summary>Where the minion is walking to. HasTarget is false when the minion is idle.</summary>
public struct MoveTarget : IComponentData
{
    public float3 Position;
    public bool HasTarget;
}

// ---------------------------------------------------------------------------
// AUTHORING + BAKER: the bridge from the Editor to ECS.
// Add MinionAuthoring to a GameObject inside a SubScene. When the SubScene is
// baked, the Baker turns that GameObject into an entity with the components above.
// ---------------------------------------------------------------------------

/// <summary>
/// Makes a GameObject in a SubScene into a minion entity.
/// Inspector fields: MoveSpeed sets walking speed; NormalColor/SelectedColor tint the minion
/// (its material must be URP Lit or similar); the Combat fields set health and auto-attack.
/// </summary>
public class MinionAuthoring : MonoBehaviour
{
    public float MoveSpeed = 5f;
    public Color NormalColor = Color.white;
    public Color SelectedColor = new Color(0.2f, 1f, 0.2f);

    [Header("Combat")]
    public float MaxHealth = 100f;
    public float AttackDamage = 25f;
    [Tooltip("Attacks the nearest enemy within this distance.")]
    public float AttackRange = 5f;
    [Tooltip("Seconds between attacks.")]
    public float AttackCooldown = 0.5f;

    class Baker : Baker<MinionAuthoring>
    {
        public override void Bake(MinionAuthoring authoring)
        {
            // Dynamic = this entity's transform changes at runtime (it moves).
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Minion>(entity);
            AddComponent(entity, new MoveSpeed { Value = authoring.MoveSpeed });
            AddComponent(entity, new MoveTarget { HasTarget = false }); // idle until ordered

            // Selection (see MinionSelection.cs). Starts unselected.
            AddComponent<Selected>(entity);
            SetComponentEnabled<Selected>(entity, false);

            // Materials expect linear colors; Inspector colors are gamma, so convert.
            float4 normal = (Vector4)authoring.NormalColor.linear;
            float4 selected = (Vector4)authoring.SelectedColor.linear;
            AddComponent(entity, new MinionColors { Normal = normal, Selected = selected });
            AddComponent(entity, new URPMaterialPropertyBaseColor { Value = normal });

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

// ---------------------------------------------------------------------------
// SYSTEM + JOB: the logic. The system runs every frame and schedules a job that
// processes every entity with LocalTransform + MoveSpeed + MoveTarget, spread
// across CPU cores and compiled by Burst. This is what lets hordes scale.
// ---------------------------------------------------------------------------

/// <summary>Moves every minion with a target toward it across the ground (X/Z plane).</summary>
[BurstCompile]
public partial struct MinionMoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new MinionMoveJob { DeltaTime = SystemAPI.Time.DeltaTime }.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct MinionMoveJob : IJobEntity
{
    public float DeltaTime;

    // The parameters define which entities this job runs on:
    // "ref" = read and write, "in" = read only.
    void Execute(ref LocalTransform transform, ref MoveTarget target, in MoveSpeed speed)
    {
        if (!target.HasTarget)
            return;

        float3 toTarget = target.Position - transform.Position;
        toTarget.y = 0f; // stay on the ground

        float distance = math.length(toTarget);
        float step = speed.Value * DeltaTime;

        if (distance <= step)
        {
            // Close enough: snap onto the target and stop.
            transform.Position.x = target.Position.x;
            transform.Position.z = target.Position.z;
            target.HasTarget = false;
            return;
        }

        float3 direction = toTarget / distance;
        transform.Position += direction * step;
        transform.Rotation = quaternion.LookRotationSafe(direction, math.up());
    }
}
