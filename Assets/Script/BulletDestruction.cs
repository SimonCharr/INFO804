using UnityEngine;

// Ce script gère la destruction de la balle et l'application des dégâts
public class BulletDestruction : MonoBehaviour
{
    [Header("Propriétés de la Balle")]
    public float lifeTime = 2f;      // Durée de vie en secondes si rien n'est touché
    public float damage = 25f;        // Dégâts infligés par la balle

    [Header("Informations sur le Tireur")]
    [Tooltip("Assigné automatiquement au moment du tir")]
    public GameObject shooter;       // Référence au GameObject qui a tiré
    [Tooltip("Assigné automatiquement au moment du tir (ex: 'Player' ou 'Enemy')")]
    public string shooterTag;        // Tag du GameObject qui a tiré

    // ----- AJOUTÉ -----
    [Header("Collision Obstacles")]
    [Tooltip("Définissez ici les layers qui doivent arrêter la balle (ex: Murs, Obstacles Statiques). NE PAS inclure 'CaptureZones' !")]
    public LayerMask obstacleLayers; // À configurer dans l'inspecteur sur le prefab de la balle
    // --------------------

    // ----- AJOUTÉ -----
    // Pour stocker l'index numérique du layer "CaptureZones" pour une vérification efficace
    private int captureZoneLayerIndex = -1;
    // --------------------

    // ----- NOUVELLE MÉTHODE Awake -----
    void Awake()
    {
        // Trouve l'index numérique du layer nommé "CaptureZones"
        // Assurez-vous que ce layer existe EXACTEMENT sous ce nom dans vos Project Settings -> Tags and Layers
        captureZoneLayerIndex = LayerMask.NameToLayer("CaptureZones");

        // Affiche un avertissement si le layer n'est pas trouvé
        if (captureZoneLayerIndex == -1)
        {
            Debug.LogWarning($"[{gameObject.name}] Le Layer 'CaptureZones' n'a pas été trouvé dans BulletDestruction. La balle risque d'être bloquée par les zones de capture. Vérifiez le nom exact dans Project Settings -> Tags and Layers.", this);
        }

         // Affiche un avertissement si aucun layer d'obstacle n'est défini
         if (obstacleLayers.value == 0) // .value donne le masque d'entier, 0 signifie "Nothing"
         {
             Debug.LogWarning($"[{gameObject.name}] Le LayerMask 'Obstacle Layers' n'est pas configuré dans l'inspecteur. La balle risque de traverser les murs.", this);
         }
    }
    // -------------------------------

    void Start()
    {
        // Détruit la balle après 'lifeTime' secondes si elle n'a rien touché
        Destroy(gameObject, lifeTime);
    }

    // Utilise OnTriggerEnter2D car le collider de la balle doit être un Trigger
    void OnTriggerEnter2D(Collider2D otherCollider)
    {
        // ----- LOG DE DEBUG DÉTAILLÉ (Optionnel, décommenter si besoin) -----
        // Debug.Log($"TEST [{gameObject.name}] Bullet Trigger Enter: Balle a touché '{otherCollider.gameObject.name}' sur Layer '{LayerMask.LayerToName(otherCollider.gameObject.layer)}' (Index: {otherCollider.gameObject.layer}). L'index attendu pour CaptureZone est {captureZoneLayerIndex}.");
        // --------------------------------------------------------------------

        // --- Étape 0: IGNORER LES ZONES DE CAPTURE ---  <-- AJOUTÉ
        // Si l'objet touché est sur le layer "CaptureZones" (et qu'on a trouvé ce layer), on arrête tout ici.
        if (captureZoneLayerIndex != -1 && otherCollider.gameObject.layer == captureZoneLayerIndex)
        {
            // Debug.Log($"[{gameObject.name}] Balle traverse la zone de capture {otherCollider.gameObject.name}");
            return; // La balle continue son chemin sans être détruite
        }
        // ---------------------------------------------

        // --- Étape 1: Ignorer le Tireur ---
        if (otherCollider.gameObject == shooter)
        {
            // Debug.Log($"[{gameObject.name}] Collision Trigger avec le tireur ignorée.");
            return; // Ne rien faire
        }
        // ----------------------------------

        // --- Étape 2: Vérifier si c'est une cible avec TankHealth (un Tank) ---
        TankHealth targetHealth = otherCollider.GetComponent<TankHealth>();
        if (targetHealth != null) // C'est un tank !
        {
            // Vérifie si c'est un ennemi (pas le même tag que le tireur)
            if (otherCollider.CompareTag(shooterTag) == false)
            {
                 Debug.Log($"[{gameObject.name}] Balle tirée par {shooterTag} inflige {damage} dégâts à ENNEMI {otherCollider.gameObject.name} (Tag: {otherCollider.gameObject.tag})");
                 targetHealth.TakeDamage(damage);
            }
            else // C'est un allié
            {
                Debug.Log($"[{gameObject.name}] Tir ami sur {otherCollider.gameObject.name} ignoré (Trigger).");
                // Pas de dégâts
            }

            // Dans tous les cas où on touche un tank (allié ou ennemi), la balle est détruite
            // Debug.Log($"[{gameObject.name}] Balle détruite après contact Tank.");
            Destroy(gameObject);
            return; // Sortir après destruction
        }
        // -------------------------------------------------------------

        // --- Étape 3: Vérifier si c'est un Obstacle Solide défini dans le LayerMask --- <-- MODIFIÉ
        // On ne détruit plus la balle sur TOUT ce qui n'a pas TankHealth,
        // mais seulement sur ce qui est DANS le LayerMask 'obstacleLayers'.
        int otherLayerMaskValue = 1 << otherCollider.gameObject.layer; // Crée un masque pour le layer de l'objet touché
        if ((obstacleLayers.value & otherLayerMaskValue) != 0) // Vérifie si ce layer est coché dans notre LayerMask
        {
             Debug.Log($"[{gameObject.name}] Balle détruite par obstacle ({otherCollider.gameObject.name}) sur un layer bloquant.");
             Destroy(gameObject);
             return; // Sortir après destruction
        }
        // --------------------------------------------------------------------------

        // --- Étape 4: Autres Collisions ---
        // Si on arrive ici, c'est qu'on a touché quelque chose qui n'est ni :
        // - Une zone de capture
        // - Le tireur
        // - Un tank (allié ou ennemi)
        // - Un obstacle défini dans obstacleLayers
        // Par défaut, la balle continue. Si vous voulez qu'elle soit détruite par d'autres types d'objets,
        // ajoutez des vérifications ici ou modifiez la logique ci-dessus.
         // Debug.Log($"[{gameObject.name}] Balle a touché {otherCollider.gameObject.name} mais ce n'est pas géré comme un obstacle. Elle continue.");
    }
}