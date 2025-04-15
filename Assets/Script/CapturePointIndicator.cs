using UnityEngine;

public class CapturePointIndicator : MonoBehaviour
{
    [Header("Références")]
    [Tooltip("Le point de capture que cette flèche doit suivre")]
    public CapturePoint targetPoint; // À assigner dans l'inspecteur pour chaque flèche (A, B, C)

    [Tooltip("Le transform du tank joueur (peut être trouvé automatiquement)")]
    public Transform playerTankTransform;

    [Header("Visuel")]
    [Tooltip("Couleur quand le point est Neutre")]
    public Color neutralColor = Color.gray;
    [Tooltip("Couleur quand le point est Contrôlé par le Joueur/Allié")]
    public Color playerControlledColor = Color.blue;
    [Tooltip("Couleur quand le point est Contrôlé par l'Ennemi")]
    public Color enemyControlledColor = Color.red;
    [Tooltip("Couleur quand le point est Contesté")]
    public Color contestedColor = Color.yellow;
    [Tooltip("Couleur quand le point est en cours de capture")]
    public Color capturingColor = Color.white; // Utilisée pour le clignotement

    private SpriteRenderer arrowSpriteRenderer;

    void Awake()
    {
        arrowSpriteRenderer = GetComponent<SpriteRenderer>();
        if (arrowSpriteRenderer == null)
        {
            Debug.LogError("CapturePointIndicator nécessite un SpriteRenderer sur le même GameObject!", this);
            enabled = false;
            return;
        }

        // Trouve le tank joueur par tag s'il n'est pas assigné
        if (playerTankTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player"); // Assurez-vous que votre tank joueur a le tag "Player"
            if (player != null)
            {
                playerTankTransform = player.transform;
            }
            else
            {
                 Debug.LogError("Impossible de trouver le tank joueur (tag 'Player') pour l'indicateur!", this);
                 enabled = false;
                 return;
            }
        }
    }

    void Update()
    {
        // Vérifie si les cibles existent toujours
        if (targetPoint == null || playerTankTransform == null)
        {
            if(arrowSpriteRenderer != null) arrowSpriteRenderer.enabled = false;
            return;
        }
        // Assure que le renderer est actif s'il existe
        if (arrowSpriteRenderer != null) arrowSpriteRenderer.enabled = true;
        else return; // Sortir si pas de renderer

        // --- 1. Calculer l'Orientation ---
        Vector3 directionToPoint = targetPoint.transform.position - playerTankTransform.position;
        directionToPoint.z = 0;

        if (directionToPoint != Vector3.zero)
        {
            float angle = Mathf.Atan2(directionToPoint.y, directionToPoint.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0, 0, angle);
        }

        // --- 2. Mettre à Jour la Couleur ---
        Color targetColor = neutralColor; // Couleur par défaut

        // Vérifie si le composant CapturePoint existe toujours sur la cible
        CapturePoint cp = targetPoint; // Utilise la référence directe

        // *** CORRECTION DANS LE SWITCH ***
        // On utilise directement PointStatus.Valeur au lieu de CapturePoint.PointStatus.Valeur
        switch (cp.CurrentStatus) // Utilise la propriété publique de CapturePoint
        {
            case PointStatus.Neutral: // <-- Correction
                targetColor = neutralColor;
                break;
            case PointStatus.ControlledPlayer: // <-- Correction
                targetColor = playerControlledColor;
                break;
            case PointStatus.ControlledEnemy: // <-- Correction
                targetColor = enemyControlledColor;
                break;
            case PointStatus.CapturingPlayer: // <-- Correction
            case PointStatus.CapturingEnemy: // <-- Correction
                // Clignotement simple entre capturingColor et couleur équipe
                string capturingTeam = cp.capturingTeamTag; // Récupère qui capture
                Color teamColor = (capturingTeam == "Player") ? playerControlledColor : enemyControlledColor;
                targetColor = (Mathf.Sin(Time.time * 5f) > 0) ? capturingColor : teamColor;
                break;
            case PointStatus.Contested: // <-- Correction
                // Clignotement simple entre contestedColor et couleur neutre
                targetColor = (Mathf.Sin(Time.time * 8f) > 0) ? contestedColor : neutralColor;
                break;
        }
        // *** FIN CORRECTION SWITCH ***

        arrowSpriteRenderer.color = targetColor;
    }
}