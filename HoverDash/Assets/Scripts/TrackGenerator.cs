// TrackGenerator.cs
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class TrackGenerator : MonoBehaviour
{
    [Header("Determinism")]
    public int seed = 12345;

    [Header("Track")]
    public float trackLength = 500f;
    public int lanes = 3;
    [Tooltip("Half the total width of the track (wall centers live here).")]
    public float halfTrackWidth = 4f;

    [Header("Ground (auto-built)")]
    public bool buildGround = true;
    public float groundY = 0f;
    public Material groundMaterial;
    public bool addGroundCollider = true;
    public float groundColliderThickness = 0.2f;
    public Vector2 groundUVTilesPerUnit = new Vector2(0.1f, 0.1f);

    [Header("Walls (auto-built)")]
    public bool buildWalls = true;
    [Tooltip("How tall the side walls are.")]
    public float wallHeight = 2.5f;
    [Tooltip("Wall thickness along X (thicker = sturdier collider).")]
    public float wallThickness = 0.25f;
    [Tooltip("Optional material for the walls.")]
    public Material wallMaterial;
    [Tooltip("Add BoxColliders to the walls.")]
    public bool addWallColliders = true;

    // Obstacles (weighted)
    [System.Serializable]
    public class WeightedObstacle
    {
        public GameObject prefab;
        [Min(0f)] public float weight = 1f;
    }

    [Header("Obstacle Spawning")]
    [Tooltip("Choose obstacles using weights (probabilities ∝ weight).")]
    public List<WeightedObstacle> weightedObstaclePrefabs = new List<WeightedObstacle>();

    [Range(0f, 1f)] public float spawnProbability = 0.35f;
    [Tooltip("Minimum forward gap in the SAME lane.")]
    public float minForwardGap = 8f;
    public float yOffset = 0f;

    [Header("Random Gap Settings (Obstacles)")]
    [Tooltip("Minimum random gap between potential obstacle Z positions.")]
    public float gapMin = 6f;
    [Tooltip("Maximum random gap between potential obstacle Z positions.")]
    public float gapMax = 14f;

    [Header("X Bounds Safety")]
    [Tooltip("Extra margin from the inner face of each wall where obstacles will NOT spawn.")]
    public float obstaclePaddingFromWall = 0.25f;
    [Tooltip("Fallback half-width if we can't measure a prefab (kept for safety).")]
    public float obstacleApproxHalfWidth = 0.25f;

    [Header("Rotation/Scale Variations (deterministic)")]
    public bool randomYaw = true;
    public float maxYawDegrees = 12f;
    public bool randomUniformScale = false;
    public Vector2 scaleRange = new Vector2(0.9f, 1.1f);

    // Lane Variety (anti-alignment)
    [Header("Lane Variety (anti-alignment)")]
    [Tooltip("Prefer lanes that are far from recently spawned X positions; reduces straight-line runs (BlueNoise mode).")]
    public bool enforceLaneVariety = true;
    [Tooltip("How many recent obstacles to consider for spacing/variety (0 disables history).")]
    public int varietyHistory = 4;
    [Tooltip("Weight for blue-noise spacing from recent Xs. Higher = more spread out.")]
    public float blueNoiseWeight = 1.0f;
    [Tooltip("Bonus for using a lane that hasn't appeared in the recent window.")]
    public float newLaneBonus = 0.5f;
    [Tooltip("Max consecutive times the same lane can repeat. <=0 disables this cap.")]
    public int maxSameLaneStreak = 2;
    [Tooltip("Small deterministic jitter added to lane scores to break ties.")]
    public float laneJitter = 0.01f;

    // Lane Centers & Picking
    public enum LanePickMode { PureRandom, BlueNoise }

    [Header("Lane Centers")]
    [Tooltip("If true, place lane centers across the full usable track width (recommended). If false, shrinks lane centers by worst-case obstacle width.")]
    public bool laneCentersUseFullWidth = true;

    [Header("Lane Picking")]
    [Tooltip("PureRandom = pick any allowed lane uniformly. BlueNoise = spread across recent Xs.")]
    public LanePickMode lanePickMode = LanePickMode.PureRandom;
    [Tooltip("Small random horizontal jitter inside a slot to avoid perfect alignment.")]
    public float laneJitterX = 0f; 

    // Stars (one per row across whole track)
    [Header("Stars (random gaps; one per row)")]
    public bool generateStars = true;
    public GameObject starPrefab;
    [Tooltip("Minimum random Z gap between star rows.")]
    public float starsGapMinZ = 9f;
    [Tooltip("Maximum random Z gap between star rows.")]
    public float starsGapMaxZ = 15f;
    [Range(0f, 1f)]
    [Tooltip("Chance to place a star at each randomized row.")]
    public float starsRowSpawnProbability = 0.8f;
    [Tooltip("Extra vertical offset relative to groundY for stars.")]
    public float starsYOffset = 0.5f;
    [Tooltip("Rotate stars by 90° around X by default.")]
    public Vector3 starsLocalEuler = new Vector3(90f, 0f, 0f);
    [Tooltip("Prevent a star from spawning too near an obstacle in the same lane.")]
    public bool preventStarObstacleOverlap = true;
    [Tooltip("Min |ΔZ| from an obstacle in the same lane to allow a star.")]
    public float starClearanceZ = 2.0f;

    // Finish Line
    [Header("Finish Line")]
    public bool placeFinishLine = true;
    [Tooltip("Prefab to place at the end of the track.")]
    public GameObject finishLinePrefab;
    [Tooltip("Offset in Z from the end of the track (trackLength).")]
    public float finishZOffset = 0f;
    [Tooltip("Extra vertical offset after grounding the prefab.")]
    public float finishYOffset = 0f;
    [Tooltip("Rotate so it faces the player (usually 180 yaw if the player runs +Z).")]
    public Vector3 finishLocalEuler = new Vector3(0f, 180f, 0f);
    [Tooltip("If true, scale the finish line’s width to match the usable track width (between walls).")]
    public bool autoScaleFinishToTrackWidth = true;
    [Tooltip("Approximate original width (X) of your finish prefab (used for autoscale).")]
    public float finishPrefabApproxWidth = 3f;

    [Header("Lifecycle")]
    public bool clearBeforeGenerate = true;

    // Internal
    private float[] _laneLastZ;                 // for minForwardGap per lane
    private List<float>[] _lanePlacedZs;        // all obstacle Zs per lane (for star overlap checks)
    private Queue<float> _recentXs;             // obstacle variety history (positions)
    private Queue<int> _recentLanes;            // obstacle variety history (lane ids)
    private int _lastLaneUsed = -1;
    private int _sameLaneRun = 0;

    private const string GroundNodeName = "Ground_Auto";
    private const string ObstaclesNodeName = "Obstacles_Auto";
    private const string WallsNodeName = "Walls_Auto";
    private const string StarsNodeName = "Stars_Auto";
    private const string FinishNodeName = "Finish_Auto";

#if UNITY_EDITOR
    [ContextMenu("Generate (Editor)")] private void GenerateContextMenu() => Generate();
    [ContextMenu("Clear Generated")] private void ClearContextMenu() => ClearGenerated();
#endif

    public void Generate()
    {
        if (clearBeforeGenerate) ClearGenerated();

        if (buildGround) BuildGround();
        if (buildWalls) BuildWalls();
        if (placeFinishLine && finishLinePrefab != null) PlaceFinishLine();

        var obstaclesRoot = GetOrCreateChild(ObstaclesNodeName);
        var prng = new System.Random(seed);

        // Reset lane history/spacing trackers
        _recentXs = new Queue<float>(Mathf.Max(1, varietyHistory));
        _recentLanes = new Queue<int>(Mathf.Max(1, varietyHistory));
        _lastLaneUsed = -1;
        _sameLaneRun = 0;

        // Per-lane last Z for minForwardGap & storage of all Z for star overlap
        _laneLastZ = new float[Mathf.Max(1, lanes)];
        _lanePlacedZs = new List<float>[Mathf.Max(1, lanes)];
        for (int i = 0; i < lanes; i++)
        {
            _laneLastZ[i] = float.NegativeInfinity;
            _lanePlacedZs[i] = new List<float>(64);
        }

        // Lane centers across usable width (or worst-case shrink)
        float usableHalfBase = Mathf.Max(
            0.05f,
            halfTrackWidth - (wallThickness * 0.5f) - obstaclePaddingFromWall
        );

        float[] laneXs;
        if (laneCentersUseFullWidth)
        {
            laneXs = ComputeLaneXs(lanes, usableHalfBase);
        }
        else
        {
            float worstCaseHalfWidth = EstimateMaxHalfWidthAcrossPrefabs();
            float usableHalfForLanes = Mathf.Max(0.05f, usableHalfBase - Mathf.Max(0f, worstCaseHalfWidth));
            laneXs = ComputeLaneXs(lanes, usableHalfForLanes);
        }

        // Obstacles (always random gaps)
        if (HasAnyObstaclePrefab())
        {
            float z = 0f;
            float gMin = Mathf.Max(0.01f, gapMin);
            float gMax = Mathf.Max(gMin, gapMax);

            while (z <= trackLength)
            {
                bool spawnHere = (float)prng.NextDouble() <= spawnProbability;
                if (spawnHere)
                    TrySpawnOneObstacle(prng, z, laneXs, obstaclesRoot);

                float gap = Mathf.Lerp(gMin, gMax, (float)prng.NextDouble());
                z += gap;
            }
        }
        else
        {
            Debug.LogWarning("[TrackGenerator] No weighted obstacle prefabs assigned.");
        }

        // Stars (whole track, random gaps, ONE star per row)
        if (generateStars && starPrefab != null && lanes > 0)
        {
            GenerateStarsAlongTrack(laneXs);
        }
    }

    // OBSTACLES 

    private void TrySpawnOneObstacle(System.Random prng, float z, float[] laneXs, Transform parent)
    {
        if (lanes <= 0) return;

        var prefab = SelectObstaclePrefab(prng);
        if (!prefab) return;

        // Random yaw (affects width)
        Quaternion rot = Quaternion.identity;
        if (randomYaw)
        {
            float yaw = Mathf.Lerp(-maxYawDegrees, maxYawDegrees, (float)prng.NextDouble());
            rot = Quaternion.Euler(0f, yaw, 0f);
        }

        // Instantiate (editor/runtime safe)
#if UNITY_EDITOR
        GameObject go;
        if (!Application.isPlaying)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Generate Obstacle");
        }
        else { go = Object.Instantiate(prefab, parent); }
#else
        var go = Object.Instantiate(prefab, parent);
#endif
        go.transform.localRotation = rot;

        var accentColors = new[]
    {
        new Color(0f, 0.9f, 1f),   // cyan
        new Color(1f, 0.8f, 0.2f), // amber
        new Color(0.6f, 1f, 0.8f)  // mint
    };

var applier = go.GetComponent<ObstacleAccentApplier>();
if (!applier) applier = go.AddComponent<ObstacleAccentApplier>();

applier.emissionColor = accentColors[prng.Next(0, accentColors.Length)];
applier.intensity = 0.8f; // ↓ less bright (try 0.6–1.0 if still hot)
applier.Apply();

        var mrList = go.GetComponentsInChildren<MeshRenderer>(true);
        var mpb = new MaterialPropertyBlock();
        Color[] accent = {
        new Color(0f, 0.9f, 1f),   // cyan
        new Color(1f, 0.8f, 0.2f), // amber
        new Color(0.6f, 1f, 0.8f)  // mint
    };
        var c = accent[prng.Next(0, accent.Length)];
        foreach (var mr in mrList)
        {
            mr.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissionColor", c * 2.2f); // tweak intensity to taste
            mr.SetPropertyBlock(mpb);
        }

        // Grounding offset from renderers
        float bottomY = 0f;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            float minY = float.MaxValue;
            foreach (var r in renderers) minY = Mathf.Min(minY, r.bounds.min.y);
            bottomY = go.transform.position.y - minY;
        }

        // Per-spawn usable width from real measured half-width
        float halfWidthX = MeasureHalfWidthLocalX(go.transform);
        float usableHalfPerObstacle = Mathf.Max(
            0.05f,
            halfTrackWidth - (wallThickness * 0.5f) - obstaclePaddingFromWall - Mathf.Max(0f, halfWidthX)
        );

        // Choose lane (pure random or blue-noise), minForwardGap, clamping
        if (!TryChooseLane(prng, z, laneXs, usableHalfPerObstacle, out int chosenLane, out float xFinal))
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.DestroyObjectImmediate(go);
            else Destroy(go);
