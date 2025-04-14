using UnityEngine;

public class CapturingState : IState
{
    private CapturePoint owner;
    private string teamCapturing; // Qui capture ("Player" ou "Enemy")

    public CapturingState(CapturePoint owner) {
        this.owner = owner;
        this.teamCapturing = owner.capturingTeamTag; // Récupère qui capture depuis le propriétaire
    }

    public void Enter() {
       // Debug.Log($"Entering Capturing State for {owner.pointName} by {teamCapturing}");
        owner.controllingTeamTag = null; // Pas encore contrôlé
    }

    public void Execute() {
        int capturingTeamCount = owner.GetTeamCountInZone(teamCapturing);
        // L'autre équipe
        string opposingTeam = (teamCapturing == "Player") ? "Enemy" : "Player";
        int opposingTeamCount = owner.GetTeamCountInZone(opposingTeam);

        if (capturingTeamCount > 0 && opposingTeamCount == 0) {
            // Continue la capture
            owner.currentCaptureProgress += Time.deltaTime;
            owner.UpdateVisuals(); // Met à jour la couleur pendant la capture

            if (owner.currentCaptureProgress >= owner.captureTime) {
                // Capture terminée !
                owner.controllingTeamTag = teamCapturing;
                owner.SetState(new CapturedState(owner));
            }
        } else if (capturingTeamCount == 0 && opposingTeamCount == 0) {
             // L'équipe qui capturait est partie, personne d'autre n'est là
             owner.SetState(new NeutralState(owner)); // Retour à neutre (reset progress dans Enter de Neutral)
        }
        else {
            // Contesté (l'autre équipe est arrivée ou l'équipe qui capturait est partie mais l'autre est là)
             owner.SetState(new ContestedState(owner)); // Reset progress dans Enter de Contested
        }
    }

    public void Exit() {
       // Debug.Log($"Exiting Capturing State for {owner.pointName}");
       // Ne pas reset le progrès ici si on va vers Captured
    }
}