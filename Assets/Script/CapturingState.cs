using UnityEngine; // N'oubliez pas les using !

public class CapturingState : IState
{
    private CapturePoint owner;
    private string teamCapturing;

    public CapturingState(CapturePoint owner) {
        this.owner = owner;
        this.teamCapturing = owner.capturingTeamTag;
    }

    public void Enter() {
        // Debug.Log($"Entering Capturing State for {owner.pointName} by {teamCapturing}");
        owner.controllingTeamTag = null;
        // Met à jour le statut public selon l'équipe qui capture
        owner.SetStatus( (teamCapturing == "Player") ? PointStatus.CapturingPlayer : PointStatus.CapturingEnemy );
        // owner.UpdateVisuals(); // Pas forcément nécessaire ici si Execute le fait
    }

    public void Execute() {
        int capturingTeamCount = owner.GetTeamCountInZone(teamCapturing);
        string opposingTeam = (teamCapturing == "Player") ? "Enemy" : "Player";
        int opposingTeamCount = owner.GetTeamCountInZone(opposingTeam);

        if (capturingTeamCount > 0 && opposingTeamCount == 0) {
            owner.currentCaptureProgress += Time.deltaTime;
            owner.UpdateVisuals(); // Met à jour l'indicateur de progrès

            if (owner.currentCaptureProgress >= owner.captureTime) {
                owner.controllingTeamTag = teamCapturing;
                owner.SetState(new CapturedState(owner));
            }
        } else if (capturingTeamCount == 0 && opposingTeamCount == 0) {
             owner.SetState(new NeutralState(owner));
        } else { // Contesté
             owner.SetState(new ContestedState(owner));
        }
    }

    public void Exit() { }
}