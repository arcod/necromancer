using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// ---------------------------------------------------------------------------
// COMPONENTS
// ---------------------------------------------------------------------------

/// <summary>
/// Marks a minion as selected. It's an "enableable" component: every minion always has it,
/// and selecting just flips it on or off. That's much cheaper than adding and removing
/// a component, which forces ECS to move the entity to different memory.
/// </summary>
public struct Selected : IComponentData, IEnableableComponent { }

/// <summary>The colors a minion is drawn with when not selected / selected (linear color space).</summary>
public struct MinionColors : IComponentData
{
    public float4 Normal;
    public float4 Selected;
}

// ---------------------------------------------------------------------------
// INPUT: a regular MonoBehaviour, because there's only one mouse.
// It reads minion positions from ECS, flips their Selected component, and
// writes MoveTarget when the player gives a move order.
// ---------------------------------------------------------------------------

/// <summary>
/// Left-click or drag a box to select minions; hold Shift to add to the selection.
/// Right-click the ground to send selected minions there in a grid formation.
/// Add to any GameObject in GameScene (NOT inside the SubScene).
/// </summary>
public class MinionSelection : MonoBehaviour
{
    [Tooltip("When clicking, how close (in pixels) the cursor must be to a minion to select it.")]
    public float ClickRadius = 30f;
    [Tooltip("Mouse must move this many pixels before a click becomes a box drag.")]
    public float DragThreshold = 8f;
    public Color BoxFill = new Color(0.3f, 0.9f, 0.3f, 0.15f);
    public Color BoxBorder = new Color(0.3f, 0.9f, 0.3f, 0.9f);

    [Header("Move orders")]
    [Tooltip("Distance between minions in the formation they walk into.")]
    public float FormationSpacing = 2f;

    Camera cam;
    EntityQuery minionQuery;
    EntityQuery selectedQuery;
    bool queryCreated;
    bool dragging;
    Vector2 dragStart;

    void Start()
    {
        cam = Camera.main;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null)
            return;

        // While placing buildings, the mouse belongs to BuildingPlacer.
        if (BuildingPlacer.IsMouseBusy)
        {
            dragging = false;
            return;
        }

