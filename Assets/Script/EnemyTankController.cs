using UnityEngine;
using System.Collections.Generic;
using System.Linq; // Nécessaire pour Linq (OrderBy, FirstOrDefault, etc.)

[RequireComponent(typeof(TankHealth))] // Assure la présence du script TankHealth
public class EnemyTankController : MonoBehaviour
{
    #region Variables Configurables (Inspecteur)

    // -- Références aux composants du tank et au prefab de balle --
    [Header("Références Tank & Combat")]
    [Tooltip("Le Transform du canon qui doit pivoter (enfant du tank)")]
    public Transform canonPivot;
    [Tooltip("Le point d'où les balles sont tirées (enfant du canon)")]
    public Transform firePoint;
    [Tooltip("Le prefab de la balle à instancier")]
    public GameObject bulletPrefab;

    // -- Paramètres de mouvement et rotation --
    [Header("Mouvement & Rotation")]
    [Tooltip("Vitesse de déplacement en suivant le chemin")]
    public float moveSpeed = 3f;
    [Tooltip("Vitesse de rotation du CHASSIS vers le prochain point du chemin (degrés/sec)")]
    public float chassisRotationSpeed = 200f;
    [Tooltip("Vitesse de rotation du CANON vers la cible (degrés/sec)")]
    public float canonRotationSpeed = 300f;

    // -- Paramètres de combat --
    [Header("Combat")]
    [Tooltip("Temps minimum (secondes) entre deux tirs")]
    public float fireRate = 1.0f;
    [Tooltip("Distance maximale pour que le tank commence à tirer sur la cible")]
    public float shootingRange = 10f;
    [Tooltip("Distance à laquelle le tank détecte les joueurs ennemis")]
    public float detectionRange = 15f;
    [Tooltip("Layer(s) sur lequel/lesquels se trouvent les tanks joueurs (IMPORTANT!)")]
    public LayerMask playerLayerMask;

    // -- Paramètres de Pathfinding (Dijkstra) --
    [Header("Pathfinding (Dijkstra)")]
    [Tooltip("Taille de la grille de navigation en nombre de tuiles")]
    public Vector2Int gridSize = new Vector2Int(30, 30);
    [Tooltip("Taille d'une tuile en unités de monde Unity")]
    public float tileSize = 1f;
    [Tooltip("Layer(s) contenant les obstacles que le pathfinding doit éviter (IMPORTANT!)")]
    public LayerMask obstacleLayer;
    [Tooltip("Fréquence de recalcul du chemin vers la cible (secondes)")]
    public float pathRecalculationRate = 0.5f;

    // -- Comportement IA --
    [Header("Comportement IA")]
    [Tooltip("Liste des points de capture à considérer comme objectifs (assigner ici ou laisser vide pour recherche auto)")]
    public List<CapturePoint> capturePointsOfInterest;
    [Tooltip("Distance minimale pour considérer un point de patrouille comme atteint")]
    public float patrolPointReachedDistance = 1.5f; // Ajuster si nécessaire

    #endregion

    #region Variables Internes

    // --- État Actuel de l'IA ---
    private enum AIState
    {
        Idle,               // Utilisé si la patrouille ne trouve pas de point
        SeekingCapturePoint,
        AttackingPlayer,
        Patrolling          // Nouvel état
    }
    private AIState currentState = AIState.Patrolling; // Commence en patrouille par défaut

    // --- Cibles ---
    private Transform currentTargetPlayer = null;
    private CapturePoint currentTargetCapturePoint = null;
    private Vector3 currentNavigationTargetPosition; // Destination précise

    // --- Pathfinding ---
    private List<Vector2> currentPath;
    private int currentPathIndex;
    private float timeSinceLastPathRecalc = 0f;
    private Vector3 lastTargetPosition; // Dernière pos de NAV cible connue

    // --- Combat ---
    private float nextFireTime = 0f;

    // --- Références internes ---
    private TankHealth selfHealth;
    private string ownTag; // Tag de ce tank ("Enemy")

    // --- Coordination Statique Simple ---
    private static Dictionary<CapturePoint, int> capturePointTargetCounts = new Dictionary<CapturePoint, int>();

    #endregion

