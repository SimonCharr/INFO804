using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))] // Assure qu'un Rigidbody2D est présent
[RequireComponent(typeof(TankHealth))]  // Assure que TankHealth est présent
public class TankController : MonoBehaviour
{
    [Header("Mouvement")]
    public float speed = 10f;                // Vitesse de déplacement
    public float rotationSpeed = 500f;       // Vitesse de rotation du châssis
    public float minDistanceToRotate = 0.5f; // Distance minimale pour que le châssis tourne en avançant

    [Header("Tir")]
    public GameObject bulletPrefab;          // Prefab de la balle à tirer
    public Transform firePoint;              // Point d'où la balle est tirée (un enfant du tank/canon)
    public Transform canonPivot;             // GameObject du canon qui doit pivoter vers la souris
    public float fireRate = 0.5f;            // Temps minimum entre deux tirs (en secondes)

    // Variables privées
    private Rigidbody2D rb;                  // Référence au Rigidbody2D
    private Vector2 targetPosition;          // Position cible cliquée par la souris
    private bool shouldMove = false;         // Indicateur si le tank doit bouger
    private float nextFireTime = 0f;         // Temps auquel le prochain tir sera autorisé
    private Camera mainCamera;               // Cache la référence à la caméra principale

    public AudioClip shootSound;
    private AudioSource audioSource;

    void Awake()
    {
        // Récupère les composants nécessaires au démarrage
        rb = GetComponent<Rigidbody2D>();
        mainCamera = Camera.main; // Met en cache la caméra pour l'efficacité

        if (firePoint == null)
        {
            Debug.LogError("Le 'Fire Point' n'est pas assigné sur le TankController !", this);
        }
        if (bulletPrefab == null)
        {
            Debug.LogError("Le 'Bullet Prefab' n'est pas assigné sur le TankController !", this);
        }
        if (canonPivot == null)
        {
            Debug.LogWarning("Le 'Canon Pivot' n'est pas assigné, la rotation du canon sera désactivée.", this);
        }
         // S'assurer que le tag est bien "Player" (important pour le tir ami)
        if (gameObject.tag != "Player")
        {
             Debug.LogWarning($"Le tank joueur '{gameObject.name}' n'a pas le tag 'Player'. Veuillez l'assigner pour que le système de dégâts fonctionne correctement.", this);
        }

                if (audioSource == null) {
            audioSource = GetComponentInChildren<AudioSource>();
            if (audioSource == null) {
                Debug.LogWarning("Aucun AudioSource trouvé dans les enfants !");
            } else {
                Debug.Log("AudioSource trouvé dans un enfant : " + audioSource.gameObject.name);
            }
        }     
    }

    void Update()
    {
        // --- Gestion des Inputs ---

        // Mouvement : Clic gauche pour définir la destination
        if (Input.GetMouseButtonDown(0)) // Clic gauche
        {
            targetPosition = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            shouldMove = true; // Active le mouvement vers la cible
        }

        // Tir : Clic droit (ou autre touche) pour tirer
        if (Input.GetMouseButton(1) && Time.time >= nextFireTime) // Clic droit maintenu + respect du fireRate
        {
            Shoot(); // Appelle la fonction de tir
            nextFireTime = Time.time + fireRate; // Met à jour le temps du prochain tir autorisé
        }

        // --- Rotation du Canon ---
        if(canonPivot != null) {
            RotateCanonTowardsMouse(); // Fait toujours pivoter le canon vers la souris
        }


        // --- Exécution du Mouvement ---
        if (shouldMove)
        {
            MoveTowardsTarget(); // Déplace le tank
            // Rotation du Châssis (uniquement si assez loin de la cible)
            if (Vector2.Distance(transform.position, targetPosition) > minDistanceToRotate)
            {
                RotateBodyTowards(targetPosition); // Fait tourner le châssis vers la cible
            }
        }
    }

    // FixedUpdate est recommandé pour les manipulations de Rigidbody (mouvement physique)
    // Mais ici, on utilise transform.position, donc Update ou FixedUpdate peuvent convenir.
    // Si vous utilisez rb.MovePosition, mettez le code de mouvement dans FixedUpdate.

    /// <summary>
    /// Déplace le tank vers la position cible.
    /// </summary>
    void MoveTowardsTarget()
    {
        // Utilise MoveTowards pour un déplacement simple sans physique complexe
        transform.position = Vector2.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);

        // Vérifie si la cible est atteinte (ou presque)
        if (Vector2.Distance(transform.position, targetPosition) < 0.1f)
        {
            shouldMove = false; // Arrête le mouvement une fois la cible atteinte
        }
    }

    /// <summary>
    /// Fait pivoter le châssis du tank vers une direction cible.
    /// </summary>
    /// <param name="targetPoint">Le point vers lequel le châssis doit s'orienter.</param>
    void RotateBodyTowards(Vector2 targetPoint)
    {
        // Calcule la direction vers la cible
        Vector2 direction = (targetPoint - (Vector2)transform.position).normalized;

        // Calcule l'angle (attention à l'orientation du sprite : -90f si le 'haut' du sprite est vers le Y+)
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);

        // Applique la rotation progressive au châssis (transform principal)
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

     /// <summary>
    /// Fait pivoter le canon (objet enfant) vers la position de la souris.
    /// </summary>
    void RotateCanonTowardsMouse()
    {
        Vector3 mouseWorldPosition = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = mouseWorldPosition - canonPivot.position; // Direction depuis le pivot du canon
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f; // Ajustez -90f si besoin
        canonPivot.rotation = Quaternion.Euler(0f, 0f, angle); // Rotation instantanée pour le canon
        // Si vous voulez une rotation douce :
        // Quaternion targetCanonRotation = Quaternion.Euler(0f, 0f, angle);
        // canonPivot.rotation = Quaternion.RotateTowards(canonPivot.rotation, targetCanonRotation, canonRotationSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Instancie une balle et configure son script.
    /// </summary>
    void Shoot()
    {
        if (bulletPrefab == null || firePoint == null) return; // Sécurité

        // Instancie la balle à la position et rotation du firePoint
        GameObject bulletInstance = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        if (shootSound != null && audioSource != null) {
            audioSource.PlayOneShot(shootSound);
            Debug.Log("Son de tir joué !");
        } else {
            if (shootSound == null) Debug.LogWarning("shootSound est null !");
            if (audioSource == null) Debug.LogWarning("audioSource est null !");
        }
        // Récupère le script de la balle instanciée
        BulletDestruction bulletScript = bulletInstance.GetComponent<BulletDestruction>();

        if (bulletScript != null)
        {
            // Assigne les informations du tireur à la balle
            bulletScript.shooter = this.gameObject;      // Qui a tiré ? Ce tank.
            bulletScript.shooterTag = this.gameObject.tag; // Quel est son tag ? ("Player")
        }
        else
        {
            // Avertissement si le prefab de balle est mal configuré
            Debug.LogError("Le prefab de balle assigné n'a pas le script 'BulletDestruction' !", bulletPrefab);
        }
    }
}