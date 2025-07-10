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
        Stars = 0;
        UIManager.Instance.UpdateStarCount(Stars);
    }

    // Public methods to add one or multiple stars
    public void AddStar() => AddStars(1);
    public void AddStars(int c)
    {
        Stars += c;
        UIManager.Instance.UpdateStarCount(Stars);
    }
}




