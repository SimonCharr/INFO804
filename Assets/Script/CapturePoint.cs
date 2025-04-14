using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Nécessaire pour utiliser .Count( ... )

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
    public Transform captureProgressIndicator; // Assignez l'enfant "ProgressIndicator" ici

    [Header("Couleurs Visuelles")]
    public Color neutralColor = Color.gray;
    public Color playerColor = Color.blue;         // Couleur quand capturé par le joueur
    public Color enemyColor = Color.red;           // Couleur quand capturé par l'ennemi
    public Color contestedColor = Color.yellow;    // Couleur quand contesté
    public Color playerCapturingColor = new Color(0.5f, 0.5f, 1f); // Couleur de l'indicateur de progression Joueur
    public Color enemyCapturingColor = new Color(1f, 0.5f, 0.5f);  // Couleur de l'indicateur de progression Ennemi

    // --- Variables d'état (gérées par les états) ---
    [HideInInspector] public float currentCaptureProgress = 0f;
    [HideInInspector] public string controllingTeamTag = null; // Qui contrôle ("Player", "Enemy", ou null si personne)
    [HideInInspector] public string capturingTeamTag = null;   // Qui est en train de capturer ("Player" ou "Enemy")

    // --- Logique interne ---
    private List<Collider2D> tanksInZone = new List<Collider2D>(); // Liste des tanks (collider) présents dans la zone
    private StateMachine stateMachine;
    private SpriteRenderer progressSpriteRenderer; // Cache le SpriteRenderer de l'indicateur

    void Start()
    {
        // Initialisation de la State Machine
        stateMachine = new StateMachine();

        // Récupération et vérification du SpriteRenderer de l'indicateur
        if (captureProgressIndicator != null)
        {
            progressSpriteRenderer = captureProgressIndicator.GetComponent<SpriteRenderer>();
            if (progressSpriteRenderer == null)
            {
                Debug.LogError($"L'objet assigné à captureProgressIndicator sur {gameObject.name} n'a pas de SpriteRenderer!", this);
                captureProgressIndicator = null; // Invalide la référence si pas de renderer
            }
        }
        else
        {
            Debug.LogError($"La référence captureProgressIndicator n'est pas assignée sur {gameObject.name}!", this);
        }

         if (zoneSprite == null)
        {
             Debug.LogError($"La référence zoneSprite n'est pas assignée sur {gameObject.name}!", this);
        }


        // Définit l'état initial comme Neutre
        // L'appel à ChangeState va automatiquement appeler Enter() sur NeutralState,
        // qui appellera UpdateVisuals() pour la première fois.
        stateMachine.ChangeState(new NeutralState(this));
    }

    void Update()
    {
        // Exécute la logique de l'état actuel à chaque frame
        // (par exemple, incrémenter la capture dans CapturingState)
        stateMachine.Update();
    }

    // --- Détection d'entrée dans la zone ---
    void OnTriggerEnter2D(Collider2D other)
    {
        // On ne s'intéresse qu'aux objets tagués "Player" ou "Enemy"
        if (other.CompareTag("Player") || other.CompareTag("Enemy"))
        {
            // Ajoute le collider du tank à la liste s'il n'y est pas déjà
            if (!tanksInZone.Contains(other))
            {
                tanksInZone.Add(other);
                // Ré-évalue immédiatement l'état actuel car la situation a changé
                // CurrentState peut être null très brièvement au démarrage, d'où le '?'
                stateMachine.CurrentState?.Execute();
                 // Debug.Log($"{other.tag} ENTERED {pointName}. Count: {tanksInZone.Count}. Player: {GetTeamCountInZone("Player")}, Enemy: {GetTeamCountInZone("Enemy")}");

            }
        }
    }

    // --- Détection de sortie de la zone ---
    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player") || other.CompareTag("Enemy"))
        {
            // Retire le collider du tank de la liste s'il y était
            if (tanksInZone.Remove(other))
            {
                 // Ré-évalue immédiatement l'état actuel
                stateMachine.CurrentState?.Execute();
                 // Debug.Log($"{other.tag} EXITED {pointName}. Count: {tanksInZone.Count}. Player: {GetTeamCountInZone("Player")}, Enemy: {GetTeamCountInZone("Enemy")}");
            }
        }
    }

    // --- Méthodes utilitaires appelées par les états ---

    // Compte combien de tanks d'une équipe sont présents
    public int GetTeamCountInZone(string teamTag)
    {
        // Nettoie la liste des tanks qui auraient pu être détruits entre temps
        tanksInZone.RemoveAll(tankCollider => tankCollider == null);
        // Compte les tanks restants avec le bon tag
        return tanksInZone.Count(tankCollider => tankCollider.CompareTag(teamTag));
    }

    // Remet à zéro la progression de capture
    public void ResetCaptureProgress()
    {
        currentCaptureProgress = 0f;
    }

    // Met à jour l'apparence visuelle du point et de l'indicateur
    public void UpdateVisuals()
    {
        // 1. Mise à jour du sprite principal de la zone
        if (zoneSprite != null)
        {
            if (stateMachine.CurrentState is ContestedState) {
                zoneSprite.color = contestedColor;
            } else if (stateMachine.CurrentState is CapturedState) {
                zoneSprite.color = controllingTeamTag == "Player" ? playerColor : enemyColor;
            } else { // Inclut NeutralState et CapturingState pour la couleur de base
                zoneSprite.color = neutralColor;
            }
        }

        // 2. Mise à jour de l'indicateur de progression (cercle qui grandit)
        if (captureProgressIndicator != null && progressSpriteRenderer != null)
        {
            if (stateMachine.CurrentState is CapturingState)
            {
                // Calcule le ratio 0..1
                float progressRatio = Mathf.Clamp01(currentCaptureProgress / captureTime);
                // Définit la couleur de l'indicateur
                progressSpriteRenderer.color = capturingTeamTag == "Player" ? playerCapturingColor : enemyCapturingColor;
                // Ajuste la taille (scale)
                captureProgressIndicator.localScale = new Vector3(progressRatio, progressRatio, 1f);
                // S'assure qu'il est visible
                captureProgressIndicator.gameObject.SetActive(true);
            }
            else // Pour tous les autres états (Neutral, Captured, Contested)
            {
                // Cache l'indicateur
                captureProgressIndicator.gameObject.SetActive(false);
            }
        }
    }

    // Permet aux états de changer l'état de ce CapturePoint
    public void SetState(IState newState)
    {
        stateMachine.ChangeState(newState);
    }
}