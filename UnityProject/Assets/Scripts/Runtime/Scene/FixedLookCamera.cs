using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// First-person viewer that stays at a fixed position and can only look around.
/// Click to capture the mouse, Escape to release it. Right stick / arrow keys also look.
[DisallowMultipleComponent]
public class FixedLookCamera : MonoBehaviour
{
    [Tooltip("Degrees per pixel of mouse movement.")] public float mouseSensitivity = 0.12f;
    [Tooltip("Degrees per second for keys / gamepad.")] public float stickSpeed = 90f;
    public float minPitch = -75f, maxPitch = 80f;
    [Tooltip("Exponential smoothing time (s); 0 = raw.")] public float smoothing = 0.06f;

    [Tooltip("Show a short how-to hint until the mouse is first captured.")] public bool showHint = true;

    float yaw, pitch, curYaw, curPitch, hintAlpha = 1f;
    bool everCaptured;
    GUIStyle hintStyle;
    Vector3 lockedPosition;

    void OnEnable()
    {
        lockedPosition = transform.position;
        var e = transform.eulerAngles;
        yaw = curYaw = e.y;
        pitch = curPitch = e.x > 180f ? e.x - 360f : e.x;
    }

    /// Turn (smoothly) to look at a world point; used by tests and scripted moments.
    public void LookAt(Vector3 world)
    {
        var d = world - transform.position;
        yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        pitch = Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, minPitch, maxPitch);
    }

    void Update()
    {
        Vector2 mouse = Vector2.zero, stick = Vector2.zero;
        bool click = false, escape = false;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) { mouse = Mouse.current.delta.ReadValue(); click = Mouse.current.leftButton.wasPressedThisFrame; }
        if (Keyboard.current != null)
        {
            var k = Keyboard.current;
            escape = k.escapeKey.wasPressedThisFrame;
            stick.x = (k.rightArrowKey.isPressed || k.dKey.isPressed ? 1 : 0) - (k.leftArrowKey.isPressed || k.aKey.isPressed ? 1 : 0);
            stick.y = (k.upArrowKey.isPressed || k.wKey.isPressed ? 1 : 0) - (k.downArrowKey.isPressed || k.sKey.isPressed ? 1 : 0);
        }
        if (Gamepad.current != null) stick += Gamepad.current.rightStick.ReadValue();
#else
        mouse = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 20f;
        click = Input.GetMouseButtonDown(0);
        escape = Input.GetKeyDown(KeyCode.Escape);
        stick = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        if (click) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; everCaptured = true; }
        if (escape) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        bool captured = Cursor.lockState == CursorLockMode.Locked || Application.isMobilePlatform;
        if (captured) { yaw += mouse.x * mouseSensitivity; pitch -= mouse.y * mouseSensitivity; }
        yaw += stick.x * stickSpeed * Time.unscaledDeltaTime;
        pitch -= stick.y * stickSpeed * Time.unscaledDeltaTime;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        float k01 = smoothing > 0f ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothing) : 1f;
        curYaw = Mathf.LerpAngle(curYaw, yaw, k01);
        curPitch = Mathf.Lerp(curPitch, pitch, k01);
        transform.SetPositionAndRotation(lockedPosition, Quaternion.Euler(curPitch, curYaw, 0f));
        hintAlpha = Mathf.MoveTowards(hintAlpha, everCaptured ? 0f : 1f, Time.unscaledDeltaTime * 1.5f);
    }

    void OnGUI()
    {
        if (!showHint || hintAlpha <= 0.01f) return;
        hintStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        var r = new Rect(0, Screen.height - 64, Screen.width, 32);
        GUI.color = new Color(0, 0, 0, 0.45f * hintAlpha);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), "Click to look around  ·  Esc to release the mouse  ·  arrows / WASD also look", hintStyle);
        GUI.color = new Color(1, 1, 1, hintAlpha);
        GUI.Label(r, "Click to look around  ·  Esc to release the mouse  ·  arrows / WASD also look", hintStyle);
        GUI.color = Color.white;
    }
}
