using UnityEngine;
using PurrNet;

public class YawReplicator : NetworkBehaviour
{
    [SerializeField] Transform rotateTarget;  // same target as motor
    [SerializeField] TopDownMotor motor;
    private SyncVar<float> yawDeg = new(0f, ownerAuth: true);

    void Awake()
    {
        if (!rotateTarget) rotateTarget = transform; // fallback
        if (!motor) motor = GetComponent<TopDownMotor>();
    }

    protected override void OnSpawned(bool asServer)
    {
        // apply replicated yaw for late joiners / remotes
        if (motor)
            motor.ApplyReplicatedYaw(yawDeg.value);
        else
            TopDownMotor.ApplyYawTo(rotateTarget, yawDeg.value);
    }

    void Update()
    {
        // Non-owners: apply replicated yaw every frame
        if (!isOwner)
        {
            if (motor)
                motor.ApplyReplicatedYaw(yawDeg.value);
            else
                TopDownMotor.ApplyYawTo(rotateTarget, yawDeg.value);
        }
    }

    public void OwnerSetYaw(float yaw)
    {
        if (!isOwner) return;
        yawDeg.value = yaw;  // replicated to others
    }
}
