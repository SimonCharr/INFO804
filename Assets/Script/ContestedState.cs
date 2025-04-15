using UnityEngine; // N'oubliez pas les using !

public class ContestedState : IState
{
    private CapturePoint owner;
    public ContestedState(CapturePoint owner) { this.owner = owner; }

    public void Enter() {
       // Debug.Log($"Entering Contested State for {owner.pointName}");
        owner.controllingTeamTag = null;
        owner.capturingTeamTag = null;
        owner.ResetCaptureProgress();
        owner.SetStatus(PointStatus.Contested);
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
        } else if (playersInZone == 0 && enemiesInZone == 0) {
            owner.SetState(new NeutralState(owner));
        }
        // Si toujours contesté, reste dans cet état
    }

    public void Exit() { }
}