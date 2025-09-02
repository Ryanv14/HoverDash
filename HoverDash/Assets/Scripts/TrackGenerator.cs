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
    [Tooltip("If NOT using random gaps, we evaluate every 'step' meters.")]
    public float step = 5f;
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

    [Header("Obstacle Spawning")]
    public List<GameObject> obstaclePrefabs = new List<GameObject>();
    [Range(0f, 1f)] public float spawnProbability = 0.35f;
    [Tooltip("Minimum forward gap in the SAME lane (still applies in random mode).")]
    public float minForwardGap = 8f;
    public float yOffset = 0f;

    [Header("X Bounds Safety")]
    [Tooltip("Extra margin from the inner face of each wall where obstacles will NOT spawn.")]
    public float obstaclePaddingFromWall = 0.25f;
    [Tooltip("Approximate half-width of your widest obstacle (used to keep it inside the walls).")]
    public float obstacleApproxHalfWidth = 0.25f;

    [Header("Spacing Randomization")]
    [Tooltip("If true, we pick the next Z by a random gap instead of fixed 'step'.")]
    public bool useRandomGaps = true;
    [Tooltip("Minimum random gap between potential spawn Z positions.")]
    public float gapMin = 6f;
    [Tooltip("Maximum random gap between potential spawn Z positions.")]
    public float gapMax = 14f;
    [Tooltip("If true, we spawn at each randomized Z; if false, we still roll spawnProbability.")]
    public bool alwaysSpawnAtGap = true;

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
    private const string FinishNodeName = "Finish_Auto";

    [Header("Rotation/Scale Variations (deterministic)")]
    public bool randomYaw = true;
    public float maxYawDegrees = 12f;
    public bool randomUniformScale = false;
    public Vector2 scaleRange = new Vector2(0.9f, 1.1f);

    [Header("Lifecycle")]
    public bool clearBeforeGenerate = true;
    public bool generateOnPlay = true;

    // Internal
    private float[] _laneLastZ;

    private const string GroundNodeName = "Ground_Auto";
    private const string ObstaclesNodeName = "Obstacles_Auto";
    private const string WallsNodeName = "Walls_Auto";

    void Awake()
    {
        if (Application.isPlaying && generateOnPlay)
        {
            Generate();
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Generate (Editor)")] private void GenerateContextMenu() => Generate();
    [ContextMenu("Clear Generated")] private void ClearContextMenu() => ClearGenerated();
#endif

    public void Generate()
    {
        if (clearBeforeGenerate) ClearGenerated();

        // Build visual/playable track first
        if (buildGround) BuildGround();
        if (buildWalls) BuildWalls();
        if (placeFinishLine && finishLinePrefab != null)
        {
            PlaceFinishLine();
        }

        // Abort early if no prefabs
        if (obstaclePrefabs == null || obstaclePrefabs.Count == 0)
        {
            Debug.LogWarning("[TrackGenerator] No obstacle prefabs assigned. Ground/Walls built (if enabled), but no obstacles were placed.");
            return;
        }

        var obstaclesRoot = GetOrCreateChild(ObstaclesNodeName);
        var prng = new System.Random(seed);

        // Compute usable half-width so spawns are BETWEEN the walls.
        float usableHalf = Mathf.Max(
            0.05f,
            halfTrackWidth - (wallThickness * 0.5f) - obstaclePaddingFromWall - Mathf.Max(0f, obstacleApproxHalfWidth)
        );

        float[] laneXs = ComputeLaneXs(lanes, usableHalf);

        // Per-lane last Z for minForwardGap
        _laneLastZ = new float[lanes];
        for (int i = 0; i < lanes; i++) _laneLastZ[i] = float.NegativeInfinity;

        if (useRandomGaps)
        {
            // Randomised Gaps
            float z = 0f;
            while (z <= trackLength)
            {
                bool spawnHere = alwaysSpawnAtGap || (NextFloat(prng) <= spawnProbability);
                if (spawnHere)
                {
                    TrySpawnOne(prng, z, laneXs, usableHalf, obstaclesRoot);
                }

                // advance by a deterministic random gap
                float gMin = Mathf.Max(0.01f, gapMin);
                float gMax = Mathf.Max(gMin, gapMax);
                float gap = Mathf.Lerp(gMin, gMax, NextFloat(prng));
                z += gap;
            }
        }
        else
        {
            // FIXED STEP MODE 
            float s = Mathf.Max(0.001f, step);
            for (float z = 0f; z <= trackLength; z += s)
            {
                if (NextFloat(prng) <= spawnProbability)
                {
                    TrySpawnOne(prng, z, laneXs, usableHalf, obstaclesRoot);
                }
            }
        }
    }

    private void TrySpawnOne(System.Random prng, float z, float[] laneXs, float usableHalf, Transform parent)
    {
        int lane = prng.Next(0, lanes);
        if (z - _laneLastZ[lane] < minForwardGap) return;

        var prefab = obstaclePrefabs[prng.Next(0, obstaclePrefabs.Count)];
        float x = laneXs[lane];
        x = Mathf.Clamp(x, -usableHalf, +usableHalf);

        Quaternion rot = Quaternion.identity;
        if (randomYaw)
        {
            float yaw = Mathf.Lerp(-maxYawDegrees, maxYawDegrees, NextFloat(prng));
            rot = Quaternion.Euler(0f, yaw, 0f);
        }

        // Instantiate first (but inactive) 
#if UNITY_EDITOR
        GameObject go;
        if (!Application.isPlaying)
        {
            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Generate Obstacle");
        }
        else
        {
            go = Object.Instantiate(prefab, parent);
        }
#else
    var go = Object.Instantiate(prefab, parent);
#endif
        go.transform.localRotation = rot;

        // Compute bottom offset 
        float bottomY = 0f;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            float minY = float.MaxValue;
            foreach (var r in renderers)
                minY = Mathf.Min(minY, r.bounds.min.y);
            // local offset from pivot to bottom
            bottomY = go.transform.position.y - minY;
        }

        // Position so bottom rests on ground
        go.transform.localPosition = new Vector3(x, groundY + yOffset + bottomY, z);

        if (randomUniformScale)
        {
            float s = Mathf.Lerp(scaleRange.x, scaleRange.y, NextFloat(prng));
            go.transform.localScale = new Vector3(s, s, s);
        }

        _laneLastZ[lane] = z;
    }

    public void ClearGenerated()
    {
        Transform ground = transform.Find(GroundNodeName);
        Transform walls = transform.Find(WallsNodeName);
        Transform obstacles = transform.Find(ObstaclesNodeName);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (ground) Undo.DestroyObjectImmediate(ground.gameObject);
            if (walls) Undo.DestroyObjectImmediate(walls.gameObject);
            if (obstacles) Undo.DestroyObjectImmediate(obstacles.gameObject);
        }
        else
        {
            if (ground) Destroy(ground.gameObject);
            if (walls) Destroy(walls.gameObject);
            if (obstacles) Destroy(obstacles.gameObject);
        }
#else
        if (ground) Destroy(ground.gameObject);
        if (walls)  Destroy(walls.gameObject);
        if (obstacles) Destroy(obstacles.gameObject);
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

        // Position
        groundRoot.localPosition = new Vector3(0f, groundY, 0f);
        groundRoot.localRotation = Quaternion.identity;
        groundRoot.localScale = Vector3.one;

        // Collider
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

        // Clear any old children under walls root
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

        // Create left & right wall quads with optional colliders
        CreateWall("Wall_Left", -1, wallsRoot);
        CreateWall("Wall_Right", +1, wallsRoot);
    }

    private void CreateWall(string name, int sideSign, Transform parent)
    {
        // sideSign: -1 for left, +1 for right
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

        // Build a vertical quad of thickness along X using 8 vertices (a thin box)
        float x0 = wallXCenter - (wallThickness * 0.5f);
        float x1 = wallXCenter + (wallThickness * 0.5f);

        // Bottom at groundY, top at groundY + wallHeight
        float y0 = groundY;
        float y1 = groundY + wallHeight;

        var verts = new List<Vector3>
        {
            // Front (inner face, looks toward center)
            new Vector3(x0, y0, 0f), new Vector3(x1, y0, 0f), new Vector3(x0, y1, 0f), new Vector3(x1, y1, 0f),
            // Back (outer face, points away)
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

        void AddQuad(int a, int b, int c, int d)
        {
            tris.AddRange(new[] { a, c, b, c, d, b });
        }

        // Using the order we appended:
        AddQuad(0, 1, 2, 3);     // inner
        AddQuad(5, 4, 7, 6);     // outer
        AddQuad(8, 9, 10, 11);   // left side
        AddQuad(13, 12, 15, 14); // right side
        AddQuad(16, 17, 18, 19); // top
        AddQuad(22, 21, 23, 20); // bottom

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
        // Create/clear parent node
        var finishRoot = GetOrCreateChild(FinishNodeName);
        // remove previous children
        var oldChildren = new List<GameObject>();
        foreach (Transform c in finishRoot) oldChildren.Add(c.gameObject);
#if UNITY_EDITOR
        if (!Application.isPlaying) { foreach (var g in oldChildren) Undo.DestroyObjectImmediate(g); }
        else { foreach (var g in oldChildren) Destroy(g); }
#else
    foreach (var g in oldChildren) Destroy(g);
#endif

        // Instantiate prefab under finish node
#if UNITY_EDITOR
        GameObject fin;
        if (!Application.isPlaying)
        {
            fin = (GameObject)PrefabUtility.InstantiatePrefab(finishLinePrefab, finishRoot);
            Undo.RegisterCreatedObjectUndo(fin, "Place Finish Line");
        }
        else
        {
            fin = Instantiate(finishLinePrefab, finishRoot);
        }
#else
    var fin = Instantiate(finishLinePrefab, finishRoot);
#endif

        Vector3 endLocal = new Vector3(0f, groundY, trackLength + finishZOffset);
        Vector3 endWorld = transform.TransformPoint(endLocal);          // local -> world
        fin.transform.position = endWorld;

        // Apply facing rotation in local space (keeps your intended yaw relative to the track)
        fin.transform.localRotation = Quaternion.Euler(finishLocalEuler);

        if (autoScaleFinishToTrackWidth && finishPrefabApproxWidth > 0.001f)
        {
            float usableWidth = (halfTrackWidth * 2f) - Mathf.Max(0f, wallThickness); // inner-to-inner
            float scaleX = usableWidth / finishPrefabApproxWidth;
            var s = fin.transform.localScale;
            fin.transform.localScale = new Vector3(scaleX, s.y, s.z);
        }

        float groundWorldY = transform.TransformPoint(new Vector3(0f, groundY + finishYOffset, 0f)).y;

        float bottomWorldY = float.PositiveInfinity;

        // Prefer collider bounds if present, else renderer bounds
        var cols = fin.GetComponentsInChildren<Collider>(true);
        if (cols.Length > 0)
        {
            foreach (var c in cols) bottomWorldY = Mathf.Min(bottomWorldY, c.bounds.min.y);
        }
        else
        {
            var rends = fin.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) bottomWorldY = Mathf.Min(bottomWorldY, r.bounds.min.y);
        }

        // Nudge the whole prefab straight up/down in world space so bottom sits on ground
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

    private static float NextFloat(System.Random r) => (float)r.NextDouble();

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;

        // Track bounds at ground level
        Gizmos.color = new Color(1, 1, 1, 0.2f);
        Gizmos.DrawWireCube(new Vector3(0f, groundY, trackLength * 0.5f),
                            new Vector3(halfTrackWidth * 2f, 0.01f, trackLength));

        // Lane lines (constrained to usable area)
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
}