#else
            Destroy(go);
#endif
            return;
        }

        // Place
        go.transform.localPosition = new Vector3(xFinal, groundY + yOffset + bottomY, z);

        // Optional random uniform scale — re-evaluate usable region and lane pick
        if (randomUniformScale)
        {
            float s = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)prng.NextDouble());
            go.transform.localScale = new Vector3(s, s, s);

            halfWidthX = MeasureHalfWidthLocalX(go.transform);
            usableHalfPerObstacle = Mathf.Max(
                0.05f,
                halfTrackWidth - (wallThickness * 0.5f) - obstaclePaddingFromWall - Mathf.Max(0f, halfWidthX)
            );

            if (!TryChooseLane(prng, z, laneXs, usableHalfPerObstacle, out chosenLane, out xFinal))
            {
                float xDesired = laneXs[Mathf.Clamp(chosenLane, 0, lanes - 1)];
                xFinal = Mathf.Clamp(xDesired, -usableHalfPerObstacle, +usableHalfPerObstacle);
            }
            go.transform.localPosition = new Vector3(xFinal, groundY + yOffset + bottomY, z);
        }

        // Update lane spacing & history
        _laneLastZ[chosenLane] = z;
        _lanePlacedZs[chosenLane].Add(z);

        if (_lastLaneUsed == chosenLane) _sameLaneRun++;
        else { _lastLaneUsed = chosenLane; _sameLaneRun = 1; }

        if (enforceLaneVariety && varietyHistory > 0)
        {
            _recentXs.Enqueue(xFinal);
            if (_recentXs.Count > varietyHistory) _recentXs.Dequeue();

            _recentLanes.Enqueue(chosenLane);
            if (_recentLanes.Count > varietyHistory) _recentLanes.Dequeue();
        }
    }

    private bool TryChooseLane(System.Random prng, float z, float[] laneXs, float usableHalfPerObstacle, out int laneIndex, out float xFinal)
    {
        laneIndex = -1;
        xFinal = 0f;
        if (lanes <= 0) return false;

        // Build candidate lanes that satisfy per-lane forward gap
        var candidates = new List<int>(lanes);
        for (int i = 0; i < lanes; i++)
        {
            bool gapOk = (z - _laneLastZ[i]) >= minForwardGap;
            if (gapOk) candidates.Add(i);
        }
        if (candidates.Count == 0) return false;

        // MODE A: PURE RANDOM (uniform over valid lanes) 
        if (lanePickMode == LanePickMode.PureRandom)
        {
            int pick = candidates[prng.Next(0, candidates.Count)];
            float xDesired = laneXs[pick];
            float xCand = Mathf.Clamp(xDesired, -usableHalfPerObstacle, +usableHalfPerObstacle);

            // Optional small jitter to avoid perfect slot stacking
            if (laneJitterX > 0f)
            {
                float j = Mathf.Lerp(-laneJitterX, laneJitterX, (float)prng.NextDouble());
                xCand = Mathf.Clamp(xCand + j, -usableHalfPerObstacle, +usableHalfPerObstacle);
            }

            laneIndex = pick;
            xFinal = xCand;
            return true;
        }

        // MODE B: BLUE-NOISE (scoring) 
        double best = double.NegativeInfinity;
        int bestLane = -1;
        float bestX = 0f;

        foreach (int i in candidates)
        {
            float xDesired = laneXs[i];
            bool inside = Mathf.Abs(xDesired) <= (usableHalfPerObstacle + 0.0001f);
            float xCand = inside ? xDesired : Mathf.Clamp(xDesired, -usableHalfPerObstacle, +usableHalfPerObstacle);

            double score = laneJitter * (float)prng.NextDouble(); // tie-breaker

            if (enforceLaneVariety)
            {
                if (varietyHistory > 0 && _recentXs != null && _recentXs.Count > 0)
                {
                    int idx = 0;
                    foreach (var rx in _recentXs)
                    {
                        float w = Mathf.Pow(0.7f, idx);
                        score += blueNoiseWeight * w * Mathf.Abs(xCand - rx);
                        idx++;
                    }
                }

                if (newLaneBonus != 0f && _recentLanes != null && _recentLanes.Count > 0)
                {
                    bool seen = false;
                    foreach (var rl in _recentLanes) { if (rl == i) { seen = true; break; } }
                    if (!seen) score += newLaneBonus;
                }

                if (maxSameLaneStreak > 0 && i == _lastLaneUsed && _sameLaneRun >= maxSameLaneStreak)
                    score -= 9999.0;
            }

            if (inside) score += 0.05;

            if (score > best)
            {
                best = score;
                bestLane = i;
                bestX = xCand;
            }
        }

        if (bestLane < 0) return false;

        if (laneJitterX > 0f)
        {
            float j = Mathf.Lerp(-laneJitterX, laneJitterX, (float)prng.NextDouble());
            bestX = Mathf.Clamp(bestX + j, -usableHalfPerObstacle, +usableHalfPerObstacle);
        }

        laneIndex = bestLane;
        xFinal = bestX;
        return true;
    }

    // STARS 

    private void GenerateStarsAlongTrack(float[] laneXs)
    {
        float startZ = 0f;
        float endZ = trackLength;
        if (endZ <= startZ || starPrefab == null) return;

        var starsRoot = GetOrCreateChild(StarsNodeName);

        float gMin = Mathf.Max(0.05f, starsGapMinZ);
        float gMax = Mathf.Max(gMin, starsGapMaxZ);

        var prng = new System.Random(seed + 1337); // separate stream

        float z = startZ;
        while (z <= endZ)
        {
            bool place = (float)prng.NextDouble() <= Mathf.Clamp01(starsRowSpawnProbability);
            if (place)
            {
                int lane = prng.Next(0, Mathf.Max(1, lanes)); // one star per row, random lane
                TrySpawnStarAt(z, lane, laneXs, starsRoot);
            }

            float gap = Mathf.Lerp(gMin, gMax, (float)prng.NextDouble());
            z += gap;
        }
    }

    private void TrySpawnStarAt(float z, int lane, float[] laneXs, Transform parent)
    {
        if (!starPrefab) return;

        if (preventStarObstacleOverlap && _lanePlacedZs != null && lane < _lanePlacedZs.Length)
        {
            foreach (float oz in _lanePlacedZs[lane])
            {
                if (Mathf.Abs(oz - z) < starClearanceZ) return;
            }
        }

        // Spawn stars
#if UNITY_EDITOR
        GameObject s;
        if (!Application.isPlaying)
        {
            s = (GameObject)PrefabUtility.InstantiatePrefab(starPrefab, parent);
            Undo.RegisterCreatedObjectUndo(s, "Generate Star");
        }
        else { s = Instantiate(starPrefab, parent); }
#else
        var s = Instantiate(starPrefab, parent);
#endif

        // Grounding (use renderer bottom)
        float bottomY = 0f;
        var rends = s.GetComponentsInChildren<Renderer>(true);
        if (rends.Length > 0)
        {
            float minY = float.MaxValue;
            foreach (var r in rends) minY = Mathf.Min(minY, r.bounds.min.y);
            bottomY = s.transform.position.y - minY;
        }

        float x = laneXs[Mathf.Clamp(lane, 0, lanes - 1)];
        s.transform.localPosition = new Vector3(x, groundY + starsYOffset + bottomY, z);
        s.transform.localRotation = Quaternion.Euler(starsLocalEuler);
    }

    // UTIL / BUILD 

    private bool HasAnyObstaclePrefab()
    {
        foreach (var w in weightedObstaclePrefabs)
            if (w != null && w.prefab != null && w.weight > 0f) return true;
        return false;
    }

    private GameObject SelectObstaclePrefab(System.Random prng)
    {
        float total = 0f;
        for (int i = 0; i < weightedObstaclePrefabs.Count; i++)
        {
            var w = weightedObstaclePrefabs[i];
            if (w == null || w.prefab == null || w.weight <= 0f) continue;
            total += w.weight;
        }
        if (total <= 0f) return null;

        float pick = (float)prng.NextDouble() * total;
        float accum = 0f;
        for (int i = 0; i < weightedObstaclePrefabs.Count; i++)
        {
            var w = weightedObstaclePrefabs[i];
            if (w == null || w.prefab == null || w.weight <= 0f) continue;
            accum += w.weight;
            if (pick <= accum) return w.prefab;
        }
        return null;
    }

    public void ClearGenerated()
    {
        Transform ground = transform.Find(GroundNodeName);
        Transform walls = transform.Find(WallsNodeName);
        Transform obstacles = transform.Find(ObstaclesNodeName);
        Transform stars = transform.Find(StarsNodeName);
        Transform finish = transform.Find(FinishNodeName);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (ground) Undo.DestroyObjectImmediate(ground.gameObject);
            if (walls) Undo.DestroyObjectImmediate(walls.gameObject);
            if (obstacles) Undo.DestroyObjectImmediate(obstacles.gameObject);
            if (stars) Undo.DestroyObjectImmediate(stars.gameObject);
            if (finish) Undo.DestroyObjectImmediate(finish.gameObject);
        }
        else
        {
            if (ground) Destroy(ground.gameObject);
            if (walls) Destroy(walls.gameObject);
            if (obstacles) Destroy(obstacles.gameObject);
            if (stars) Destroy(stars.gameObject);
            if (finish) Destroy(finish.gameObject);
        }
