// --- Interface/Classe de base pour tous les états ---
public interface IState
{
    void Enter();    // Exécuté quand on entre dans l'état
    void Execute();  // Exécuté à chaque Update tant qu'on est dans l'état
    void Exit();     // Exécuté quand on quitte l'état
}

// --- Classe qui gère l'état actuel ---
public class StateMachine
{
    public IState CurrentState { get; private set; }

    public void ChangeState(IState newState)
    {
        CurrentState?.Exit(); // Appelle Exit sur l'ancien état s'il existe
        CurrentState = newState;
        CurrentState.Enter(); // Appelle Enter sur le nouvel état
    }

    public void Update()
    {
        CurrentState?.Execute(); // Appelle Execute sur l'état actuel
    }
}