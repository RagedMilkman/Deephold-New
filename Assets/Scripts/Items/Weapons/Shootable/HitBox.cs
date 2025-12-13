using Assets.Scripts.Items.Weapons;
using FishNet.Object;
using UnityEngine;

public enum BodyPart
{
    Head,
    Torso,
    ArmL,
    ArmR,
    LegL,
    LegR
}

public enum PuppetBodyPart
{
    Auto,
    Hips,
    Spine,
    Chest,
    UpperChest,
    Head,
    LeftUpperArm,
    LeftLowerArm,
    LeftHand,
    RightUpperArm,
    RightLowerArm,
    RightHand,
    LeftUpperLeg,
    LeftLowerLeg,
    LeftFoot,
    RightUpperLeg,
    RightLowerLeg,
    RightFoot
}

public class HitBox : MonoBehaviour, IShootable
{
    [SerializeField] CharacterHealth owner;
    [SerializeField] BodyPart bodyPart;
    [SerializeField] float damageMultiplier = 1f;

    [SerializeField, Tooltip("Optional PuppetMaster bone to apply forces to. Set to Auto to fallback to nearest bone.")]
    PuppetBodyPart puppetBodyPart = PuppetBodyPart.Auto;

    // Now private  auto-populated from PuppetMaster
    [SerializeField, Tooltip("Debug only  auto-assigned at runtime")]
    int puppetMasterMuscleIndex = -1;

    public Transform OwnerRoot => owner ? owner.OwnerRoot : transform.root;

    public CharacterHealth Owner => owner;
    public BodyPart BodyPart => bodyPart;
    public float DamageMultiplier => damageMultiplier;
    public int PuppetMasterMuscleIndex => puppetMasterMuscleIndex;

    void Awake()
    {
        if (!owner)
            owner = GetComponentInParent<CharacterHealth>(true);

        if (owner)
            SetOwner(owner);
    }

    public void SetOwner(CharacterHealth newOwner)
    {
        owner = newOwner;

        puppetMasterMuscleIndex = -1;

        if (owner != null && owner.PuppetMaster != null)
        {
            puppetMasterMuscleIndex = owner.PuppetMaster.GetMuscleIndex(owner.Animator, puppetBodyPart, transform);
        }
    }

    public void ApplyHit(float baseDamage, Vector3 hitPoint, Vector3 hitDir, float force, NetworkObject shooter = null)
    {
        if (!owner)
            return;

        float finalDamage = baseDamage * damageMultiplier;
        int hitBoxIndex = owner.GetHitBoxIndex(this);
        owner.OnHit(bodyPart, finalDamage, hitPoint, hitDir, force, puppetMasterMuscleIndex, hitBoxIndex, shooter);
    }

    public bool CanBeShot(NetworkObject shooter, Vector3 point, Vector3 normal)
    {
        return owner == null || owner.CanBeShot(shooter, point, normal);
    }

    public void ServerOnShot(NetworkObject shooter, float damage, float force, Vector3 point, Vector3 normal)
    {
        if (owner != null && !owner.IsServer)
            return;

        // normal points *out* of the surface, so incoming dir is -normal
        ApplyHit(damage, point, -normal, force, shooter);
    }
}
