using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Nécessaire pour certaines opérations sur les dictionnaires/listes

// Ajoute une dépendance : ce GameObject DOIT avoir un composant TankHealth.
[RequireComponent(typeof(TankHealth))]
public class EnemyTankController : MonoBehaviour
{
    [Header("Références Cible & Combat")]
    public Transform playerTank;             // Assigner le Transform du tank joueur dans l'Inspecteur
    public Transform canonPivot;             // Le Transform du canon qui doit pivoter (enfant du tank)
    public Transform firePoint;              // Le point d'où les balles sont tirées (enfant du canon)
    public GameObject bulletPrefab;          // Le prefab de la balle à instancier

    [Header("Mouvement & Rotation Châssis")]
    public float moveSpeed = 3f;             // Vitesse de déplacement en suivant le chemin
    public float rotationSpeed = 200f;       // Vitesse de rotation du CHASSIS (en degrés par seconde)

    [Header("Combat")]
    public float fireRate = 1.0f;            // Temps minimum (secondes) entre deux tirs
    public float shootingRange = 10f;        // Distance maximale pour commencer à tirer
    public float canonRotationSpeed = 300f;  // Vitesse de rotation du CANON

    [Header("Grille de Pathfinding")]
    public Vector2Int gridSize = new Vector2Int(20, 20); // Taille de la grille en tuiles
    public float tileSize = 1f;              // Taille d'une tuile en unités monde
    public LayerMask obstacleLayer;          // Layer (calque) contenant les obstacles pour le pathfinding
    public float recalculatePathDistanceThreshold = 1.0f; // Seuil de distance pour recalculer le chemin

    // --- État du Pathfinding (variables internes) ---
    private Vector2Int currentGridPosition;
    private Vector2Int targetGridPosition;
    private List<Vector2> path;
    private int pathIndex;
    private Vector3 lastPlayerPosition;

    // --- État du Combat (variables internes) ---
    private float nextFireTime = 0f;         // Temps auquel le prochain tir est autorisé
    private TankHealth playerHealth;         // Référence au script de vie du joueur

    // ========================================
    // --- Méthodes Unity ---
    // ========================================

    void Awake() // Awake est appelé avant Start, idéal pour les références internes
    {
        // --- Vérifications initiales ---
        if (playerTank == null) {
            Debug.LogError("Le 'Player Tank' n'est pas assigné !", this);
            enabled = false; return;
        }
        if (canonPivot == null) Debug.LogError("Le 'Canon Pivot' n'est pas assigné !", this);
        if (firePoint == null) Debug.LogError("Le 'Fire Point' n'est pas assigné !", this);
        if (bulletPrefab == null) Debug.LogError("Le 'Bullet Prefab' n'est pas assigné !", this);

        // Vérification du Tag (important pour le système de dégâts)
        if (gameObject.tag != "Enemy") {
             Debug.LogWarning($"Le tank ennemi '{gameObject.name}' n'a pas le tag 'Enemy'. Veuillez l'assigner.", this);
        }

        // Récupère la référence à la santé du joueur pour vérifier s'il est en vie
        playerHealth = playerTank.GetComponent<TankHealth>();
        if (playerHealth == null) {
            Debug.LogError("Le tank joueur assigné n'a pas de composant TankHealth !", playerTank);
            // On pourrait désactiver le script ici aussi si la santé du joueur est essentielle
            // enabled = false; return;
        }
    }

    void Start() // Appelé une seule fois après Awake
    {
        // Calcul initial du chemin
        currentGridPosition = WorldToGrid(transform.position);
        lastPlayerPosition = playerTank.position;
        FindPath();
    }

    void Update() // Appelé à chaque image
    {
        // --- Vérification de la Cible ---
        // Si le joueur n'existe plus ou est mort, ne rien faire (ou autre logique : patrouille, etc.)
        if (playerTank == null || (playerHealth != null && playerHealth.CurrentHealth <= 0)) {
             path = null; // Arrête le mouvement
            // Ajouter ici un comportement si le joueur est mort (ex: rester immobile)
            return;
        }

        // --- Logique de Recalcul du Chemin (inchangée) ---
        Vector3 currentPlayerPosition = playerTank.position;
        Vector2Int newTargetGridPosition = WorldToGrid(currentPlayerPosition);
        float playerMovedDistance = Vector3.Distance(currentPlayerPosition, lastPlayerPosition);
        bool needsRecalculation = path == null || path.Count == 0 ||
                                  targetGridPosition != newTargetGridPosition ||
                                  playerMovedDistance > recalculatePathDistanceThreshold;
        if (needsRecalculation) {
            FindPath();
            lastPlayerPosition = currentPlayerPosition;
        }

        // --- Mouvement et Rotation du Châssis (inchangés) ---
        MoveAlongPath();         // Suit le chemin
        RotateTowardsTarget();   // Fait pivoter le CHASSIS vers le joueur (selon votre code original)
                                 // Note : Pourrait être modifié pour suivre le chemin plutôt que le joueur

        // --- Combat ---
        float distanceToPlayer = Vector2.Distance(transform.position, playerTank.position);

        // 1. Rotation du Canon (toujours viser le joueur si possible)
        if (canonPivot != null) {
            RotateCanonTowardsPlayer();
        }

        // 2. Tir (si à portée et si le délai est écoulé)
        if (distanceToPlayer <= shootingRange && Time.time >= nextFireTime) {
            Shoot();
            nextFireTime = Time.time + fireRate; // Met à jour le temps pour le prochain tir
        }
    }

