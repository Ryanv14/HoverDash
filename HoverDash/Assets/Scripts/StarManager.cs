// StarManager.cs
using UnityEngine;

public class StarManager : MonoBehaviour
{
    public static StarManager Instance { get; private set; }

    [Header("Starting")]
    [SerializeField] private int startingStars = 100;

    public int Stars { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        Stars = Mathf.Max(0, startingStars);
        UpdateUI();
    }

    // -------- Public API --------

    public void AddStar() => AddStars(1);

    public void AddStars(int amount)
    {
        if (amount == 0) return;
        Stars = Mathf.Max(0, Stars + amount);
        UpdateUI();
    }

    /// <summary>
    /// Generic spend (for boosts, abilities, etc). Honors the amount always.
    /// </summary>
    public bool SpendStars(int amount)
    {
        if (amount <= 0) return true;
        if (Stars < amount) return false;

        Stars -= amount;
        UpdateUI();
        return true;
    }

    /// <summary>
    /// Use this specifically for JUMPS.
    /// In Zen levels (flagged by GameRules), jumps cost 0.
    /// </summary>
    public bool SpendForJump(int amount)
    {
        // If Zen rule is active, jumps are free.
        if (GameRules.JumpsAreFreeThisScene)
            return true;

        // Otherwise, spend like normal.
        return SpendStars(amount);
    }

    public bool CanAfford(int amount) => amount <= 0 || Stars >= amount;

    public void ResetStars(int newAmount)
    {
        Stars = Mathf.Max(0, newAmount);
        UpdateUI();
    }

    // -------- Internals --------

    private void UpdateUI()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.UpdateStarCount(Stars);
    }
}



