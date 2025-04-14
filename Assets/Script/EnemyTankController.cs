using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(TankHealth))]
public class EnemyTankController : MonoBehaviour
{
    #region Variables Configurables (Inspecteur)

    [Header("Références Tank & Combat")]
    [Tooltip("Le Transform du canon qui doit pivoter (enfant du tank)")]
    public Transform canonPivot;
    [Tooltip("Le point d'où les balles sont tirées (enfant du canon)")]
    public Transform firePoint;
    [Tooltip("Le prefab de la balle à instancier")]
    public GameObject bulletPrefab;

    [Header("Mouvement & Rotation")]
    [Tooltip("Vitesse de déplacement en suivant le chemin")]
    public float moveSpeed = 3f;
    [Tooltip("Vitesse de rotation du CHASSIS vers le prochain point du chemin (degrés/sec)")]
    public float chassisRotationSpeed = 200f;
    [Tooltip("Vitesse de rotation du CANON vers la cible (degrés/sec)")]
    public float canonRotationSpeed = 300f;

    [Header("Combat")]
    [Tooltip("Temps minimum (secondes) entre deux tirs")]
    public float fireRate = 1.0f;
    [Tooltip("Distance maximale pour que le tank commence à tirer sur la cible")]
    public float shootingRange = 10f;
    [Tooltip("Distance à laquelle le tank détecte les joueurs ennemis")]
    public float detectionRange = 15f;
    [Tooltip("Layer(s) sur lequel/lesquels se trouvent les tanks joueurs (IMPORTANT!)")]
    public LayerMask playerLayerMask;

    [Header("Pathfinding (Dijkstra)")]
    [Tooltip("Taille de la grille de navigation en nombre de tuiles")]
    public Vector2Int gridSize = new Vector2Int(30, 30);
    [Tooltip("Taille d'une tuile en unités de monde Unity")]
    public float tileSize = 1f;
    [Tooltip("Layer(s) contenant les obstacles que le pathfinding doit éviter (IMPORTANT!)")]
    public LayerMask obstacleLayer;
    [Tooltip("Fréquence de recalcul du chemin vers la cible (secondes)")]
    public float pathRecalculationRate = 0.5f;

    [Header("Comportement IA")]
    [Tooltip("Liste des points de capture à considérer comme objectifs (assigner ici ou laisser vide pour recherche auto)")]
    public List<CapturePoint> capturePointsOfInterest;
    [Tooltip("Rayon autour de la cible capture point pour choisir une destination proche (en tuiles)")]
    public int targetPointSearchRadius = 2;
    [Tooltip("Nombre max d'essais pour trouver un point proche différent de la position actuelle")]
    public int nearbyPointMaxAttempts = 20;
    
    [Header("Patrouille")]
    [Tooltip("Activer la patrouille quand il n'y a pas d'autres objectifs")]
    public bool enablePatrolling = true;
    [Tooltip("Distance maximale pour générer un point de patrouille (en unités)")]
    public float patrolRadius = 10f;
    [Tooltip("Temps minimum entre deux changements de point de patrouille (en secondes)")]
    public float minPatrolWaitTime = 3f;
    [Tooltip("Temps maximum entre deux changements de point de patrouille (en secondes)")]
    public float maxPatrolWaitTime = 8f;

    #endregion

    #region Variables Internes

    // --- État Actuel de l'IA ---
    private enum AIState { Idle, SeekingCapturePoint, AttackingPlayer, Patrolling }
    private AIState currentState = AIState.Idle;

    // --- Cibles ---
    private Transform currentTargetPlayer = null;
    private CapturePoint currentTargetCapturePoint = null;
    private Vector3 currentNavigationTargetPosition; // Destination précise pour la nav
    private Vector3 patrolTargetPosition;
    private float nextPatrolChangeTime = 0f;

    // --- Pathfinding ---
    private List<Vector2> currentPath;
    private int currentPathIndex;
    private float timeSinceLastPathRecalc = 0f;
    private Vector3 lastTargetPosition; // Dernière pos de NAV cible connue
    
    // --- Identité unique pour ce tank ---
    private string uniqueID;

    // --- Combat ---
    private float nextFireTime = 0f;

    // --- Références internes ---
    private TankHealth selfHealth;
    private string ownTag; // Tag de ce tank ("Enemy")

