// StarManager.cs
using UnityEngine;

public class StarManager : MonoBehaviour
{
    public static StarManager Instance { get; private set; }
    public int Stars { get; private set; }

    private void Awake()
    {
        // Ensure only one StarManager exists
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        // Initialize star count UI
        Stars = 100;
        UIManager.Instance.UpdateStarCount(Stars);
    }

    // Public methods to add one or multiple stars
    public void AddStar() => AddStars(1);

    public void AddStars(int c)
    {
        Stars += c;
        if (Stars < 0) Stars = 0;
        UIManager.Instance.UpdateStarCount(Stars);
    }

    // Spend stars; returns true if the cost was paid
    public bool SpendStars(int amount)
    {
        if (amount <= 0) return true;
        if (Stars < amount) return false;

        Stars -= amount;
        UIManager.Instance.UpdateStarCount(Stars);
        return true;
    }
}




