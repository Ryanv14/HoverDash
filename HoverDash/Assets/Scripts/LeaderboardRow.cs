// LeaderboardRow.cs

using UnityEngine;
using TMPro;

public class LeaderboardRow : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text scoreText;

    // Bind a single row
    public void Bind(int rank, LeaderboardClient.ScoreRow row)
    {
        if (rankText) rankText.text = rank.ToString();
        if (nameText) nameText.text = string.IsNullOrWhiteSpace(row.name) ? "Anonymous" : row.name;

        // Format score nicely; tweak if you prefer integers
        if (scoreText) scoreText.text = Mathf.RoundToInt((float)row.score).ToString("N0");
    }
}

