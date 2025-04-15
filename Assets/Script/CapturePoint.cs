using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Nécessaire pour utiliser .Count( ... )

// --- AJOUT DE L'ENUM ---
// Définit les états logiques possibles du point de capture
public enum PointStatus { Neutral, CapturingPlayer, CapturingEnemy, ControlledPlayer, ControlledEnemy, Contested }
// -----------------------

public class CapturePoint : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Temps en secondes nécessaire pour capturer le point")]
    public float captureTime = 5.0f;
    [Tooltip("Nom du point (pour debug ou affichage)")]
    public string pointName = "A";

    [Header("Références Visuelles")]
    [Tooltip("Le SpriteRenderer principal de la zone (change de couleur)")]
    public SpriteRenderer zoneSprite;
    [Tooltip("Le Transform de l'objet enfant qui sert d'indicateur de progression (scale de 0 à 1)")]
    public Transform captureProgressIndicator;

    [Header("Couleurs Visuelles")]
    public Color neutralColor = Color.gray;
    public Color playerColor = Color.blue;         // Couleur quand capturé par le joueur
    public Color enemyColor = Color.red;           // Couleur quand capturé par l'ennemi
    public Color contestedColor = Color.yellow;    // Couleur quand contesté
    public Color playerCapturingColor = new Color(0.5f, 0.5f, 1f); // Couleur de l'indicateur de progression Joueur
    public Color enemyCapturingColor = new Color(1f, 0.5f, 0.5f);  // Couleur de l'indicateur de progression Ennemi

    // --- Variables d'état (gérées par les états) ---
    [HideInInspector] public float currentCaptureProgress = 0f;
    [HideInInspector] public string controllingTeamTag = null;
    [HideInInspector] public string capturingTeamTag = null;

    // --- AJOUT DE LA PROPRIÉTÉ PUBLIQUE ---
    // Permet aux autres scripts de connaître facilement l'état actuel
    public PointStatus CurrentStatus { get; private set; } = PointStatus.Neutral; // État initial
    // --------------------------------------

    // --- Logique interne ---
    private List<Collider2D> tanksInZone = new List<Collider2D>();
    private StateMachine stateMachine; // Garder privé si possible
    private SpriteRenderer progressSpriteRenderer;

    void Start()
    {
        stateMachine = new StateMachine();

        // Récupération et vérification du SpriteRenderer de l'indicateur
        if (captureProgressIndicator != null) {
            progressSpriteRenderer = captureProgressIndicator.GetComponent<SpriteRenderer>();
            if (progressSpriteRenderer == null) {
                Debug.LogError($"L'objet assigné à captureProgressIndicator sur {gameObject.name} n'a pas de SpriteRenderer!", this);
                captureProgressIndicator = null;
            }
        } else {
            Debug.LogError($"La référence captureProgressIndicator n'est pas assignée sur {gameObject.name}!", this);
        }

         if (zoneSprite == null) {
              Debug.LogError($"La référence zoneSprite n'est pas assignée sur {gameObject.name}!", this);
        }

        // Définit l'état initial comme Neutre (son Enter() mettra à jour CurrentStatus)
        stateMachine.ChangeState(new NeutralState(this));
    }

    void Update()
    {
        stateMachine.Update();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.CompareTag("Enemy")) {
            if (!tanksInZone.Contains(other)) {
                tanksInZone.Add(other);
                stateMachine.CurrentState?.Execute(); // Ré-évalue l'état
            }
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.CompareTag("Enemy")) {
            if (tanksInZone.Remove(other)) {
                stateMachine.CurrentState?.Execute(); // Ré-évalue l'état
            }
        }
    }

    // --- Méthodes utilitaires ---
    public int GetTeamCountInZone(string teamTag) {
        tanksInZone.RemoveAll(tankCollider => tankCollider == null);
        return tanksInZone.Count(tankCollider => tankCollider.CompareTag(teamTag));
    }

    public void ResetCaptureProgress() {
        currentCaptureProgress = 0f;
    }

    // La mise à jour visuelle peut maintenant aussi utiliser CurrentStatus si besoin
    public void UpdateVisuals() {
        // 1. Mise à jour du sprite principal
        if (zoneSprite != null) {
            switch (CurrentStatus) { // Utilise la nouvelle propriété
                case PointStatus.Neutral:
                    zoneSprite.color = neutralColor;
                    break;
                case PointStatus.ControlledPlayer:
                    zoneSprite.color = playerColor;
                    break;
                case PointStatus.ControlledEnemy:
                    zoneSprite.color = enemyColor;
                    break;
                case PointStatus.Contested:
                    // Option: Clignotement ou couleur fixe
                    zoneSprite.color = (Mathf.Sin(Time.time * 8f) > 0) ? contestedColor : neutralColor;
                    break;
                case PointStatus.CapturingPlayer:
                case PointStatus.CapturingEnemy:
                    // Option: Garder neutre ou faire un fondu (comme indicateur)
                     zoneSprite.color = neutralColor; // Garde neutre pendant capture
                    break;
            }
        }

        // 2. Mise à jour de l'indicateur de progression
        if (captureProgressIndicator != null && progressSpriteRenderer != null) {
            // Active si en capture, désactive sinon
            bool isCapturing = (CurrentStatus == PointStatus.CapturingPlayer || CurrentStatus == PointStatus.CapturingEnemy);
            captureProgressIndicator.gameObject.SetActive(isCapturing);

            if (isCapturing) {
                float progressRatio = Mathf.Clamp01(currentCaptureProgress / captureTime);
                progressSpriteRenderer.color = (CurrentStatus == PointStatus.CapturingPlayer) ? playerCapturingColor : enemyCapturingColor;
                captureProgressIndicator.localScale = new Vector3(progressRatio, progressRatio, 1f);
            }
        }
    }

    // Permet aux états de changer l'état et implicitement le CurrentStatus
    public void SetState(IState newState) {
        stateMachine.ChangeState(newState); // L'état appelera Enter() qui mettra à jour CurrentStatus
    }

    // --- AJOUT : Méthode pour définir le statut (appelée par les états) ---
    // Ceci est une alternative pour que les états n'aient pas besoin de connaître l'enum
    // Mais il est plus propre de mettre à jour directement la propriété depuis chaque état.
    // On va utiliser la mise à jour directe depuis les états.

    // Ajout d'une méthode pour définir le statut depuis les états (MEILLEURE APPROCHE)
     public void SetStatus(PointStatus newStatus)
     {
         CurrentStatus = newStatus;
     }
     // --------------------------------------------------------------------

}