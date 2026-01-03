using Assets.Scripts.Items.Weapons;
using UnityEngine;

/// <summary>
/// Behaviour that selects an engagement tactic for the agent.
/// </summary>
public class EngageBehaviour : BehaviourBase
{
    [Header("Dependencies")]
    [SerializeField] private CombatActions combatActions;
    [SerializeField] private ToolbeltActuator toolbeltActuator;
    [SerializeField] private EngageTacticBehaviour[] tactics;

    private readonly System.Collections.Generic.Dictionary<EngageTactic, EngageTacticBehaviour> tacticLookup
        = new System.Collections.Generic.Dictionary<EngageTactic, EngageTacticBehaviour>();
    private EngageTacticBehaviour activeTactic;

    protected override void Awake()
    {
        base.Awake();

        if (!combatActions)
            combatActions = GetComponentInParent<CombatActions>();

        if (!toolbeltActuator)
            toolbeltActuator = GetComponentInParent<ToolbeltActuator>();

        if (tactics == null || tactics.Length == 0)
            tactics = GetComponents<EngageTacticBehaviour>();

        if (tactics != null)
        {
            foreach (var tactic in tactics)
            {
                if (tactic == null)
                    continue;

                if (tacticLookup.ContainsKey(tactic.TacticType))
                    continue;

                tactic.Initialize(this, Knowledge, combatActions);
                tacticLookup.Add(tactic.TacticType, tactic);
            }
        }
    }

    public override IntentType IntentType => IntentType.Engage;

    public override void BeginBehaviour(IIntent intent)
    {
        SwitchTactic(intent as EngageIntent);
    }

    public override void TickBehaviour(IIntent intent)
    {
        var engageIntent = intent as EngageIntent;
        if (engageIntent == null)
            return;

        EnsureEngagementWeapon();
        SwitchTactic(engageIntent);
        activeTactic?.Tick(engageIntent);
    }

    public override void EndBehaviour()
    {
        if (activeTactic != null)
        {
            activeTactic.OnEnd();
            activeTactic = null;
        }
    }

    private void SwitchTactic(EngageIntent intent)
    {
        var tactic = ResolveTactic(intent);
        if (tactic == activeTactic)
            return;

        activeTactic?.OnEnd();
        activeTactic = tactic;
        activeTactic?.OnBegin(intent);
    }

    private EngageTacticBehaviour ResolveTactic(EngageIntent intent)
    {
        var tacticType = intent?.Tactics?.Tactic ?? EngageTactic.Pursue;

        if (tacticLookup.TryGetValue(tacticType, out var tactic))
            return tactic;

        if (tacticLookup.TryGetValue(EngageTactic.Pursue, out var pursueTactic))
            return pursueTactic;

        return tactics != null && tactics.Length > 0 ? tactics[0] : null;
    }

    private void EnsureEngagementWeapon()
    {
        if (!toolbeltActuator)
            return;

        var toolbelt = toolbeltActuator.Toolbelt;
        if (!toolbelt)
            return;

        var equippedWeapon = toolbeltActuator.EquippedWeapon;
        if (equippedWeapon != null)
        {
            var ammoType = equippedWeapon.AmmoType;
            if (ammoType == AmmoType.None || toolbelt.HasAmmo(ammoType))
                return;
        }

        int[] slotOrder = { 1, 2, 3 };

        foreach (int slot in slotOrder)
        {
            if (!toolbelt.SlotHasUsableAmmo(slot))
                continue;

            if (toolbelt.CurrentSlot != slot)
                toolbeltActuator.ChangeEquippedItem(slot);

            return;
        }
    }
}
