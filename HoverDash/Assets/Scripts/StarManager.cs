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
            Destroy(gameObject); // avoid duplicates
    }

    private void Start()
    {
        Stars = Mathf.Max(0, startingStars);
        UpdateUI();
    }

    // ---------------- public api ----------------
    public void AddStar() => AddStars(1);

    public void AddStars(int amount)
    {
        if (amount == 0) return;
        Stars = Mathf.Max(0, Stars + amount);
        UpdateUI();
    }

    public bool SpendStars(int amount)
    {
        if (amount <= 0) return true;
        if (Stars < amount) return false;

        Stars -= amount;
        UpdateUI();
        return true;
    }

    public bool SpendForJump(int amount)
    {
        // jumps are free in zen levels
        if (GameRules.JumpsAreFreeThisScene)
            return true;

        return SpendStars(amount);
    }

    public bool CanAfford(int amount) => amount <= 0 || Stars >= amount;

    public void ResetStars(int newAmount)
    {
        Stars = Mathf.Max(0, newAmount);
        UpdateUI();
    }

    // ---------------- internals ----------------
    private void UpdateUI()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.UpdateStarCount(Stars);
    }
}