#else
        if (ground) Destroy(ground.gameObject);
        if (walls)  Destroy(walls.gameObject);
        if (obstacles) Destroy(obstacles.gameObject);
        if (stars) Destroy(stars.gameObject);
        if (finish) Destroy(finish.gameObject);
#endif
    }

    private Transform GetOrCreateChild(string name)
    {
        var t = transform.Find(name);
        if (t) return t;

        var go = new GameObject(name);
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Create Auto Node");
#endif
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        return go.transform;
    }

    private void BuildGround()
    {
        var groundRoot = GetOrCreateChild(GroundNodeName);

        var mf = groundRoot.GetComponent<MeshFilter>();
        if (!mf) mf = groundRoot.gameObject.AddComponent<MeshFilter>();

        var mr = groundRoot.GetComponent<MeshRenderer>();
        if (!mr) mr = groundRoot.gameObject.AddComponent<MeshRenderer>();
        if (groundMaterial) mr.sharedMaterial = groundMaterial;

        Mesh mesh = new Mesh { name = "GroundAutoMesh" };

        float width = halfTrackWidth * 2f;
        float length = Mathf.Max(0f, trackLength);

        Vector3 v0 = new Vector3(-halfTrackWidth, 0f, 0f);
        Vector3 v1 = new Vector3(+halfTrackWidth, 0f, 0f);
        Vector3 v2 = new Vector3(-halfTrackWidth, 0f, length);
        Vector3 v3 = new Vector3(+halfTrackWidth, 0f, length);

        mesh.vertices = new[] { v0, v1, v2, v3 };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

        float uMax = width * Mathf.Max(0f, groundUVTilesPerUnit.x);
        float vMax = length * Mathf.Max(0f, groundUVTilesPerUnit.y);
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(uMax, 0f),
            new Vector2(0f, vMax),
            new Vector2(uMax, vMax)
        };

        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        groundRoot.localPosition = new Vector3(0f, groundY, 0f);
        groundRoot.localRotation = Quaternion.identity;
        groundRoot.localScale = Vector3.one;

        var col = groundRoot.GetComponent<BoxCollider>();
        if (addGroundCollider)
        {
            if (!col) col = groundRoot.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -groundColliderThickness * 0.5f, length * 0.5f);
            col.size = new Vector3(width, groundColliderThickness, length);
        }
        else if (col)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.DestroyObjectImmediate(col);
            else Destroy(col);
