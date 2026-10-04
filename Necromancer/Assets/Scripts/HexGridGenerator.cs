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
}

// ---------------------------------------------------------------------------
// COMPONENTS
// ---------------------------------------------------------------------------

public enum HexTerrain : byte
{
    Grass,
    Dirt,
    Rock,
    Water,
}

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
    public bool Occupied; // e.g. a building stands here (step 6)
    public Entity Tile;   // the visible tile entity

    /// <summary>Can units walk here / can something be built here?</summary>
    public bool IsOpen => !Occupied && (Terrain == HexTerrain.Grass || Terrain == HexTerrain.Dirt);
}

/// <summary>On each visible tile entity: which hex it is.</summary>
public struct HexTile : IComponentData
{
    public int2 Axial;
}

// ---------------------------------------------------------------------------
// GENERATOR
// A MonoBehaviour that builds the map in code when Play starts. Tiles are created
// at runtime (not baked) because terrain will be procedurally generated anyway.
// ---------------------------------------------------------------------------

/// <summary>
/// Generates a hex tile map as entities when Play starts.
/// Add to a GameObject in GameScene (NOT inside the SubScene) and assign TileMaterial
/// (a URP Lit material — tiles are tinted per terrain type).
/// </summary>
public class HexGridGenerator : MonoBehaviour
{
    [Header("Size")]
    public int Width = 60;
    public int Height = 60;
    [Tooltip("Center-to-corner distance. 1 = hexes 2 units tall.")]
    public float OuterRadius = 1f;
    [Tooltip("Tiles are drawn slightly smaller than their cell so grid lines show. 1 = no gaps.")]
    [Range(0.8f, 1f)] public float TileScale = 0.95f;

    [Header("Terrain")]
    public Material TileMaterial;
    public int Seed = 1;
    [Tooltip("Smaller = bigger patches of each terrain type.")]
    public float NoiseScale = 0.08f;
    [Tooltip("Tiles within this many hexes of the map center are always grass, so units start on open ground.")]
    public int ClearRadius = 5;

    public Color GrassColor = new Color(0.36f, 0.56f, 0.26f);
    public Color DirtColor = new Color(0.56f, 0.43f, 0.29f);
    public Color RockColor = new Color(0.45f, 0.45f, 0.48f);
    public Color WaterColor = new Color(0.22f, 0.42f, 0.72f);

    Mesh hexMesh; // kept as a field so it isn't garbage-collected while tiles use it

    void Start()
    {
        if (TileMaterial == null)
        {
            Debug.LogError("HexGridGenerator: assign a TileMaterial (URP Lit) in the Inspector.");
            return;
        }

        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        hexMesh = BuildHexMesh(OuterRadius * TileScale, 0.5f);

        // 1. Make one "prototype" tile entity with everything needed to render it.
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

        // 2. Copy it once per tile. Instantiating in bulk is much faster than creating one by one.
        int count = Width * Height;
        NativeArray<Entity> tiles = entityManager.Instantiate(prototype, count, Allocator.Temp);
        entityManager.DestroyEntity(prototype);

        // 3. Create the grid singleton that systems use to look up tiles.
        int2 originOffset = new int2(-Width / 2, -Height / 2);
        Entity gridEntity = entityManager.CreateEntity();
        entityManager.SetName(gridEntity, "HexGrid");
        entityManager.AddComponentData(gridEntity, new HexGrid
        {
            Width = Width,
            Height = Height,
            OuterRadius = OuterRadius,
            OriginOffset = originOffset,
        });
        // Fill a temporary array first, then copy it into the grid's buffer at the end.
        NativeArray<HexCell> cells = new NativeArray<HexCell>(count, Allocator.Temp);

        // 4. Decide each tile's terrain and place it.
        float2 seedOffset = new float2(Seed * 37.1f, Seed * 91.7f);
        for (int row = 0; row < Height; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                int index = row * Width + column;
                int2 axial = HexMath.OffsetToAxial(originOffset + new int2(column, row));
                float3 position = HexMath.AxialToWorld(axial, OuterRadius);

                HexTerrain terrain = PickTerrain(axial, position, seedOffset);
                float height = terrain == HexTerrain.Water ? -0.15f : terrain == HexTerrain.Rock ? 0.3f : 0f;
                position.y = height;

                Entity tile = tiles[index];
                entityManager.SetComponentData(tile, new LocalToWorld { Value = float4x4.Translate(position) });
                entityManager.SetComponentData(tile, new HexTile { Axial = axial });
                entityManager.SetComponentData(tile, new URPMaterialPropertyBaseColor { Value = ColorFor(terrain) });

                cells[index] = new HexCell { Terrain = terrain, Occupied = false, Tile = tile };
            }
        }

        entityManager.AddBuffer<HexCell>(gridEntity).AddRange(cells);
        cells.Dispose();
        tiles.Dispose();
    }

    HexTerrain PickTerrain(int2 axial, float3 position, float2 seedOffset)
    {
        if (HexMath.Distance(axial, int2.zero) <= ClearRadius)
            return HexTerrain.Grass;

        // Simplex noise gives smooth random values from -1 to 1 that form natural-looking blobs.
        float n = noise.snoise(position.xz * NoiseScale + seedOffset);
        if (n < -0.45f) return HexTerrain.Water;
        if (n < 0.3f) return HexTerrain.Grass;
        if (n < 0.55f) return HexTerrain.Dirt;
        return HexTerrain.Rock;
    }

    float4 ColorFor(HexTerrain terrain)
    {
        Color color = terrain switch
        {
            HexTerrain.Dirt => DirtColor,
            HexTerrain.Rock => RockColor,
            HexTerrain.Water => WaterColor,
            _ => GrassColor,
        };
        return (Vector4)color.linear;
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