    // ========================================
    // --- Fonctions de Pathfinding (Inchangées par rapport à votre code) ---
    // ========================================

    void FindPath() {
         if (playerTank == null) { path = null; return; } // Sécurité
        currentGridPosition = WorldToGrid(transform.position);
        targetGridPosition = WorldToGrid(playerTank.position);
        path = Dijkstra(currentGridPosition, targetGridPosition);
        if (path == null || path.Count == 0) {
            Debug.LogWarning($"[{gameObject.name}] Chemin non trouvé ou vide de {currentGridPosition} à {targetGridPosition}.", this);
            path = null;
        } else {
            if (path.Count > 0 && Vector2.Distance(transform.position, path[0]) < tileSize * 0.5f) {
                path.RemoveAt(0);
            }
        }
        pathIndex = 0;
    }

    List<Vector2> Dijkstra(Vector2Int start, Vector2Int end) {
        if (!IsWithinBounds(start) || !IsWalkable(start)) { return null; }
        var frontier = new PriorityQueue<Vector2Int>(); frontier.Enqueue(start, 0);
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>{ [start] = start };
        var costSoFar = new Dictionary<Vector2Int, float>{ [start] = 0 };
        while (frontier.Count > 0) {
            var current = frontier.Dequeue();
            if (current == end) { return ReconstructPath(cameFrom, end); }
            foreach (var next in GetNeighbors(current)) {
                float moveCost = Vector2Int.Distance(current, next);
                float newCost = costSoFar[current] + moveCost;
                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next]) {
                    costSoFar[next] = newCost;
                    float priority = newCost;
                    frontier.Enqueue(next, priority);
                    cameFrom[next] = current;
                }
            }
        }
        Debug.LogWarning($"[{gameObject.name}] Frontière vide, aucun chemin trouvé de {start} à {end}.", this);
        return null;
    }

    List<Vector2> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) {
        var totalPath = new List<Vector2>();
        if (!cameFrom.ContainsKey(current)) { Debug.LogError("ReconstructPath: Nœud cible non trouvé."); return totalPath; }
        Vector2Int startNode = cameFrom.FirstOrDefault(kvp => kvp.Key == kvp.Value).Key; // Trouve le départ
        Vector2Int step = current;
        int safety = 0; const int maxSteps = 10000; // Sécurité anti-boucle infinie
        while (step != startNode && safety < maxSteps) {
            totalPath.Add(GridToWorld(step));
            if (!cameFrom.ContainsKey(step)) { Debug.LogError($"ReconstructPath: Nœud {step} manquant dans cameFrom."); break; }
            step = cameFrom[step];
            safety++;
        }
         if(safety >= maxSteps) Debug.LogError("ReconstructPath: Limite de sécurité atteinte, boucle infinie ?");
        totalPath.Reverse();
        return totalPath;
    }

    List<Vector2Int> GetNeighbors(Vector2Int position) {
        var neighbors = new List<Vector2Int>();
        for (int x = -1; x <= 1; x++) {
            for (int y = -1; y <= 1; y++) {
                if (x == 0 && y == 0) continue;
                Vector2Int neighborPos = new Vector2Int(position.x + x, position.y + y);
                if (!IsWithinBounds(neighborPos) || !IsWalkable(neighborPos)) continue;
                if (Mathf.Abs(x) + Mathf.Abs(y) == 2) { // Diagonale
                    Vector2Int n1 = new Vector2Int(position.x + x, position.y);
                    Vector2Int n2 = new Vector2Int(position.x, position.y + y);
                    if (!IsWalkable(n1) || !IsWalkable(n2)) continue; // Bloqué par un coin
                }
                neighbors.Add(neighborPos);
            }
        }
        return neighbors;
    }

    bool IsWithinBounds(Vector2Int gridPosition) => gridPosition.x >= 0 && gridPosition.x < gridSize.x && gridPosition.y >= 0 && gridPosition.y < gridSize.y;

    bool IsWalkable(Vector2Int gridPosition) {
        Vector2 worldPosition = GridToWorld(gridPosition);
        float checkRadius = tileSize / 2f * 0.9f;
        return Physics2D.OverlapCircle(worldPosition, checkRadius, obstacleLayer) == null;
    }

    Vector2Int WorldToGrid(Vector2 worldPosition) {
        int x = Mathf.RoundToInt(worldPosition.x / tileSize);
        int y = Mathf.RoundToInt(worldPosition.y / tileSize);
        x = Mathf.Clamp(x, 0, gridSize.x - 1);
        y = Mathf.Clamp(y, 0, gridSize.y - 1);
        return new Vector2Int(x, y);
    }

    Vector2 GridToWorld(Vector2Int gridPosition) {
        float x = gridPosition.x * tileSize + tileSize / 2f;
        float y = gridPosition.y * tileSize + tileSize / 2f;
        return new Vector2(x, y);
    }

    // ========================================
    // --- Fonctions de Mouvement/Rotation (Inchangées par rapport à votre code) ---
    // ========================================

    void MoveAlongPath() {
        if (path != null && pathIndex < path.Count) {
            Vector2 targetWaypoint = path[pathIndex];
            transform.position = Vector2.MoveTowards(transform.position, targetWaypoint, moveSpeed * Time.deltaTime);
            if (Vector2.Distance(transform.position, targetWaypoint) < 0.1f) {
                pathIndex++;
            }
        }
    }

    // Note: Cette fonction, dans votre code original, fait tourner TOUT le tank vers le joueur.
    // Elle pourrait entrer en conflit avec la rotation nécessaire pour suivre le chemin.
    // Il serait peut-être préférable que cette fonction oriente vers le prochain waypoint,
    // et que seule la fonction RotateCanonTowardsPlayer() vise le joueur.
    void RotateTowardsTarget() {
        if (playerTank != null) {
            Vector2 direction = (playerTank.position - transform.position).normalized;
            if (direction != Vector2.zero) {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f; // Ajustez -90f si besoin
                Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }
    }

    // ========================================
    // --- Nouvelles Fonctions de Combat ---
    // ========================================

    /// <summary>
    /// Fait pivoter le CANON (objet enfant) vers le joueur.
    /// </summary>
    void RotateCanonTowardsPlayer() {
        if (playerTank == null || canonPivot == null) return; // Sécurité

        Vector2 direction = (playerTank.position - canonPivot.position).normalized;
        if (direction != Vector2.zero) {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f; // Ajustez -90f si besoin
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
            // Applique la rotation progressive au canon
            canonPivot.rotation = Quaternion.RotateTowards(canonPivot.rotation, targetRotation, canonRotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Instancie une balle ennemie et configure son script BulletDestruction.
    /// </summary>
    void Shoot() {
        if (bulletPrefab == null || firePoint == null) {
             Debug.LogWarning($"[{gameObject.name}] Tentative de tir mais prefab ou firepoint manquant.");
             return; // Ne peut pas tirer
        }

        // Instancie la balle à la position/rotation du firePoint (qui doit suivre le canon)
        GameObject bulletInstance = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        BulletDestruction bulletScript = bulletInstance.GetComponent<BulletDestruction>();

        if (bulletScript != null) {
            // Assigne les informations du tireur (l'ennemi) à la balle
            bulletScript.shooter = this.gameObject;
            bulletScript.shooterTag = this.gameObject.tag; // Devrait être "Enemy"
        } else {
            Debug.LogError($"Le prefab de balle '{bulletPrefab.name}' n'a pas le script BulletDestruction !", bulletPrefab);
        }
    }

    // ========================================
    // --- Classe Interne PriorityQueue (Inchangée) ---
    // ========================================
    // (Copiez/Collez la classe PriorityQueue<T> ici, comme dans les versions précédentes)
    public class PriorityQueue<T> {
         private List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();
         public int Count => elements.Count;
         public void Enqueue(T item, float priority) {
             elements.Add(new KeyValuePair<T, float>(item, priority));
             int i = elements.Count - 1;
             var newItem = elements[i];
             while (i > 0 && elements[i - 1].Value > newItem.Value) {
                 elements[i] = elements[i - 1]; i--;
             }
             elements[i] = newItem;
         }
         public T Dequeue() {
             if (elements.Count == 0) throw new System.InvalidOperationException("File vide");
             var item = elements[0].Key; elements.RemoveAt(0); return item;
         }
         public bool Contains(T item) {
              EqualityComparer<T> comparer = EqualityComparer<T>.Default;
              foreach (var element in elements) if (comparer.Equals(element.Key, item)) return true;
              return false;
         }
         public bool IsEmpty => elements.Count == 0;
     }
    // ========================================
}