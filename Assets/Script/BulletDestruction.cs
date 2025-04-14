using UnityEngine;

public class BulletDestruction : MonoBehaviour
{
    [Header("Propriétés de la Balle")]
    public float lifeTime = 2f;      // Durée de vie en secondes
    public float damage = 25f;        // Dégâts infligés par la balle

    [Header("Informations sur le Tireur")]
    [Tooltip("Assigné automatiquement au moment du tir")]
    public GameObject shooter;       // Référence au GameObject qui a tiré
    [Tooltip("Assigné automatiquement au moment du tir (ex: 'Player' ou 'Enemy')")]
    public string shooterTag;        // Tag du GameObject qui a tiré

    void Start()
    {
        // Détruit la balle après 'lifeTime' secondes si elle n'a rien touché
        Destroy(gameObject, lifeTime);
    }

    // CHANGEMENT ICI : Utiliser OnTriggerEnter2D au lieu de OnCollisionEnter2D
    void OnTriggerEnter2D(Collider2D otherCollider) // <-- Changement de nom et de paramètre
    {
        // --- 1. Éviter l'auto-destruction ---
        // Si la balle touche le tank qui l'a tirée, on ignore la collision
        // CHANGEMENT ICI : Utiliser otherCollider.gameObject
        if (otherCollider.gameObject == shooter)
        {
            // Debug.Log("Collision Trigger avec le tireur ignorée.");
            return; // Ne fait rien et ne se détruit pas encore
        }

        // Debug.Log($"Collision Trigger de la balle détectée avec {otherCollider.gameObject.name} (Tag: {otherCollider.gameObject.tag})");

        // --- 2. Tenter d'infliger des dégâts ---
        // Récupère le composant TankHealth de l'objet touché
        // CHANGEMENT ICI : Utiliser otherCollider.gameObject
        TankHealth targetHealth = otherCollider.gameObject.GetComponent<TankHealth>();

        // Vérifie si l'objet touché a un composant TankHealth ET si ce n'est pas un allié
        // CHANGEMENT ICI : Utiliser otherCollider.gameObject.tag
        if (targetHealth != null && otherCollider.gameObject.tag != shooterTag)
        {
            // Inflige les dégâts à la cible
            Debug.Log($"Balle tirée par {shooterTag} inflige {damage} dégâts à {otherCollider.gameObject.name} (Tag: {otherCollider.gameObject.tag})");
            targetHealth.TakeDamage(damage);

            // --- 3. Détruire la balle APRÈS avoir infligé les dégâts ---
            // La balle est détruite après avoir touché quelque chose (sauf son tireur)
            // Debug.Log("Balle détruite après collision Trigger.");
            Destroy(gameObject); // Important : Détruire ici pour que la balle ne traverse pas plusieurs ennemis
        }
        // Gérer le cas du tir ami (optionnel, mais propre)
        else if (targetHealth != null && otherCollider.gameObject.tag == shooterTag)
        {
            Debug.Log($"Tir ami sur {otherCollider.gameObject.name} ignoré (Trigger).");
             // On pourrait aussi détruire la balle ici si on veut qu'elle disparaisse au contact d'un allié
             // Destroy(gameObject);
        }
        // Gérer la collision avec d'autres objets (murs, etc.)
        // Si l'objet touché n'a PAS de TankHealth (ex: un mur)
        else if(targetHealth == null)
        {
             // Détruire la balle au contact d'un obstacle
             // Debug.Log($"Balle détruite au contact de {otherCollider.gameObject.name}");
             Destroy(gameObject);
        }

        // Note : La destruction pour les cas "Tir Ami" et "Obstacle" a été ajoutée ci-dessus.
        // La destruction ne se fait plus systématiquement à la fin, mais seulement
        // quand un ennemi est touché ou quand un obstacle/allié est touché.
    }
}