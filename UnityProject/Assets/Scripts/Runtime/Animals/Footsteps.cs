using UnityEngine;

/// Soft footfalls on the forest floor at each touchdown of QuadrupedLocomotion: a short procedural thud + litter
/// crackle (generated once, no asset), 3D at the paw, pitch / volume varied, heavier animals lower and louder.
public class Footsteps : MonoBehaviour
{
    public QuadrupedLocomotion loco;
    [Range(0, 1)] public float volume = 0.35f;
    public float pitch = 1f;                       // < 1 = heavier
    public float maxDistance = 22f;

    static AudioClip[] clips;
    AudioSource[] pool; int next;

    void OnEnable()
    {
        if (!loco) loco = GetComponent<QuadrupedLocomotion>();
        if (loco) loco.Touchdown += Play;
        if (clips == null) { clips = new AudioClip[4]; for (int i = 0; i < clips.Length; i++) clips[i] = Make(i); }
        if (pool == null)
        {
            pool = new AudioSource[4];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("Footstep"); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = 1f; a.maxDistance = maxDistance; a.dopplerLevel = 0f;
                pool[i] = a;
            }
        }
    }

    void OnDisable() { if (loco) loco.Touchdown -= Play; }

    void Play(Vector3 at)
    {
        var a = pool[next]; next = (next + 1) % pool.Length;
        a.transform.position = at;
        a.pitch = pitch * Random.Range(0.88f, 1.12f);
        a.PlayOneShot(clips[Random.Range(0, clips.Length)], volume * Random.Range(0.7f, 1f));
    }

    // thud: low decaying sine with a little noise; crackle: a few short high clicks (dry leaves)
    static AudioClip Make(int variant)
    {
        const int rate = 44100; int n = (int)(rate * 0.22f);
        var rnd = new System.Random(17 + variant * 31);
        var d = new float[n];
        float f0 = 70f + variant * 9f, lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float thud = Mathf.Sin(2 * Mathf.PI * f0 * t * (1f - 0.6f * t)) * Mathf.Exp(-t * 38f);
            float noise = (float)rnd.NextDouble() * 2f - 1f;
            lp += (noise - lp) * 0.25f;                                  // brown-ish rustle
            d[i] = 0.75f * thud + 0.35f * lp * Mathf.Exp(-t * 22f);
        }
        for (int c = 0; c < 3 + variant; c++)
        {
            int at = (int)(rate * (0.005f + 0.06f * rnd.NextDouble())), len = 120 + rnd.Next(200);
            for (int i = 0; i < len && at + i < n; i++) d[at + i] += ((float)rnd.NextDouble() * 2f - 1f) * 0.25f * Mathf.Exp(-i / 40f);
        }
        var clip = AudioClip.Create("Footstep_" + variant, n, 1, rate, false);
        clip.SetData(d, 0);
        return clip;
    }
}
