// UIManager.cs
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("In-Game HUD (assign if you like; otherwise we'll auto-find)")]
    [SerializeField] private TextMeshProUGUI starText;
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("End-Game Screens (assign or auto-find)")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject levelCompletePanel;
    [SerializeField] private TextMeshProUGUI finalScoreText;

    [Header("Fallback Names (used if fields above are empty)")]
    [SerializeField] private string starTextName = "StarText";
    [SerializeField] private string timerTextName = "TimerText";
    [SerializeField] private string gameOverPanelName = "GameOverPanel";
    [SerializeField] private string levelCompleteName = "LevelCompletePanel";
    [SerializeField] private string finalScoreTextName = "ScoreText"; 

    private float startTime;
    private bool timerRunning;

    // ---------- lifecycle ----------
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        RebindUI();
        // Default state at scene load
        SafeSetActive(gameOverPanel, false);
        SafeSetActive(levelCompletePanel, false);
        if (timerText) timerText.text = "0.00";
        if (starText && StarManager.Instance) starText.text = StarManager.Instance.Stars.ToString();
        timerRunning = false;
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Rebind every load in case new scene instances are created
        RebindUI();

        // Reset default UI state on load
        SafeSetActive(gameOverPanel, false);
        SafeSetActive(levelCompletePanel, false);
        if (timerText) timerText.text = "0.00";
        if (starText && StarManager.Instance) starText.text = StarManager.Instance.Stars.ToString();
        timerRunning = false;
    }

    private void Update()
    {
        if (!timerRunning || !timerText) return;

        float elapsed = Time.time - startTime;
        timerText.text = elapsed.ToString("F2");
    }

    // ---------- public API ----------
    public void StartTimer()
    {
        startTime = Time.time;
        timerRunning = true;
    }

    public void StopTimer() => timerRunning = false;

    public void UpdateStarCount(int stars)
    {
        if (starText) starText.text = stars.ToString();
    }

    public void ShowGameOver()
    {
        StopTimer();
        SafeSetActive(gameOverPanel, true);
    }

    public void HideGameOver()
    {
        SafeSetActive(gameOverPanel, false);
    }

    public void ShowLevelComplete(float finalScore)
    {
        StopTimer();
        SafeSetActive(levelCompletePanel, true);
        if (!finalScoreText)
        {
            // try late-bind once more (in case the panel was created/enabled now)
            finalScoreText = finalScoreText ?? FindTextByName(finalScoreTextName);
        }
        if (finalScoreText) finalScoreText.text = $"Final Score: {finalScore:F1}";
    }

    public void HideLevelComplete()
    {
        SafeSetActive(levelCompletePanel, false);
    }

    public void ResetUI() =>
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);

    // ---------- binding helpers ----------
    private void RebindUI()
    {
        // If fields are already assigned, keep them. Otherwise find by name (includes inactive).

        if (!starText) starText = FindTextByName(starTextName);
        if (!timerText) timerText = FindTextByName(timerTextName);
        if (!finalScoreText) finalScoreText = FindTextByName(finalScoreTextName);

        if (!gameOverPanel) gameOverPanel = FindGOByName(gameOverPanelName);
        if (!levelCompletePanel) levelCompletePanel = FindGOByName(levelCompleteName);

        Debug.Log($"[UI] Bind -> starText={(starText ? starText.name : "null")}, timerText={(timerText ? timerText.name : "null")}, " +
                  $"gameOver={(gameOverPanel ? gameOverPanel.name : "null")}, levelComplete={(levelCompletePanel ? levelCompletePanel.name : "null")}, " +
                  $"finalScoreText={(finalScoreText ? finalScoreText.name : "null")}");
    }

    private static void SafeSetActive(GameObject go, bool active)
    {
        if (go && go.activeSelf != active) go.SetActive(active);
    }

    private static GameObject FindGOByName(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) return null;
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (var t in all)
        {
            if (t.hideFlags != HideFlags.None) continue;
            if (!t.gameObject.scene.IsValid()) continue; // ignore prefabs/assets
            if (t.name == targetName) return t.gameObject;
        }
        return null;
    }

    private static TextMeshProUGUI FindTextByName(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) return null;
        var all = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
        foreach (var txt in all)
        {
            if (txt.hideFlags != HideFlags.None) continue;
            if (!txt.gameObject.scene.IsValid()) continue;
            if (txt.name == targetName || txt.gameObject.name == targetName) return txt;
        }
        return null;
    }
}
