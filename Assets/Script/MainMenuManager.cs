using UnityEngine;
using UnityEngine.SceneManagement; // Important pour gérer les scènes

public class MainMenuManager : MonoBehaviour
{
    // IMPORTANT : Remplacez "NomDeVotreSceneDeJeu" par le nom exact
    // de votre fichier de scène de jeu principal (ex: "MainLevel", "GameScene").
    public string gameSceneName = "SampleScene";

    // Cette méthode sera appelée par le bouton "Jouer"
    public void StartGame()
    {
        Debug.Log($"Lancement de la scène : {gameSceneName}");
        // Charge la scène de jeu spécifiée par son nom
        SceneManager.LoadScene(gameSceneName);

        // Réinitialise le temps au cas où il aurait été mis en pause
        Time.timeScale = 1f;

        // Réinitialise les compteurs de cibles des IA (si nécessaire et si le GameManager n'est pas DontDestroyOnLoad)
        // Si votre GameManager utilise DontDestroyOnLoad, il vaut mieux appeler ResetTargetCounts
        // depuis une méthode StartGame() DANS le GameManager, après le chargement de scène.
        // AllyTankController.ResetTargetCounts();
        // EnemyTankController.ResetTargetCounts();
    }

    // Cette méthode sera appelée par le bouton "Quitter"
    public void QuitGame()
    {
        Debug.Log("Demande de fermeture du jeu...");
        // Quitte l'application (ne fonctionne que dans un build final, pas dans l'éditeur)
        Application.Quit();

        // Ajout pour feedback dans l'éditeur
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }
}