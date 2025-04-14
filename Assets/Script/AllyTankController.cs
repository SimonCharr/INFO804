using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(TankHealth))]
public class AllyTankController : MonoBehaviour
{
    #region Variables Configurables (Inspecteur)

    [Header("Références Tank & Combat")]
    public Transform canonPivot;
    public Transform firePoint;
    public GameObject bulletPrefab;

    [Header("Mouvement & Rotation")]
    public float moveSpeed = 3f;
    public float chassisRotationSpeed = 200f;
    public float canonRotationSpeed = 300f;

    [Header("Combat")]
    public float fireRate = 1.0f;
    public float shootingRange = 10f;
    public float detectionRange = 15f;
    [Tooltip("Layer(s) sur lequel/lesquels se trouvent les tanks ENNEMIS (IMPORTANT!)")]
    public LayerMask enemyLayerMask;

    [Header("Pathfinding (Dijkstra)")]
    public Vector2Int gridSize = new Vector2Int(30, 30);
    public float tileSize = 1f;
    public LayerMask obstacleLayer;
    public float pathRecalculationRate = 0.5f;

    [Header("Comportement IA")]
    public List<CapturePoint> capturePointsOfInterest;
    public float patrolPointReachedDistance = 1.5f;

    #endregion

    #region Variables Internes

    private enum AIState { Idle, SeekingCapturePoint, AttackingEnemy, Patrolling }
    private AIState currentState = AIState.Patrolling;
    private Transform currentTargetEnemy = null;
    private CapturePoint currentTargetCapturePoint = null;
    private Vector3 currentNavigationTargetPosition;
    private List<Vector2> currentPath;
    private int currentPathIndex;
    private float timeSinceLastPathRecalc = 0f;
    private Vector3 lastTargetPosition;
    private float nextFireTime = 0f;
    private TankHealth selfHealth;
    private string ownTag;
    private string enemyTag = "Enemy";
    private static Dictionary<CapturePoint, int> capturePointTargetCounts = new Dictionary<CapturePoint, int>();

    #endregion

    #region Méthodes de Coordination Statique
    private static void IncrementTargetCount(CapturePoint point) {
        if (point == null) return;
        if (!capturePointTargetCounts.ContainsKey(point)) capturePointTargetCounts[point] = 0;
        capturePointTargetCounts[point]++;
    }
    private static void DecrementTargetCount(CapturePoint point) {
        if (point == null) return;
        if (capturePointTargetCounts.ContainsKey(point)) {
            capturePointTargetCounts[point] = Mathf.Max(0, capturePointTargetCounts[point] - 1);
        }
    }
    private static int GetTargetCount(CapturePoint point) {
        if (point == null) return 0;
        return capturePointTargetCounts.TryGetValue(point, out int count) ? count : 0;
    }
    public static void ResetTargetCounts() {
        capturePointTargetCounts.Clear();
        Debug.Log("TEST Ally/Enemy: Compteurs de cibles de points de capture réinitialisés.");
    }
    #endregion

    #region Méthodes Unity (Awake, Start, Update, OnDestroy)

    void Awake() {
        selfHealth = GetComponent<TankHealth>();
        ownTag = gameObject.tag;
        if (canonPivot == null) Debug.LogError($"[{gameObject.name} ({ownTag})] Canon Pivot manquant", this);
        if (firePoint == null) Debug.LogError($"[{gameObject.name} ({ownTag})] Fire Point manquant", this);
        if (bulletPrefab == null) Debug.LogError($"[{gameObject.name} ({ownTag})] Bullet Prefab manquant", this);
        if (enemyLayerMask == 0) Debug.LogWarning($"[{gameObject.name} ({ownTag})] Enemy Layer Mask non défini!", this);
        if (obstacleLayer == 0) Debug.LogWarning($"[{gameObject.name} ({ownTag})] Obstacle Layer non défini", this);
    }

