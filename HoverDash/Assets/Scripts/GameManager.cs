// GameManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    private const string leaderboardSceneName = "Level2"; // scene considered a leaderboard level
    private const string leaderboardLevelId = "level-2";  // server-side level id

    [SerializeField] private string zenLevelSceneName = "ZenLevel";

    [Header("UI Panels (GameObjects)")]
    [SerializeField] private GameObject namePromptUI;    // holds NamePromptUI
    [SerializeField] private GameObject leaderboardUI;   // holds LeaderboardUI 
    [SerializeField] private GameObject startPromptUI;   // "Press W to start"

    [Header("Name Input (optional direct reference)")]
    [Tooltip("TMP_InputField inside the name prompt. If left empty, we'll auto-find it under namePromptUI.")]
    [SerializeField] private TMP_InputField nameInput;

    [Header("Fallback names (optional)")]
    [SerializeField] private string namePromptObjectName = "NamePromptUI";
    [SerializeField] private string leaderboardObjectName = "LeaderboardUI";
    [SerializeField] private string startPromptObjectName = "StartPromptUI";

    // Cached components (bound on load or via fallbacks)
    private NamePromptUI namePrompt;
    private LeaderboardUI leaderboard;
    private LeaderboardClient lb;

    private bool isLeaderboardLevel;

    // Run-state gate flags
    private bool waitingForPlayerStart = false;
    private bool runInProgress = false;

    // Player controller (toggled at start/finish)
    private HoverVehicleController player;

    // Submission snapshot (captured at finish)
    private int pendingStars = 0;
    private float pendingDuration = 0f; // frozen duration
    private string pendingName = "";
    private bool canSubmit = false;
    private bool isSubmitting = false;

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void Awake()
    {
        RebindAll();
        var scene = SceneManager.GetActiveScene();
        isLeaderboardLevel = scene.name == leaderboardSceneName;

        // Toggle Zen rules per scene (wrapped in try in case GameRules is absent)
        SetZenRules(scene.name);

        if (isLeaderboardLevel && lb == null)
            Debug.LogWarning("Level 2 is a leaderboard level but no LeaderboardClient was found.");
    }

    private void Start() => PrepareRunGate();

    private void Update()
    {
        // Single key gate to begin the run
        if (waitingForPlayerStart && Input.GetKeyDown(KeyCode.W))
            BeginRunNow();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindAll();
        isLeaderboardLevel = scene.name == leaderboardSceneName;
        SetZenRules(scene.name);
        PrepareRunGate();
    }

    // ---------------- binding & helpers ----------------
    private void RebindAll()
    {
        // Includes inactive objects so UI/clients can be bound even when disabled in hierarchy
        lb = FindFirstObjectByType<LeaderboardClient>(FindObjectsInactive.Include);
        player = FindFirstObjectByType<HoverVehicleController>(FindObjectsInactive.Include);

        if (namePromptUI) namePrompt = namePromptUI.GetComponent<NamePromptUI>();
        if (leaderboardUI) leaderboard = leaderboardUI.GetComponent<LeaderboardUI>();

        if (!namePrompt) namePrompt = FindFirstObjectByType<NamePromptUI>(FindObjectsInactive.Include);
        if (!leaderboard) leaderboard = FindFirstObjectByType<LeaderboardUI>(FindObjectsInactive.Include);

        // Fallback by name if direct refs weren’t assigned
        if (!namePromptUI && !string.IsNullOrWhiteSpace(namePromptObjectName))
            namePromptUI = FindSceneObjectByName(namePromptObjectName);
        if (!leaderboardUI && !string.IsNullOrWhiteSpace(leaderboardObjectName))
            leaderboardUI = FindSceneObjectByName(leaderboardObjectName);
        if (!startPromptUI && !string.IsNullOrWhiteSpace(startPromptObjectName))
            startPromptUI = FindSceneObjectByName(startPromptObjectName);

        // Late-bind input field from the prompt panel if needed
        if (!nameInput && namePromptUI)
            nameInput = namePromptUI.GetComponentInChildren<TMP_InputField>(true);

        Debug.Log($"[GM] Bind: lb={(lb ? "ok" : "null")}, startPromptUI={(startPromptUI ? startPromptUI.name : "null")}, namePrompt={(namePrompt ? "ok" : "null")}, leaderboard={(leaderboard ? "ok" : "null")}, player={(player ? player.name : "null")}, nameInput={(nameInput ? nameInput.name : "null")}");
    }

    private static GameObject FindSceneObjectByName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;
        // Searches only active scene objects (ignores assets/prefabs via scene.IsValid)
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
        SafeSetActive(namePromptUI, false);
        SafeSetActive(leaderboardUI, false);
        UIManager.Instance.HideLevelComplete();
        UIManager.Instance.HideGameOver();
    }

    private void SetZenRules(string sceneName)
    {
        // Optional dependency: only applies if GameRules exists in the project
        try { GameRules.JumpsAreFreeThisScene = (sceneName == zenLevelSceneName); }
        catch { /* GameRules not present; ignore */ }
    }

    // ---------------- run flow ----------------
    private void PrepareRunGate()
    {
        HidePanelsAtRunStart();

        // Ensure HUD matches the current star state at gate
        UIManager.Instance.UpdateStarCount(StarManager.Instance ? StarManager.Instance.Stars : 0);

        // Allow re-using the same FinishLine without reloads
        var finish = FindFirstObjectByType<FinishLine>(FindObjectsInactive.Include);
        if (finish) finish.ResetGate();

        // Player is parked & disabled until the run starts
        if (player)
        {
            player.SetMovementEnabled(false);
            player.SetControlsEnabled(false);
            player.ZeroOutVelocity();
        }

        SafeSetActive(startPromptUI, true);

        waitingForPlayerStart = true;
        runInProgress = false;

        // Reset submission state for a fresh attempt
        canSubmit = false;
        isSubmitting = false;
        pendingName = "";
        pendingDuration = 0f;
        pendingStars = 0;
    }

    private void BeginRunNow()
    {
        waitingForPlayerStart = false;
        runInProgress = true;

        SafeSetActive(startPromptUI, false);

        UIManager.Instance.StartTimer();
        ScoreManager.Instance.StartRun();

        if (player)
        {
            player.SetMovementEnabled(true);
            player.SetControlsEnabled(true);
        }

        // Start a server session if this scene participates in the leaderboard
        if (isLeaderboardLevel && lb != null)
        {
            StartCoroutine(lb.StartLevel(leaderboardLevelId));
        }
    }

    public void StartLevel() => PrepareRunGate();

    public void FinishRun()
    {
        if (!runInProgress) return;
        runInProgress = false;

        // Lock the hovercraft immediately for consistent finish behavior
        if (player)
        {
            player.SetMovementEnabled(false);
            player.SetControlsEnabled(false);
            player.ZeroOutVelocity();
        }

        // Freeze time/score visuals and local snapshot
        ScoreManager.Instance.FinishLevel();
        UIManager.Instance.StopTimer();
        UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);

        // Capture data for a later manual submit click (don’t submit automatically)
        pendingDuration = ScoreManager.Instance.FinishedDuration;
        pendingStars = StarManager.Instance != null ? StarManager.Instance.Stars : 0;
        canSubmit = true;
        isSubmitting = false;

        // Ask for a name on leaderboard scenes; store result but wait for user to press Submit
        if (isLeaderboardLevel && lb != null)
        {
            if (namePrompt != null)
            {
                string prefill = "";
                namePrompt.Show(
                    prefill: prefill,
                    onConfirm: confirmedName =>
                    {
                        pendingName = string.IsNullOrWhiteSpace(confirmedName) ? "Anonymous" : confirmedName.Trim();
                        if (pendingName.Length > 20) pendingName = pendingName.Substring(0, 20);

                        // Mirror the confirmed name into the input field if present
                        if (!nameInput && namePromptUI)
                            nameInput = namePromptUI.GetComponentInChildren<TMP_InputField>(true);
                        if (nameInput) nameInput.text = pendingName;
                    },
                    onCancel: () => { }
                );
            }
            else
            {
                Debug.LogWarning("[GM] NamePromptUI not found; cannot ask for a name. You can still submit using the input field.");
            }
        }
    }

    public void OnClickSubmitScore()
    {
        // Guard rails to avoid invalid submits or double-submits
        if (!isLeaderboardLevel || lb == null)
        {
            Debug.LogWarning("[GM] Submit clicked but leaderboard client is not available.");
            return;
        }
        if (!canSubmit)
        {
            Debug.LogWarning("[GM] Submit clicked but there is no pending run to submit.");
            return;
        }
        if (isSubmitting)
        {
            Debug.Log("[GM] Already submitting, ignoring extra click.");
            return;
        }

        // Prefer the prompt-confirmed name, then the field, else Anonymous
        string name = !string.IsNullOrWhiteSpace(pendingName) ? pendingName :
                      (nameInput ? nameInput.text : "");
        name = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name.Trim();
        if (name.Length > 20) name = name.Substring(0, 20);

        isSubmitting = true;
        StartCoroutine(SubmitAfterName(name, pendingStars, pendingDuration));
    }

    private IEnumerator SubmitAfterName(string name, int stars, float frozenDuration)
    {
        // Make sure we have a valid session before finishing
        if (lb != null)
            yield return lb.EnsureSession(leaderboardLevelId);

        // Use the frozen score snapshot; fall back to FinalScore if needed
        var snapScore = ScoreManager.Instance ? ScoreManager.Instance.FrozenScore : 0f;
        if (snapScore <= 0f) snapScore = ScoreManager.Instance.FinalScore;

        yield return lb.FinishLevel(
            leaderboardLevelId,
            stars,
            name,
            frozenDuration,
            snapScore,
            serverScore =>
            {
                MaybeShowLeaderboard();
            },
            err =>
            {
                Debug.LogError("[GM] FinishLevel failed: " + err);
                MaybeShowLeaderboard();
            }
        );
    }

    private void MaybeShowLeaderboard()
    {
        // If we have a UI + client, fetch and fill; otherwise just reveal the panel
        if (leaderboard != null && lb != null)
        {
            StartCoroutine(lb.GetLeaderboard(leaderboardLevelId,
                (LeaderboardClient.ScoreRow[] rows) =>
                {
                    SafeSetActive(leaderboardUI, true);
                    leaderboard.Show(rows);
                },
                err => Debug.LogWarning("Leaderboard fetch failed: " + err)));
        }
        else
        {
            SafeSetActive(leaderboardUI, true);
        }
    }

    public void GoToMainMenu() => SceneManager.LoadScene("MainMenu");

    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
