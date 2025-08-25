// LeaderboardUI.cs

using UnityEngine;

public class LeaderboardUI : MonoBehaviour
{
    [SerializeField] private Transform content;          // parent for rows
    [SerializeField] private LeaderboardRow rowPrefab;   // row prefab
    [SerializeField] private int maxRows = 100;

    public void Show(LeaderboardClient.ScoreRow[] rows)
    {
        Clear();
        int n = Mathf.Min(maxRows, rows?.Length ?? 0);
        for (int i = 0; i < n; i++)
        {
            var row = Instantiate(rowPrefab, content);
            row.Bind(i + 1, rows[i]);
        }
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    private void Clear()
    {
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }
}
