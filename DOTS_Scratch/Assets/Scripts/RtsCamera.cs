using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-down RTS camera. Add to the Main Camera in GameScene (NOT inside the SubScene —
/// the camera is a regular GameObject, since there's only one of it).
///
/// Controls: WASD / arrow keys or mouse at the screen edge to pan, scroll wheel to zoom.
///
/// How it works: the camera orbits a "focus point" on the ground. Panning moves the
/// focus point; zooming changes how far the camera sits from it. The camera's
/// position and angle are recalculated from those every frame.
/// </summary>
public class RtsCamera : MonoBehaviour
{
    [Header("Pan")]
    [Tooltip("Pan speed in world units per second at medium zoom. Speeds up when zoomed out.")]
    public float PanSpeed = 25f;
    [Tooltip("Pan when the mouse touches the edge of the screen. Untick if it's annoying in the Editor.")]
    public bool EdgePan = true;
    [Tooltip("How close to the screen edge (in pixels) the mouse must be to pan.")]
    public float EdgeSize = 15f;

    [Header("Zoom")]
    public float MinDistance = 10f;
    public float MaxDistance = 80f;
    [Tooltip("How much each scroll step zooms. Raise or lower if zoom feels too slow or fast.")]
    public float ZoomSensitivity = 0.05f;
    [Tooltip("Higher = zoom snaps faster; lower = smoother.")]
    public float ZoomSmoothing = 10f;

    [Header("Angle")]
    [Tooltip("Tilt in degrees: 90 looks straight down.")]
    [Range(20f, 90f)] public float Pitch = 55f;
    [Tooltip("Which way the camera faces, in degrees. 0 = looking toward +Z.")]
    public float Yaw = 0f;

    [Header("Map bounds (X/Z)")]
    public Vector2 MapMin = new Vector2(-50f, -50f);
    public Vector2 MapMax = new Vector2(50f, 50f);

    Vector3 focusPoint;
    float distance;
    float targetDistance;

    void Start()
    {
        // Start focused on the ground point the camera is currently looking at.
        Ray view = new Ray(transform.position, transform.forward);
        if (new Plane(Vector3.up, Vector3.zero).Raycast(view, out float hitDistance))
            focusPoint = view.GetPoint(hitDistance);
        else
            focusPoint = new Vector3(transform.position.x, 0f, transform.position.z);

        distance = targetDistance = (MinDistance + MaxDistance) * 0.5f;
        ApplyTransform();
    }

    void Update()
    {
        Pan();
        Zoom();
        ApplyTransform();
    }

    void Pan()
    {
        Vector2 input = Vector2.zero;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
        }

        Mouse mouse = Mouse.current;
        if (EdgePan && mouse != null && Application.isFocused)
        {
            Vector2 m = mouse.position.ReadValue();
            bool insideWindow = m.x >= 0f && m.x <= Screen.width && m.y >= 0f && m.y <= Screen.height;
            if (insideWindow)
            {
                if (m.x < EdgeSize) input.x -= 1f;
                else if (m.x > Screen.width - EdgeSize) input.x += 1f;
                if (m.y < EdgeSize) input.y -= 1f;
                else if (m.y > Screen.height - EdgeSize) input.y += 1f;
            }
        }

        if (input == Vector2.zero)
            return;

        input = Vector2.ClampMagnitude(input, 1f);

        // "Forward" means the direction the camera faces, flattened onto the ground.
        Vector3 move = Quaternion.Euler(0f, Yaw, 0f) * new Vector3(input.x, 0f, input.y);

        // Pan faster when zoomed out so it feels the same at every zoom level.
        float zoom01 = Mathf.InverseLerp(MinDistance, MaxDistance, distance);
        float speed = PanSpeed * Mathf.Lerp(0.5f, 2f, zoom01);

        focusPoint += move * speed * Time.deltaTime;
        focusPoint.x = Mathf.Clamp(focusPoint.x, MapMin.x, MapMax.x);
        focusPoint.z = Mathf.Clamp(focusPoint.z, MapMin.y, MapMax.y);
    }

    void Zoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            targetDistance = Mathf.Clamp(targetDistance - scroll * ZoomSensitivity, MinDistance, MaxDistance);
        }

        // Ease toward the target distance instead of jumping, so zoom feels smooth.
        distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-ZoomSmoothing * Time.deltaTime));
    }

    void ApplyTransform()
    {
        transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
        transform.position = focusPoint - transform.forward * distance;
    }
}
