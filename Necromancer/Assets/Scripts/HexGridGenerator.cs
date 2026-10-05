using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

// ---------------------------------------------------------------------------
// HEX MATH
// Pointy-top hexes using "axial" coordinates (q, r): q runs along a row,
// r goes up/down rows. Axial coords make neighbor and distance math simple.
// Good reference: https://www.redblobgames.com/grids/hexagons/
// ---------------------------------------------------------------------------

/// <summary>Converts between hex coordinates and world positions. Works inside Burst jobs too.</summary>
public static class HexMath
{
    public const float Sqrt3 = 1.7320508f;

    /// <summary>World position (on the ground) of the center of hex (q, r).</summary>
    public static float3 AxialToWorld(int2 axial, float outerRadius)
    {
        float x = outerRadius * Sqrt3 * (axial.x + axial.y * 0.5f);
        float z = outerRadius * 1.5f * axial.y;
        return new float3(x, 0f, z);
    }

    /// <summary>Which hex a world position is inside.</summary>
    public static int2 WorldToAxial(float3 position, float outerRadius)
    {
        float q = (Sqrt3 / 3f * position.x - 1f / 3f * position.z) / outerRadius;
        float r = (2f / 3f * position.z) / outerRadius;
        return RoundAxial(new float2(q, r));
    }

    /// <summary>Rounds fractional axial coords to the nearest whole hex.</summary>
    public static int2 RoundAxial(float2 axial)
    {
        // Convert to cube coords (x + y + z = 0), round each, then fix the one
        // that rounded furthest so the constraint still holds.
        float3 cube = new float3(axial.x, -axial.x - axial.y, axial.y);
        float3 rounded = math.round(cube);
        float3 diff = math.abs(rounded - cube);

        if (diff.x > diff.y && diff.x > diff.z)
            rounded.x = -rounded.y - rounded.z;
        else if (diff.z > diff.y)
            rounded.z = -rounded.x - rounded.y;

        return new int2((int)rounded.x, (int)rounded.z);
    }

    /// <summary>
    /// "Offset" coords are (column, row) in a rectangle — handy for storing the map in an array.
    /// Odd rows are shifted half a hex to the right.
    /// </summary>
    public static int2 OffsetToAxial(int2 offset)
    {
        return new int2(offset.x - (offset.y - (offset.y & 1)) / 2, offset.y);
    }

    public static int2 AxialToOffset(int2 axial)
    {
        return new int2(axial.x + (axial.y - (axial.y & 1)) / 2, axial.y);
    }

    /// <summary>Number of hex steps between two hexes.</summary>
    public static int Distance(int2 a, int2 b)
    {
        int2 d = a - b;
        return (math.abs(d.x) + math.abs(d.y) + math.abs(d.x + d.y)) / 2;
    }

    /// <summary>The neighboring hex in one of the 6 directions (0–5, going around the hex).</summary>
    public static int2 Neighbor(int2 axial, int direction)
    {
        switch (direction)
        {
            case 0: return axial + new int2(1, 0);
            case 1: return axial + new int2(1, -1);
            case 2: return axial + new int2(0, -1);
            case 3: return axial + new int2(-1, 0);
            case 4: return axial + new int2(-1, 1);
            default: return axial + new int2(0, 1);
        }
    }
}

// ---------------------------------------------------------------------------
// TERRAIN (see "World & map" in docs/GAME_MECHANICS.md)
// ---------------------------------------------------------------------------

public enum HexTerrain : byte
{
    Grassland,
    Desert,
    Forest,
    Cliff,
    Water,
    Swamp,
}

/// <summary>Resource or landmark sitting on a tile. Only appears on buildable terrain.</summary>
public enum HexFeature : byte
{
    None,
    Granite,
    BlackBasalt,
    Iron,
    EldritchVein, // source of ichor
    Graveyard,
}

/// <summary>What each terrain type allows. Wraith exceptions (water, forest) are handled by pathfinding.</summary>
public static class HexTerrainRules
{
    public static bool IsBuildable(HexTerrain terrain) => terrain == HexTerrain.Grassland || terrain == HexTerrain.Desert;
    public static bool IsPassable(HexTerrain terrain) => terrain != HexTerrain.Forest && terrain != HexTerrain.Cliff && terrain != HexTerrain.Water;
    public static float SpeedMultiplier(HexTerrain terrain) => terrain == HexTerrain.Swamp ? 0.5f : 1f;
}