    // --- Coordination Statique Améliorée ---
    private static Dictionary<CapturePoint, HashSet<string>> capturePointTargeters = new Dictionary<CapturePoint, HashSet<string>>();
    
    #endregion

    #region Méthodes de Coordination Statique Améliorées

    private static void AddTankToTargeters(CapturePoint point, string tankId) {
        if (point == null || string.IsNullOrEmpty(tankId)) return;
        
        if (!capturePointTargeters.ContainsKey(point)) {
            capturePointTargeters[point] = new HashSet<string>();
        }
        capturePointTargeters[point].Add(tankId);
    }
    
    private static void RemoveTankFromTargeters(CapturePoint point, string tankId) {
        if (point == null || string.IsNullOrEmpty(tankId)) return;
        
        if (capturePointTargeters.ContainsKey(point)) {
            capturePointTargeters[point].Remove(tankId);
        }
    }
    
    private static int GetTargetCount(CapturePoint point) {
        if (point == null) return 0;
        return capturePointTargeters.TryGetValue(point, out HashSet<string> tanks) ? tanks.Count : 0;
    }
    
    private static bool IsTankTargetingPoint(CapturePoint point, string tankId) {
        if (point == null || string.IsNullOrEmpty(tankId)) return false;
        
        return capturePointTargeters.TryGetValue(point, out HashSet<string> tanks) && tanks.Contains(tankId);
    }
    
    public static void ResetTargetCounts() {
        capturePointTargeters.Clear();
        Debug.Log("Compteurs de cibles de points de capture réinitialisés.");
    }

    #endregion

    #region Méthodes Unity (Awake, Start, Update, OnDestroy)

    void Awake() {
        selfHealth = GetComponent<TankHealth>();
        ownTag = gameObject.tag;
        // Générer un ID unique pour ce tank
        uniqueID = System.Guid.NewGuid().ToString();
        
        // --- Vérifications Awake ---
        if (canonPivot == null) Debug.LogError($"[{gameObject.name}] Canon Pivot manquant", this);
        if (firePoint == null) Debug.LogError($"[{gameObject.name}] Fire Point manquant", this);
        if (bulletPrefab == null) Debug.LogError($"[{gameObject.name}] Bullet Prefab manquant", this);
        if (playerLayerMask == 0) Debug.LogWarning($"[{gameObject.name}] Player Layer Mask non défini", this);
        if (obstacleLayer == 0) Debug.LogWarning($"[{gameObject.name}] Obstacle Layer non défini", this);
    }

    void Start() {
        if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) {
            capturePointsOfInterest = FindObjectsOfType<CapturePoint>().ToList();
            Debug.Log($"[{gameObject.name}] Trouvé {capturePointsOfInterest.Count} points de capture.");
        }
        
        // Ajout d'un délai aléatoire pour éviter que tous les tanks décident en même temps
        Invoke("InitialDecision", Random.Range(0.0f, 0.5f));
        
        currentState = AIState.Idle;
        currentNavigationTargetPosition = transform.position; // Commence sur place
        lastTargetPosition = transform.position;
        