    void Start() {
        if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) {
            capturePointsOfInterest = FindObjectsOfType<CapturePoint>().ToList();
             Debug.Log($"TEST [{gameObject.name} ({ownTag})] Start: Trouvé {capturePointsOfInterest.Count} points de capture.");
        }
        currentState = AIState.Patrolling;
        currentNavigationTargetPosition = transform.position;
        lastTargetPosition = transform.position;
        SelectNewPatrolPoint(); // Important pour initialiser la première destination de patrouille
        Debug.Log($"TEST [{gameObject.name} ({ownTag})] Start: Initialisation terminée. Pathfinding: Dijkstra. Etat: {currentState}. Navigue vers {currentNavigationTargetPosition}");
        // Rappel: ResetTargetCounts() doit être appelé par un GameManager
    }

    void Update() {
        if (selfHealth != null && selfHealth.CurrentHealth <= 0) return;
        CheckPatrolPointReached(); // Vérifie si la destination de patrouille est atteinte
        DetectEnemies();           // Détecte les ennemis proches
        DecideNextAction();        // Choisit l'état et la cible
        HandlePathfinding();       // Calcule le chemin Dijkstra
        MoveAlongPath();           // Suit le chemin
        RotateChassisTowardsWaypoint(); // Oriente le châssis
        RotateCanonTowardsTarget();  // Oriente le canon
        HandleShooting();          // Gère le tir
    }

    void OnDestroy() {
        Debug.Log($"TEST [{gameObject.name} ({ownTag})] OnDestroy: Libération cible {currentTargetCapturePoint?.name ?? "aucune"}.");
        DecrementTargetCount(currentTargetCapturePoint);
    }

    #endregion

    #region Logique de Décision et Détection

    void DetectEnemies() {
        Collider2D[] enemiesInRange = Physics2D.OverlapCircleAll(transform.position, detectionRange, enemyLayerMask);
        Transform closestEnemy = null; float minDistance = float.MaxValue;
        foreach (Collider2D enemyCollider in enemiesInRange) {
            float distance = Vector2.Distance(transform.position, enemyCollider.transform.position);
            if (distance < minDistance) {
                TankHealth detectedEnemyHealth = enemyCollider.GetComponent<TankHealth>();
                if (detectedEnemyHealth != null && detectedEnemyHealth.CurrentHealth > 0 && enemyCollider.CompareTag(enemyTag)) {
                    minDistance = distance; closestEnemy = enemyCollider.transform;
                }
            }
        }
        if(currentTargetEnemy != closestEnemy) { Debug.Log($"TEST [{gameObject.name} ({ownTag})] DetectEnemies: Cible ENNEMIE {(closestEnemy == null ? "perdue/aucune" : "trouvée: " + closestEnemy.name)}"); }
        currentTargetEnemy = closestEnemy;
    }

    void DecideNextAction() {
        CapturePoint previousTargetCapturePoint = currentTargetCapturePoint;
        AIState previousState = currentState;
        bool targetPointChanged = false;
        bool forcePathRecalc = false;

        if (currentTargetEnemy != null) { // Priorité 1: Ennemi
            if (previousState != AIState.AttackingEnemy) {
                 Debug.Log($"TEST [{gameObject.name} ({ownTag})] DecideNextAction: -> AttackingEnemy ({currentTargetEnemy.name})");
                 currentState = AIState.AttackingEnemy;
                 if(currentTargetCapturePoint != null) targetPointChanged = true;
                 currentTargetCapturePoint = null;
                 forcePathRecalc = true;
            }
            if(currentNavigationTargetPosition != currentTargetEnemy.position) {
                 currentNavigationTargetPosition = currentTargetEnemy.position;
                 forcePathRecalc = true;
            }
        } else { // Pas d'ennemi
            CapturePoint bestCapturePoint = FindBestCapturePoint_LessTargetedFirst();
            if (bestCapturePoint != null) { // Priorité 2: Point
                 if (previousState != AIState.SeekingCapturePoint) {
                     Debug.Log($"TEST [{gameObject.name} ({ownTag})] DecideNextAction: -> SeekingCapturePoint.");
                     currentState = AIState.SeekingCapturePoint;
                     forcePathRecalc = true;
                 }
                 if (bestCapturePoint != previousTargetCapturePoint) {
                     Debug.Log($"TEST [{gameObject.name} ({ownTag})] DecideNextAction: Nouvelle cible POINT: {bestCapturePoint.pointName}");
                     currentTargetCapturePoint = bestCapturePoint;
                     currentNavigationTargetPosition = currentTargetCapturePoint.transform.position; // Cible = centre
                     targetPointChanged = true; forcePathRecalc = true;
                     Debug.Log($"TEST [{gameObject.name} ({ownTag})] DecideNextAction: Nouvelle cible NAV: {currentNavigationTargetPosition}");
                 }
            } else { // Priorité 3: Patrouille
                if (previousState != AIState.Patrolling) {
                     Debug.Log($"TEST [{gameObject.name} ({ownTag})] DecideNextAction: -> Patrolling.");
                     currentState = AIState.Patrolling;
                     if(previousTargetCapturePoint != null) targetPointChanged = true;
                     currentTargetCapturePoint = null;
                     // Si on entre en Patrouille, il faut s'assurer qu'on a une destination
                     // SelectNewPatrolPoint sera appelé par CheckPatrolPointReached si besoin,
                     // mais on force un recalcul vers la destination actuelle au cas où.
                     forcePathRecalc = true;
                }
                 // La destination (currentNavigationTargetPosition) est gérée par CheckPatrol/SelectNew
            }
        }
        if(targetPointChanged) {
            Debug.Log($"TEST [{gameObject.name} ({ownTag})] DecideNextAction: Maj compteurs -> Dec({previousTargetCapturePoint?.name ?? "null"}), Inc({currentTargetCapturePoint?.name ?? "null"})");
            DecrementTargetCount(previousTargetCapturePoint);
            IncrementTargetCount(currentTargetCapturePoint);
        }
        if (forcePathRecalc) { currentPath = null; }
        // Debug.Log($"[{gameObject.name}] Etat Final: {currentState}, Nav Target: {currentNavigationTargetPosition}");
    }

    CapturePoint FindBestCapturePoint_LessTargetedFirst() {
        if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) return null;
        var suitablePoints = capturePointsOfInterest
            .Where(point => point != null && point.controllingTeamTag != ownTag)
            .Select(point => new { Point = point, DistanceSqr = (point.transform.position - transform.position).sqrMagnitude, Targets = GetTargetCount(point) })
            .OrderBy(x => x.Targets)
            .ThenBy(x => x.DistanceSqr);
        var bestTargetInfo = suitablePoints.FirstOrDefault();
        // Debug.Log($"TEST [{gameObject.name}] FindBestCapturePoint: Choix: {bestTargetInfo?.Point.name ?? "Aucun"}");
        return bestTargetInfo?.Point;
    }

    #endregion

    #region Logique de Patrouille

    void CheckPatrolPointReached() {
        if (currentState != AIState.Patrolling) return;
        bool reached = false;
        if (Vector2.Distance(transform.position, currentNavigationTargetPosition) < patrolPointReachedDistance ||
           (currentPath == null && Vector2.Distance(transform.position, currentNavigationTargetPosition) < tileSize * 2f)) {
            reached = true;
        }
        if (reached) {
            Debug.Log($"TEST [{gameObject.name} ({ownTag})] CheckPatrolPointReached: Point de patrouille {currentNavigationTargetPosition} ATTEINT.");
            SelectNewPatrolPoint();
            currentPath = null; // Force recalcul vers nouvelle destination
        }
    }

    void SelectNewPatrolPoint() {
        bool pointFound = false; Vector2Int currentGridPos = WorldToGrid(transform.position);
        for (int i = 0; i < 30; i++) {
            int randomX = Random.Range(0, gridSize.x); int randomY = Random.Range(0, gridSize.y);
            Vector2Int randomGridPos = new Vector2Int(randomX, randomY);
            if (IsWalkable(randomGridPos) && randomGridPos != currentGridPos) {
                currentNavigationTargetPosition = GridToWorld(randomGridPos);
                lastTargetPosition = currentNavigationTargetPosition; // Mémorise pour HandlePathfinding
                pointFound = true;
                Debug.Log($"TEST [{gameObject.name} ({ownTag})] SelectNewPatrolPoint: Nouvelle destination: {currentNavigationTargetPosition} (Grille: {randomGridPos})");
                break;
            }
        }
        if (!pointFound) {
            Debug.LogWarning($"TEST [{gameObject.name} ({ownTag})] SelectNewPatrolPoint: Impossible de trouver point valide. Passage en Idle.");
            currentState = AIState.Idle; // Fallback Idle
            currentNavigationTargetPosition = transform.position; // Reste sur place
        }
    }
    #endregion

    #region Pathfinding (Uniquement Dijkstra)

    void HandlePathfinding() {
        timeSinceLastPathRecalc += Time.deltaTime;
        bool hasTarget = (currentState != AIState.Idle);

        if (!hasTarget) { currentPath = null; return; }

        Vector2Int startGridPos = WorldToGrid(transform.position);
        Vector2Int endGridPos = WorldToGrid(currentNavigationTargetPosition);

        if (startGridPos == endGridPos && currentState != AIState.Patrolling) {
             // Debug.LogWarning($"TEST [{gameObject.name} ({ownTag})] HandlePathfinding: Déjà dans la case cible {endGridPos}. Chemin annulé.");
            currentPath = null;
            return;
        }

        bool targetPosChanged = Vector3.Distance(currentNavigationTargetPosition, lastTargetPosition) > 0.1f;
        bool timerElapsed = timeSinceLastPathRecalc >= pathRecalculationRate;
        bool pathInvalid = currentPath == null || currentPath.Count == 0;
        bool needsRecalc = pathInvalid || timerElapsed || (currentState == AIState.AttackingEnemy && targetPosChanged) || (currentState == AIState.Patrolling && targetPosChanged);

        if (needsRecalc && startGridPos != endGridPos) {
            // Debug.Log($"TEST [{gameObject.name} ({ownTag})] HandlePathfinding: Recalcul Dijkstra demandé...");
            FindPath_Dijkstra(currentNavigationTargetPosition, startGridPos, endGridPos);
            timeSinceLastPathRecalc = 0f;
            lastTargetPosition = currentNavigationTargetPosition;
        }
    }

    void FindPath_Dijkstra(Vector3 targetNavPosition, Vector2Int startGridPos, Vector2Int endGridPos) {
        currentPath = Dijkstra(startGridPos, endGridPos); // Appel Dijkstra

        if (currentPath == null || currentPath.Count == 0) {
             if (startGridPos != endGridPos) {
                 Debug.LogWarning($"TEST [{gameObject.name} ({ownTag})] FindPath Dijkstra: ECHEC Pathfinding de {startGridPos} vers {endGridPos}");
             }
        } else {
            // Debug.Log($"TEST [{gameObject.name} ({ownTag})] FindPath Dijkstra: REUSSI. Chemin {currentPath.Count} points.");
        }

        if (currentPath != null && currentPath.Count > 0 && Vector2.Distance(transform.position, currentPath[0]) < tileSize * 0.1f) {
            currentPath.RemoveAt(0);
        }
        currentPathIndex = 0;
    }

    // --- Algorithme Dijkstra et Fonctions Helpers ---
    List<Vector2> Dijkstra(Vector2Int start, Vector2Int end) {
        if (!IsWithinBounds(start) || !IsWalkable(start)) { return null; }
        if (!IsWithinBounds(end) || !IsWalkable(end)) { return null; }
        var frontier = new PriorityQueue<Vector2Int>(); frontier.Enqueue(start, 0);
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>{ [start] = start };
        var costSoFar = new Dictionary<Vector2Int, float>{ [start] = 0 };
        while (frontier.Count > 0) {
            var current = frontier.Dequeue();
            if (current == end) { return ReconstructPath(cameFrom, end); }
            foreach (var next in GetNeighbors(current)) {
                float moveCost = Vector2.Distance(current, next);
                float newCost = costSoFar[current] + moveCost;
                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next]) {
                    costSoFar[next] = newCost;
                    float priority = newCost; // Priorité Dijkstra
                    frontier.Enqueue(next, priority);
                    cameFrom[next] = current;
                }
            }
        }
        return null;
    }

    List<Vector2> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) {
        var totalPath = new List<Vector2>();
        if (!cameFrom.ContainsKey(current)) { Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Nœud cible non trouvé."); return totalPath; }
        Vector2Int startNode = cameFrom.FirstOrDefault(kvp => kvp.Key == kvp.Value).Key;
        Vector2Int step = current; int safety = 0; const int maxSteps = 10000;
        while (step != startNode && safety < maxSteps) {
            totalPath.Add(GridToWorld(step));
            if (!cameFrom.ContainsKey(step)) { Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Nœud {step} manquant."); break; }
            step = cameFrom[step]; safety++;
        }
        if(safety >= maxSteps) Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Limite de sécurité atteinte!");
        totalPath.Reverse(); return totalPath;
    }

    List<Vector2Int> GetNeighbors(Vector2Int position) {
        var neighbors = new List<Vector2Int>();
        for (int x = -1; x <= 1; x++) { for (int y = -1; y <= 1; y++) {
            if (x == 0 && y == 0) continue;
            Vector2Int neighborPos = new Vector2Int(position.x + x, position.y + y);
            if (!IsWithinBounds(neighborPos) || !IsWalkable(neighborPos)) continue;
            if (Mathf.Abs(x) + Mathf.Abs(y) == 2) { // Diagonale
                Vector2Int n1 = new Vector2Int(position.x + x, position.y); Vector2Int n2 = new Vector2Int(position.x, position.y + y);
                if (!IsWalkable(n1) || !IsWalkable(n2)) continue;
            } neighbors.Add(neighborPos);
        } } return neighbors;
    }

    bool IsWithinBounds(Vector2Int gridPosition) => gridPosition.x >= 0 && gridPosition.x < gridSize.x && gridPosition.y >= 0 && gridPosition.y < gridSize.y;

    bool IsWalkable(Vector2Int gridPosition) {
        Vector2 worldPosition = GridToWorld(gridPosition); float checkRadius = tileSize / 2f * 0.9f;
        return Physics2D.OverlapCircle(worldPosition, checkRadius, obstacleLayer) == null;
    }

    Vector2Int WorldToGrid(Vector2 worldPosition) {
        int x = Mathf.FloorToInt(worldPosition.x / tileSize); int y = Mathf.FloorToInt(worldPosition.y / tileSize);
        x = Mathf.Clamp(x, 0, gridSize.x - 1); y = Mathf.Clamp(y, 0, gridSize.y - 1); return new Vector2Int(x, y);
    }

    Vector2 GridToWorld(Vector2Int gridPosition) {
        float x = gridPosition.x * tileSize + tileSize / 2f; float y = gridPosition.y * tileSize + tileSize / 2f; return new Vector2(x, y);
    }

    // --- Classe Interne PriorityQueue ---
    public class PriorityQueue<T> {
        private List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();
        public int Count => elements.Count;
        public void Enqueue(T item, float priority) {
            elements.Add(new KeyValuePair<T, float>(item, priority));
            elements.Sort((pair1, pair2) => pair1.Value.CompareTo(pair2.Value));
        }
        public T Dequeue() {
            if (elements.Count == 0) throw new System.InvalidOperationException("Priority queue is empty");
            var item = elements[0].Key; elements.RemoveAt(0); return item;
        }
        public bool IsEmpty => elements.Count == 0;
    }
    // --- Fin Pathfinding ---

    #endregion

    #region Mouvement & Rotation

    void MoveAlongPath() {
         if (currentPath == null || currentPathIndex >= currentPath.Count) return;
         // Debug.Log($"TEST [{gameObject.name}] MoveAlongPath: Vers waypoint {currentPathIndex + 1}/{currentPath.Count}");
         Vector2 targetWaypoint = currentPath[currentPathIndex];
         transform.position = Vector2.MoveTowards(transform.position, targetWaypoint, moveSpeed * Time.deltaTime);
         if (Vector2.Distance(transform.position, targetWaypoint) < 0.1f) {
             currentPathIndex++;
             if (currentPathIndex >= currentPath.Count) {
                  // Debug.Log($"TEST [{gameObject.name}] MoveAlongPath: Chemin terminé.");
                  currentPath = null; // Efface le chemin une fois terminé
             }
         }
     }

     void RotateChassisTowardsWaypoint() {
         if (currentPath == null || currentPathIndex >= currentPath.Count) return;
         Vector2 targetWaypoint = currentPath[currentPathIndex];
         Vector2 direction = (targetWaypoint - (Vector2)transform.position).normalized;
         if (direction != Vector2.zero) {
             float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
             Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
             transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, chassisRotationSpeed * Time.deltaTime);
         }
     }

     // Fonction sans erreur de duplication
     void RotateCanonTowardsTarget() {
        if (canonPivot == null) return;
        Vector2 direction = Vector2.zero; bool hasAimTarget = false;
        if (currentState == AIState.AttackingEnemy && currentTargetEnemy != null) {
            direction = ((Vector2)currentTargetEnemy.position - (Vector2)canonPivot.position).normalized; hasAimTarget = true;
        } else { direction = transform.up; hasAimTarget = true; } // Vise devant sinon
        if (hasAimTarget && direction != Vector2.zero) {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
            canonPivot.rotation = Quaternion.RotateTowards(canonPivot.rotation, targetRotation, canonRotationSpeed * Time.deltaTime);
        }
    }
    #endregion

    #region Combat (Tir)

    void HandleShooting() {
        if (currentState != AIState.AttackingEnemy || currentTargetEnemy == null) return;
        float distanceToEnemy = Vector2.Distance(transform.position, currentTargetEnemy.position);
        if (distanceToEnemy <= shootingRange && Time.time >= nextFireTime) {
            // Debug.Log($"TEST [{gameObject.name} ({ownTag})] HandleShooting: TIR sur ENNEMI {currentTargetEnemy.name}");
            Shoot(); nextFireTime = Time.time + fireRate;
        }
    }

     void Shoot() {
        if (bulletPrefab == null || firePoint == null) return;
        GameObject bulletInstance = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        BulletDestruction bulletScript = bulletInstance.GetComponent<BulletDestruction>();
        if (bulletScript != null) {
            bulletScript.shooter = this.gameObject; bulletScript.shooterTag = this.gameObject.tag;
        } else { Debug.LogError($"Prefab balle '{bulletPrefab.name}' manque script BulletDestruction!", bulletPrefab); }
    }
    #endregion
}