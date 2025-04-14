using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Suivi")]
    public Transform target; // La cible à suivre (le tank joueur)
    // La variable smoothSpeed n'est plus nécessaire pour le suivi direct
    // public float smoothSpeed = 0.125f;
    public Vector3 offset; // Décalage par rapport à la cible (ex: 0, 0, -10)

    [Header("Zoom (Caméra Orthographique)")]
    [Tooltip("Vitesse de zoom avec la molette.")]
    public float zoomSpeed = 4f;
    [Tooltip("Taille orthographique minimale (zoom maximum).")]
    public float minZoom = 2f;
    [Tooltip("Taille orthographique maximale (dézoom maximum).")]
    public float maxZoom = 15f;

    // Référence privée à la caméra
    private Camera cam;

    void Awake()
    {
        // Récupère le composant Camera attaché à ce GameObject
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            Debug.LogError("Le script CameraFollow nécessite un composant Camera sur le même GameObject!", this);
            enabled = false; // Désactive le script s'il n'y a pas de caméra
            return;
        }

        // Vérification importante : ce type de zoom fonctionne mieux avec une caméra orthographique
        if (!cam.orthographic)
        {
            Debug.LogWarning("Le zoom par taille orthographique fonctionne mieux avec une caméra en mode 'Orthographic'. La caméra actuelle est en mode 'Perspective'.", this);
        }
    }

    void Update() // Update est souvent utilisé pour les inputs
    {
        // --- Gestion du Zoom ---
        HandleZoom();
    }

    // LateUpdate est toujours recommandé pour les mouvements de caméra
    // pour s'assurer que la cible a terminé son mouvement pour la frame.
    void LateUpdate()
    {
        // --- Gestion du Suivi de Cible ---
        HandleInstantFollowing();
    }

    /// <summary>
    /// Gère le zoom en modifiant la taille orthographique de la caméra
    /// en fonction de l'input de la molette de la souris.
    /// </summary>
    void HandleZoom()
    {
        if (!cam.orthographic) return; // Ne fait rien si la caméra n'est pas orthographique

        float scrollInput = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            float newSize = cam.orthographicSize - scrollInput * zoomSpeed;
            cam.orthographicSize = Mathf.Clamp(newSize, minZoom, maxZoom);
        }
    }

    /// <summary>
    /// Déplace la caméra INSTANTANÉMENT pour suivre la cible.
    /// </summary>
    void HandleInstantFollowing()
    {
        if (target != null)
        {
            // Calcule la position désirée (position de la cible + décalage)
            Vector3 desiredPosition = target.position + offset;

            // Applique DIRECTEMENT la nouvelle position à la caméra
            // Pas de Lerp, donc pas de lissage/transition.
            transform.position = desiredPosition;
        }
    }
}