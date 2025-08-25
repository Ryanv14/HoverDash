// GameManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    private const string leaderboardSceneName = "Level2"; // change if needed
    private const string leaderboardLevelId = "level-2";

    [Header("UI Panels (GameObjects)")]
    [SerializeField] private GameObject namePromptUI;    // Panel with NamePromptUI
    [SerializeField] private GameObject leaderboardUI;   // Panel with LeaderboardUI 

    [Header("Fallback names (optional)")]
    [SerializeField] private string namePromptObjectName = "NamePromptUI";
    [SerializeField] private string leaderboardObjectName = "LeaderboardUI";

    // Cached components (resolved from the panels above, or auto-found)
    private NamePromptUI namePrompt;
    private LeaderboardUI leaderboard;

    private bool isLeaderboardLevel;
    private LeaderboardClient lb;

    // ---------- lifecycle ----------
    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void Awake()
    {
        RebindAll();
        isLeaderboardLevel = SceneManager.GetActiveScene().name == leaderboardSceneName;

        if (isLeaderboardLevel && lb == null)
            Debug.LogWarning("Level 2 is a leaderboard level but no LeaderboardClient was found.");
    }

    private void Start() => StartRun();

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindAll();
        isLeaderboardLevel = scene.name == leaderboardSceneName;
        StartRun();
    }

    // ---------- binding & helpers ----------
    private void RebindAll()
    {
        // API client
        lb = FindFirstObjectByType<LeaderboardClient>(FindObjectsInactive.Include);

        // Try to get components from assigned GameObjects
        if (namePromptUI) namePrompt = namePromptUI.GetComponent<NamePromptUI>();
        if (leaderboardUI) leaderboard = leaderboardUI.GetComponent<LeaderboardUI>();

        // If not assigned/found, try to find by component (includes inactive)
        if (!namePrompt)
        {
            namePrompt = FindFirstObjectByType<NamePromptUI>(FindObjectsInactive.Include);
        }

        if (!leaderboard)
        {
            leaderboard = FindFirstObjectByType<LeaderboardUI>(FindObjectsInactive.Include);
        }

        // If still missing, try to find panels by name as a last resort
        if (!namePromptUI && !string.IsNullOrWhiteSpace(namePromptObjectName))
            namePromptUI = FindSceneObjectByName(namePromptObjectName);

        if (!leaderboardUI && !string.IsNullOrWhiteSpace(leaderboardObjectName))
            leaderboardUI = FindSceneObjectByName(leaderboardObjectName);

        // Log what we ended up with
        Debug.Log($"[GM] Bind: lb={(lb ? "ok" : "null")}, namePrompt={(namePrompt ? "ok" : "null")}, namePromptUI={(namePromptUI ? namePromptUI.name : "null")}, leaderboard={(leaderboard ? "ok" : "null")}, leaderboardUI={(leaderboardUI ? leaderboardUI.name : "null")}");
    }

    private static GameObject FindSceneObjectByName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;

        // Find inactive too, but only in the active scene (not prefabs/assets)
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in all)
        {
            if (t.hideFlags != HideFlags.None) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            if (t.name == targetName) return t.gameObject;
        }
        return null;
    }

    private static void SafeSetActive(GameObject go, bool active)
    {
        if (go && go.activeSelf != active) go.SetActive(active);
    }

    private void HidePanelsAtRunStart()
    {
        // Hide prompt + leaderboard panels if present
        SafeSetActive(namePromptUI, false);
        SafeSetActive(leaderboardUI, false);

        // Hide UIManager end-game panels
        UIManager.Instance.HideLevelComplete();
        UIManager.Instance.HideGameOver();
    }

    // ---------- run flow ----------
    public void StartLevel() => StartRun(); // call this for no-reload restarts

    private void StartRun()
    {
        HidePanelsAtRunStart();

        // Reset HUD + score
        UIManager.Instance.UpdateStarCount(StarManager.Instance ? StarManager.Instance.Stars : 0);
        UIManager.Instance.StartTimer();
        ScoreManager.Instance.StartRun();

        // Re-enable finish trigger if you don't reload the scene
        var finish = FindFirstObjectByType<FinishLine>(FindObjectsInactive.Include);
        if (finish) finish.ResetGate();

        // Start server session for leaderboard level
        if (isLeaderboardLevel && lb != null)
            StartCoroutine(lb.StartLevel(leaderboardLevelId));
    }

    public void FinishRun()
    {
        // Show local result immediately
        ScoreManager.Instance.FinishLevel();
        UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);

        // Prompt & submit in background if it's the leaderboard level
        if (isLeaderboardLevel && lb != null)
        {
            if (namePrompt != null)
            {
                namePrompt.Show(
                    prefill: "",
                    onConfirm: name => StartCoroutine(SubmitAfterName(name)),
                    onCancel: () => { /* local score already shown */ }
                );
            }
            else
            {
                Debug.LogWarning("[GM] NamePromptUI component not found; cannot ask for a name. Keeping local score only.");
            }
        }
    }

    private System.Collections.IEnumerator SubmitAfterName(string name)
    {
        // Ensure a session (handles very fast finishes)
        var ensureMethod = lb?.GetType().GetMethod("EnsureSession");
        if (ensureMethod != null)
            yield return (System.Collections.IEnumerator)ensureMethod.Invoke(lb, new object[] { leaderboardLevelId });

        // Submit; update to server-authoritative score; show leaderboard if available
        yield return lb.FinishLevel(leaderboardLevelId, StarManager.Instance.Stars, name, serverScore =>
        {
            ScoreManager.Instance.ApplyServerScore(serverScore);
            UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);

            // Show leaderboard panel even if there is no LeaderboardUI script (best-effort)
            if (leaderboard != null)
            {
                StartCoroutine(lb.GetLeaderboard(leaderboardLevelId,
                    rows =>
                    {
                        SafeSetActive(leaderboardUI, true);
                        leaderboard.Show(rows);
                    },
                    err => Debug.LogWarning("Leaderboard fetch failed: " + err)));
            }
            else
            {
                // No component—just show the panel if assigned/found
                SafeSetActive(leaderboardUI, true);
            }
        });
    }

    public void GoToMainMenu() =>
       SceneManager.LoadScene("MainMenu");

    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
