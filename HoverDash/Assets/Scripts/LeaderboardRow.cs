// LeaderboardRow.cs
using UnityEngine;
using TMPro;

public class LeaderboardRow : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text scoreText;

    public void Bind(int rank, LeaderboardClient.ScoreRow row)
    {
        if (rankText) rankText.text = rank.ToString();

        // fallback to "Anonymous" if name is empty/whitespace
        if (nameText) nameText.text = string.IsNullOrWhiteSpace(row.name) ? "Anonymous" : row.name;

        // scores are rounded to whole numbers with commas
        if (scoreText) scoreText.text = Mathf.RoundToInt((float)row.score).ToString("N0");
    }
}
