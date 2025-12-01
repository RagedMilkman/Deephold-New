using FishNet.Object;
using UnityEngine;

/// Base for any local-player interaction (movement, toolbelt, firing, etc.)
/// Handles: isOwner + PlayerState (Alive) gating, auto-caching, and state events.
public abstract class PlayerInteraction : NetworkBehaviour
{
    [Header("Interaction Gating")]
    [SerializeField] protected bool requireOwner = true;   // local-only control by default
    [SerializeField] protected bool requireAlive = true;   // block when Dead
    [SerializeField] protected bool allowOnServer = false;  // rarely needed; most input is client-side

    protected CharacterState playerState;
    protected bool isAlive = true;

    // Convenience: are we allowed to run "active" logic this frame?
    protected bool IsActive
        => (!requireOwner || IsOwner)
        && (allowOnServer || IsClient)
        && (!requireAlive || isAlive);

    protected virtual void Awake()
    {
        // Resolve PlayerState on the same root
        playerState = GetComponent<CharacterState>();
        if (!playerState)
            playerState = GetComponentInParent<CharacterState>(true);
    }

    public override void OnStartServer()
    {
        // Seed cached isAlive and subscribe to updates
        if (playerState != null)
        {
            isAlive = playerState.State != LifeState.Dead;
          //  playerState.OnStateChanged += HandleStateChanged;
        }

        // Template hook
        OnInteractionSpawned(IsServer);
    }

    protected virtual void OnDestroy()
    {
      //  if (playerState != null)
     //       playerState.OnStateChanged -= HandleStateChanged;

        OnInteractionDestroyed();
    }

    void HandleStateChanged(int hp, int maxHp, LifeState state)
    {
        bool wasAlive = isAlive;
        isAlive = (state != LifeState.Dead);

        if (wasAlive != isAlive)
        {
            if (isAlive) OnBecameAlive();
            else OnBecameDead();
        }
        OnStateChanged(hp, maxHp, state);
    }

    // ---------- Frame driving (Template Method pattern) ----------
    protected virtual void Update()
    {
        if (IsActive) OnActiveUpdate();
        else OnInactiveUpdate();
    }

    protected virtual void FixedUpdate()
    {
        if (IsActive) 
            OnActiveFixedUpdate();
        else
            OnInactiveFixedUpdate();
    }

    // ---------- Overridables for children ----------
    protected virtual void OnInteractionSpawned(bool asServer) { }
    protected virtual void OnInteractionDestroyed() { }

    /// Called every Update when all gates pass (owner/client/alive as configured)
    protected virtual void OnActiveUpdate() { }

    /// Called every FixedUpdate when all gates pass
    protected virtual void OnActiveFixedUpdate() { }

    /// Called when gates fail (e.g., dead or not owner). Good place to zero inputs / stop effects.
    protected virtual void OnInactiveUpdate() { }
    protected virtual void OnInactiveFixedUpdate() { }

    /// Called when life state flips
    protected virtual void OnBecameAlive() { }
    protected virtual void OnBecameDead() { }

    /// Called on any state change (hp/max/state)
    protected virtual void OnStateChanged(int hp, int maxHp, LifeState state) { }
}
