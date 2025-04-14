using UnityEngine;
using UnityEngine.Events;

public class TankHealth : MonoBehaviour
{
    [Header("Santé")]
    public float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("UI")]
    [Tooltip("Assigner le Prefab de la barre de vie (World Space Canvas)")]
    public GameObject healthBarPrefab; // Référence au prefab que vous avez créé

    [Header("Événements")]
    public UnityEvent OnTankDeath;

    // Référence à l'instance de la barre de vie créée pour ce tank
    private WorldSpaceHealthBar healthBarInstance;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    void Start() // On le fait dans Start pour s'assurer que la position initiale est correcte
    {
        // --- Création de la Barre de Vie ---
        if (healthBarPrefab != null)
        {
            // Instancie le prefab de la barre de vie
            GameObject hbInstanceGO = Instantiate(healthBarPrefab, transform.position, Quaternion.identity); // Position initiale, rotation caméra gérée par le script de la barre

            // Récupère le script de contrôle de la barre
            healthBarInstance = hbInstanceGO.GetComponent<WorldSpaceHealthBar>();

            if (healthBarInstance != null)
            {
                // Initialise la barre en lui passant le Transform de ce tank
                healthBarInstance.Initialize(this.transform);
            }
            else
            {
                Debug.LogError("Le prefab de barre de vie n'a pas le script WorldSpaceHealthBar attaché !", healthBarPrefab);
            }
        }
        else
        {
            Debug.LogWarning("Aucun prefab de barre de vie n'est assigné à " + gameObject.name);
        }
    }

    public void TakeDamage(float damageAmount)
    {
        if (currentHealth <= 0) return;
        currentHealth -= damageAmount;
        // Debug.Log($"{gameObject.name} a pris {damageAmount} dégâts. Santé restante: {currentHealth}"); // Déjà dans votre code
        currentHealth = Mathf.Max(currentHealth, 0f);

        // Note: La barre de vie se mettra à jour d'elle-même dans son LateUpdate

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        Debug.Log($"{gameObject.name} est détruit !");
        OnTankDeath?.Invoke();

        // --- Destruction de la Barre de Vie ---
        // Important : Détruire aussi la barre de vie associée quand le tank meurt
        if (healthBarInstance != null)
        {
            Destroy(healthBarInstance.gameObject);
        }
        // ------------------------------------

        Destroy(gameObject); // Détruit le tank
    }

    public void Heal(float healAmount)
    {
         if (currentHealth <= 0) return; // Ne peut pas soigner un mort
        currentHealth += healAmount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
         // Debug.Log($"{gameObject.name} a été soigné de {healAmount}. Santé actuelle: {currentHealth}"); // Déjà dans votre code

        // Note: La barre de vie se mettra à jour d'elle-même dans son LateUpdate
    }

     // S'assurer de détruire la barre si le tank est détruit pour une autre raison
     void OnDestroy() {
          if (healthBarInstance != null)
         {
             // Vérifie si l'application ne quitte pas déjà (évite des erreurs à la fermeture)
             // Note: Ceci nécessite Unity 2019.3+ pour `applicationIsQuitting`
             // Si version antérieure, cette vérification peut causer problème lors de l'arrêt.
             // Vous pouvez l'enlever si vous avez des erreurs à la fermeture du jeu.
             // if (!applicationIsQuitting) { // Nécessite une variable statique gérée globalement
             //     Destroy(healthBarInstance.gameObject);
             // }
             // Alternative simple (peut afficher une erreur bénigne à la fermeture de l'éditeur):
              Destroy(healthBarInstance.gameObject);
         }
     }
}