#else
            Destroy(col);
#endif
        }
    }

    private void BuildWalls()
    {
        var wallsRoot = GetOrCreateChild(WallsNodeName);

        var toDelete = new List<GameObject>();
        foreach (Transform c in wallsRoot) toDelete.Add(c.gameObject);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            foreach (var g in toDelete) Undo.DestroyObjectImmediate(g);
        }
        else
        {
            foreach (var g in toDelete) Destroy(g);
        }
#else
        foreach (var g in toDelete) Destroy(g);
#endif

        CreateWall("Wall_Left", -1, wallsRoot);
        CreateWall("Wall_Right", +1, wallsRoot);
    }

    private void CreateWall(string name, int sideSign, Transform parent)
    {
        float length = Mathf.Max(0f, trackLength);
        float wallXCenter = sideSign * halfTrackWidth;

        var go = new GameObject(name);
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(go, "Create Wall");
#endif
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0f);

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        if (wallMaterial) mr.sharedMaterial = wallMaterial;

        Mesh mesh = new Mesh { name = name + "_Mesh" };

        float x0 = wallXCenter - (wallThickness * 0.5f);
        float x1 = wallXCenter + (wallThickness * 0.5f);
        float y0 = groundY;
        float y1 = groundY + wallHeight;

        var verts = new List<Vector3>
        {
            // Front (inner face)
            new Vector3(x0, y0, 0f), new Vector3(x1, y0, 0f), new Vector3(x0, y1, 0f), new Vector3(x1, y1, 0f),
            // Back (outer face)
            new Vector3(x0, y0, length), new Vector3(x1, y0, length), new Vector3(x0, y1, length), new Vector3(x1, y1, length),
            // Left face (x=x0)
            new Vector3(x0, y0, 0f), new Vector3(x0, y0, length), new Vector3(x0, y1, 0f), new Vector3(x0, y1, length),
            // Right face (x=x1)
            new Vector3(x1, y0, 0f), new Vector3(x1, y0, length), new Vector3(x1, y1, 0f), new Vector3(x1, y1, length),
            // Top
            new Vector3(x0, y1, 0f), new Vector3(x1, y1, 0f), new Vector3(x0, y1, length), new Vector3(x1, y1, length),
            // Bottom
            new Vector3(x0, y0, 0f), new Vector3(x1, y0, 0f), new Vector3(x0, y0, length), new Vector3(x1, y0, length),
        };
        var tris = new List<int>();
        void AddQuad(int a, int b, int c, int d) { tris.AddRange(new[] { a, c, b, c, d, b }); }

        AddQuad(0, 1, 2, 3);
        AddQuad(5, 4, 7, 6);
        AddQuad(8, 9, 10, 11);
        AddQuad(13, 12, 15, 14);
        AddQuad(16, 17, 18, 19);
        AddQuad(22, 21, 23, 20);

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;

        if (addWallColliders)
        {
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(wallXCenter, groundY + wallHeight * 0.5f, length * 0.5f);
            col.size = new Vector3(wallThickness, wallHeight, length);
        }
    }

    private void PlaceFinishLine()
    {
        var finishRoot = GetOrCreateChild(FinishNodeName);
        var oldChildren = new List<GameObject>();
        foreach (Transform c in finishRoot) oldChildren.Add(c.gameObject);
#if UNITY_EDITOR
        if (!Application.isPlaying) { foreach (var g in oldChildren) Undo.DestroyObjectImmediate(g); }
        else { foreach (var g in oldChildren) Destroy(g); }
#else
        foreach (var g in oldChildren) Destroy(g);
#endif

#if UNITY_EDITOR
        GameObject fin;
        if (!Application.isPlaying)
        {
            fin = (GameObject)PrefabUtility.InstantiatePrefab(finishLinePrefab, finishRoot);
            Undo.RegisterCreatedObjectUndo(fin, "Place Finish Line");
        }
        else { fin = Instantiate(finishLinePrefab, finishRoot); }
#else
        var fin = Instantiate(finishLinePrefab, finishRoot);
#endif

        Vector3 endLocal = new Vector3(0f, groundY, trackLength + finishZOffset);
        Vector3 endWorld = transform.TransformPoint(endLocal);
        fin.transform.position = endWorld;

        fin.transform.localRotation = Quaternion.Euler(finishLocalEuler);

        if (autoScaleFinishToTrackWidth && finishPrefabApproxWidth > 0.001f)
        {
            float usableWidth = (halfTrackWidth * 2f) - Mathf.Max(0f, wallThickness);
            float scaleX = usableWidth / finishPrefabApproxWidth;
            var s = fin.transform.localScale;
            fin.transform.localScale = new Vector3(scaleX, s.y, s.z);
        }

        float groundWorldY = transform.TransformPoint(new Vector3(0f, groundY + finishYOffset, 0f)).y;

        float bottomWorldY = float.PositiveInfinity;
        var cols = fin.GetComponentsInChildren<Collider>(true);
        if (cols.Length > 0) { foreach (var c in cols) bottomWorldY = Mathf.Min(bottomWorldY, c.bounds.min.y); }
        else
        {
            var rends = fin.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) bottomWorldY = Mathf.Min(bottomWorldY, r.bounds.min.y);
        }

        if (!float.IsInfinity(bottomWorldY))
        {
            float deltaY = groundWorldY - bottomWorldY;
            var p = fin.transform.position;
            fin.transform.position = new Vector3(p.x, p.y + deltaY, p.z);
        }
    }

    private static float[] ComputeLaneXs(int laneCount, float halfWidthUsable)
    {
        laneCount = Mathf.Max(1, laneCount);
        float[] xs = new float[laneCount];
        if (laneCount == 1) { xs[0] = 0f; return xs; }

        float totalWidth = halfWidthUsable * 2f;
        float step = totalWidth / (laneCount - 1);
        for (int i = 0; i < laneCount; i++)
            xs[i] = -halfWidthUsable + i * step;

        return xs;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;

        // Track bounds
        Gizmos.color = new Color(1, 1, 1, 0.2f);
        Gizmos.DrawWireCube(new Vector3(0f, groundY, trackLength * 0.5f),
                            new Vector3(halfTrackWidth * 2f, 0.01f, trackLength));

        // Lane preview (lightweight, uses fallback)
        float usableHalf = Mathf.Max(
            0.05f,
            halfTrackWidth - (wallThickness * 0.5f) - obstaclePaddingFromWall - Mathf.Max(0f, obstacleApproxHalfWidth)
        );
        float[] laneXs = ComputeLaneXs(Mathf.Max(1, lanes), usableHalf);
        Gizmos.color = new Color(0, 1, 0, 0.35f);
        foreach (var x in laneXs)
        {
            Vector3 a = new Vector3(x, groundY + 0.02f, 0f);
            Vector3 b = new Vector3(x, groundY + 0.02f, trackLength);
            Gizmos.DrawLine(a, b);
        }

        // Walls preview lines
        if (buildWalls)
        {
            Gizmos.color = new Color(1, 0.8f, 0f, 0.35f);
            float xL = -halfTrackWidth + wallThickness * 0.5f;
            float xR = +halfTrackWidth - wallThickness * 0.5f;
            Gizmos.DrawLine(new Vector3(xL, groundY, 0), new Vector3(xL, groundY, trackLength));
            Gizmos.DrawLine(new Vector3(xR, groundY, 0), new Vector3(xR, groundY, trackLength));
        }
    }

    // Automatic width handling 

    private float MeasureHalfWidthLocalX(Transform t)
    {
        var rends = t.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
            return Mathf.Max(0.001f, obstacleApproxHalfWidth);

        bool first = true;
        Vector3 min = default, max = default;

        foreach (var r in rends)
        {
            var b = r.bounds; // world-space AABB
            Vector3[] c =
            {
                new Vector3(b.min.x, b.min.y, b.min.z),
                new Vector3(b.max.x, b.min.y, b.min.z),
                new Vector3(b.min.x, b.max.y, b.min.z),
                new Vector3(b.max.x, b.max.y, b.min.z),
                new Vector3(b.min.x, b.min.y, b.max.z),
                new Vector3(b.max.x, b.min.y, b.max.z),
                new Vector3(b.min.x, b.max.y, b.max.z),
                new Vector3(b.max.x, b.max.y, b.max.z),
            };

            foreach (var worldPt in c)
            {
                Vector3 local = transform.InverseTransformPoint(worldPt); // into generator-local
                if (first) { min = max = local; first = false; }
                else { min = Vector3.Min(min, local); max = Vector3.Max(max, local); }
            }
        }

        return Mathf.Max(0.001f, (max.x - min.x) * 0.5f);
    }

    private float EstimateMaxHalfWidthAcrossPrefabs()
    {
        float maxHalf = Mathf.Max(0.05f, obstacleApproxHalfWidth);

        foreach (var w in weightedObstaclePrefabs)
        {
            if (w == null || w.prefab == null || w.weight <= 0f) continue;

#if UNITY_EDITOR
            GameObject temp = null;
            if (!Application.isPlaying)
            {
                temp = (GameObject)PrefabUtility.InstantiatePrefab(w.prefab, transform);
                temp.transform.localPosition = Vector3.zero;
                temp.transform.localRotation = Quaternion.identity;
                float half = MeasureHalfWidthLocalX(temp.transform);
                maxHalf = Mathf.Max(maxHalf, half);
                Undo.DestroyObjectImmediate(temp);
            }
            else
            {
                temp = Instantiate(w.prefab, transform);
                temp.transform.localPosition = Vector3.zero;
                temp.transform.localRotation = Quaternion.identity;
                float half = MeasureHalfWidthLocalX(temp.transform);
                maxHalf = Mathf.Max(maxHalf, half);
                Destroy(temp);
            }
#else
            var temp = Instantiate(w.prefab, transform);
            temp.transform.localPosition = Vector3.zero;
            temp.transform.localRotation = Quaternion.identity;
            float half = MeasureHalfWidthLocalX(temp.transform);
            maxHalf = Mathf.Max(maxHalf, half);
            Destroy(temp);
#endif
        }

        return maxHalf;
    }
}
