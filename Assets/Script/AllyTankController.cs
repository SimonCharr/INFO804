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

    [Header("Pathfinding (A*)")]
    public Vector2Int gridSize = new Vector2Int(30, 30);
    public float tileSize = 1f;
    public LayerMask obstacleLayer;
    public float pathRecalculationRate = 0.75f;

    [Header("Comportement IA")]
    public List<CapturePoint> capturePointsOfInterest;
    [Tooltip("Distance pour considérer un point de patrouille atteint")]
    public float patrolPointReachedDistance = 1.5f;
    [Tooltip("Temps minimum entre changements d'état pour éviter oscillations")]
    public float minStateChangeInterval = 1.5f;
    [Tooltip("Temps minimum entre changements de point de patrouille")]
    public float minPatrolPointChangeInterval = 4.0f;
    [Tooltip("Seuil de mouvement pour considérer une cible comme 'déplacée'")]
    public float targetMovementThreshold = 0.3f;

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
    // Suppression de la coordination par comptage
    private float lastStateChangeTime = -10f;
    private float lastPatrolPointChangeTime = -10f;

    #endregion

    #region Méthodes de Coordination Statique (Vide)
    // Vide car la coordination par comptage a été retirée pour simplifier
    public static void ResetTargetCounts() {
        Debug.Log("TEST Ally/Enemy: ResetTargetCounts appelé (Logique de comptage désactivée).");
    }
    #endregion

    #region Méthodes Unity (Awake, Start, Update, OnDestroy)

    // Défini UNE SEULE FOIS
    void Awake() {
        selfHealth = GetComponent<TankHealth>();
        ownTag = gameObject.tag;
        // Vérifications
        if (canonPivot == null) Debug.LogError($"[{gameObject.name} ({ownTag})] Canon Pivot manquant", this);
        if (firePoint == null) Debug.LogError($"[{gameObject.name} ({ownTag})] Fire Point manquant", this);
        if (bulletPrefab == null) Debug.LogError($"[{gameObject.name} ({ownTag})] Bullet Prefab manquant", this);
        if (enemyLayerMask == 0) Debug.LogWarning($"[{gameObject.name} ({ownTag})] Enemy Layer Mask non défini!", this);
        if (obstacleLayer == 0) Debug.LogWarning($"[{gameObject.name} ({ownTag})] Obstacle Layer non défini", this);
    }

    // Défini UNE SEULE FOIS
    void Start() {
        if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) {
            capturePointsOfInterest = FindObjectsOfType<CapturePoint>().ToList();
             Debug.Log($"TEST [{gameObject.name} ({ownTag})] Start: Trouvé {capturePointsOfInterest.Count} points de capture.");
        }
        currentState = AIState.Patrolling;
        currentNavigationTargetPosition = transform.position;
        lastTargetPosition = transform.position;
        lastStateChangeTime = Time.time;
        lastPatrolPointChangeTime = Time.time - minPatrolPointChangeInterval;
        SelectNewPatrolPoint();
        Debug.Log($"TEST [{gameObject.name} ({ownTag})] Start: Init terminée. Pathfinding: A*. Etat: {currentState}. Navigue vers {currentNavigationTargetPosition}");
    }

    // Défini UNE SEULE FOIS
    void Update() {
        if (selfHealth != null && selfHealth.CurrentHealth <= 0) return;

        CheckPatrolPointReached();
        DetectEnemies();
        DecideNextAction();
        HandlePathfinding();
        MoveAlongPath();
        RotateChassisTowardsWaypoint();
        RotateCanonTowardsTarget();
        HandleShooting();
        DrawPath();
    }

    // Défini UNE SEULE FOIS
    void OnDestroy() {
        // Plus de DecrementTargetCount à faire
        Debug.Log($"TEST [{gameObject.name} ({ownTag})] OnDestroy.");
    }

    #endregion

    #region Logique de Décision et Détection

    // Défini UNE SEULE FOIS
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

    // Défini UNE SEULE FOIS (Version Simplifiée "Têtue")
    void DecideNextAction() {
        AIState previousState = currentState;
        CapturePoint previousTargetCapturePoint = currentTargetCapturePoint; // Pour détecter changement
        bool forcePathRecalc = false;
        bool stateChanged = false;

        bool canChangeNonAttackState = Time.time - lastStateChangeTime >= minStateChangeInterval;

        // 1. Priorité : Attaquer
        if (currentTargetEnemy != null) {
            if (currentState != AIState.AttackingEnemy) {
                Debug.Log($"TEST [{gameObject.name} ({ownTag})] STATE CHANGE: {previousState} -> AttackingEnemy ({currentTargetEnemy.name})");
                currentState = AIState.AttackingEnemy; stateChanged = true;
                lastStateChangeTime = Time.time;
                currentTargetCapturePoint = null; // Oublie le point
                forcePathRecalc = true;
            }
            if (Vector3.Distance(currentNavigationTargetPosition, currentTargetEnemy.position) > targetMovementThreshold) {
                currentNavigationTargetPosition = currentTargetEnemy.position;
                forcePathRecalc = true;
            }
        }
        // 2. Pas d'ennemi : Vérifier cible capture actuelle ou chercher la plus proche
        else {
            bool currentPointIsValid = false;
            if (currentTargetCapturePoint != null && currentTargetCapturePoint.controllingTeamTag != ownTag) {
                currentPointIsValid = true;
            }

            if (currentPointIsValid && currentState == AIState.SeekingCapturePoint) {
                 // Garde la cible actuelle, vérifie juste la destination NAV
                 if (currentNavigationTargetPosition != currentTargetCapturePoint.transform.position) {
                    currentNavigationTargetPosition = currentTargetCapturePoint.transform.position;
                    forcePathRecalc = true;
                }
            }
            else { // Pas de cible valide ou pas en Seeking
                CapturePoint closestValidPoint = FindClosestCapturePoint();
                if (closestValidPoint != null) { // Un point valide existe
                    // Change seulement si on peut OU si l'état était différent OU si le point le plus proche a changé
                    if (canChangeNonAttackState || currentState != AIState.SeekingCapturePoint || closestValidPoint != currentTargetCapturePoint) {
                        if (currentState != AIState.SeekingCapturePoint || closestValidPoint != currentTargetCapturePoint) { // Logique de changement
                            Debug.Log($"TEST [{gameObject.name} ({ownTag})] STATE/TARGET CHANGE: {previousState} -> SeekingCapturePoint vers {closestValidPoint.pointName}.");
                            currentState = AIState.SeekingCapturePoint; stateChanged = true;
                            lastStateChangeTime = Time.time;
                            currentTargetCapturePoint = closestValidPoint;
                            currentNavigationTargetPosition = currentTargetCapturePoint.transform.position;
                            forcePathRecalc = true;
                        }
                    }
                    // Si on ne peut pas changer d'état, on reste où on est
                }
                // 3. Pas d'ennemi et aucun point valide : Patrouille
                else {
                    if (currentState != AIState.Patrolling) {
                         if (canChangeNonAttackState || previousState == AIState.Idle) {
                            Debug.Log($"TEST [{gameObject.name} ({ownTag})] STATE CHANGE: {previousState} -> Patrolling.");
                            currentState = AIState.Patrolling; stateChanged = true;
                            lastStateChangeTime = Time.time;
                            currentTargetCapturePoint = null;
                            if (stateChanged) { SelectNewPatrolPoint(); }
                            forcePathRecalc = true;
                        }
                    }
                }
            }
        }
        if (forcePathRecalc) { currentPath = null; }
    }


    // Défini UNE SEULE FOIS (Version Simple "Plus Proche")
    CapturePoint FindClosestCapturePoint() {
        if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) return null;
        CapturePoint closestPoint = null;
        float minDistanceSqr = float.MaxValue;
        foreach(CapturePoint point in capturePointsOfInterest) {
            if (point != null && point.controllingTeamTag != ownTag) {
                float distSqr = (point.transform.position - transform.position).sqrMagnitude;
                if (distSqr < minDistanceSqr) {
                    minDistanceSqr = distSqr;
                    closestPoint = point;
                }
            }
        }
        return closestPoint;
    }

    #endregion

    #region Logique de Patrouille

    // Défini UNE SEULE FOIS
    void CheckPatrolPointReached() {
        if (currentState != AIState.Patrolling) return;
        bool reached = false;
        if (Vector2.Distance(transform.position, currentNavigationTargetPosition) < patrolPointReachedDistance) {
             reached = true;
        }
        if (reached && Time.time - lastPatrolPointChangeTime >= minPatrolPointChangeInterval) {
            Debug.Log($"TEST [{gameObject.name} ({ownTag})] CheckPatrolPointReached: Point de patrouille {currentNavigationTargetPosition} ATTEINT.");
            SelectNewPatrolPoint();
            currentPath = null;
            lastPatrolPointChangeTime = Time.time;
        }
    }

    // Défini UNE SEULE FOIS
    void SelectNewPatrolPoint() {
        bool pointFound = false; Vector2Int currentGridPos = WorldToGrid(transform.position);
        Vector3 newTarget = transform.position;
        float minPatrolDistSqr = (patrolPointReachedDistance * 2.5f) * (patrolPointReachedDistance * 2.5f);
        for (int i = 0; i < 30; i++) {
            int randomX = Random.Range(0, gridSize.x); int randomY = Random.Range(0, gridSize.y);
            Vector2Int randomGridPos = new Vector2Int(randomX, randomY);
            if (IsWalkable(randomGridPos) && randomGridPos != currentGridPos) {
                 Vector3 potentialTarget = GridToWorld(randomGridPos);
                 if((potentialTarget - currentNavigationTargetPosition).sqrMagnitude > minPatrolDistSqr) {
                    newTarget = potentialTarget; pointFound = true;
                    Debug.Log($"TEST [{gameObject.name} ({ownTag})] SelectNewPatrolPoint: Nouvelle destination: {newTarget} (Grille: {randomGridPos})");
                    break;
                 }
            }
        }
        if (!pointFound) {
            Debug.LogWarning($"TEST [{gameObject.name} ({ownTag})] SelectNewPatrolPoint: Impossible de trouver point valide. Passage en Idle.");
            currentState = AIState.Idle;
            newTarget = transform.position;
        }
        currentNavigationTargetPosition = newTarget;
        lastTargetPosition = currentNavigationTargetPosition; // Mémorise pour HandlePathfinding
        currentPath = null; // Invalide chemin actuel
    }
    #endregion

    #region Pathfinding (A*)

    // Défini UNE SEULE FOIS
    void HandlePathfinding() {
        timeSinceLastPathRecalc += Time.deltaTime;
        bool hasTarget = (currentState != AIState.Idle);

        if (!hasTarget) { currentPath = null; return; }

        Vector2Int startGridPos = WorldToGrid(transform.position);
        Vector2Int endGridPos = WorldToGrid(currentNavigationTargetPosition);

        // Si déjà dans la case cible ET PAS en patrouille
        if (startGridPos == endGridPos && currentState != AIState.Patrolling) {
            if(currentPath != null) { currentPath = null; } // Vide le chemin si on vient d'arriver
            return;
        }

        // Logique de recalcul
        bool targetPosChanged = Vector3.Distance(currentNavigationTargetPosition, lastTargetPosition) > targetMovementThreshold;
        bool timerElapsed = timeSinceLastPathRecalc >= pathRecalculationRate;
        bool pathInvalid = currentPath == null || currentPath.Count == 0;
        bool needsRecalc = pathInvalid || timerElapsed || (currentState == AIState.AttackingEnemy && targetPosChanged) || (currentState != AIState.AttackingEnemy && targetPosChanged);

        if (needsRecalc && startGridPos != endGridPos) { // Ne recalcule que si start != end
            // string reason = $"Invalid:{pathInvalid}, Timer:{timerElapsed}, TargetMoved:{targetPosChanged}";
            // Debug.Log($"TEST [{gameObject.name} ({ownTag})] HandlePathfinding: Recalcul A* demandé. Raison(s): ({reason})");
            FindPath_AStar(currentNavigationTargetPosition, startGridPos, endGridPos); // Appel A*
            timeSinceLastPathRecalc = 0f;
            lastTargetPosition = currentNavigationTargetPosition;
        }
    }

    // Défini UNE SEULE FOIS
    void FindPath_AStar(Vector3 targetNavPosition, Vector2Int startGridPos, Vector2Int endGridPos) {
        List<Vector2> rawPath = AStarSearch(startGridPos, endGridPos); // Chemin brut A*

        if (rawPath != null && rawPath.Count > 0) {
             currentPath = ApplyPathOffset(rawPath); // Applique le décalage
             // Debug.Log($"TEST [{gameObject.name} ({ownTag})] FindPath A*: REUSSI + OFFSET. Chemin {currentPath.Count} points.");
        } else {
             currentPath = null;
             if (startGridPos != endGridPos) {
                 Debug.LogWarning($"TEST [{gameObject.name} ({ownTag})] FindPath A*: ECHEC Pathfinding de {startGridPos} vers {endGridPos}");
             }
        }

        if (currentPath != null && currentPath.Count > 0 && Vector2.Distance(transform.position, currentPath[0]) < tileSize * 0.15f) {
            currentPath.RemoveAt(0);
        }
        currentPathIndex = 0; // Commence au début du chemin
    }

    // --- Algorithme A* et Helpers ---
    // Défini UNE SEULE FOIS
    float Heuristic(Vector2Int a, Vector2Int b) {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    // Défini UNE SEULE FOIS
    List<Vector2> AStarSearch(Vector2Int start, Vector2Int end) {
        if (!IsWithinBounds(start) || !IsWalkable(start)) { return null; }
        if (!IsWithinBounds(end) || !IsWalkable(end)) { Debug.LogWarning($"A* Target node {end} is not walkable or out of bounds!"); return null; } // Ajout warning si cible invalide
        var frontier = new PriorityQueue<Vector2Int>();
        frontier.Enqueue(start, Heuristic(start, end));
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
                    float priority = newCost + Heuristic(next, end);
                    frontier.Enqueue(next, priority);
                    cameFrom[next] = current;
                }
            }
        }
        return null;
    }

    // Défini UNE SEULE FOIS
    List<Vector2> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) {
        var totalPath = new List<Vector2>();
        if (!cameFrom.ContainsKey(current)) { Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Nœud cible {current} non trouvé dans cameFrom."); return totalPath; }
        Vector2Int startNode = cameFrom.FirstOrDefault(kvp => kvp.Key == kvp.Value).Key; // Devrait être la clé où la valeur est elle-même
        // Fallback si le startNode n'est pas trouvé (devrait pas arriver si start est dans cameFrom)
        if(!cameFrom.ContainsKey(startNode) && cameFrom.Count > 0) startNode = cameFrom.Keys.First(); // Prend une clé au hasard? Non, mauvaise idée.
                                                                                                    // Le startNode est celui qui a été mis avec lui-même comme valeur.

        Vector2Int step = current; int safety = 0; const int maxSteps = 10000;
        while (step != startNode && safety < maxSteps) {
            totalPath.Add(GridToWorld(step));
            if (!cameFrom.ContainsKey(step)) { Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Nœud {step} manquant."); break; }
             // Vérifie si la clé existe avant d'y accéder pour éviter boucle infinie si startNode est mal identifié
             if (cameFrom.ContainsKey(step)) {
                 step = cameFrom[step];
             } else {
                 Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Clé {step} non trouvée dans cameFrom, arrêt.");
                 break; // Arrête pour éviter une boucle infinie
             }
            safety++;
        }
        if(safety >= maxSteps) Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Limite de sécurité atteinte!");
        totalPath.Reverse(); return totalPath;
    }

    // Défini UNE SEULE FOIS
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

    // Défini UNE SEULE FOIS
    bool IsWithinBounds(Vector2Int gridPosition) => gridPosition.x >= 0 && gridPosition.x < gridSize.x && gridPosition.y >= 0 && gridPosition.y < gridSize.y;

    // Défini UNE SEULE FOIS (avec log de debug)
     bool IsWalkable(Vector2Int gridPosition) {
        Vector2 worldPosition = GridToWorld(gridPosition); float checkRadius = tileSize / 2f * 0.9f;
        Collider2D hit = Physics2D.OverlapCircle(worldPosition, checkRadius, obstacleLayer);
        if (hit != null) {
             Debug.LogWarning($"TEST IsWalkable: Case {gridPosition} considérée OBSTACLE à cause de '{hit.name}' sur layer '{LayerMask.LayerToName(hit.gameObject.layer)}'");
        }
        return hit == null;
    }

    // Défini UNE SEULE FOIS
    Vector2Int WorldToGrid(Vector2 worldPosition) {
        int x = Mathf.FloorToInt(worldPosition.x / tileSize); int y = Mathf.FloorToInt(worldPosition.y / tileSize);
        x = Mathf.Clamp(x, 0, gridSize.x - 1); y = Mathf.Clamp(y, 0, gridSize.y - 1); return new Vector2Int(x, y);
    }

    // Défini UNE SEULE FOIS
    Vector2 GridToWorld(Vector2Int gridPosition) {
        float x = gridPosition.x * tileSize + tileSize / 2f; float y = gridPosition.y * tileSize + tileSize / 2f; return new Vector2(x, y);
    }

    // --- Classe Interne PriorityQueue (définie UNE SEULE FOIS) ---
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
    // --- Fin Pathfinding Helpers ---

    #endregion

    #region Pathfinding Helpers (Offset + LOS)

    // Défini UNE SEULE FOIS
    private List<Vector2> ApplyPathOffset(List<Vector2> originalPath) {
        if (originalPath == null || originalPath.Count < 2) return originalPath;
        float baseOffset = 0.2f * tileSize;
        float offsetDirection = (gameObject.GetInstanceID() % 2 == 0) ? 1f : -1f;
        float finalOffset = baseOffset * offsetDirection;
        List<Vector2> offsetPath = new List<Vector2>(originalPath.Count);
        Vector2 startDir = (originalPath[0] - (Vector2)transform.position).normalized;
        if (startDir == Vector2.zero && originalPath.Count > 1) startDir = (originalPath[1] - originalPath[0]).normalized;
        if (startDir == Vector2.zero) startDir = transform.up;
        Vector2 prevPerp = new Vector2(-startDir.y, startDir.x);
        offsetPath.Add(originalPath[0] + prevPerp * finalOffset);
        for (int i = 1; i < originalPath.Count; i++) {
            Vector2 p_prev = originalPath[i - 1]; Vector2 p_curr = originalPath[i];
            Vector2 dir_in = (p_curr - p_prev).normalized;
             if (dir_in == Vector2.zero && i > 0) dir_in = (p_curr - originalPath[i-1]).normalized;
             if (dir_in == Vector2.zero) dir_in = transform.up;
            Vector2 perp_in = new Vector2(-dir_in.y, dir_in.x);
            Vector2 offsetPos = p_curr + perp_in * finalOffset;
            offsetPath.Add(offsetPos);
        }
        return offsetPath;
    }

    // Défini UNE SEULE FOIS
     bool IsDirectPathClear(Vector2 start, Vector2 end) {
       Vector2 direction = end - start; float distance = direction.magnitude;
       if (distance < 0.1f) return true;
       int layerMask = obstacleLayer;
       RaycastHit2D hit = Physics2D.Raycast(start, direction.normalized, distance, layerMask);
       if (hit.collider != null) {
            Transform targetTransform = null;
            if (currentState == AIState.AttackingEnemy && currentTargetEnemy != null) targetTransform = currentTargetEnemy;
            // Si on a touché qq chose et que ce n'est PAS notre cible ennemie -> pas clair
            if (targetTransform != null && hit.transform != targetTransform) return false;
            // Si on n'a pas de cible ennemie définie et qu'on touche qq chose -> pas clair
            if(targetTransform == null) return false;
       }
       // Si rien touché ou si on a touché la cible elle-même -> clair
       return true;
     }

    #endregion

    #region Mouvement & Rotation

     // Défini UNE SEULE FOIS
     void MoveAlongPath() {
         if (currentPath == null || currentPathIndex >= currentPath.Count) return;
         Vector2 targetWaypoint = currentPath[currentPathIndex];
         transform.position = Vector2.MoveTowards(transform.position, targetWaypoint, moveSpeed * Time.deltaTime);
         if (Vector2.Distance(transform.position, targetWaypoint) < 0.1f) {
             currentPathIndex++;
             if (currentPathIndex >= currentPath.Count) { currentPath = null; }
         }
     }

     // Défini UNE SEULE FOIS
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

    // Défini UNE SEULE FOIS
    void RotateCanonTowardsTarget() {
        if (canonPivot == null) return;
        Vector2 direction = Vector2.zero; bool hasAimTarget = false;
        if (currentState == AIState.AttackingEnemy && currentTargetEnemy != null) {
            direction = ((Vector2)currentTargetEnemy.position - (Vector2)canonPivot.position).normalized; hasAimTarget = true;
        } else { direction = transform.up; hasAimTarget = true; }
        if (hasAimTarget && direction != Vector2.zero) {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
            canonPivot.rotation = Quaternion.RotateTowards(canonPivot.rotation, targetRotation, canonRotationSpeed * Time.deltaTime);
        }
    }
    #endregion

    #region Combat (Tir)

    // Défini UNE SEULE FOIS
    void HandleShooting() {
        if (currentState != AIState.AttackingEnemy || currentTargetEnemy == null) return;
        float distanceToEnemy = Vector2.Distance(transform.position, currentTargetEnemy.position);
        bool hasLineOfSight = IsDirectPathClear(firePoint.position, currentTargetEnemy.position);
        if (distanceToEnemy <= shootingRange && hasLineOfSight && Time.time >= nextFireTime) {
            Shoot(); nextFireTime = Time.time + fireRate;
        }
    }

     // Défini UNE SEULE FOIS
     void Shoot() {
        if (bulletPrefab == null || firePoint == null) return;
        GameObject bulletInstance = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        BulletDestruction bulletScript = bulletInstance.GetComponent<BulletDestruction>();
        if (bulletScript != null) {
            bulletScript.shooter = this.gameObject; bulletScript.shooterTag = this.gameObject.tag;
        } else { Debug.LogError($"Prefab balle '{bulletPrefab.name}' utilisé par {gameObject.name} ({ownTag}) n'a pas le script 'BulletDestruction'!", bulletPrefab); }
    }
    #endregion

    #region Visualisation du Chemin (Debug)
    // Défini UNE SEULE FOIS
    void DrawPath() {
        if (currentPath != null && currentPath.Count > 0) {
            Color pathColor = Color.green;
             // Vérifie si l'index est valide avant d'accéder au chemin
             if (currentPathIndex < currentPath.Count) {
                 Debug.DrawLine(transform.position, currentPath[currentPathIndex], pathColor);
                 for (int i = currentPathIndex; i < currentPath.Count - 1; i++) {
                     Debug.DrawLine(currentPath[i], currentPath[i+1], pathColor);
                 }
             } else if (currentPath.Count > 0) { // Si index dépasse mais chemin existe (on vient d'arriver?)
                 Debug.DrawLine(transform.position, currentPath[currentPath.Count-1], pathColor * 0.5f); // Ligne estompée vers dernier point
             }
        }
         if (currentState != AIState.Idle) {
             Debug.DrawLine(transform.position, currentNavigationTargetPosition, Color.yellow); // Jaune pour la destination NAV
         }
    }
    #endregion
}

// --- Classe PriorityQueue (Doit être définie UNE SEULE FOIS dans votre projet) ---
// Si vous l'avez déjà dans un autre fichier .cs, SUPPRIMEZ cette définition ci-dessous.
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
// --- Fin Classe PriorityQueue ---