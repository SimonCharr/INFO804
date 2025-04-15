using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic; // NOUVEAU : Pour utiliser List<>
using System.Linq;            // NOUVEAU : Pour FindObjectsOfType

public class GameManager : MonoBehaviour
{
    #region Singleton Implementation
    private static GameManager _instance;
    public static GameManager Instance
    {
        get
        {
            // Si l'instance n'existe pas encore...
            if (_instance == null)
            {
                // Essaye de trouver une instance existante dans la scène
                _instance = FindObjectOfType<GameManager>();

                // Si aucune instance n'est trouvée, crée un nouveau GameObject et ajoute le composant
                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject("GameManager_Instance");
                    _instance = singletonObject.AddComponent<GameManager>();
                    // Le message Log est souvent mis dans Awake, mais peut être ici si besoin
                    // Debug.Log("GameManager instance créée dynamiquement.");
                }
            }
            // Retourne l'instance (existante ou nouvellement créée)
            return _instance;
        }
    }
    #endregion

    #region Gestion de l'état du jeu

    public enum GameState { MainMenu, Playing, Paused, GameOver }
    private GameState currentGameState = GameState.Playing;

    [Header("Game Rules")] // MODIFIÉ : Section renommée
    [Tooltip("Score nécessaire pour gagner la partie")]
    public float scoreToWin = 100f; // MODIFIÉ : float et valeur
    [Tooltip("Points gagnés par seconde pour chaque point de capture contrôlé")]
    public float pointsPerSecondPerPoint = 1f; // NOUVEAU
    [Header("References")] // NOUVEAU Header
    [SerializeField] private UIManager uiManager;
    [Header("Game State")] // MODIFIÉ : Section renommée
    // MODIFIÉ : Utilisation de float pour une accumulation précise
    public float playerScore = 0f;
    public float enemyScore = 0f;
    public float gameTimeLimit = 300f;
    private float currentGameTime = 0f;
    private bool isTimerRunning = false;

    // NOUVEAU : Liste pour stocker les points de capture
    private List<CapturePoint> allCapturePoints = new List<CapturePoint>();

    #endregion

    private void Awake()
    {
        // --- Gestion du Singleton ---
        if (_instance != null && _instance != this) {
            Debug.LogWarning("Une autre instance de GameManager existe déjà. Destruction de celle-ci.");
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(this.gameObject);
        // ---------------------------

        // --- Réinitialisation IA (inchangé) ---
        EnemyTankController.ResetTargetCounts();
        AllyTankController.ResetTargetCounts();
        // ------------------------------------

        Debug.Log("GameManager Awake: Instance initialisée.");
    }

    void Start()
    {
        // NOUVEAU : Trouver tous les points de capture au démarrage
        allCapturePoints = FindObjectsOfType<CapturePoint>().ToList();
        if (allCapturePoints.Count == 0) {
             Debug.LogWarning("GameManager: Aucun CapturePoint trouvé dans la scène ! Le score ne fonctionnera pas.");
        } else {
             Debug.Log($"GameManager: Trouvé {allCapturePoints.Count} points de capture.");
        }

        InitializeGame();
    }

    void Update() {
        // NOUVEAU : Gestion de la touche Echap pour Pause/Reprise
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (currentGameState == GameState.Playing)
            {
                PauseGame();
            }
            else if (currentGameState == GameState.Paused)
            {
                ResumeGame();
            }
            // Ne fait rien si on est dans le menu principal ou Game Over
        }


        // --- Logique du jeu ---
        switch (currentGameState) {
            case GameState.Playing:
                UpdatePlayingState();
                break;
            case GameState.Paused:
                // Le jeu est en pause (Time.timeScale = 0), rien à faire ici en général
                break;
            case GameState.GameOver:
                // Le jeu est terminé (Time.timeScale = 0), rien à faire ici en général
                break;
            // ... (autres états si besoin) ...
        }
    }

    void InitializeGame() {
       Debug.Log("GameManager: Initialisation de la partie...");
         currentGameState = GameState.Playing;
         playerScore = 0f;
         enemyScore = 0f;
         currentGameTime = gameTimeLimit;
         isTimerRunning = true;
         Time.timeScale = 1f; // Assure que le temps s'écoule
         // Cacher les menus au cas où ils seraient restés actifs
         if(uiManager != null) {
             uiManager.ShowPauseMenu(false);
             uiManager.ShowGameOverScreen("None", 0, 0); // Appelle juste pour cacher le panel
             if(uiManager.gameObject.TryGetComponent<CanvasGroup>(out var cg)) cg.interactable = false; // Désactive interaction si caché
         }
    }

    void UpdatePlayingState() {
        // --- Gestion du Timer (inchangé) ---
        if (isTimerRunning) {
            currentGameTime -= Time.deltaTime;
            if (currentGameTime <= 0) {
                currentGameTime = 0;
                isTimerRunning = false;
                Debug.Log("Temps écoulé !");
                EndGame(); // Fin de partie quand temps écoulé
            }
            // Mettre à jour l'UI du timer ici
            if (uiManager != null) // Vérifie si la référence existe
            {
                uiManager.UpdateTimer(currentGameTime);
            }
            // Mettre à jour l'UI du score ici
            if (uiManager != null) // Vérifie si la référence existe
            {
            // On passe les scores float, UIManager les formatera en entier
            uiManager.UpdateScore(playerScore, enemyScore);
            }
            // UIManager.Instance?.UpdateTimer(currentGameTime);
        }
        // ---------------------------------

        // --- NOUVEAU : Calcul du score basé sur les points contrôlés ---
        float currentPlayerScoreGain = 0f;
        float currentEnemyScoreGain = 0f;

        foreach (CapturePoint point in allCapturePoints)
        {
            if (point == null) continue; // Sécurité si un point est détruit

            // Vérifie quel équipe contrôle le point
            // Utilise la propriété CurrentStatus définie dans CapturePoint.cs
            if (point.CurrentStatus == PointStatus.ControlledPlayer)
            {
                currentPlayerScoreGain += pointsPerSecondPerPoint;
            }
            else if (point.CurrentStatus == PointStatus.ControlledEnemy)
            {
                currentEnemyScoreGain += pointsPerSecondPerPoint;
            }
            // Note: Les points neutres, contestés ou en cours de capture ne donnent pas de points/sec
        }

        // Applique le gain de score pour cette frame
        playerScore += currentPlayerScoreGain * Time.deltaTime;
        enemyScore += currentEnemyScoreGain * Time.deltaTime;

        // Mettre à jour l'UI du score ici (si vous avez un UIManager)
        // UIManager.Instance?.UpdateScore(Mathf.FloorToInt(playerScore), Mathf.FloorToInt(enemyScore)); // Affiche en entier

        // --- Vérification de la condition de victoire ---
        if (playerScore >= scoreToWin) {
             Debug.Log($"VICTOIRE JOUEUR! Score final {Mathf.FloorToInt(playerScore)} >= {scoreToWin}"); // Log amélioré
            EndGame("Player");
        } else if (enemyScore >= scoreToWin) {
             Debug.Log($"VICTOIRE ENNEMI! Score final {Mathf.FloorToInt(enemyScore)} >= {scoreToWin}"); // Log amélioré
            EndGame("Enemy");
        }
        // ------------------------------------------------

    }

    // MODIFIÉ : Cette méthode n'ajoute plus de score direct, elle log juste l'événement.
    public void NotifyPointCaptured(CapturePoint point, string controllingTeamTag) {
        if (currentGameState != GameState.Playing) return;

        Debug.Log($"GameManager EVENT: Point {point.pointName} capturé par {controllingTeamTag}. Le score augmente maintenant passivement.");

        // L'ANCIENNE LOGIQUE DE SCORE EST SUPPRIMÉE ICI
        // if (controllingTeamTag == "Player") { playerScore++; ... }
        // else if (controllingTeamTag == "Enemy") { enemyScore++; ... }
    }

    public void PauseGame() {
        // Ne pas autoriser la pause si le jeu est déjà fini ou pas en cours
        if (currentGameState != GameState.Playing) return;

        currentGameState = GameState.Paused;
        Time.timeScale = 0f; // Arrête le temps du jeu !
        Debug.Log("Game Paused");
        if (uiManager != null) {
            uiManager.ShowPauseMenu(true); // Affiche le menu Pause
        }
    }

    public void ResumeGame() {
        // Ne peut reprendre que si le jeu est en pause
        if (currentGameState != GameState.Paused) return;

        currentGameState = GameState.Playing;
        Time.timeScale = 1f; // Redémarre le temps du jeu !
        Debug.Log("Game Resumed");
         if (uiManager != null) {
            uiManager.ShowPauseMenu(false); // Cache le menu Pause
        }
    }

    public void EndGame(string winner = "None") {
        // Ne termine le jeu que s'il est en cours
        if (currentGameState != GameState.Playing) return;

        currentGameState = GameState.GameOver;
        isTimerRunning = false;
        Time.timeScale = 0f; // Arrête le jeu

        Debug.Log($"Game Over! Winner: {winner}. Score final - Joueur: {playerScore:F2}, Ennemi: {enemyScore:F2}");

        // Afficher l'écran de fin de partie via UIManager
        if (uiManager != null) {
            uiManager.ShowGameOverScreen(winner, Mathf.FloorToInt(playerScore), Mathf.FloorToInt(enemyScore));
        }
    }

    /// <summary> Méthode appelée par le bouton "Reprendre". </summary>
    public void HandleResumeButton() {
        Debug.Log("Resume Button Clicked");
        ResumeGame();
    }

    /// <summary> Méthode appelée par les boutons "Rejouer". </summary>
    public void HandleRestartButton() {
        Debug.Log("Restart Button Clicked");
        RestartGame(); // La méthode RestartGame remet Time.timeScale à 1 et recharge la scène
    }

    /// <summary> Méthode appelée par les boutons "Quitter". </summary>
    public void HandleQuitButton() {
        Debug.Log("Quit Button Clicked");
        // Ne fonctionne que dans un build compilé, pas dans l'éditeur Unity
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false; // Arrête le jeu dans l'éditeur
        #else
            Application.Quit(); // Ferme l'application
        #endif
    }
    
    public void RestartGame() {
        Debug.Log("Restarting Game...");
        Time.timeScale = 1f; // Important avant de recharger

        // Réinitialise les compteurs IA (inchangé)
        EnemyTankController.ResetTargetCounts();
        AllyTankController.ResetTargetCounts();

        // Recharge la scène
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        // L'initialisation se fera via Awake/Start du nouveau GameManager chargé
    }

    // --- Fonctions du Singleton (inchangées) ---
    // ... (propriété Instance et Awake du Singleton) ...
}