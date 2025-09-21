// ObstacleAccentApplier.cs
using UnityEngine;

[DisallowMultipleComponent]
public class ObstacleAccentApplier : MonoBehaviour
{
    [ColorUsage(true, true)] public Color emissionColor = Color.cyan;   // HDR color so it can bloom
    [Min(0f)] public float intensity = 0.8f;                            // multiplier applied to color
    [Tooltip("only apply to materials whose name contains any of these tokens")]
    public string[] materialNameMustContain = new[] { "glow", "trim", "emiss" };

    static readonly int EmissionColorID = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock _mpb;

    private void OnEnable() => Apply(); // refresh when object is enabled (works for pooling too)

    public void Apply()
    {
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        // check all mesh renderers on this object and children
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var mr in renderers)
        {
            var mats = mr.sharedMaterials; // shared so we don’t accidentally duplicate materials
            for (int i = 0; i < mats.Length; i++)
            {
                var smat = mats[i];
                if (smat == null) continue;

                // only apply if the material name has one of the filter tokens
                string n = smat.name.ToLowerInvariant();
                bool looksGlow = false;
                foreach (var token in materialNameMustContain)
                {
                    if (n.Contains(token)) { looksGlow = true; break; }
                }
                if (!looksGlow) continue;

                // set emission color without modifying the actual material asset
                mr.GetPropertyBlock(_mpb, i);
                _mpb.SetColor(EmissionColorID, emissionColor * intensity);
                mr.SetPropertyBlock(_mpb, i);
            }
        }
    }
}