// ---------------------------------------------------------------------------
// COMPONENTS
// ---------------------------------------------------------------------------

/// <summary>
/// Singleton describing the map. Lives on one "grid" entity, which also holds a
/// DynamicBuffer of HexCell — one entry per tile, so any system can look up a tile.
/// </summary>
public struct HexGrid : IComponentData
{
    public int Width;         // columns
    public int Height;        // rows
    public float OuterRadius; // center to corner
    public int2 OriginOffset; // offset coords of the bottom-left tile (map is centered on 0,0)

    /// <summary>Index into the HexCell buffer, or -1 if the hex is off the map.</summary>
    public int IndexOf(int2 axial)
    {
        int2 local = HexMath.AxialToOffset(axial) - OriginOffset;
        if (local.x < 0 || local.y < 0 || local.x >= Width || local.y >= Height)
            return -1;
        return local.y * Width + local.x;
    }
}

/// <summary>One map tile's game data. Stored in a buffer on the grid entity.</summary>
public struct HexCell : IBufferElementData
{
    public HexTerrain Terrain;
    public HexFeature Feature;
    public bool Occupied; // a building stands here
    public Entity Tile;   // the visible tile entity

    /// <summary>Can a building be placed here? (Per-building rules come in step 5.)</summary>
    public bool CanBuild => !Occupied && HexTerrainRules.IsBuildable(Terrain);
}

/// <summary>On each visible tile entity: which hex it is.</summary>
public struct HexTile : IComponentData
{
    public int2 Axial;
}

// ---------------------------------------------------------------------------
// GENERATOR
// A MonoBehaviour that builds the map in code when Play starts. Tiles are created
// at runtime (not baked) because terrain is procedurally generated.
//   1. Terrain: two noise maps, "elevation" and "moisture", pick each tile's terrain.
//   2. Features: resource patches are grown on buildable tiles until ResourceCoverage
//      of them are covered, plus a few rare unguarded graveyards.
//   3. Tiles: one entity per hex, colored and raised by terrain/feature.
// All numbers here are starting values, meant to be tweaked in the Inspector.
// ---------------------------------------------------------------------------

/// <summary>
/// Generates the hex map as entities when Play starts.
/// Add to a GameObject in GameScene (NOT inside the SubScene) and assign TileMaterial
/// (a URP Lit material — tiles are tinted per terrain type).
/// </summary>
public class HexGridGenerator : MonoBehaviour
{
    [Header("Size")]
    public int Width = 120;
    public int Height = 120;
    [Tooltip("Center-to-corner distance. 1 = hexes 2 units tall.")]
    public float OuterRadius = 1f;
    [Tooltip("Tiles are drawn slightly smaller than their cell so grid lines show. 1 = no gaps.")]
    [Range(0.8f, 1f)] public float TileScale = 0.95f;

    [Header("Terrain")]
    public Material TileMaterial;
    public int Seed = 1;
    [Tooltip("Smaller = bigger regions of each terrain type.")]
    public float NoiseScale = 0.06f;
    [Tooltip("Tiles within this many hexes of the map center are always plain grassland (room for the Phylactery).")]
    public int ClearRadius = 5;
    [Tooltip("Elevation below this is water (-1 to 1).")]
    public float WaterLevel = -0.5f;
    [Tooltip("Elevation above this is cliff.")]
    public float CliffLevel = 0.6f;
    [Tooltip("Moisture above this is forest.")]
    public float ForestMoisture = 0.3f;
    [Tooltip("Wet ground below this elevation is swamp instead of forest.")]
    public float SwampElevation = -0.25f;
    [Tooltip("Moisture below this is desert.")]
    public float DesertMoisture = -0.4f;

    [Header("Terrain heights")]
    [Tooltip("How high each terrain's tiles sit, in world units. Grassland and desert are at 0.")]
    public float CliffHeight = 0.9f;
    public float ForestHeight = 0.4f;
    public float SwampHeight = -0.05f;
    public float WaterHeight = -0.2f;

