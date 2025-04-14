public class NeutralState : IState
{
    private CapturePoint owner;
    public NeutralState(CapturePoint owner) { this.owner = owner; }

    public void Enter() {
        // Debug.Log($"Entering Neutral State for {owner.pointName}");
        owner.controllingTeamTag = null;
        owner.capturingTeamTag = null;
        owner.ResetCaptureProgress();
        owner.UpdateVisuals();
    }

    public void Execute() {
        int playersInZone = owner.GetTeamCountInZone("Player");
        int enemiesInZone = owner.GetTeamCountInZone("Enemy");

        if (playersInZone > 0 && enemiesInZone == 0) {
            // Le joueur commence à capturer
            owner.capturingTeamTag = "Player";
            owner.SetState(new CapturingState(owner));
        } else if (enemiesInZone > 0 && playersInZone == 0) {
            // L'ennemi commence à capturer
            owner.capturingTeamTag = "Enemy";
            owner.SetState(new CapturingState(owner));
        } else if (playersInZone > 0 && enemiesInZone > 0) {
            // Zone contestée
            owner.SetState(new ContestedState(owner));
        }
        // Si personne n'est dans la zone, on reste Neutre
    }

    public void Exit() {
        // Debug.Log($"Exiting Neutral State for {owner.pointName}");
    }
}