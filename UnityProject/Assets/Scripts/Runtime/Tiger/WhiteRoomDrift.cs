using UnityEngine;

/// Slow bob and spin for the abstract shapes floating in the white showcase room.
public class WhiteRoomDrift : MonoBehaviour
{
    public float bob = 0.25f, bobSpeed = 0.35f, spin = 6f;
    public Vector3 axis = Vector3.up;
    Vector3 basePos; float phase;

    void OnEnable() { basePos = transform.localPosition; phase = (transform.position.x * 1.7f + transform.position.z * 0.9f) % 6.283f; }

    void Update()
    {
        transform.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * bobSpeed + phase) * bob;
        transform.Rotate(axis, spin * Time.deltaTime, Space.Self);
    }
}
