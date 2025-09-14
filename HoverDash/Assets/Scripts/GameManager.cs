// GameManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    private const string leaderboardSceneName = "Level2"; // change if needed
    private const string leaderboardLevelId = "level-2";

    [Header("UI Panels (GameObjects)")]
    [SerializeField] private GameObject namePromptUI;    // Panel with NamePromptUI
    [SerializeField] private GameObject leaderboardUI;   // Panel with LeaderboardUI 
    [SerializeField] private GameObject startPromptUI;   // "Press W to start"

    [Header("Name Input (optional direct reference)")]
    [Tooltip("TMP_InputField inside the name prompt. If left empty, we'll auto-find it under namePromptUI.")]
    [SerializeField] private TMP_InputField nameInput;

    [Header("Fallback names (optional)")]
    [SerializeField] private string namePromptObjectName = "NamePromptUI";
    [SerializeField] private string leaderboardObjectName = "LeaderboardUI";
    [SerializeField] private string startPromptObjectName = "StartPromptUI";

    // Cached components
    private NamePromptUI namePrompt;
    private LeaderboardUI leaderboard;
    private LeaderboardClient lb;

    private bool isLeaderboardLevel;

    // Run-state gating
    private bool waitingForPlayerStart = false; // true while "Press W" is up
    private bool runInProgress = false;         // true from actual start until finish

    // Player controller (to toggle movement)
    private HoverVehicleController player;

    // Pending submission data (set at finish; used on Submit)
    private int pendingStars = 0;
    private float pendingDuration = 0f;
    private string pendingName = "";  
    private bool canSubmit = false;
    private bool isSubmitting = false;

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void Awake()
    {
        RebindAll();
        isLeaderboardLevel = SceneManager.GetActiveScene().name == leaderboardSceneName;

        if (isLeaderboardLevel && lb == null)
            Debug.LogWarning("Level 2 is a leaderboard level but no LeaderboardClient was found.");
    }

    private void Start() => PrepareRunGate();

    private void Update()
    {
        if (waitingForPlayerStart && Input.GetKeyDown(KeyCode.W))
        {
            BeginRunNow();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RebindAll();
        isLeaderboardLevel = scene.name == leaderboardSceneName;
        PrepareRunGate();
    }

    // binding & helpers 
    private void RebindAll()
    {
        lb = FindFirstObjectByType<LeaderboardClient>(FindObjectsInactive.Include);
        player = FindFirstObjectByType<HoverVehicleController>(FindObjectsInactive.Include);

        if (namePromptUI) namePrompt = namePromptUI.GetComponent<NamePromptUI>();
        if (leaderboardUI) leaderboard = leaderboardUI.GetComponent<LeaderboardUI>();

        if (!namePrompt) namePrompt = FindFirstObjectByType<NamePromptUI>(FindObjectsInactive.Include);
        if (!leaderboard) leaderboard = FindFirstObjectByType<LeaderboardUI>(FindObjectsInactive.Include);

        if (!namePromptUI && !string.IsNullOrWhiteSpace(namePromptObjectName))
            namePromptUI = FindSceneObjectByName(namePromptObjectName);

        if (!leaderboardUI && !string.IsNullOrWhiteSpace(leaderboardObjectName))
            leaderboardUI = FindSceneObjectByName(leaderboardObjectName);

        if (!startPromptUI && !string.IsNullOrWhiteSpace(startPromptObjectName))
            startPromptUI = FindSceneObjectByName(startPromptObjectName);

        // Lazy hook the input if not set
        if (!nameInput && namePromptUI)
            nameInput = namePromptUI.GetComponentInChildren<TMP_InputField>(true);

        Debug.Log($"[GM] Bind: lb={(lb ? "ok" : "null")}, startPromptUI={(startPromptUI ? startPromptUI.name : "null")}, namePrompt={(namePrompt ? "ok" : "null")}, leaderboard={(leaderboard ? "ok" : "null")}, player={(player ? player.name : "null")}, nameInput={(nameInput ? nameInput.name : "null")}");
    }

    private static GameObject FindSceneObjectByName(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return null;
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

    // run flow 
    private void PrepareRunGate()
    {
        HidePanelsAtRunStart();

        UIManager.Instance.UpdateStarCount(StarManager.Instance ? StarManager.Instance.Stars : 0);

        var finish = FindFirstObjectByType<FinishLine>(FindObjectsInactive.Include);
        if (finish) finish.ResetGate();

        // Gate movement
        if (player)
        {
            player.SetMovementEnabled(false);
            // legacy aliases 
            player.SetControlsEnabled(false);
            player.ZeroOutVelocity();
        }

        SafeSetActive(startPromptUI, true);

        waitingForPlayerStart = true;
        runInProgress = false;

        // reset submission state
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

        if (player)
        {
            player.SetMovementEnabled(false);
            player.SetControlsEnabled(false); // legacy alias
            player.ZeroOutVelocity();
        }

        // Freeze score/time immediately (visual + local)
        ScoreManager.Instance.FinishLevel();
        UIManager.Instance.StopTimer(); 
        UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);

        // Cache data for later manual submission
        pendingDuration = ScoreManager.Instance.FinishedDuration;
        pendingStars = StarManager.Instance != null ? StarManager.Instance.Stars : 0;
        canSubmit = true;
        isSubmitting = false;

        if (isLeaderboardLevel && lb != null)
        {
            if (namePrompt != null)
            {
                // Show prompt; capture the confirmed name, but do not submit yet.
                string prefill = "";
                namePrompt.Show(
                    prefill: prefill,
                    onConfirm: confirmedName =>
                    {
                        pendingName = string.IsNullOrWhiteSpace(confirmedName) ? "Anonymous" : confirmedName.Trim();
                        if (pendingName.Length > 20) pendingName = pendingName.Substring(0, 20);

                        // Also sync the visible input if present
                        if (!nameInput && namePromptUI)
                            nameInput = namePromptUI.GetComponentInChildren<TMP_InputField>(true);
                        if (nameInput) nameInput.text = pendingName;
                    },
                    onCancel: () =>
                    {
                    }
                );
            }
            else
            {
                Debug.LogWarning("[GM] NamePromptUI component not found; cannot ask for a name. Keeping local score only.");
            }
        }
    }

    public void OnClickSubmitScore()
    {
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

        // Prefer the confirmed name from the prompt; otherwise read from input; otherwise Anonymous.
        string name = !string.IsNullOrWhiteSpace(pendingName) ? pendingName :
                      (nameInput ? nameInput.text : "");
        name = string.IsNullOrWhiteSpace(name) ? "Anonymous" : name.Trim();
        if (name.Length > 20) name = name.Substring(0, 20);

        isSubmitting = true;
        StartCoroutine(SubmitAfterName(name, pendingStars, pendingDuration));
    }

    private IEnumerator SubmitAfterName(string name, int stars, float frozenDuration)
    {
        // Ensure session
        if (lb != null)
            yield return lb.EnsureSession(leaderboardLevelId);

        // Use the duration-aware overload 
        yield return lb.FinishLevel(
            leaderboardLevelId,
            stars,
            name,
            frozenDuration,                    
            ScoreManager.Instance.FrozenScore, 
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
