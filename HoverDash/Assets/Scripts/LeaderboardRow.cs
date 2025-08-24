// LeaderboardRow.cs

using UnityEngine;
using UnityEngine.UI;

public class LeaderboardRow : MonoBehaviour
{
    public Text rankText;
    public Text nameText;
    public Text scoreText;

    public void Bind(int rank, LeaderboardClient.ScoreRow r)
    {
        rankText.text = rank.ToString();
        nameText.text = string.IsNullOrWhiteSpace(r.name) ? "Anonymous" : r.name;
        scoreText.text = Mathf.RoundToInt((float)r.score).ToString();
    }
}
