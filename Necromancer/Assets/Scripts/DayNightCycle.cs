using Unity.Burst;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

// ---------------------------------------------------------------------------
// GAME CLOCK (see "Game time" in docs/GAME_MECHANICS.md)
// 1 tick = 1 game minute = 1 real second. 1 game hour = 1 real minute.
// The clock lives in ECS so gameplay systems (production per game hour, the
// final onslaught at hour 72) can read it, including from Burst jobs.
// ---------------------------------------------------------------------------

/// <summary>Singleton holding the match's game time. Created automatically by GameClockSystem.</summary>
public struct GameClock : IComponentData
{
    public const int StartHour = 6; // matches begin at 06:00 on Day 1
    public const int MinutesPerHour = 60;
    public const int HoursPerDay = 24;
    public const int MinutesPerDay = MinutesPerHour * HoursPerDay;
    public const float SecondsPerTick = 1f;
    public const int FinalOnslaughtHour = 72; // hours after the match starts

    /// <summary>Game minutes since the match started.</summary>
    public int Tick;
    /// <summary>How many ticks happened this frame (usually 0 or 1). Per-tick systems loop this many times.</summary>
    public int TicksThisFrame;
    /// <summary>0..1 progress toward the next tick, for smooth visuals like the sun's movement.</summary>
    public float TickProgress;
    /// <summary>1 = normal speed. Only changed for testing (DayNightCycle's Clock Speed).</summary>
    public float Speed;

    /// <summary>Game minutes since midnight at the start of Day 1.</summary>
    public int TotalMinutes => StartHour * MinutesPerHour + Tick;
    public int Day => TotalMinutes / MinutesPerDay + 1;
    public int Hour => TotalMinutes / MinutesPerHour % HoursPerDay;
    public int Minute => TotalMinutes % MinutesPerHour;
    /// <summary>Whole game hours since the match started (the final onslaught arrives at 72).</summary>
    public int ElapsedHours => Tick / MinutesPerHour;
    /// <summary>Time of day from 0 to 1: 0 = midnight, 0.25 = 06:00, 0.5 = noon. Smooth between ticks.</summary>
    public float TimeOfDay01 => (TotalMinutes + TickProgress) % MinutesPerDay / MinutesPerDay;
}

/// <summary>Advances the game clock. Runs first in the frame so every other system sees this frame's time.</summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
public partial struct GameClockSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton(new GameClock { Speed = 1f }, "GameClock");
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        RefRW<GameClock> clock = SystemAPI.GetSingletonRW<GameClock>();
        clock.ValueRW.TicksThisFrame = 0;

        float progress = clock.ValueRO.TickProgress
            + SystemAPI.Time.DeltaTime * clock.ValueRO.Speed / GameClock.SecondsPerTick;

        while (progress >= 1f)
        {
            progress -= 1f;
            clock.ValueRW.Tick++;
            clock.ValueRW.TicksThisFrame++;
        }
        clock.ValueRW.TickProgress = progress;
    }
}

// ---------------------------------------------------------------------------
// DAY/NIGHT VISUALS + UI CLOCK
// A MonoBehaviour: there's one sun and one clock display. The cycle is purely
// visual: it reads the game clock and never affects gameplay.
// ---------------------------------------------------------------------------

/// <summary>
/// Moves the sun with the game clock, dims to moonlight at night, and shows the
/// clock at the top of the screen (e.g. "Day 2 — 14:30").
/// Add to any GameObject in GameScene (NOT inside the SubScene). Sun defaults to the Directional Light.
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    [Tooltip("The Directional Light. Found automatically if left empty.")]
    public Light Sun;
    [Tooltip("Compass direction of the sun's path, in degrees.")]
    public float SunAzimuth = -30f;

    [Header("Day")]
    public float SunIntensity = 1.2f;
    public Color NoonColor = new Color(1f, 0.96f, 0.88f);
    public Color HorizonColor = new Color(1f, 0.55f, 0.3f);
    public Color DayAmbient = new Color(0.55f, 0.58f, 0.62f);

    [Header("Night")]
    [Tooltip("Moonlight keeps the map readable at night.")]
    public float MoonIntensity = 0.3f;
    public Color MoonColor = new Color(0.45f, 0.55f, 0.85f);
    public Color NightAmbient = new Color(0.1f, 0.12f, 0.22f);

    [Header("Testing")]
    [Tooltip("Speeds up ONLY the clock (not minions or enemies). 60 = a game hour per real second.")]
    public float ClockSpeed = 1f;

    EntityQuery clockQuery;
    bool queryCreated;
    GUIStyle clockStyle;
    GUIStyle shadowStyle;
    string clockText = "";

    void Start()
    {
        if (Sun == null)
            Sun = RenderSettings.sun;
        if (Sun == null)
            Sun = FindAnyObjectByType<Light>();

        // Flat ambient light lets us fade the scene's overall brightness between day and night.
        RenderSettings.ambientMode = AmbientMode.Flat;
    }

    void Update()
    {
        if (!TryGetClock(out GameClock clock))
            return;

        if (clock.Speed != ClockSpeed)
        {
            clock.Speed = ClockSpeed;
            clockQuery.SetSingleton(clock);
        }

        clockText = $"Day {clock.Day} — {clock.Hour:00}:{clock.Minute:00}";
        UpdateLighting(clock.TimeOfDay01);
    }

    void UpdateLighting(float timeOfDay)
    {
        // Sun angle above the horizon: 0° at 06:00, 90° (overhead) at noon, 180° at 18:00.
        float sunElevation = (timeOfDay - 0.25f) * 360f;
        float sunHeight = Mathf.Sin(sunElevation * Mathf.Deg2Rad); // 1 at noon, 0 at dawn/dusk, -1 at midnight

        if (Sun != null)
        {
            if (sunHeight > 0f)
            {
                Sun.transform.rotation = Quaternion.Euler(sunElevation, SunAzimuth, 0f);
                Sun.color = Color.Lerp(HorizonColor, NoonColor, Mathf.Sqrt(sunHeight)); // orange near the horizon
                Sun.intensity = SunIntensity * Mathf.Clamp01(sunHeight * 3f);
            }
            else
            {
                // At night the same light becomes the moon, on the opposite side of the sky.
                Sun.transform.rotation = Quaternion.Euler(sunElevation + 180f, SunAzimuth, 0f);
                Sun.color = MoonColor;
                Sun.intensity = MoonIntensity * Mathf.Clamp01(-sunHeight * 3f);
            }
        }

        float daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.2f, sunHeight));
        RenderSettings.ambientLight = Color.Lerp(NightAmbient, DayAmbient, daylight);
    }

    bool TryGetClock(out GameClock clock)
    {
        clock = default;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        if (!queryCreated)
        {
            clockQuery = world.EntityManager.CreateEntityQuery(typeof(GameClock));
            queryCreated = true;
        }
        return clockQuery.TryGetSingleton(out clock);
    }

    void OnGUI()
    {
        if (clockText.Length == 0)
            return;

        // Explicit font: works around a Unity IMGUI bug when the default font is missing.
        if (clockStyle == null)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            clockStyle = new GUIStyle { font = font, fontSize = 22, alignment = TextAnchor.UpperCenter };
            clockStyle.normal.textColor = Color.white;
            shadowStyle = new GUIStyle(clockStyle);
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        }

        Rect rect = new Rect(0f, 10f, Screen.width, 30f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), clockText, shadowStyle);
        GUI.Label(rect, clockText, clockStyle);
    }
}