    [Header("Resources")]
    [Tooltip("Fraction of buildable tiles covered by resources. 0.25 = 75% have none.")]
    [Range(0f, 1f)] public float ResourceCoverage = 0.25f;
    public int MinPatchSize = 3;
    public int MaxPatchSize = 10;
    [Tooltip("Relative chance of each patch type.")]
    public float GraniteWeight = 4f;
    public float BlackBasaltWeight = 2f;
    public float IronWeight = 2f;
    public float EldritchVeinWeight = 1f;
    [Tooltip("Rare unguarded graveyards. (Graveyards next to towns arrive with the Church in Milestone 2.)")]
    public int UnguardedGraveyards = 3;

    [Header("Terrain colors")]
    public Color GrassColor = new Color(0.36f, 0.56f, 0.26f);
    public Color DesertColor = new Color(0.82f, 0.72f, 0.48f);
    public Color ForestColor = new Color(0.12f, 0.32f, 0.14f);
    public Color CliffColor = new Color(0.42f, 0.38f, 0.35f);
    public Color WaterColor = new Color(0.22f, 0.42f, 0.72f);
    public Color SwampColor = new Color(0.3f, 0.36f, 0.22f);

    [Header("Feature colors")]
    public Color GraniteColor = new Color(0.75f, 0.68f, 0.66f);
    public Color BlackBasaltColor = new Color(0.12f, 0.12f, 0.14f);
    public Color IronColor = new Color(0.55f, 0.27f, 0.17f);
    public Color EldritchVeinColor = new Color(0.45f, 0.85f, 0.35f);
    public Color GraveyardColor = new Color(0.62f, 0.6f, 0.68f);

    Mesh hexMesh; // kept as a field so it isn't garbage-collected while tiles use it

