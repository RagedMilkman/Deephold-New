// IPlayerTool.cs
using UnityEngine;

public interface IPlayerTool
{
    // Let the interaction hand you the owner’s camera (optional for some tools)
    void InteractionSetCamera(Camera cam);

    // Called every frame when owner+alive
    // primary = LMB, secondary = RMB (held & edge)
    void InteractionTick(bool primaryHeld, bool primaryPressed,
                         bool secondaryHeld, bool secondaryPressed);
}
