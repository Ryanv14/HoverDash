// GameManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;

[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    private const string leaderboardSceneName = "Level2"; // change if needed
    private const string leaderboardLevelId = "level-2";

    [Header("UI Panels (GameObjects)")]
    [SerializeField] private GameObject namePromptUI;    // Panel with NamePromptUI
    [SerializeField] private GameObject leaderboardUI;   // Panel with LeaderboardUI 
    [SerializeField] private GameObject startPromptUI;   // "Press W to start"

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

    // lifecycle 
    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void Awake()
    {
        RebindAll();
        isLeaderboardLevel = SceneManager.GetActiveScene().name == leaderboardSceneName;

        if (isLeaderboardLevel && lb == null)
            Debug.LogWarning("Level 2 is a leaderboard level but no LeaderboardClient was found.");
    }

    private void Start() => PrepareRunGate(); // show "Press W" and wait

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

        Debug.Log($"[GM] Bind: lb={(lb ? "ok" : "null")}, startPromptUI={(startPromptUI ? startPromptUI.name : "null")}, namePrompt={(namePrompt ? "ok" : "null")}, leaderboard={(leaderboard ? "ok" : "null")}, player={(player ? player.name : "null")}");
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
            player.SetControlsEnabled(true); // legacy alias
        }

        if (isLeaderboardLevel && lb != null)
            StartCoroutine(lb.StartLevel(leaderboardLevelId));
    }

    public void StartLevel() => PrepareRunGate(); // public restart without reload

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

        // Freeze score/time immediately
        ScoreManager.Instance.FinishLevel();
        UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);

        if (isLeaderboardLevel && lb != null)
        {
            if (namePrompt != null)
            {
                string prefill = "";
                float frozenDuration = ScoreManager.Instance.FinishedDuration;
                int stars = StarManager.Instance != null ? StarManager.Instance.Stars : 0;

                namePrompt.Show(
                    prefill: prefill,
                    onConfirm: name => StartCoroutine(SubmitAfterName(name, stars, frozenDuration)),
                    onCancel: () => { /* local score already shown */ }
                );
            }
            else
            {
                Debug.LogWarning("[GM] NamePromptUI component not found; cannot ask for a name. Keeping local score only.");
            }
        }
    }

    private IEnumerator SubmitAfterName(string name, int stars, float frozenDuration)
    {
        var ensureMethod = lb?.GetType().GetMethod("EnsureSession");
        if (ensureMethod != null)
            yield return (IEnumerator)ensureMethod.Invoke(lb, new object[] { leaderboardLevelId });

        // Prefer duration-aware overload if present
        var durationOverload = lb.GetType().GetMethod(
            "FinishLevel",
            new Type[] { typeof(string), typeof(int), typeof(string), typeof(float), typeof(Action<double>) }
        );

        if (durationOverload != null)
        {
            yield return (IEnumerator)durationOverload.Invoke(lb, new object[]
            {
                leaderboardLevelId, stars, name, frozenDuration,
                (Action<double>)((serverScore) =>
                {
                    ScoreManager.Instance.ApplyServerScore(serverScore);
                    UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);
                    MaybeShowLeaderboard();
                })
            });
        }
        else
        {
            Debug.LogWarning("[GM] Duration-aware FinishLevel overload not found; falling back (score may drift if player waits).");
            yield return lb.FinishLevel(leaderboardLevelId, stars, name, serverScore =>
            {
                ScoreManager.Instance.ApplyServerScore(serverScore);
                UIManager.Instance.ShowLevelComplete(ScoreManager.Instance.FinalScore);
                MaybeShowLeaderboard();
            });
        }
    }

    private void MaybeShowLeaderboard()
    {
        if (leaderboard != null)
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
