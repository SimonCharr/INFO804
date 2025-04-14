using UnityEngine;

public class CapturedState : IState
{
    private CapturePoint owner;        // Référence au point de capture
    private string teamControlling;  // Quelle équipe contrôle ce point

    // Constructeur
    public CapturedState(CapturePoint owner)
    {
        this.owner = owner;
        // Récupère l'équipe qui contrôle depuis le propriétaire
        this.teamControlling = owner.controllingTeamTag;
    }

    public void Enter()
    {
        // Debug.Log($"Entering Captured State for {owner.pointName} by {teamControlling}");
        // Assure que le progrès est au max (ou non pertinent ici)
        owner.currentCaptureProgress = owner.captureTime;
        owner.capturingTeamTag = null; // Plus personne ne capture activement
        owner.UpdateVisuals(); // Met la couleur de l'équipe qui contrôle

        // C'est ici qu'on pourrait notifier un GameManager
        // GameManager.Instance?.PointCaptured(owner.pointName, teamControlling);
    }

    public void Execute()
    {
        // Vérifie si l'équipe adverse entre dans la zone
        string opposingTeam = (teamControlling == "Player") ? "Enemy" : "Player";
        int opposingTeamCount = owner.GetTeamCountInZone(opposingTeam);

        if (opposingTeamCount > 0)
        {
            // Si l'équipe adverse entre -> Zone contestée
            // Note: On pourrait avoir un état intermédiaire "Neutralizing" si on veut
            // que l'ennemi doive d'abord neutraliser avant de capturer.
            // Pour l'instant, on passe directement en contesté.
            owner.SetState(new ContestedState(owner));
        }

        // Optionnel: Vérifier si l'équipe qui contrôle quitte la zone.
        // Que se passe-t-il si la zone est capturée mais vide ? Reste-t-elle capturée ?
        // Pour l'instant, oui. Elle ne change d'état que si l'ennemi arrive.
        // int controllingTeamCount = owner.GetTeamCountInZone(teamControlling);
        // if (controllingTeamCount == 0 && opposingTeamCount == 0) {
        //     // Rester Capturé OU retourner à Neutre ? Design choice. On reste capturé pour l'instant.
        // }
    }

    public void Exit()
    {
        // Debug.Log($"Exiting Captured State for {owner.pointName}");
        // Si on quitte cet état, c'est forcément vers Contesté (selon la logique actuelle)
        // On pourrait notifier le GameManager que le point n'est plus contrôlé.
        // GameManager.Instance?.PointLostControl(owner.pointName);
    }
}