    void Start()
    {
        if (TileMaterial == null)
        {
            Debug.LogError("HexGridGenerator: assign a TileMaterial (URP Lit) in the Inspector.");
            return;
        }

        int count = Width * Height;
        int2 originOffset = new int2(-Width / 2, -Height / 2);
        var grid = new HexGrid { Width = Width, Height = Height, OuterRadius = OuterRadius, OriginOffset = originOffset };

        // Work out the map's data first, then create the entities.
        NativeArray<HexCell> cells = new NativeArray<HexCell>(count, Allocator.Temp);
        NativeArray<int2> axials = new NativeArray<int2>(count, Allocator.Temp);
        GenerateTerrain(grid, cells, axials);
        GenerateFeatures(grid, cells, axials);

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        // Tile sides must reach below the lowest tile, or raised tiles show gaps underneath.
        float depth = math.max(CliffHeight, ForestHeight) - math.min(WaterHeight, 0f) + 0.5f;
        hexMesh = BuildHexMesh(OuterRadius * TileScale, depth);

        // Make one "prototype" tile entity with everything needed to render it,
        // then copy it once per tile. Instantiating in bulk is much faster than one by one.
        Entity prototype = entityManager.CreateEntity();
        RenderMeshUtility.AddComponents(
            prototype,
            entityManager,
            new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: true),
            new RenderMeshArray(new[] { TileMaterial }, new[] { hexMesh }),
            MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));
        entityManager.AddComponentData(prototype, new LocalToWorld { Value = float4x4.identity });
        entityManager.AddComponentData(prototype, new URPMaterialPropertyBaseColor());
        entityManager.AddComponentData(prototype, new HexTile());
        NativeArray<Entity> tiles = entityManager.Instantiate(prototype, count, Allocator.Temp);
        entityManager.DestroyEntity(prototype);

        for (int i = 0; i < count; i++)
        {
            HexCell cell = cells[i];
            float3 position = HexMath.AxialToWorld(axials[i], OuterRadius);
            position.y = HeightFor(cell.Terrain);

            entityManager.SetComponentData(tiles[i], new LocalToWorld { Value = float4x4.Translate(position) });
            entityManager.SetComponentData(tiles[i], new HexTile { Axial = axials[i] });
            entityManager.SetComponentData(tiles[i], new URPMaterialPropertyBaseColor { Value = ColorFor(cell) });

            cell.Tile = tiles[i];
            cells[i] = cell;
        }

        // The grid singleton other systems use to look up tiles.
        Entity gridEntity = entityManager.CreateEntity();
        entityManager.SetName(gridEntity, "HexGrid");
        entityManager.AddComponentData(gridEntity, grid);
        entityManager.AddBuffer<HexCell>(gridEntity).AddRange(cells);

        FitCameraToMap();

        cells.Dispose();
        axials.Dispose();
        tiles.Dispose();
    }

    void GenerateTerrain(HexGrid grid, NativeArray<HexCell> cells, NativeArray<int2> axials)
    {
        // Two unrelated noise maps: offsetting the inputs makes them independent.
        float2 elevationOffset = new float2(Seed * 37.1f, Seed * 91.7f);
        float2 moistureOffset = new float2(Seed * 53.3f + 1000f, Seed * 17.9f - 1000f);

        for (int row = 0; row < grid.Height; row++)
        {
            for (int column = 0; column < grid.Width; column++)
            {
                int index = row * grid.Width + column;
                int2 axial = HexMath.OffsetToAxial(grid.OriginOffset + new int2(column, row));
                axials[index] = axial;

                HexTerrain terrain = HexTerrain.Grassland;
                if (HexMath.Distance(axial, int2.zero) > ClearRadius)
                {
                    float2 p = HexMath.AxialToWorld(axial, OuterRadius).xz * NoiseScale;
                    float elevation = noise.snoise(p + elevationOffset);
                    float moisture = noise.snoise(p * 1.3f + moistureOffset);

                    if (elevation < WaterLevel) terrain = HexTerrain.Water;
                    else if (elevation > CliffLevel) terrain = HexTerrain.Cliff;
                    else if (moisture > ForestMoisture) terrain = elevation < SwampElevation ? HexTerrain.Swamp : HexTerrain.Forest;
                    else if (moisture < DesertMoisture) terrain = HexTerrain.Desert;
                }

                cells[index] = new HexCell { Terrain = terrain, Feature = HexFeature.None };
            }
        }
    }

    void GenerateFeatures(HexGrid grid, NativeArray<HexCell> cells, NativeArray<int2> axials)
    {
        var random = new Unity.Mathematics.Random((uint)(Seed * 7919 + 1));

        // Tiles that can hold a feature: buildable terrain outside the starting clearing.
        var candidates = new System.Collections.Generic.List<int>();
        for (int i = 0; i < cells.Length; i++)
        {
            if (HexTerrainRules.IsBuildable(cells[i].Terrain) && HexMath.Distance(axials[i], int2.zero) > ClearRadius)
                candidates.Add(i);
        }
        if (candidates.Count == 0)
            return;

        // Rare unguarded graveyards: small patches of 1–3 tiles.
        for (int g = 0; g < UnguardedGraveyards; g++)
        {
            int start = candidates[random.NextInt(candidates.Count)];
            GrowPatch(grid, cells, axials, start, HexFeature.Graveyard, random.NextInt(1, 4), ref random);
        }

        // Resource patches until the target coverage is reached.
        int target = Mathf.RoundToInt(candidates.Count * ResourceCoverage);
        int covered = 0;
        for (int attempt = 0; covered < target && attempt < 100000; attempt++)
        {
            int start = candidates[random.NextInt(candidates.Count)];
            if (cells[start].Feature != HexFeature.None)
                continue;
            int size = random.NextInt(MinPatchSize, MaxPatchSize + 1);
            covered += GrowPatch(grid, cells, axials, start, PickResource(ref random), math.min(size, target - covered), ref random);
        }
    }

    /// <summary>
    /// Spreads a feature outward from a start tile onto random neighboring buildable tiles,
    /// making an irregular blob. Returns how many tiles it covered.
    /// </summary>
    int GrowPatch(HexGrid grid, NativeArray<HexCell> cells, NativeArray<int2> axials, int start, HexFeature feature, int size, ref Unity.Mathematics.Random random)
    {
        var frontier = new System.Collections.Generic.List<int> { start };
        int placed = 0;
        while (placed < size && frontier.Count > 0)
        {
            // Take a random tile from the frontier (swap with the last one, then remove it).
            int pick = random.NextInt(frontier.Count);
            int index = frontier[pick];
            frontier[pick] = frontier[frontier.Count - 1];
            frontier.RemoveAt(frontier.Count - 1);

            HexCell cell = cells[index];
            if (cell.Feature != HexFeature.None || !HexTerrainRules.IsBuildable(cell.Terrain)
                || HexMath.Distance(axials[index], int2.zero) <= ClearRadius)
                continue;

            cell.Feature = feature;
            cells[index] = cell;
            placed++;

            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = grid.IndexOf(HexMath.Neighbor(axials[index], direction));
                if (neighbor >= 0)
                    frontier.Add(neighbor);
            }
        }
        return placed;
    }

    HexFeature PickResource(ref Unity.Mathematics.Random random)
    {
        float total = GraniteWeight + BlackBasaltWeight + IronWeight + EldritchVeinWeight;
        float roll = random.NextFloat(total);
        if ((roll -= GraniteWeight) < 0f) return HexFeature.Granite;
        if ((roll -= BlackBasaltWeight) < 0f) return HexFeature.BlackBasalt;
        if ((roll -= IronWeight) < 0f) return HexFeature.Iron;
        return HexFeature.EldritchVein;
    }

    float HeightFor(HexTerrain terrain)
    {
        switch (terrain)
        {
            case HexTerrain.Water: return WaterHeight;
            case HexTerrain.Swamp: return SwampHeight;
            case HexTerrain.Forest: return ForestHeight;
            case HexTerrain.Cliff: return CliffHeight;
            default: return 0f;
        }
    }

    float4 ColorFor(HexCell cell)
    {
        Color color = cell.Feature switch
        {
            HexFeature.Granite => GraniteColor,
            HexFeature.BlackBasalt => BlackBasaltColor,
            HexFeature.Iron => IronColor,
            HexFeature.EldritchVein => EldritchVeinColor,
            HexFeature.Graveyard => GraveyardColor,
            _ => cell.Terrain switch
            {
                HexTerrain.Desert => DesertColor,
                HexTerrain.Forest => ForestColor,
                HexTerrain.Cliff => CliffColor,
                HexTerrain.Water => WaterColor,
                HexTerrain.Swamp => SwampColor,
                _ => GrassColor,
            },
        };
        return (Vector4)color.linear;
    }

    /// <summary>Keeps the RTS camera from panning off the edge of the map.</summary>
    void FitCameraToMap()
    {
        RtsCamera rtsCamera = FindAnyObjectByType<RtsCamera>();
        if (rtsCamera == null)
            return;
        float halfWidth = Width * 0.5f * HexMath.Sqrt3 * OuterRadius;
        float halfHeight = Height * 0.5f * 1.5f * OuterRadius;
        rtsCamera.MapMin = new Vector2(-halfWidth, -halfHeight);
        rtsCamera.MapMax = new Vector2(halfWidth, halfHeight);
    }

    /// <summary>
    /// Builds a pointy-top hexagonal prism: a flat top at y = 0 and six sides going down by depth.
    /// Sides get their own vertices so lighting shows crisp edges.
    /// </summary>
    static Mesh BuildHexMesh(float radius, float depth)
    {
        Vector3[] corners = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = math.radians(60f * i - 30f); // -30° offset makes it pointy-top
            corners[i] = new Vector3(radius * math.cos(angle), 0f, radius * math.sin(angle));
        }

        var vertices = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();

        // Top face: a fan of 6 triangles around the center. Unity draws the side of a
        // triangle whose corners go clockwise as seen by the camera.
        vertices.Add(Vector3.zero);
        normals.Add(Vector3.up);
        for (int i = 0; i < 6; i++)
        {
            vertices.Add(corners[i]);
            normals.Add(Vector3.up);
        }
        for (int i = 0; i < 6; i++)
        {
            triangles.Add(0);
            triangles.Add(1 + (i + 1) % 6);
            triangles.Add(1 + i);
        }

        // Sides: one quad (two triangles) per edge.
        Vector3 down = new Vector3(0f, -depth, 0f);
        for (int i = 0; i < 6; i++)
        {
            Vector3 a = corners[i];
            Vector3 b = corners[(i + 1) % 6];
            Vector3 normal = ((a + b) * 0.5f).normalized;

            int start = vertices.Count;
            vertices.Add(a);        // top-left (seen from outside)
            vertices.Add(b);        // top-right
            vertices.Add(b + down); // bottom-right
            vertices.Add(a + down); // bottom-left
            for (int n = 0; n < 4; n++)
                normals.Add(normal);

            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        Mesh mesh = new Mesh { name = "Hex" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
