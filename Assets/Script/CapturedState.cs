using UnityEngine; // N'oubliez pas les using !

public class CapturedState : IState
{
    private CapturePoint owner;
    private string teamControlling;

    public CapturedState(CapturePoint owner) {
        this.owner = owner;
        this.teamControlling = owner.controllingTeamTag;
    }

    public void Enter() {
        // Debug.Log($"Entering Captured State for {owner.pointName} by {teamControlling}");
        owner.currentCaptureProgress = owner.captureTime;
        owner.capturingTeamTag = null;
        // Met à jour le statut public selon l'équipe qui contrôle
        owner.SetStatus( (teamControlling == "Player") ? PointStatus.ControlledPlayer : PointStatus.ControlledEnemy );
    owner.UpdateVisuals();

        // Potentiel appel au GameManager
        // GameManager.Instance?.NotifyPointCaptured(owner, teamControlling);
    }

    public void Execute() {
        string opposingTeam = (teamControlling == "Player") ? "Enemy" : "Player";
        int opposingTeamCount = owner.GetTeamCountInZone(opposingTeam);

        if (opposingTeamCount > 0) {
            owner.SetState(new ContestedState(owner));
        }
        // Sinon, reste capturé
    }

    public void Exit() {
         // Potentiel appel au GameManager
         // GameManager.Instance?.PointLostControl(owner.pointName);
    }
}