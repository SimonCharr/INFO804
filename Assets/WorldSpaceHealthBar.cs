using UnityEngine;
using UnityEngine.UI; // Nécessaire pour accéder aux composants UI comme Image

public class WorldSpaceHealthBar : MonoBehaviour
{
    [Header("Références UI")]
    [Tooltip("Assigner l'Image UI qui sert de barre de remplissage (Fill)")]
    public Image healthBarFillImage; // Référence à l'image "Fill"

    [Header("Configuration")]
    [Tooltip("Décalage vertical pour positionner la barre au-dessus de la cible")]
    public Vector3 offset = new Vector3(0, 0.8f, 0); // Position relative au tank

    // Références internes (assignées automatiquement)
    [HideInInspector] // Cache dans l'inspecteur, mais public pour être set par TankHealth
    public Transform targetToFollow; // Le Transform du tank à suivre
    private TankHealth targetHealth; // Le script de vie du tank
    private Camera mainCamera;

    void Awake()
    {
        // Récupère la caméra une seule fois
        mainCamera = Camera.main;

        if (healthBarFillImage == null)
        {
            Debug.LogError("L'image 'Health Bar Fill Image' n'est pas assignée sur le script WorldSpaceHealthBar!", this);
            enabled = false; // Désactive si la référence manque
        }
    }

    // Méthode pour initialiser la barre (appelée par TankHealth)
    public void Initialize(Transform target)
    {
        targetToFollow = target;
        targetHealth = target.GetComponent<TankHealth>();

        if (targetHealth == null)
        {
            Debug.LogError($"La cible {target.name} n'a pas de composant TankHealth!", this);
            Destroy(gameObject); // Détruit la barre si la cible n'a pas de vie
            return;
        }
         // Met à jour la barre une première fois
        UpdateHealthBar();
    }


    void LateUpdate() // LateUpdate est souvent mieux pour suivre des objets qui bougent dans Update
    {
        // Si la cible (le tank) a été détruite, on détruit aussi la barre de vie
        if (targetToFollow == null || targetHealth == null)
        {
            Destroy(gameObject);
            return;
        }

        // --- Mise à jour de la Position ---
        // Place la barre de vie à la position de la cible + le décalage défini
        transform.position = targetToFollow.position + offset;

        // --- Mise à jour de l'Orientation (Billboard) ---
        // Fait en sorte que la barre de vie fasse toujours face à la caméra
        // transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
        //                  mainCamera.transform.rotation * Vector3.up);
        // Ou plus simple si la caméra ne tourne que sur Y :
        transform.rotation = mainCamera.transform.rotation;


        // --- Mise à jour de la Barre de Vie ---
        // Met à jour la valeur de remplissage de l'image
        UpdateHealthBar();

    }

    /// <summary>
    /// Met à jour la valeur visuelle de la barre de vie.
    /// </summary>
    private void UpdateHealthBar()
    {
         if (targetHealth != null && healthBarFillImage != null)
        {
            // Calcule la proportion de vie restante (entre 0 et 1)
            float fill = targetHealth.CurrentHealth / targetHealth.MaxHealth;
            healthBarFillImage.fillAmount = fill;
        }
    }
}