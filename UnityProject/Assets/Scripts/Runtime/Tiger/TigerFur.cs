using UnityEngine;

/// Shell fur on the HD tiger's fur mesh (SK_WhiteTigerHD_Fur: a decimated copy on the same bones, with fur data in UV1 /
/// UV2 and baked AO in its vertex colour): its materials are replaced by one FurShell material per shell height, so the
/// skinned mesh is drawn once per shell (skinned once). Shell count follows the camera distance (close-ups dense,
/// overview / portal lighter); the full HD mesh is drawn once underneath as the skin.
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class TigerFur : MonoBehaviour
{
    public Material shell;                                // FurShell template (M_WhiteTigerFur)
    public int[] lodLayers = { 24, 16, 10 };              // 32 / 20 / 12 cost 19-32 ms GPU in close-ups (fragment-bound:
    public float[] lodDistance = { 1.8f, 4.2f };          // every shell covers the silhouette); camera distance (m) per level
    public int forceLevel = -1;                           // tests: fixed level (-1 = by distance)
    public float lengthScale = 1f;                        // multiplies the template's _FurLength

    static readonly int LayerId = Shader.PropertyToID("_Layer"), LengthId = Shader.PropertyToID("_FurLength");
    SkinnedMeshRenderer r; Material[][] sets; int level = -1;
    public int Layers => level >= 0 ? lodLayers[level] : 0;

    void Awake()
    {
        r = GetComponent<SkinnedMeshRenderer>();
        if (!shell) return;
        float scale = transform.lossyScale.x / 100f;      // FBX node scale 100 x tiger scale: fur length in world metres
        sets = new Material[lodLayers.Length][];
        for (int l = 0; l < lodLayers.Length; l++)
        {
            int n = lodLayers[l]; sets[l] = new Material[n];
            for (int i = 0; i < n; i++)
            {
                var m = new Material(shell) { name = $"{shell.name}_L{l}_{i:D2}", renderQueue = shell.renderQueue + i };   // inner shells first
                m.SetFloat(LayerId, (i + 1f) / n);
                m.SetFloat(LengthId, shell.GetFloat(LengthId) * lengthScale * Mathf.Max(scale, 0.01f));
                sets[l][i] = m;
            }
        }
        SetLevel(0);
    }

    void SetLevel(int l) { if (l == level || sets == null) return; level = l; r.sharedMaterials = sets[l]; }

    /// Tests (unity/perf_ab.cs): the old setup for an A/B in the same run: shells on the full HD mesh, n layers.
    public void TestLegacy(Mesh fullMesh, int layers)
    {
        GetComponent<SkinnedMeshRenderer>().sharedMesh = fullMesh;
        var set = new Material[layers];
        for (int i = 0; i < layers; i++) { set[i] = new Material(sets[0][0]); set[i].SetFloat(LayerId, (i + 1f) / layers); set[i].renderQueue = shell.renderQueue + i; }
        sets = new[] { set }; lodLayers = new[] { layers }; level = -1; forceLevel = 0; SetLevel(0);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (!cam || sets == null) return;
        float d = Vector3.Distance(cam.transform.position, r.bounds.center);
        int l = 0; while (l < lodDistance.Length && d > lodDistance[l] + (l < level ? 0.25f : 0f)) l++;   // small hysteresis
        SetLevel(forceLevel >= 0 ? Mathf.Min(forceLevel, sets.Length - 1) : Mathf.Min(l, sets.Length - 1));
    }
}
