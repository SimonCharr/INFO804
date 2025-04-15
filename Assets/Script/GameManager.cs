using UnityEngine;
using UnityEngine.SceneManagement; // Potentiellement utile pour recharger la scène

public class GameManager : MonoBehaviour
{
    #region Singleton Implementation

    // La variable statique privée qui stockera l'unique instance de GameManager
    private static GameManager _instance;

    // La propriété statique publique pour accéder à l'instance depuis n'importe où
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
                    Debug.Log("GameManager instance créée dynamiquement.");
                }
            }
            // Retourne l'instance (existante ou nouvellement créée)
            return _instance;
        }
    }

    // Awake est appelé très tôt au démarrage du jeu
    private void Awake()
    {
        // Vérifie s'il existe déjà une instance
        if (_instance != null && _instance != this)
        {
            // Si une autre instance existe déjà, on détruit ce GameObject pour garantir l'unicité
            Debug.LogWarning("Une autre instance de GameManager existe déjà. Destruction de celle-ci.");
            Destroy(this.gameObject);
            return; // Important pour arrêter l'exécution de Awake ici
        }

        // Si on arrive ici, soit _instance était null, soit _instance est déjà 'this'
        // On définit l'instance statique
        _instance = this;

        // Optionnel mais courant: Empêche cet objet d'être détruit lors du chargement d'une nouvelle scène
        // Utile si votre GameManager doit gérer des choses entre les scènes (ex: score persistant)
        DontDestroyOnLoad(this.gameObject);

        // --- APPEL INITIAL IMPORTANT ---
        // Réinitialise les compteurs de cibles pour les IA au démarrage du jeu
        // (On suppose qu'il n'y a qu'un seul GameManager qui fait ça)
        // Note: Si vous séparez Ally et Enemy, appelez les deux Reset ici.
        EnemyTankController.ResetTargetCounts();
        AllyTankController.ResetTargetCounts(); // Si vous avez aussi la version Ally
         // -----------------------------

         Debug.Log("GameManager Awake: Instance initialisée.");
    }

    #endregion

    #region Gestion de l'état du jeu (Exemples / À Compléter)

    // Énumération pour les états possibles du jeu
    public enum GameState { MainMenu, Playing, Paused, GameOver }
    private GameState currentGameState = GameState.Playing; // État au démarrage (à ajuster)

    // Exemple: Variables pour le score et le temps
    [Header("Game Variables")]
    public int playerScore = 0;
    public int enemyScore = 0;
    public float gameTimeLimit = 300f; // Ex: 5 minutes
    private float currentGameTime = 0f;
    private bool isTimerRunning = false;

    // Exemple de méthodes à appeler pour gérer le jeu

    void Start() {
        // Mettre ici l'initialisation spécifique au début de la partie (si Awake gère le Singleton)
        // StartGame(); // Peut-être appeler StartGame depuis un bouton de menu UI?
        InitializeGame(); // Pour l'instant, on initialise directemennt
    }

     void Update() {
        // Logique à exécuter à chaque frame, dépend de l'état du jeu
        switch (currentGameState) {
            case GameState.Playing:
                UpdatePlayingState();
                break;
            case GameState.Paused:
                // Logique du jeu en pause
                break;
            case GameState.GameOver:
                // Logique de fin de partie
                break;
            case GameState.MainMenu:
                 // Logique du menu principal
                 break;
        }
    }

    void InitializeGame() {
         Debug.Log("GameManager: Initialisation de la partie...");
         currentGameState = GameState.Playing;
         playerScore = 0;
         enemyScore = 0;
         currentGameTime = gameTimeLimit;
         isTimerRunning = true;
         // Potentiel: (Ré)activer les contrôles joueurs/IA, (re)placer les tanks, etc.
    }

    void UpdatePlayingState() {
        // Gérer le timer
        if (isTimerRunning) {
            currentGameTime -= Time.deltaTime;
            if (currentGameTime <= 0) {
                currentGameTime = 0;
                isTimerRunning = false;
                Debug.Log("Temps écoulé !");
                EndGame(); // Fin de partie quand temps écoulé
            }
            // Mettre à jour l'UI du timer ici
            // UIManager.Instance.UpdateTimer(currentGameTime);
        }

        // Vérifier les conditions de victoire/défaite basées sur le score ou les points capturés
        // Exemple simple: Première équipe à 3 points de score gagne
        if (playerScore >= 3) {
            EndGame("Player");
        } else if (enemyScore >= 3) {
            EndGame("Enemy");
        }
    }

    // Méthode appelée par CapturePoint quand un point change de main
    public void NotifyPointCaptured(CapturePoint point, string controllingTeamTag) {
        if (currentGameState != GameState.Playing) return; // Ignore si pas en jeu

        Debug.Log($"GameManager: Point {point.pointName} capturé par {controllingTeamTag}");

        // Logique de Score (Exemple: +1 point par capture)
        if (controllingTeamTag == "Player") { // Assurez-vous que le tag du joueur est bien "Player"
            playerScore++;
            // UIManager.Instance.UpdateScore(playerScore, enemyScore);
             Debug.Log($"Score: Player {playerScore} - Enemy {enemyScore}");
        } else if (controllingTeamTag == "Enemy") {
             enemyScore++;
             // UIManager.Instance.UpdateScore(playerScore, enemyScore);
              Debug.Log($"Score: Player {playerScore} - Enemy {enemyScore}");
        }
        // Vérifier conditions de victoire après chaque capture ?
    }

    public void PauseGame() {
        if (currentGameState == GameState.Playing) {
            currentGameState = GameState.Paused;
            Time.timeScale = 0f; // Arrête le temps du jeu
            Debug.Log("Game Paused");
            // Afficher le menu Pause
            // UIManager.Instance.ShowPauseMenu(true);
        }
    }

    public void ResumeGame() {
         if (currentGameState == GameState.Paused) {
            currentGameState = GameState.Playing;
            Time.timeScale = 1f; // Redémarre le temps du jeu
             Debug.Log("Game Resumed");
             // Cacher le menu Pause
             // UIManager.Instance.ShowPauseMenu(false);
        }
    }

    public void EndGame(string winner = "None") {
        if (currentGameState == GameState.Playing) {
            currentGameState = GameState.GameOver;
            isTimerRunning = false;
            Time.timeScale = 0f; // Optionnel: mettre en pause à la fin
             Debug.Log($"Game Over! Winner: {winner}");
             // Afficher l'écran de fin de partie
             // UIManager.Instance.ShowGameOverScreen(winner, playerScore, enemyScore);
             // Désactiver contrôles joueurs/IA ?
        }
    }

    public void RestartGame() {
         Debug.Log("Restarting Game...");
         Time.timeScale = 1f; // Important si on a mis en pause à la fin
         // Réinitialise les compteurs de cibles avant de recharger
         EnemyTankController.ResetTargetCounts();
         AllyTankController.ResetTargetCounts();
         // Recharge la scène actuelle (ou une scène spécifique)
         SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
         // L'initialisation se fera via Awake/Start du nouveau GameManager chargé
    }


    // Ajoutez ici d'autres méthodes pour gérer les équipes, le score basé sur le temps de capture, etc.

    #endregion
}