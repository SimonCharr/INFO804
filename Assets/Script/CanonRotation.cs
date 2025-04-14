using UnityEngine;

public class CanonRotation : MonoBehaviour
{
    void Update()
    {
        RotateCanonTowardsMouse();
    }

    void RotateCanonTowardsMouse()
    {
        // Obtient la position de la souris dans le monde du jeu
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        // Calcule la direction du canon vers la souris
        Vector2 direction = mousePosition - transform.position;

        // Calcule l'angle de rotation en degrés
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;

        // Applique la rotation au canon
        transform.rotation = Quaternion.Euler(new Vector3(0, 0, angle));
    }
}