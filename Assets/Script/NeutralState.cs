using UnityEngine; // N'oubliez pas les using !

public class NeutralState : IState
{
    private CapturePoint owner;
    public NeutralState(CapturePoint owner) { this.owner = owner; }

    public void Enter() {
        // Debug.Log($"Entering Neutral State for {owner.pointName}");
        owner.controllingTeamTag = null;
        owner.capturingTeamTag = null;
        owner.ResetCaptureProgress();
        owner.SetStatus(PointStatus.Neutral); // <-- Met à jour le statut public
        owner.UpdateVisuals();
    }

    public void Execute() {
        int playersInZone = owner.GetTeamCountInZone("Player");
        int enemiesInZone = owner.GetTeamCountInZone("Enemy");

        if (playersInZone > 0 && enemiesInZone == 0) {
            owner.capturingTeamTag = "Player";
            owner.SetState(new CapturingState(owner));
        } else if (enemiesInZone > 0 && playersInZone == 0) {
            owner.capturingTeamTag = "Enemy";
            owner.SetState(new CapturingState(owner));
        } else if (playersInZone > 0 && enemiesInZone > 0) {
            owner.SetState(new ContestedState(owner));
        }
    }

    public void Exit() { }
}