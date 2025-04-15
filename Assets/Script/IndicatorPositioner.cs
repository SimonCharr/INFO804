using UnityEngine;

public class IndicatorPositioner : MonoBehaviour
{
    [Tooltip("Le Transform du tank joueur à suivre")]
    public Transform targetToFollow;

    [Tooltip("Décalage par rapport à la cible (surtout en Y pour la hauteur)")]
    public Vector3 positionOffset = new Vector3(0, 1.5f, 0); // Ajustez Y si besoin

    // Garde une référence à la caméra pour positionnement relatif potentiel (optionnel)
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        if (targetToFollow == null) {
            // Essaye de trouver le joueur par tag si non assigné
            GameObject player = GameObject.FindGameObjectWithTag("Player");
             if (player != null) {
                targetToFollow = player.transform;
             } else {
                 Debug.LogError("IndicatorPositioner : TargetToFollow non assigné et joueur (tag 'Player') non trouvé!", this);
                 enabled = false;
             }
        }
    }

    // LateUpdate est préférable pour suivre un objet qui bouge dans Update
    void LateUpdate()
    {
        if (targetToFollow != null)
        {
            // Met simplement à jour la position pour correspondre à celle de la cible + offset
            transform.position = targetToFollow.position + positionOffset;

            // IMPORTANT : On ne touche PAS à transform.rotation ici !
            // L'objet Indicators garde sa rotation par défaut (généralement 0,0,0 monde).
        }
    }
}