// ObstacleAccentApplier.cs
using UnityEngine;

[DisallowMultipleComponent]
public class ObstacleAccentApplier : MonoBehaviour
{
    [ColorUsage(true, true)] public Color emissionColor = Color.cyan; // HDR color
    [Min(0f)] public float intensity = 0.8f; // lower = less bright 
    [Tooltip("Only apply to materials whose name contains any of these tokens.")]
    public string[] materialNameMustContain = new[] { "glow", "trim", "emiss" };

    static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock _mpb;

    void OnEnable() => Apply();

    public void Apply()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in renderers)
        {
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var smat = mats[i];
                if (smat == null) continue;

                string n = smat.name.ToLowerInvariant();
                bool looksGlow = false;
                foreach (var token in materialNameMustContain)
                    if (n.Contains(token)) { looksGlow = true; break; }
                if (!looksGlow) continue;

                mr.GetPropertyBlock(_mpb, i);
                _mpb.SetColor(EmissionColorID, emissionColor * intensity);
                mr.SetPropertyBlock(_mpb, i);
            }
        }
    }
}
