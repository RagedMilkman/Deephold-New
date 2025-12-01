using UnityEngine;

/// <summary>
/// Centralized execution order values for MonoBehaviour services.
/// Lower numbers execute earlier. Adjust values here to control load order.
/// </summary>
public static class ServiceExecutionOrder
{
    public const int GridDirector = -400;
    public const int NoiseGeneration = -390;
    public const int PlayerLocation = -360;
    public const int EnemySpawnLocation = -350;
    public const int NavFieldService = -340;
    public const int BespokePathService = -335;
    public const int NpcSyncServer = -333;
    public const int NpcSyncClient = -332;
    public const int PathingService = -330;
    public const int NPCMoverService = -329;
    public const int AIMoverService = -328;
    public const int WorldStateService = -326;
    public const int PlayerSpawnLocation = -325;
    public const int EnemySpawnService = -300;
    public const int NpcSpawnService = -260;
    public const int PlayerSpawnService = -395;
    public const int SpawnMenu = -200;
    public const int HitReporter = -150;
}