    #region Méthodes de Coordination Statique
    private static void IncrementTargetCount(CapturePoint point) {
        if (point == null) return;
        if (!capturePointTargetCounts.ContainsKey(point)) capturePointTargetCounts[point] = 0;
        capturePointTargetCounts[point]++;
        // Debug.Log($"[{point.pointName}] Target Count INC to {capturePointTargetCounts[point]}");
    }
    private static void DecrementTargetCount(CapturePoint point) {
        if (point == null) return;
        if (capturePointTargetCounts.ContainsKey(point)) {
            capturePointTargetCounts[point] = Mathf.Max(0, capturePointTargetCounts[point] - 1);
            // Debug.Log($"[{point.pointName}] Target Count DEC to {capturePointTargetCounts[point]}");
        }
    }
    private static int GetTargetCount(CapturePoint point) {
        if (point == null) return 0;
        return capturePointTargetCounts.TryGetValue(point, out int count) ? count : 0;
    }
    // IMPORTANT: Appeler cette fonction depuis un GameManager au début de chaque partie !
    public static void ResetTargetCounts() {
        capturePointTargetCounts.Clear();
        Debug.Log("TEST: Compteurs de cibles de points de capture réinitialisés.");
    }
    #endregion

    #region Méthodes Unity (Awake, Start, Update, OnDestroy)

    void Awake() {
        selfHealth = GetComponent<TankHealth>();
        ownTag = gameObject.tag;
        if (canonPivot == null) Debug.LogError($"[{gameObject.name}] Canon Pivot manquant", this);
        if (firePoint == null) Debug.LogError($"[{gameObject.name}] Fire Point manquant", this);
        if (bulletPrefab == null) Debug.LogError($"[{gameObject.name}] Bullet Prefab manquant", this);
        if (playerLayerMask == 0) Debug.LogWarning($"[{gameObject.name}] Player Layer Mask non défini", this);
        if (obstacleLayer == 0) Debug.LogWarning($"[{gameObject.name}] Obstacle Layer non défini", this);
    }

