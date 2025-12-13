using RootMotion.Dynamics;
using UnityEngine;

public static class PuppetMasterExtensions
{
    public static int GetMuscleIndex(this PuppetMaster pm, Animator animator, PuppetBodyPart bodyPart, Transform fallback)
    {
        if (bodyPart == PuppetBodyPart.Auto)
            return pm.GetClosestMuscleIndex(fallback);

        var targetBone = MapBodyPartToTransform(animator, bodyPart);
        if (targetBone == null)
            return pm.GetClosestMuscleIndex(fallback);

        return pm.GetClosestMuscleIndex(targetBone);
    }

    public static int GetClosestMuscleIndex(this PuppetMaster pm, Transform t)
    {
        if (pm == null || pm.muscles == null)
            return -1;

        // Direct match first
        for (int i = 0; i < pm.muscles.Length; i++)
        {
            if (pm.muscles[i].joint.transform == t)
                return i;
        }

        // Otherwise climb up the hierarchy until we find a bone that DOES have a muscle
        Transform current = t.parent;
        while (current != null)
        {
            for (int i = 0; i < pm.muscles.Length; i++)
            {
                if (pm.muscles[i].joint.transform == current)
                    return i;
            }
            current = current.parent;
        }

        return -1;
    }

    static Transform MapBodyPartToTransform(Animator animator, PuppetBodyPart bodyPart)
    {
        if (animator == null || animator.avatar == null || !animator.isHuman)
            return null;

        var humanBone = bodyPart switch
        {
            PuppetBodyPart.Hips => HumanBodyBones.Hips,
            PuppetBodyPart.Spine => HumanBodyBones.Spine,
            PuppetBodyPart.Chest => HumanBodyBones.Chest,
            PuppetBodyPart.UpperChest => HumanBodyBones.UpperChest,
            PuppetBodyPart.Head => HumanBodyBones.Head,
            PuppetBodyPart.LeftUpperArm => HumanBodyBones.LeftUpperArm,
            PuppetBodyPart.LeftLowerArm => HumanBodyBones.LeftLowerArm,
            PuppetBodyPart.LeftHand => HumanBodyBones.LeftHand,
            PuppetBodyPart.RightUpperArm => HumanBodyBones.RightUpperArm,
            PuppetBodyPart.RightLowerArm => HumanBodyBones.RightLowerArm,
            PuppetBodyPart.RightHand => HumanBodyBones.RightHand,
            PuppetBodyPart.LeftUpperLeg => HumanBodyBones.LeftUpperLeg,
            PuppetBodyPart.LeftLowerLeg => HumanBodyBones.LeftLowerLeg,
            PuppetBodyPart.LeftFoot => HumanBodyBones.LeftFoot,
            PuppetBodyPart.RightUpperLeg => HumanBodyBones.RightUpperLeg,
            PuppetBodyPart.RightLowerLeg => HumanBodyBones.RightLowerLeg,
            PuppetBodyPart.RightFoot => HumanBodyBones.RightFoot,
            _ => HumanBodyBones.LastBone
        };

        return humanBone == HumanBodyBones.LastBone
            ? null
            : animator.GetBoneTransform(humanBone);
    }
}
