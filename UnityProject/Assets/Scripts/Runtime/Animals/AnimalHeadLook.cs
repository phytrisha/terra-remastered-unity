using UnityEngine;

/// Head look-at for ambient animals: after the Animator, turns the neck and head toward AnimalWander.LookTarget (the
/// viewer, or ahead into a turn) by AnimalWander.LookWeight (max angle clamped). The head's forward is taken from the animal's facing at start, when the
/// skeleton is in its first idle pose. (The tiger does the same inside TigerEncounter.)
public class AnimalHeadLook : MonoBehaviour
{
    public AnimalWander wander;
    public Transform head, neck;
    public float maxAngle = 45f, neckShare = 0.4f;
    Vector3 headFwdLocal;

    void Start()
    {
        if (head) headFwdLocal = head.InverseTransformDirection((transform.forward - Vector3.up * 0.25f).normalized);
    }

    void LateUpdate()
    {
        if (!head || !wander || !wander.enabled) return;
        float w = wander.LookWeight;
        if (w <= 0.001f) return;
        var target = wander.LookTarget;                  // the viewer, or ahead into a turn
        foreach (var (bone, share) in new[] { (neck, neckShare), (head, 1f) })
        {
            if (!bone) continue;
            var cur = head.rotation * headFwdLocal;
            var want = (target - head.position).normalized;
            var delta = Quaternion.FromToRotation(cur, Vector3.RotateTowards(cur, want, maxAngle * Mathf.Deg2Rad, 0));
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, w * share * (bone == head ? 0.6f : 1f)) * bone.rotation;
        }
    }
}