    void Start() {
        if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) {
            capturePointsOfInterest = FindObjectsOfType<CapturePoint>().ToList();
            Debug.Log($"TEST [{gameObject.name}] Start: Trouvé {capturePointsOfInterest.Count} points de capture.");
        }
        currentState = AIState.Patrolling; // État initial
        currentNavigationTargetPosition = transform.position; // Sur place au début
        lastTargetPosition = transform.position;
        SelectNewPatrolPoint(); // Sélectionne la 1ère destination de patrouille
        Debug.Log($"TEST [{gameObject.name}] Start: Initialisation terminée. Etat: {currentState}. Navigue vers {currentNavigationTargetPosition}");
        // Rappel: ResetTargetCounts() doit être appelé par un GameManager
    }

    void Update() {
        if (selfHealth != null && selfHealth.CurrentHealth <= 0) return;

        CheckPatrolPointReached(); // Vérifie si on est arrivé au point de patrouille

        DetectPlayers();          // 1. Cherche les joueurs
        DecideNextAction();       // 2. Décide quoi faire (état, cible logique, cible nav)
        HandlePathfinding();      // 3. Calcule le chemin si nécessaire
        MoveAlongPath();          // 4a. Bouge le long du chemin
        RotateChassisTowardsWaypoint(); // 4b. Oriente le châssis
        RotateCanonTowardsTarget(); // 4c. Oriente le canon
        HandleShooting();         // 5. Tire si les conditions sont remplies
    }

    void OnDestroy() {
        Debug.Log($"TEST [{gameObject.name}] OnDestroy: Libération cible {currentTargetCapturePoint?.name ?? "aucune"}.");
        DecrementTargetCount(currentTargetCapturePoint);
    }

    #endregion

    #region Logique de Décision et Détection

    /// <summary> Détecte le joueur vivant le plus proche dans la zone. </summary>
    void DetectPlayers() {
        // (Code inchangé)
        Collider2D[] playersInRange = Physics2D.OverlapCircleAll(transform.position, detectionRange, playerLayerMask);
        Transform closestPlayer = null; float minDistance = float.MaxValue;
        foreach (Collider2D playerCollider in playersInRange) {
            float distance = Vector2.Distance(transform.position, playerCollider.transform.position);
            if (distance < minDistance) {
                TankHealth detectedPlayerHealth = playerCollider.GetComponent<TankHealth>();
                if (detectedPlayerHealth != null && detectedPlayerHealth.CurrentHealth > 0) {
                    minDistance = distance; closestPlayer = playerCollider.transform;
                }
            }
        }
        if(currentTargetPlayer != closestPlayer) { Debug.Log($"TEST [{gameObject.name}] DetectPlayers: Cible joueur {(closestPlayer == null ? "perdue/aucune" : "trouvée: " + closestPlayer.name)}"); }
        currentTargetPlayer = closestPlayer;
    }

    /// <summary> Choisit l'état et la cible (logique et navigation). Met à jour les compteurs. </summary>
    void DecideNextAction() {
        CapturePoint previousTargetCapturePoint = currentTargetCapturePoint;
        AIState previousState = currentState;
        bool targetPointChanged = false;
        bool forcePathRecalc = false;

        // --- Logique de décision avec priorités ---
        if (currentTargetPlayer != null) { // Priorité 1: Joueur
            if (previousState != AIState.AttackingPlayer) {
                 Debug.Log($"TEST [{gameObject.name}] DecideNextAction: Changement d'état -> AttackingPlayer (cible: {currentTargetPlayer.name})");
                 currentState = AIState.AttackingPlayer;
                 if(currentTargetCapturePoint != null) targetPointChanged = true;
                 currentTargetCapturePoint = null;
                 forcePathRecalc = true;
            }
            if(currentNavigationTargetPosition != currentTargetPlayer.position) {
                 currentNavigationTargetPosition = currentTargetPlayer.position;
                 forcePathRecalc = true; // Recalcul si joueur a bougé
            }

        } else { // Pas de joueur détecté
            CapturePoint bestCapturePoint = FindBestCapturePoint_LessTargetedFirst(); // Cherche un point intéressant

            if (bestCapturePoint != null) { // Priorité 2: Point de capture trouvé
                 if (previousState != AIState.SeekingCapturePoint) {
                     Debug.Log($"TEST [{gameObject.name}] DecideNextAction: Changement d'état -> SeekingCapturePoint.");
                     currentState = AIState.SeekingCapturePoint;
                     forcePathRecalc = true;
                 }
                 if (bestCapturePoint != previousTargetCapturePoint) {
                     Debug.Log($"TEST [{gameObject.name}] DecideNextAction: Nouvelle cible POINT logique: {bestCapturePoint.pointName}");
                     currentTargetCapturePoint = bestCapturePoint;
                     currentNavigationTargetPosition = currentTargetCapturePoint.transform.position; // Cible = centre du point
                     targetPointChanged = true;
                     forcePathRecalc = true;
                     Debug.Log($"TEST [{gameObject.name}] DecideNextAction: Nouvelle cible de NAVIGATION (centre point): {currentNavigationTargetPosition}");
                 }
                 // Si le point logique est le même, la destination de nav reste la même (le centre du point)

            } else { // Priorité 3: Patrouille
                if (previousState != AIState.Patrolling) {
                     Debug.Log($"TEST [{gameObject.name}] DecideNextAction: Aucun objectif. Changement d'état -> Patrolling.");
                     currentState = AIState.Patrolling;
                     if(previousTargetCapturePoint != null) targetPointChanged = true;
                     currentTargetCapturePoint = null;
                     // La destination de patrouille est déjà gérée/mise à jour par CheckPatrolPointReached ou Start
                     // On s'assure juste qu'on a un chemin vers la destination de patrouille actuelle
                     forcePathRecalc = true; // Force recalcul vers la destination de patrouille
                }
                // Si déjà en Patrolling, currentNavigationTargetPosition est déjà la cible de patrouille en cours
            }
        }

        // Met à jour les compteurs si le point logique a changé
        if(targetPointChanged) {
            Debug.Log($"TEST [{gameObject.name}] DecideNextAction: Changement de cible point -> Dec({previousTargetCapturePoint?.name ?? "null"}), Inc({currentTargetCapturePoint?.name ?? "null"})");
            DecrementTargetCount(previousTargetCapturePoint);
            IncrementTargetCount(currentTargetCapturePoint);
        }

        // Force la mise à jour du chemin si l'état ou la cible a changé
        if (forcePathRecalc) {
            currentPath = null;
        }

        // --- DEBUG LOG État Final ---
        // Debug.Log($"[{gameObject.name}] DecideNextAction - Etat Final: {currentState}, Nav Target: {currentNavigationTargetPosition}");
    }


    /// <summary> Trouve le meilleur point capturable: moins ciblé, puis plus proche. </summary>
    CapturePoint FindBestCapturePoint_LessTargetedFirst() {
        // (Identique à la version précédente - utilise OrderBy(Targets).ThenBy(DistanceSqr))
         if (capturePointsOfInterest == null || capturePointsOfInterest.Count == 0) return null;
        string playerTag = "Player";
        var suitablePoints = capturePointsOfInterest
            .Where(point => point != null && point.controllingTeamTag != ownTag)
            .Select(point => new {
                Point = point,
                DistanceSqr = (point.transform.position - transform.position).sqrMagnitude,
                Targets = GetTargetCount(point)
            })
            .OrderBy(x => x.Targets)
            .ThenBy(x => x.DistanceSqr);
        var bestTargetInfo = suitablePoints.FirstOrDefault();
         // --- DEBUG LOG ---
         if(bestTargetInfo != null) {
              Debug.Log($"TEST [{gameObject.name}] FindBestCapturePoint (LessTargeted): Point choisi: {bestTargetInfo.Point.pointName} (DistSqr: {bestTargetInfo.DistanceSqr:F1}, Targets: {bestTargetInfo.Targets})");
         } else {
              // Debug.Log($"TEST [{gameObject.name}] FindBestCapturePoint (LessTargeted): Aucun point capturable trouvé.");
         }
        // -----------------
        return bestTargetInfo?.Point;
    }

    // La fonction GetNearbyWalkablePoint n'est PAS utilisée dans cette version

    #endregion

    #region Logique de Patrouille

    /// <summary>
    /// Vérifie si le point de patrouille actuel est atteint et, si oui, en choisit un nouveau.
    /// Appelé depuis Update().
    /// </summary>
    void CheckPatrolPointReached()
    {
        // Fonctionne seulement si on est en état de patrouille
        if (currentState != AIState.Patrolling) return;

        bool reached = false;
        // Condition 1: Très proche de la destination
        if (Vector2.Distance(transform.position, currentNavigationTargetPosition) < patrolPointReachedDistance) {
            reached = true;
        }
        // Condition 2: Le chemin est terminé (currentPath est null car fini dans MoveAlongPath)
        // ET on est quand même raisonnablement proche (évite de changer si le path a été annulé pour une autre raison)
        else if (currentPath == null && Vector2.Distance(transform.position, currentNavigationTargetPosition) < tileSize * 2f) {
             reached = true;
             // Debug.Log($"TEST [{gameObject.name}] CheckPatrolPointReached: Chemin nul près de la cible de patrouille.");
        }


        if (reached)
        {
            Debug.Log($"TEST [{gameObject.name}] CheckPatrolPointReached: Point de patrouille {currentNavigationTargetPosition} ATTEINT.");
            SelectNewPatrolPoint(); // Choisit une nouvelle destination
            currentPath = null;     // Force le recalcul du chemin vers la nouvelle destination au prochain HandlePathfinding
        }
    }

    /// <summary>
    /// Sélectionne un nouveau point de destination aléatoire et 'walkable' sur la grille.
    /// Met à jour currentNavigationTargetPosition.
    /// </summary>
    void SelectNewPatrolPoint()
    {
        bool pointFound = false;
        Vector2Int currentGridPos = WorldToGrid(transform.position); // Pour éviter de choisir la case actuelle si possible

        for (int i = 0; i < 30; i++) // Tente 30 fois de trouver un point
        {
            int randomX = Random.Range(0, gridSize.x);
            int randomY = Random.Range(0, gridSize.y);
            Vector2Int randomGridPos = new Vector2Int(randomX, randomY);

            // Vérifie si la case est marchable ET différente de la case actuelle
            if (IsWalkable(randomGridPos) && randomGridPos != currentGridPos)
            {
                currentNavigationTargetPosition = GridToWorld(randomGridPos); // Définit comme nouvelle cible de NAV
                lastTargetPosition = currentNavigationTargetPosition; // Mémorise pour éviter recalcul immédiat
                pointFound = true;
                 Debug.Log($"TEST [{gameObject.name}] SelectNewPatrolPoint: Nouvelle destination de patrouille aléatoire: {currentNavigationTargetPosition} (Grille: {randomGridPos})");
                break; // Sort de la boucle dès qu'un point est trouvé
            }
        }

        if (!pointFound)
        {
            // N'a pas réussi à trouver de point de patrouille valide après N essais
            // -> Passe en Idle pour éviter de boucler sur place
            Debug.LogWarning($"TEST [{gameObject.name}] SelectNewPatrolPoint: Impossible de trouver un point de patrouille valide après 30 essais. Passage en Idle.");
            currentState = AIState.Idle; // Stoppe la patrouille
            currentNavigationTargetPosition = transform.position; // Ne bouge pas
        }
    }

    #endregion

    #region Pathfinding (Dijkstra)

    /// <summary> Gère le calcul/recalcul du chemin vers currentNavigationTargetPosition. </summary>
    void HandlePathfinding() {
        timeSinceLastPathRecalc += Time.deltaTime;
        bool hasTarget = (currentState != AIState.Idle);

        // Debug.Log($"TEST [{gameObject.name}] HandlePathfinding - Nav Target Pos: {(hasTarget ? currentNavigationTargetPosition.ToString() : "Aucune")}, Etat: {currentState}");

        if (!hasTarget) { currentPath = null; return; }

        Vector2Int startGridPos = WorldToGrid(transform.position);
        Vector2Int endGridPos = WorldToGrid(currentNavigationTargetPosition);

        // Vérifie si on est DÉJÀ dans la case de destination finale
        if (startGridPos == endGridPos) {
            // Ne log plus l'avertissement ici si on patrouille car CheckPatrolPointReached s'en occupe
            if (currentState != AIState.Patrolling) {
                 Debug.LogWarning($"TEST [{gameObject.name}] HandlePathfinding: Déjà dans la case cible {endGridPos} (Start==End). Chemin annulé.");
            }
            currentPath = null;
            // Important : Ne pas faire 'return' ici si on patrouille, car on pourrait avoir besoin de recalculer
            // si la cible a changé dans la même frame via SelectNewPatrolPoint.
            // Le recalcul sera déclenché par pathInvalid=true ci-dessous.
        }

        // Logique de recalcul
        bool targetPosChanged = Vector3.Distance(currentNavigationTargetPosition, lastTargetPosition) > 0.1f;
        bool timerElapsed = timeSinceLastPathRecalc >= pathRecalculationRate;
        bool pathInvalid = currentPath == null || currentPath.Count == 0;
        // Recalcul si : chemin invalide OU timer écoulé OU (si Attaque ET joueur bouge) OU (si Patrouille ET cible de NAV a changé)
        bool needsRecalc = pathInvalid || timerElapsed || (currentState == AIState.AttackingPlayer && targetPosChanged) || (currentState == AIState.Patrolling && targetPosChanged);


        if (needsRecalc && startGridPos != endGridPos) // Ne recalcule que si start != end
        {
            // Debug.Log($"TEST [{gameObject.name}] HandlePathfinding: Recalcul du chemin demandé (Invalid:{pathInvalid}, Timer:{timerElapsed}, TargetMoved:{targetPosChanged})...");
            FindPath(currentNavigationTargetPosition, startGridPos, endGridPos);
            timeSinceLastPathRecalc = 0f;
            lastTargetPosition = currentNavigationTargetPosition;
        }
        // Si on est déjà dans la case cible (startGridPos == endGridPos), 'needsRecalc' peut être vrai mais on ne fait rien ici.
        // MoveAlongPath ne fera rien car currentPath est null. CheckPatrolPointReached choisira un nouveau point.
    }


    /// <summary> Calcule le chemin Dijkstra et met à jour currentPath. </summary>
    void FindPath(Vector3 targetNavPosition, Vector2Int startGridPos, Vector2Int endGridPos) {
        // (Code inchangé - calcule le chemin et log succès/échec)
         currentPath = Dijkstra(startGridPos, endGridPos);
        if (currentPath == null || currentPath.Count == 0) {
             if (startGridPos != endGridPos) {
                 Debug.LogWarning($"TEST [{gameObject.name}] FindPath: ECHEC Pathfinding de {startGridPos} vers {endGridPos} (Dest Monde: {targetNavPosition})");
             }
        } else {
            // Debug.Log($"TEST [{gameObject.name}] FindPath: REUSSI de {startGridPos} vers {endGridPos}. Chemin trouvé avec {currentPath.Count} points.");
        }
        if (currentPath != null && currentPath.Count > 0 && Vector2.Distance(transform.position, currentPath[0]) < tileSize * 0.1f) {
            currentPath.RemoveAt(0);
        }
        currentPathIndex = 0;
    }

    // --- Fonctions Dijkstra et Helpers ---
    // (COLLER LES FONCTIONS Dijkstra, ReconstructPath, GetNeighbors, IsWithinBounds, IsWalkable, WorldToGrid, GridToWorld, PriorityQueue ICI)
    // ... (Code identique aux versions précédentes) ...
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
        if (!cameFrom.ContainsKey(current)) { Debug.LogError("TEST ReconstructPath: Nœud cible non trouvé."); return totalPath; }
        Vector2Int startNode = cameFrom.FirstOrDefault(kvp => kvp.Key == kvp.Value).Key;
        Vector2Int step = current;
        int safety = 0; const int maxSteps = 10000;
        while (step != startNode && safety < maxSteps) {
            totalPath.Add(GridToWorld(step));
            if (!cameFrom.ContainsKey(step)) { Debug.LogError($"TEST ReconstructPath: Nœud {step} manquant."); break; }
            step = cameFrom[step];
            safety++;
        }
        if(safety >= maxSteps) Debug.LogError("TEST ReconstructPath: Limite de sécurité atteinte!");
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
    // --- Fin Dijkstra ---

    #endregion

    #region Mouvement & Rotation
    // (Fonctions MoveAlongPath, RotateChassisTowardsWaypoint, RotateCanonTowardsTarget - INCHANGÉES)
     void MoveAlongPath() {
        if (currentPath == null || currentPathIndex >= currentPath.Count) return;
        // Debug.Log($"TEST [{gameObject.name}] MoveAlongPath: Vers waypoint {currentPathIndex + 1}/{currentPath.Count}");
        Vector2 targetWaypoint = currentPath[currentPathIndex];
        transform.position = Vector2.MoveTowards(transform.position, targetWaypoint, moveSpeed * Time.deltaTime);
        if (Vector2.Distance(transform.position, targetWaypoint) < 0.1f) {
            currentPathIndex++;
            if (currentPathIndex >= currentPath.Count) {
                 // Debug.Log($"TEST [{gameObject.name}] MoveAlongPath: Chemin terminé."); // Déplacé dans CheckPatrolPointReached
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

    void RotateCanonTowardsTarget() {
        if (canonPivot == null) return;
        Vector2 direction = Vector2.zero;
        bool hasAimTarget = false;
        if (currentState == AIState.AttackingPlayer && currentTargetPlayer != null) {
            direction = ((Vector2)currentTargetPlayer.position - (Vector2)canonPivot.position).normalized;
            hasAimTarget = true;
        } else {
            direction = transform.up; // Vise devant
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
    // (Fonctions HandleShooting, Shoot - INCHANGÉES)
    void HandleShooting() {
        if (currentState != AIState.AttackingPlayer || currentTargetPlayer == null) return;
        float distanceToPlayer = Vector2.Distance(transform.position, currentTargetPlayer.position);
        if (distanceToPlayer <= shootingRange && Time.time >= nextFireTime) {
             // Debug.Log($"TEST [{gameObject.name}] HandleShooting: TIR sur {currentTargetPlayer.name}");
            Shoot();
            nextFireTime = Time.time + fireRate;
        }
    }

    void Shoot() {
        if (bulletPrefab == null || firePoint == null) return;
        GameObject bulletInstance = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        BulletDestruction bulletScript = bulletInstance.GetComponent<BulletDestruction>();
        if (bulletScript != null) {
            bulletScript.shooter = this.gameObject;
            bulletScript.shooterTag = this.gameObject.tag;
        } else {
             Debug.LogError($"Prefab balle '{bulletPrefab.name}' manque script BulletDestruction!", bulletPrefab);
        }
    }
    #endregion
}