        Vector2 mousePosition = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            dragging = true;
            dragStart = mousePosition;
        }

        if (dragging && mouse.leftButton.wasReleasedThisFrame)
        {
            dragging = false;
            bool additive = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

            if (Vector2.Distance(dragStart, mousePosition) < DragThreshold)
                SelectClosestTo(mousePosition, additive);
            else
                SelectInBox(ScreenRect(dragStart, mousePosition), additive);
        }

        if (mouse.rightButton.wasPressedThisFrame && TryGetGroundPoint(mousePosition, out Vector3 groundPoint))
            IssueMoveOrder(groundPoint);
    }

    /// <summary>Where the cursor is pointing on the ground (the y = 0 plane).</summary>
    bool TryGetGroundPoint(Vector2 screenPoint, out Vector3 groundPoint)
    {
        Ray ray = cam.ScreenPointToRay(screenPoint);
        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
        {
            groundPoint = ray.GetPoint(distance);
            return true;
        }
        groundPoint = default;
        return false;
    }

    /// <summary>
    /// Sends every selected minion to a slot in a square grid centered on the target,
    /// so they don't all try to stand on the same spot.
    /// </summary>
    void IssueMoveOrder(Vector3 target)
    {
        if (!TryGetEntityManager(out EntityManager entityManager))
            return;

        // selectedQuery only matches minions whose Selected component is enabled.
        NativeArray<Entity> selected = selectedQuery.ToEntityArray(Allocator.Temp);
        int count = selected.Length;
        int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = columns == 0 ? 0 : Mathf.CeilToInt(count / (float)columns);

        for (int i = 0; i < count; i++)
        {
            int column = i % columns;
            int row = i / columns;

            // Offset from the center of the grid, so the click point is in the middle.
            float x = (column - (columns - 1) * 0.5f) * FormationSpacing;
            float z = (row - (rows - 1) * 0.5f) * FormationSpacing;

            entityManager.SetComponentData(selected[i], new MoveTarget
            {
                Position = new float3(target.x + x, 0f, target.z + z),
                HasTarget = true,
            });
        }

        selected.Dispose();
    }

    /// <summary>Select the single minion nearest the cursor (within ClickRadius).</summary>
    void SelectClosestTo(Vector2 point, bool additive)
    {
        if (!TryGetUnits(out EntityManager entityManager, out NativeArray<Entity> entities, out NativeArray<LocalTransform> transforms))
            return;

        int closest = -1;
        float closestDistance = ClickRadius;
        for (int i = 0; i < entities.Length; i++)
        {
            if (!additive)
                entityManager.SetComponentEnabled<Selected>(entities[i], false);

            Vector3 screen = cam.WorldToScreenPoint(transforms[i].Position);
            if (screen.z <= 0f)
                continue; // behind the camera

            float distance = Vector2.Distance(point, screen);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = i;
            }
        }

        if (closest >= 0)
            entityManager.SetComponentEnabled<Selected>(entities[closest], true);

        entities.Dispose();
        transforms.Dispose();
    }

    /// <summary>Select every minion whose on-screen position is inside the box.</summary>
    void SelectInBox(Rect box, bool additive)
    {
        if (!TryGetUnits(out EntityManager entityManager, out NativeArray<Entity> entities, out NativeArray<LocalTransform> transforms))
            return;

        for (int i = 0; i < entities.Length; i++)
        {
            Vector3 screen = cam.WorldToScreenPoint(transforms[i].Position);
            bool inside = screen.z > 0f && box.Contains(screen);

            if (inside)
                entityManager.SetComponentEnabled<Selected>(entities[i], true);
            else if (!additive)
                entityManager.SetComponentEnabled<Selected>(entities[i], false);
        }

        entities.Dispose();
        transforms.Dispose();
    }

    /// <summary>
    /// Copies every minion entity and its position out of ECS. Caller must Dispose both arrays.
    /// Fine for hundreds of player minions; enemies aren't selectable so they aren't included.
    /// </summary>
    bool TryGetUnits(out EntityManager entityManager, out NativeArray<Entity> entities, out NativeArray<LocalTransform> transforms)
    {
        entities = default;
        transforms = default;

        if (!TryGetEntityManager(out entityManager))
            return false;

        entities = minionQuery.ToEntityArray(Allocator.Temp);
        transforms = minionQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        return true;
    }

    /// <summary>Gets the ECS world's EntityManager, creating the queries the first time.</summary>
    bool TryGetEntityManager(out EntityManager entityManager)
    {
        entityManager = default;

        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        entityManager = world.EntityManager;
        if (!queryCreated)
        {
            // IgnoreComponentEnabledState: include minions whether Selected is on or off.
            minionQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Minion, LocalTransform, Selected>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(entityManager);

            // Without that option, a query only matches entities where Selected is enabled.
            selectedQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Minion, Selected, MoveTarget>()
                .Build(entityManager);

            queryCreated = true;
        }
        return true;
    }

    static Rect ScreenRect(Vector2 a, Vector2 b)
    {
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // Draws the selection box. OnGUI is Unity's simple immediate-mode UI: fine for debug visuals.
    void OnGUI()
    {
        if (!dragging || Mouse.current == null)
            return;

        Vector2 now = Mouse.current.position.ReadValue();
        if (Vector2.Distance(dragStart, now) < DragThreshold)
            return;

        // Screen positions count Y up from the bottom; OnGUI counts Y down from the top.
        Rect r = ScreenRect(dragStart, now);
        Rect box = new Rect(r.xMin, Screen.height - r.yMax, r.width, r.height);

        GUI.color = BoxFill;
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = BoxBorder;
        GUI.DrawTexture(new Rect(box.xMin, box.yMin, box.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMin, box.yMax - 1f, box.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMin, box.yMin, 1f, box.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(box.xMax - 1f, box.yMin, 1f, box.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}

// ---------------------------------------------------------------------------
// HIGHLIGHT: tints selected minions by overriding their material's Base Color.
// URPMaterialPropertyBaseColor is an Entities Graphics component that sets
// _BaseColor per entity, so every minion can share one material.
// ---------------------------------------------------------------------------

/// <summary>Sets each minion's color based on whether it's selected and how hurt it is.</summary>
[BurstCompile]
public partial struct SelectionHighlightSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new SelectionHighlightJob().ScheduleParallel();
    }
}

[BurstCompile]
[WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)] // run on selected AND unselected minions
public partial struct SelectionHighlightJob : IJobEntity
{
    void Execute(ref URPMaterialPropertyBaseColor color, in MinionColors colors, in Health health, EnabledRefRO<Selected> selected)
    {
        float4 baseColor = selected.ValueRO ? colors.Selected : colors.Normal;

        // Fade toward dark red as the minion loses health.
        float health01 = math.saturate(health.Current / health.Max);
        float4 hurtColor = new float4(0.5f, 0f, 0f, 1f);
        color.Value = math.lerp(hurtColor, baseColor, health01);
    }
}
