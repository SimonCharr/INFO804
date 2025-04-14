using UnityEngine;

public class ContestedState : IState
{
    private CapturePoint owner; // Référence au point de capture

    // Constructeur
    public ContestedState(CapturePoint owner) { this.owner = owner; }

    public void Enter()
    {
       // Debug.Log($"Entering Contested State for {owner.pointName}");
        // La zone n'est plus contrôlée ni en cours de capture par une seule équipe
        owner.controllingTeamTag = null;
        owner.capturingTeamTag = null;
        owner.ResetCaptureProgress(); // Le progrès est stoppé et remis à zéro
        owner.UpdateVisuals(); // Met la couleur contestée (jaune?)
    }

    public void Execute()
    {
        // Vérifie qui reste dans la zone
        int playersInZone = owner.GetTeamCountInZone("Player");
        int enemiesInZone = owner.GetTeamCountInZone("Enemy");

        if (playersInZone > 0 && enemiesInZone == 0)
        {
            // Si seulement le joueur reste -> Le joueur recommence à capturer
            owner.capturingTeamTag = "Player";
            owner.SetState(new CapturingState(owner));
        }
        else if (enemiesInZone > 0 && playersInZone == 0)
        {
            // Si seulement l'ennemi reste -> L'ennemi recommence à capturer
            owner.capturingTeamTag = "Enemy";
            owner.SetState(new CapturingState(owner));
        }
        else if (playersInZone == 0 && enemiesInZone == 0)
        {
            // Si tout le monde est parti -> Retour à l'état Neutre
            owner.SetState(new NeutralState(owner));
        }
        // Si les deux équipes sont toujours présentes (playersInZone > 0 && enemiesInZone > 0),
        // on reste dans l'état Contesté.
    }

    public void Exit()
    {
       // Debug.Log($"Exiting Contested State for {owner.pointName}");
    }
}