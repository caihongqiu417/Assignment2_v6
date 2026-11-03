using UnityEngine;
using Unity.MLAgents;

public class DeterministicRollerGameController : RollerGameController
{
    [Header("Deterministic Spawn (XZ)")]
    public Vector2 fixedSpawnAXZ = new Vector2(1f, 1f);
    public Vector2 fixedSpawnBXZ = new Vector2(0f, 0f);

    public override void TryResetEpisode()
    {
        int currentStep = Academy.Instance.StepCount;
        if (lastResetStep == currentStep)
        {
            return;
        }

        lastResetStep = currentStep;
        roundResolved = false;
        episodeStartTime = Time.time;

        Vector3 spawnA = new Vector3(fixedSpawnAXZ.x, spawnY, fixedSpawnAXZ.y);
        Vector3 spawnB = new Vector3(fixedSpawnBXZ.x, spawnY, fixedSpawnBXZ.y);

        ResetSingleAgent(AgentA, spawnA);
        ResetSingleAgent(AgentB, spawnB);
    }
}
