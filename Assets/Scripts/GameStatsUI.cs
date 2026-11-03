using UnityEngine;
using TMPro;

public class GameStatsUI : MonoBehaviour
{
    [Header("Data Source")]
    public RollerGameController GameController;

    [Header("UI")]
    public TMP_Text StatsText;

    void Update()
    {
        if (GameController == null || StatsText == null)
            return;

        StatsText.text = $"Player Wins: {GameController.PlayerWins} / {GameController.TotalRounds}";
    }
}
