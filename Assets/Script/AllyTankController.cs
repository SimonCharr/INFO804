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
    // targetCountPenaltyWeight Supprimé
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
    private CapturePoint currentTargetCapturePoint = null; // Cible point ACTUELLE et CONSERVÉE
    private Vector3 currentNavigationTargetPosition;
    private List<Vector2> currentPath;
    private int currentPathIndex;
    private float timeSinceLastPathRecalc = 0f;
    private Vector3 lastTargetPosition; // Mémorise la cible NAV du dernier calcul
    private float nextFireTime = 0f;
    private TankHealth selfHealth;
    private string ownTag; // Sera "Ally" ou "Player"
    private string enemyTag = "Enemy";
    // Plus de Dictionnaire statique de coordination
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
        lastPatrolPointChangeTime = Time.time - minPatrolPointChangeInterval; // Permet choix immédiat
        SelectNewPatrolPoint(); // Sélectionne la 1ère destination de patrouille
        Debug.Log($"TEST [{gameObject.name} ({ownTag})] Start: Init terminée. Pathfinding: A*. Etat: {currentState}. Navigue vers {currentNavigationTargetPosition}");
        // Rappel: ResetTargetCounts() doit être appelé par un GameManager
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
        // Plus rien à faire sur les compteurs
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
        CapturePoint previousTargetCapturePoint = currentTargetCapturePoint;
        bool forcePathRecalc = false;
        bool stateChanged = false;

        bool canChangeNonAttackState = Time.time - lastStateChangeTime >= minStateChangeInterval;

        // 1. Priorité : Attaquer
        if (currentTargetEnemy != null) {
            if (currentState != AIState.AttackingEnemy) {
                Debug.Log($"TEST [{gameObject.name} ({ownTag})] STATE CHANGE: {previousState} -> AttackingEnemy ({currentTargetEnemy.name})");
                currentState = AIState.AttackingEnemy; stateChanged = true;
                lastStateChangeTime = Time.time;
                currentTargetCapturePoint = null; // Oublie le point logique
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
                 if (currentNavigationTargetPosition != currentTargetCapturePoint.transform.position) {
                    currentNavigationTargetPosition = currentTargetCapturePoint.transform.position;
                    forcePathRecalc = true;
                }
            }
            else {
                CapturePoint closestValidPoint = FindClosestCapturePoint();
                if (closestValidPoint != null) {
                    if (canChangeNonAttackState || currentState != AIState.SeekingCapturePoint || closestValidPoint != currentTargetCapturePoint) {
                         if (currentState != AIState.SeekingCapturePoint || closestValidPoint != currentTargetCapturePoint) {
                            Debug.Log($"TEST [{gameObject.name} ({ownTag})] STATE/TARGET CHANGE: {previousState} -> SeekingCapturePoint vers {closestValidPoint.pointName}.");
                            currentState = AIState.SeekingCapturePoint; stateChanged = true;
                            lastStateChangeTime = Time.time;
                            currentTargetCapturePoint = closestValidPoint;
                            currentNavigationTargetPosition = currentTargetCapturePoint.transform.position;
                            forcePathRecalc = true;
                        }
                    }
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
        lastTargetPosition = currentNavigationTargetPosition;
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
        if (startGridPos == endGridPos && currentState != AIState.Patrolling) {
            if(currentPath != null) { currentPath = null; }
            return;
        }
        bool targetPosChanged = Vector3.Distance(currentNavigationTargetPosition, lastTargetPosition) > targetMovementThreshold;
        bool timerElapsed = timeSinceLastPathRecalc >= pathRecalculationRate;
        bool pathInvalid = currentPath == null || currentPath.Count == 0;
        bool needsRecalc = pathInvalid || timerElapsed || (currentState == AIState.AttackingEnemy && targetPosChanged) || (currentState != AIState.AttackingEnemy && targetPosChanged);
        if (needsRecalc && startGridPos != endGridPos) {
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
        } else {
             currentPath = null;
             if (startGridPos != endGridPos) { Debug.LogWarning($"TEST [{gameObject.name} ({ownTag})] FindPath A*: ECHEC Pathfinding de {startGridPos} vers {endGridPos}"); }
        }
        if (currentPath != null && currentPath.Count > 0 && Vector2.Distance(transform.position, currentPath[0]) < tileSize * 0.15f) {
            currentPath.RemoveAt(0);
        }
        currentPathIndex = 0;
    }

    // --- Algorithme A* et Helpers ---
    // Défini UNE SEULE FOIS
    float Heuristic(Vector2Int a, Vector2Int b) {
        // Commentaire: Heuristique de Manhattan (distance L1) - rapide et admissible pour une grille.
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    // Défini UNE SEULE FOIS
    List<Vector2> AStarSearch(Vector2Int start, Vector2Int end) {
        // Commentaire: Implémentation standard de A*.
        // Utilise une PriorityQueue pour explorer les noeuds les plus prometteurs (coût + heuristique).
        if (!IsWithinBounds(start) || !IsWalkable(start)) { return null; }
        if (!IsWithinBounds(end) || !IsWalkable(end)) { Debug.LogWarning($"A* Target node {end} is not walkable or out of bounds!"); return null; }
        var frontier = new PriorityQueue<Vector2Int>();
        frontier.Enqueue(start, Heuristic(start, end)); // Priorité = Heuristique seule au départ (coût = 0)
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>{ [start] = start };
        var costSoFar = new Dictionary<Vector2Int, float>{ [start] = 0 }; // Coût réel depuis le départ

        while (frontier.Count > 0) {
            var current = frontier.Dequeue(); // Prend le noeud avec la plus petite priorité (coût+heuristique)
            if (current == end) { return ReconstructPath(cameFrom, end); } // Objectif atteint

            foreach (var next in GetNeighbors(current)) { // Pour chaque voisin accessible
                float moveCost = Vector2.Distance(current, next); // Coût du déplacement vers ce voisin
                float newCost = costSoFar[current] + moveCost; // Coût total pour atteindre ce voisin via 'current'
                // Si on n'a jamais atteint 'next' ou si ce chemin est meilleur que le précédent pour 'next'
                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next]) {
                    costSoFar[next] = newCost; // Met à jour le meilleur coût pour atteindre 'next'
                    // --- POINT CLÉ A* ---
                    // La priorité est la somme du coût réel depuis le départ (newCost)
                    // et une estimation du coût restant jusqu'à la cible (Heuristic).
                    float priority = newCost + Heuristic(next, end);
                    // ---------------------
                    frontier.Enqueue(next, priority); // Ajoute le voisin à la liste des noeuds à explorer
                    cameFrom[next] = current; // Mémorise qu'on est passé par 'current' pour atteindre 'next'
                }
            }
        }
        return null; // Aucun chemin trouvé
    }

    // Défini UNE SEULE FOIS
    List<Vector2> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) {
        var totalPath = new List<Vector2>();
        if (!cameFrom.ContainsKey(current)) { Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Nœud cible {current} non trouvé dans cameFrom."); return totalPath; }
        Vector2Int startNode = cameFrom.FirstOrDefault(kvp => kvp.Key == kvp.Value).Key;
        Vector2Int step = current; int safety = 0; const int maxSteps = 10000;
        while (step != startNode && safety < maxSteps) {
            totalPath.Add(GridToWorld(step));
            if (!cameFrom.ContainsKey(step)) { Debug.LogError($"TEST [{gameObject.name}] ReconstructPath: Nœud {step} manquant pendant reconstruction."); break; }
             step = cameFrom[step]; // Peut causer une erreur si la clé n'existe pas
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
            if (Mathf.Abs(x) + Mathf.Abs(y) == 2) {
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
        // Décommenter pour voir les obstacles détectés
        // if (hit != null) {
        //      Debug.LogWarning($"TEST IsWalkable: Case {gridPosition} OBSTACLE cause '{hit.name}' layer '{LayerMask.LayerToName(hit.gameObject.layer)}'");
        // }
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
    // (Doit être définie UNE SEULE FOIS dans votre projet)
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
            if (targetTransform != null && hit.transform == targetTransform) return true;
            return false; // Obstacle touché
       }
       return true; // Rien touché
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
            if (currentPathIndex < currentPath.Count) {
                 Debug.DrawLine(transform.position, currentPath[currentPathIndex], pathColor);
                 for (int i = currentPathIndex; i < currentPath.Count - 1; i++) {
                     Debug.DrawLine(currentPath[i], currentPath[i+1], pathColor);
                 }
            } else if (currentPath.Count > 0) {
                 Debug.DrawLine(transform.position, currentPath[currentPath.Count-1], pathColor * 0.5f);
            }
        }
         if (currentState != AIState.Idle) {
             Debug.DrawLine(transform.position, currentNavigationTargetPosition, Color.yellow);
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