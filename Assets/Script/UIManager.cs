using UnityEngine;
using TMPro; // Important pour utiliser TextMeshPro

public class UIManager : MonoBehaviour
{
    [Header("UI Text Elements")]
    [Tooltip("Le TextMeshProUGUI pour afficher le timer")]
    [SerializeField] private TextMeshProUGUI timerText;

    [Tooltip("Le TextMeshProUGUI pour afficher le score du joueur")]
    [SerializeField] private TextMeshProUGUI playerScoreText;

    [Tooltip("Le TextMeshProUGUI pour afficher le score de l'ennemi")]
    [SerializeField] private TextMeshProUGUI enemyScoreText;

    [Header("UI Panels")]
    [Tooltip("Le panel du menu pause")]
    [SerializeField] private GameObject pauseMenuPanel;
    [Tooltip("Le panel de fin de partie")]
    [SerializeField] private GameObject gameOverPanel;
    [Tooltip("Le texte affichant le résultat dans le panel Game Over")]
    [SerializeField] private TextMeshProUGUI gameOverWinnerText;

    void Awake()
    {
        // Vérification initiale des références (optionnel mais recommandé)
        if (timerText == null) Debug.LogError("UIManager: TimerText non assigné!", this);
        if (playerScoreText == null) Debug.LogError("UIManager: PlayerScoreText non assigné!", this);
        if (enemyScoreText == null) Debug.LogError("UIManager: EnemyScoreText non assigné!", this);

        if (pauseMenuPanel == null) Debug.LogError("UIManager: PauseMenuPanel non assigné!", this);
        if (gameOverPanel == null) Debug.LogError("UIManager: GameOverPanel non assigné!", this);
        if (gameOverWinnerText == null) Debug.LogError("UIManager: GameOverWinnerText non assigné!", this);


        // Cacher les panels au démarrage (sécurité)
        if(pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if(gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    /// <summary>
    /// Met à jour l'affichage du timer.
    /// </summary>
    /// <param name="totalSeconds">Le temps total restant en secondes.</param>
    public void UpdateTimer(float totalSeconds)
    {
        if (timerText == null) return; // Ne rien faire si la référence manque

        if (totalSeconds < 0) totalSeconds = 0;

        // Calcul des minutes et secondes
        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds % 60f);

        // Formatage en "MM:SS" (ex: 03:15)
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    /// <summary>
    /// Met à jour l'affichage des scores.
    /// </summary>
    /// <param name="playerScore">Score actuel du joueur.</param>
    /// <param name="enemyScore">Score actuel de l'ennemi.</param>
    public void UpdateScore(float playerScore, float enemyScore)
    {
        if (playerScoreText != null)
        {
            // Affiche le score comme un entier
            playerScoreText.text = Mathf.FloorToInt(playerScore).ToString();
        }
        if (enemyScoreText != null)
        {
            // Affiche le score comme un entier
            enemyScoreText.text = Mathf.FloorToInt(enemyScore).ToString();
        }
    }

    /// <summary>
    /// Affiche ou cache le menu pause.
    /// </summary>
    /// <param name="show">True pour afficher, False pour cacher.</param>
    public void ShowPauseMenu(bool show)
    {
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(show);
            Debug.Log($"UIManager: Pause Menu {(show ? "Shown" : "Hidden")}");
        }
    }

    /// <summary>
    /// Affiche l'écran de fin de partie avec le résultat.
    /// </summary>
    /// <param name="winner">Tag de l'équipe gagnante ("Player", "Enemy") ou "None".</param>
    /// <param name="finalPlayerScore">Score final du joueur.</param>
    /// <param name="finalEnemyScore">Score final de l'ennemi.</param>
    public void ShowGameOverScreen(string winner, int finalPlayerScore, int finalEnemyScore)
{
    // Log pour savoir quand la fonction est appelée et si la référence est bonne
    Debug.Log($"ShowGameOverScreen appelée. Winner: {winner}. Panel reference: {(gameOverPanel == null ? "NULL" : "OK")}");

    if (gameOverPanel != null)
    {
        // Détermine si on doit afficher ou cacher
        // On affiche SEULEMENT si le jeu est vraiment fini (winner n'est pas "None")
        bool shouldShow = (winner != "None");
        gameOverPanel.SetActive(shouldShow);
        Debug.Log($"UIManager: Setting GameOverPanel Active = {shouldShow}");

        // Met à jour le texte seulement si on affiche le panel
        if (shouldShow && gameOverWinnerText != null)
        {
            if (winner == "Draw") { // Peut-être utiliser "Draw" si temps écoulé sans vainqueur clair?
                 gameOverWinnerText.text = $"ÉGALITÉ !\n\nScore Joueur : {finalPlayerScore}\nScore Ennemi : {finalEnemyScore}";
            } else if (winner == "Player") {
                 gameOverWinnerText.text = $"VICTOIRE JOUEUR !";
             } else if (winner == "Enemy") {
                gameOverWinnerText.text = $"VICTOIRE ENNEMI !";
            } else { // Cas par défaut si winner est inattendu mais pas "None"
                gameOverWinnerText.text = $"FIN DE PARTIE";
            }
        }
    } else {
         Debug.LogError("UIManager ne peut pas afficher/cacher GameOverPanel car la référence est manquante!");
    }
}
}