        // Initialisation du point de patrouille
        GenerateNewPatrolPoint();
    }
    
    void InitialDecision() {
        DecideNextAction();
    }

    void Update() {
        if (selfHealth != null && selfHealth.CurrentHealth <= 0) return; // Ne rien faire si mort
        
        // Exécute la boucle logique de l'IA
        DetectPlayers();
        DecideNextAction();
        HandlePathfinding();
        MoveAlongPath();
        RotateChassisTowardsWaypoint();
        RotateCanonTowardsTarget();
        HandleShooting();
        
        // Gestion de la patrouille
        if (currentState == AIState.Patrolling && Time.time > nextPatrolChangeTime) {
            GenerateNewPatrolPoint();
        }
    }

    void OnDestroy() {
        // Libère la cible de capture si le tank est détruit
        RemoveTankFromTargeters(currentTargetCapturePoint, uniqueID);
    }

    #endregion

    #region Logique de Décision et Détection

    void DetectPlayers() {
        Collider2D[] playersInRange = Physics2D.OverlapCircleAll(transform.position, detectionRange, playerLayerMask);
        Transform closestPlayer = null; 
        float minDistance = float.MaxValue;
        
        foreach (Collider2D playerCollider in playersInRange) {
            float distance = Vector2.Distance(transform.position, playerCollider.transform.position);
            if (distance < minDistance) {
                TankHealth detectedPlayerHealth = playerCollider.GetComponent<TankHealth>();
                if (detectedPlayerHealth != null && detectedPlayerHealth.CurrentHealth > 0) {
                    minDistance = distance;
                    closestPlayer = playerCollider.transform;
                }
            }
        }
        
        currentTargetPlayer = closestPlayer;
    }

    void DecideNextAction() {
        CapturePoint previousTargetCapturePoint = currentTargetCapturePoint;
        AIState previousState = currentState;
        bool targetPointChanged = false;

        // Priorité 1: Joueur dans la zone de détection
        if (currentTargetPlayer != null) {
            currentState = AIState.AttackingPlayer;
            currentTargetCapturePoint = null;
            currentNavigationTargetPosition = currentTargetPlayer.position;
            
            if (previousState != AIState.AttackingPlayer || Vector3.Distance(lastTargetPosition, currentNavigationTargetPosition) > 0.5f) {
                currentPath = null;
            }
            
            if (previousTargetCapturePoint != null) {
                targetPointChanged = true;
            }
        } 
        // Priorité 2: Point de capture non contrôlé disponible
        else if (capturePointsOfInterest != null && capturePointsOfInterest.Count > 0) {
            CapturePoint bestCapturePoint = FindBestCapturePoint();
            
            if (bestCapturePoint != null) {
                currentState = AIState.SeekingCapturePoint;
                
                if (bestCapturePoint != previousTargetCapturePoint) {
                    currentTargetCapturePoint = bestCapturePoint;
                    currentNavigationTargetPosition = GetNearbyWalkablePoint(
                        currentTargetCapturePoint.transform.position, 
                        WorldToGrid(transform.position), 
                        targetPointSearchRadius, 
                        nearbyPointMaxAttempts
                    );
                    currentPath = null;
                    targetPointChanged = true;
                    Debug.Log($"[{gameObject.name}] Nouvelle cible point {currentTargetCapturePoint.pointName}, navigue vers {currentNavigationTargetPosition}");
                }
            }
            // Priorité 3: Patrouille
            else if (enablePatrolling) {
                if (currentState != AIState.Patrolling) {
                    currentState = AIState.Patrolling;
                    currentNavigationTargetPosition = patrolTargetPosition;
                    currentPath = null;
                    
                    if (previousTargetCapturePoint != null) {
                        targetPointChanged = true;
                    }
                }
            } 
            // Sinon, rester inactif
            else {
                if (currentState != AIState.Idle) {
                    currentState = AIState.Idle;
                    currentNavigationTargetPosition = transform.position;
                    currentPath = null;
                    
                    if (previousTargetCapturePoint != null) {
                        targetPointChanged = true;
                    }
                }
                currentTargetCapturePoint = null;
            }
        }
        // Aucun point de capture disponible, passer en mode patrouille
        else if (enablePatrolling) {
            if (currentState != AIState.Patrolling) {
                currentState = AIState.Patrolling;
                currentNavigationTargetPosition = patrolTargetPosition;
                currentPath = null;
                
                if (previousTargetCapturePoint != null) {
                    targetPointChanged = true;
                }
            }
            currentTargetCapturePoint = null;
        }
        // Aucune action possible, rester inactif
        else {
            if (currentState != AIState.Idle) {
                currentState = AIState.Idle;
                currentNavigationTargetPosition = transform.position;
                currentPath = null;
                
                if (previousTargetCapturePoint != null) {
                    targetPointChanged = true;
                }
            }
            currentTargetCapturePoint = null;
        }

        // Mise à jour des compteurs de ciblage
        if (targetPointChanged) {
            RemoveTankFromTargeters(previousTargetCapturePoint, uniqueID);
            AddTankToTargeters(currentTargetCapturePoint, uniqueID);
        }
    }

    CapturePoint FindBestCapturePoint() {
        if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) return null;
        
        string playerTag = "Player";
        
        // Ajouter une petite perturbation aléatoire à la position du tank pour éviter les décisions identiques
        Vector3 perturbedPosition = transform.position + new Vector3(
            Random.Range(-0.5f, 0.5f),
            Random.Range(-0.5f, 0.5f),
            0
        );
        
        // Évaluer chaque point de capture non contrôlé par notre équipe
        var suitablePoints = capturePointsOfInterest
            .Where(point => point != null && point.controllingTeamTag != ownTag)
            .Select(point => new {
                Point = point,
                DistanceSqr = (point.transform.position - perturbedPosition).sqrMagnitude,
                Targets = GetTargetCount(point),
                IsAlreadyTargetedByMe = IsTankTargetingPoint(point, uniqueID)
            })
            .ToList();
        
        // Si ce tank cible déjà un point, lui donner la priorité
        var currentlyTargetedPoint = suitablePoints
            .FirstOrDefault(x => x.IsAlreadyTargetedByMe);
            
        if (currentlyTargetedPoint != null) {
            return currentlyTargetedPoint.Point;
        }
        
        // Sinon, trier par distance et nombre de tanks qui le ciblent déjà
        var bestPoint = suitablePoints
            .OrderBy(x => x.Targets)        // D'abord, prioriser les points les moins ciblés
            .ThenBy(x => x.DistanceSqr)     // Ensuite, prioriser les plus proches
            .FirstOrDefault();
            
        return bestPoint?.Point;
    }

    Vector3 GetNearbyWalkablePoint(Vector3 origin, Vector2Int currentTankGridPos, int radiusInTiles = 2, int maxAttempts = 20) {
        Vector2Int originGridPos = WorldToGrid(origin);
        Vector2Int bestFoundPos = originGridPos;
        bool foundAnyWalkable = false;

        for (int i = 0; i < maxAttempts; i++) {
            // Ajout d'une petite variabilité supplémentaire basée sur l'identifiant unique du tank
            int randomXOffset = (int)(uniqueID.GetHashCode() % 10) - 5;
            int randomYOffset = (int)((uniqueID.GetHashCode() >> 5) % 10) - 5;
            
            int randomX = Random.Range(-radiusInTiles, radiusInTiles + 1) + randomXOffset % 3;
            int randomY = Random.Range(-radiusInTiles, radiusInTiles + 1) + randomYOffset % 3;
            
            Vector2Int potentialGridPos = originGridPos + new Vector2Int(randomX, randomY);

            if (IsWithinBounds(potentialGridPos) && IsWalkable(potentialGridPos)) {
                foundAnyWalkable = true;
                bestFoundPos = potentialGridPos;

                if (potentialGridPos != currentTankGridPos) {
                    return GridToWorld(potentialGridPos);
                }
            }
        }

        if (foundAnyWalkable) {
            return GridToWorld(bestFoundPos);
        } else {
            return origin;
        }
    }
    
    void GenerateNewPatrolPoint() {
        int maxAttempts = 20;
        bool foundValidPoint = false;
        Vector2Int currentGridPos = WorldToGrid(transform.position);
        
        for (int i = 0; i < maxAttempts; i++) {
            // Générer un point à distance variable du tank
            float angle = Random.Range(0f, 360f);
            float distance = Random.Range(patrolRadius * 0.3f, patrolRadius);
            
            Vector2 offset = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad) * distance,
                Mathf.Sin(angle * Mathf.Deg2Rad) * distance
            );
            
            Vector3 potentialPoint = transform.position + new Vector3(offset.x, offset.y, 0);
            Vector2Int gridPos = WorldToGrid(potentialPoint);
            
            if (IsWithinBounds(gridPos) && IsWalkable(gridPos) && gridPos != currentGridPos) {
                patrolTargetPosition = GridToWorld(gridPos);
                foundValidPoint = true;
                break;
            }
        }
        
        if (!foundValidPoint) {
            // Si aucun point valide n'est trouvé, utiliser un point proche
            patrolTargetPosition = GetNearbyWalkablePoint(transform.position, currentGridPos, 5, 10);
        }
        
        // Si en mode patrouille, mettre à jour la destination de navigation
        if (currentState == AIState.Patrolling) {
            currentNavigationTargetPosition = patrolTargetPosition;
            currentPath = null;
        }
        
        // Planifier le prochain changement de point de patrouille
        nextPatrolChangeTime = Time.time + Random.Range(minPatrolWaitTime, maxPatrolWaitTime);
    }

    #endregion

    #region Pathfinding (Dijkstra)

    void HandlePathfinding() {
        timeSinceLastPathRecalc += Time.deltaTime;
        bool hasTarget = (currentState != AIState.Idle);

        if (!hasTarget) { 
            currentPath = null; 
            return; 
        }

        Vector2Int startGridPos = WorldToGrid(transform.position);
        Vector2Int endGridPos = WorldToGrid(currentNavigationTargetPosition);

        // Vérifie si on est déjà dans la case de destination finale
        if (startGridPos == endGridPos) {
            currentPath = null;
            
            // Si en mode patrouille et arrivé à destination, générer un nouveau point
            if (currentState == AIState.Patrolling) {
                GenerateNewPatrolPoint();
                currentNavigationTargetPosition = patrolTargetPosition;
                endGridPos = WorldToGrid(currentNavigationTargetPosition);
                
                // Si le nouveau point est différent, calculer un chemin
                if (startGridPos != endGridPos) {
                    FindPath(currentNavigationTargetPosition, startGridPos, endGridPos);
                }
            }
            
            return;
        }

        // Logique de recalcul
        bool targetPosChanged = Vector3.Distance(currentNavigationTargetPosition, lastTargetPosition) > 0.1f;
        bool timerElapsed = timeSinceLastPathRecalc >= pathRecalculationRate;
        bool pathInvalid = currentPath == null || currentPath.Count == 0;

        if (pathInvalid || timerElapsed || (currentState == AIState.AttackingPlayer && targetPosChanged)) {
            FindPath(currentNavigationTargetPosition, startGridPos, endGridPos);
            timeSinceLastPathRecalc = 0f;
            lastTargetPosition = currentNavigationTargetPosition;
        }
    }

    void FindPath(Vector3 targetNavPosition, Vector2Int startGridPos, Vector2Int endGridPos) {
        currentPath = Dijkstra(startGridPos, endGridPos);

        if (currentPath == null || currentPath.Count == 0) {
            if (startGridPos != endGridPos) {
                Debug.LogWarning($"[{gameObject.name}] ECHEC Pathfinding de {startGridPos} vers {endGridPos} (Dest Monde: {targetNavPosition})");
            }
        }

        // Ajustement initial du chemin
        if (currentPath != null && currentPath.Count > 0 && Vector2.Distance(transform.position, currentPath[0]) < tileSize * 0.1f) {
            currentPath.RemoveAt(0);
        }
        currentPathIndex = 0;
    }

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
                    float priority = newCost;
                    frontier.Enqueue(next, priority);
                    cameFrom[next] = current;
                }
            }
        }
        return null;
    }

    List<Vector2> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current) {
        var totalPath = new List<Vector2>();
        if (!cameFrom.ContainsKey(current)) { Debug.LogError("ReconstructPath: Nœud cible non trouvé."); return totalPath; }
        Vector2Int startNode = cameFrom.FirstOrDefault(kvp => kvp.Key == kvp.Value).Key;
        Vector2Int step = current;
        int safety = 0; const int maxSteps = 10000;
        while (step != startNode && safety < maxSteps) {
            totalPath.Add(GridToWorld(step));
            if (!cameFrom.ContainsKey(step)) { Debug.LogError($"ReconstructPath: Nœud {step} manquant."); break; }
            step = cameFrom[step];
            safety++;
        }
        if(safety >= maxSteps) Debug.LogError("ReconstructPath: Limite de sécurité atteinte!");
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
                    if (!IsWalkable(n1) || !IsWalkable(n2)) continue;
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
        int x = Mathf.FloorToInt(worldPosition.x / tileSize);
        int y = Mathf.FloorToInt(worldPosition.y / tileSize);
        x = Mathf.Clamp(x, 0, gridSize.x - 1);
        y = Mathf.Clamp(y, 0, gridSize.y - 1);
        return new Vector2Int(x, y);
    }

    Vector2 GridToWorld(Vector2Int gridPosition) {
        float x = gridPosition.x * tileSize + tileSize / 2f;
        float y = gridPosition.y * tileSize + tileSize / 2f;
        return new Vector2(x, y);
    }

    // Classe interne PriorityQueue
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

    #endregion

    #region Mouvement & Rotation

    void MoveAlongPath() {
        if (currentPath == null || currentPathIndex >= currentPath.Count) return;
        
        Vector2 targetWaypoint = currentPath[currentPathIndex];
        transform.position = Vector2.MoveTowards(transform.position, targetWaypoint, moveSpeed * Time.deltaTime);
        
        if (Vector2.Distance(transform.position, targetWaypoint) < 0.1f) {
            currentPathIndex++;
        }
    }

    void RotateChassisTowardsWaypoint() {
        // Point cible différent selon l'état
        Vector2 targetPoint;
        
        if (currentPath != null && currentPathIndex < currentPath.Count) {
            targetPoint = currentPath[currentPathIndex];
        } else if (currentState == AIState.AttackingPlayer && currentTargetPlayer != null) {
            targetPoint = currentTargetPlayer.position;
        } else {
            return; // Pas de cible pour rotation
        }
        
        Vector2 direction = (targetPoint - (Vector2)transform.position).normalized;
        
        if (direction != Vector2.zero) {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, chassisRotationSpeed * Time.deltaTime);
        }
    }

    void RotateCanonTowardsTarget() {
        if (canonPivot == null) return;
        
        Vector2 direction = Vector2.zero;
        bool hasAimTarget = false;
        
        if (currentState == AIState.AttackingPlayer && currentTargetPlayer != null) {
            direction = ((Vector2)currentTargetPlayer.position - (Vector2)canonPivot.position).normalized;
            hasAimTarget = true;
        } else {
            // En mode patrouille ou point de capture, le canon pointe soit vers le chemin soit dans la direction du chassis
            if (currentPath != null && currentPathIndex < currentPath.Count) {
                direction = ((Vector2)currentPath[currentPathIndex] - (Vector2)canonPivot.position).normalized;
            } else {
                direction = transform.up; // Direction du chassis
            }
            hasAimTarget = true;
        }
        
        if (hasAimTarget && direction != Vector2.zero) {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            Quaternion targetRotation = Quaternion.Euler(0f, 0f, angle);
            canonPivot.rotation = Quaternion.RotateTowards(canonPivot.rotation, targetRotation, canonRotationSpeed * Time.deltaTime);
        }
    }

    #endregion

    #region Combat (Tir)

    void HandleShooting() {
        if (currentState != AIState.AttackingPlayer || currentTargetPlayer == null) return;
        
        float distanceToPlayer = Vector2.Distance(transform.position, currentTargetPlayer.position);
        
        // Vérifier si le joueur est à portée de tir et si le délai entre les tirs est écoulé
        if (distanceToPlayer <= shootingRange && Time.time >= nextFireTime) {
            // Vérifier si le canon est suffisamment aligné avec la cible avant de tirer
            Vector2 canonDirection = canonPivot.up;
            Vector2 targetDirection = ((Vector2)currentTargetPlayer.position - (Vector2)canonPivot.position).normalized;
            float angleDifference = Vector2.Angle(canonDirection, targetDirection);
            
            // Ne tire que si l'angle est assez petit (bon alignement)
            if (angleDifference < 20f) {
                Shoot();
                nextFireTime = Time.time + fireRate;
            }
        }
    }

    void Shoot() {
        if (bulletPrefab == null || firePoint == null) return;
        
        // Instancier la balle
        GameObject bulletInstance = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        
        // Configurer la balle avec les informations de son tireur
        BulletDestruction bulletScript = bulletInstance.GetComponent<BulletDestruction>();
        if (bulletScript != null) {
            bulletScript.shooter = this.gameObject;
            bulletScript.shooterTag = this.gameObject.tag;
        } else {
            Debug.LogError($"Prefab balle '{bulletPrefab.name}' manque script BulletDestruction!", bulletPrefab);
        }
    }

    #endregion
    
    #region Debug & Visualisation
    
    // Optionnel: Visualisation pour le débogage
    void OnDrawGizmosSelected() {
        // Afficher la plage de détection des joueurs
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        
        // Afficher la portée de tir
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, shootingRange);
        
        // Afficher le chemin actuel
        if (currentPath != null && currentPath.Count > 0) {
            Gizmos.color = Color.green;
            for (int i = 0; i < currentPath.Count - 1; i++) {
                Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
            }
            
            // Afficher le point actuel du chemin
            if (currentPathIndex < currentPath.Count) {
                Gizmos.color = Color.blue;
                Gizmos.DrawSphere(currentPath[currentPathIndex], 0.2f);
            }
        }
        
        // Afficher le point de patrouille actuel
        if (currentState == AIState.Patrolling) {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(patrolTargetPosition, 0.3f);
        }
    }
    
    #endregion
}