using UnityEngine;

// Ce script nécessite un Rigidbody2D sur le même GameObject (la balle)
[RequireComponent(typeof(Rigidbody2D))]
public class BulletMovement : MonoBehaviour
{
    [Header("Paramètres de Mouvement")]
    public float speed = 20f; // Vitesse de déplacement de la balle

    private Rigidbody2D rb; // Référence au composant Rigidbody2D

    void Awake()
    {
        // Récupère le composant Rigidbody2D attaché à cette balle
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError("Le script BulletMovement nécessite un Rigidbody2D sur la balle !", this);
            enabled = false; // Désactive ce script s'il manque le Rigidbody2D
        }
    }

    void Start()
    {
        // Applique une vélocité initiale à la balle dès sa création.
        // Cette vélocité est constante et dirigée vers "l'avant" de la balle.

        // IMPORTANT : 'transform.up' représente l'axe Y local (la flèche VERTE dans l'éditeur).
        // Si, lors du tir (dans TankController/EnemyTankController), vous avez utilisé
        // un angle avec un décalage de -90 degrés (comme c'est courant), alors
        // l'axe Y local de la balle correspond bien à sa direction "avant".
        // Si vous n'avez pas ce décalage de -90f, l'avant serait probablement 'transform.right' (axe X local ROUGE).
        if (rb != null)
        {
            rb.linearVelocity = transform.up * speed;
        }
    }

    // Note : Puisqu'on définit la vélocité une fois dans Start,
    // la fonction Update() n'est pas nécessaire pour ce mouvement simple.
    // Le moteur physique de Unity maintiendra la vélocité.

    // Si vous préfériez utiliser transform.Translate (moins recommandé avec Rigidbody) :
    // void Update()
    // {
    //     // Déplace la balle vers son avant local à chaque frame
    //     transform.Translate(Vector3.up * speed * Time.deltaTime);
    // }
}