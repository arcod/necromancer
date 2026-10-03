using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// ---------------------------------------------------------------------------
// COMPONENTS
// ---------------------------------------------------------------------------

/// <summary>On each placed building entity: which hex it stands on.</summary>
public struct Building : IComponentData
{
    public int2 Axial;
}

// ---------------------------------------------------------------------------
// PLACEMENT: a MonoBehaviour (one player, one mouse). Shows a "ghost" preview
// snapped to the hex under the cursor, and creates a building entity on click.
// ---------------------------------------------------------------------------

/// <summary>
/// Press B to enter build mode. A preview follows the cursor, snapped to hexes:
/// green = can build, red = can't (water, rock, or already occupied).
/// Left-click to place; right-click, Esc, or B again to leave build mode.
/// Add to a GameObject in GameScene (NOT inside the SubScene) and assign BuildingMaterial (URP Lit).
/// </summary>
public class BuildingPlacer : MonoBehaviour
{
    public Material BuildingMaterial;
    public Vector3 BuildingSize = new Vector3(1.3f, 1.2f, 1.3f);
    public Color BuildingColor = new Color(0.4f, 0.28f, 0.5f);
    public Color ValidColor = new Color(0.3f, 1f, 0.3f);
    public Color InvalidColor = new Color(1f, 0.3f, 0.3f);

    /// <summary>True while in build mode. Other input scripts check this so clicks don't do two things.</summary>
    public static bool IsPlacing { get; private set; }

    /// <summary>True if the mouse is being used for building this frame — UnitSelection ignores the mouse then.</summary>
    public static bool IsMouseBusy => IsPlacing || lastMouseFrame == Time.frameCount;
    static int lastMouseFrame = -1;

    Camera cam;
    GameObject ghost;
    Material ghostMaterial;
    Entity buildingPrototype;
    EntityQuery gridQuery;

    void Start()
    {
        cam = Camera.main;
        if (BuildingMaterial == null)
        {
            Debug.LogError("BuildingPlacer: assign a BuildingMaterial (URP Lit) in the Inspector.");
            enabled = false;
            return;
        }

        // The ghost is a plain GameObject: there's only ever one, and it's not gameplay.
        ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ghost.name = "BuildingGhost";
        Destroy(ghost.GetComponent<Collider>());
        ghostMaterial = new Material(BuildingMaterial);
        ghost.GetComponent<MeshRenderer>().sharedMaterial = ghostMaterial;
        ghost.transform.localScale = BuildingSize;
        ghost.SetActive(false);
        Mesh cubeMesh = ghost.GetComponent<MeshFilter>().sharedMesh;

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        gridQuery = entityManager.CreateEntityQuery(typeof(HexGrid), typeof(HexCell));

        // A prototype building entity to copy on each placement. The Prefab tag hides it
        // from rendering and from every query; copies made with Instantiate don't get the tag.
        buildingPrototype = entityManager.CreateEntity();
        entityManager.SetName(buildingPrototype, "BuildingPrototype");
        RenderMeshUtility.AddComponents(
            buildingPrototype,
            entityManager,
            new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true),
            new RenderMeshArray(new[] { BuildingMaterial }, new[] { cubeMesh }),
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
        entityManager.AddComponentData(buildingPrototype, new LocalToWorld { Value = float4x4.identity });
        entityManager.AddComponentData(buildingPrototype, new URPMaterialPropertyBaseColor { Value = (Vector4)BuildingColor.linear });
        entityManager.AddComponent<Building>(buildingPrototype);
        entityManager.AddComponent<Prefab>(buildingPrototype);
    }

    void OnDisable()
    {
        IsPlacing = false;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null || cam == null)
            return;

        if (keyboard.bKey.wasPressedThisFrame)
            SetPlacing(!IsPlacing);

        if (!IsPlacing)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
        {
            SetPlacing(false);
            lastMouseFrame = Time.frameCount; // so this right-click isn't also a move order
            return;
        }

        if (!TryGetHoveredCell(mouse.position.ReadValue(), out EntityManager entityManager,
                out Entity gridEntity, out int2 axial, out int index, out float outerRadius))
        {
            ghost.SetActive(false); // cursor is off the map
            return;
        }

        DynamicBuffer<HexCell> cells = entityManager.GetBuffer<HexCell>(gridEntity);
        HexCell cell = cells[index];
        bool canBuild = cell.IsOpen;

        // Move the ghost onto the hovered hex and color it.
        float3 center = HexMath.AxialToWorld(axial, outerRadius);
        float3 position = center + new float3(0f, BuildingSize.y * 0.5f, 0f);
        ghost.SetActive(true);
        ghost.transform.position = position;
        ghostMaterial.color = canBuild ? ValidColor : InvalidColor;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            lastMouseFrame = Time.frameCount;
            if (!canBuild)
                return;

            // Mark the tile occupied BEFORE creating the building: Instantiate is a
            // "structural change", which invalidates buffers we're holding (like cells).
            cell.Occupied = true;
            cells[index] = cell;

            Entity building = entityManager.Instantiate(buildingPrototype);
            entityManager.SetComponentData(building, new Building { Axial = axial });
            entityManager.SetComponentData(building, new LocalToWorld
            {
                Value = float4x4.TRS(position, quaternion.identity, BuildingSize),
            });
        }
    }

    void SetPlacing(bool placing)
    {
        IsPlacing = placing;
        if (!placing && ghost != null)
            ghost.SetActive(false);
    }

    /// <summary>Finds the hex under the cursor. False if the grid doesn't exist yet or the cursor is off the map.</summary>
    bool TryGetHoveredCell(Vector2 screenPoint, out EntityManager entityManager, out Entity gridEntity,
        out int2 axial, out int index, out float outerRadius)
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        gridEntity = Entity.Null;
        axial = default;
        index = -1;
        outerRadius = 0f;

        if (gridQuery.IsEmpty)
            return false;

        Ray ray = cam.ScreenPointToRay(screenPoint);
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
            return false;

        HexGrid grid = gridQuery.GetSingleton<HexGrid>();
        gridEntity = gridQuery.GetSingletonEntity();
        outerRadius = grid.OuterRadius;
        axial = HexMath.WorldToAxial(ray.GetPoint(distance), grid.OuterRadius);
        index = grid.IndexOf(axial);
        return index >= 0;
    }

    void OnGUI()
    {
        string hint = IsPlacing
            ? "BUILD MODE: left-click to place, right-click or Esc to cancel"
            : "Press B to build";
        GUI.Label(new Rect(10f, 10f, 600f, 25f), hint);
    }
}
