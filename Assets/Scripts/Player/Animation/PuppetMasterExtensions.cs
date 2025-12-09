using RootMotion.Dynamics;
using UnityEngine;

public static class PuppetMasterExtensions
{
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
}
