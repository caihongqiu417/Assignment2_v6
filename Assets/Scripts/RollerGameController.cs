using UnityEngine;
using Unity.MLAgents;

public class RollerGameController : MonoBehaviour
{
    [Header("Agents")]
    public RollerAgent AgentA;
    public RollerAgent AgentB;

    [Header("Arena")]
    public float arenaHalfSize = 4f;
    public float spawnY = 0.5f;
    public float minSpawnDistance = 2f;
    public float fallYThreshold = 0f;

    [Header("Stats — which agent counts as 'Player'")]
    [Tooltip("Assign the player-controlled Agent here. Defaults to AgentA if left empty.")]
    public RollerAgent playerAgent;

    [Header("Time Limit")]
    [Tooltip("Maximum seconds per episode. 0 = no limit.")]
    public float maxEpisodeTime = 10f;

    protected int lastResetStep = -1;
    protected bool roundResolved = false;
    protected float episodeStartTime = 0f;

    public int PlayerWins  { get; private set; }
    public int TotalRounds { get; private set; }

    // Optional experiment observer. Does not change rewards, resets or physics.
    public event System.Action<bool, bool, bool, float> RoundCompleted;

    private void Awake()
    {
        if (AgentA == null || AgentB == null)
        {
            Debug.LogError("RollerGameController requires AgentA and AgentB to be assigned in Inspector.", this);
            enabled = false;
            return;
        }

        AgentA.GameController = this;
        AgentB.GameController = this;
        AgentA.Opponent = AgentB;
        AgentB.Opponent = AgentA;

        if (playerAgent == null)
            playerAgent = AgentA;
    }

    public virtual void TryResetEpisode()
    {
        int currentStep = Academy.Instance.StepCount;
        if (lastResetStep == currentStep)
        {
            return;
        }

        lastResetStep = currentStep;
        roundResolved = false;
        episodeStartTime = Time.time;

        Vector3 spawnA = RandomSpawnPosition();
        Vector3 spawnB = SymmetricSpawnPosition(spawnA);

        for (int i = 0; i < 50 && Vector3.Distance(spawnA, spawnB) < minSpawnDistance; i++)
        {
            spawnA = RandomSpawnPosition();
            spawnB = SymmetricSpawnPosition(spawnA);
        }

        ResetSingleAgent(AgentA, spawnA);
        ResetSingleAgent(AgentB, spawnB);
    }

    public virtual void ResolveRoundIfNeeded()
    {
        if (roundResolved)
        {
            return;
        }

        bool aFell = AgentA.transform.localPosition.y < fallYThreshold;
        bool bFell = AgentB.transform.localPosition.y < fallYThreshold;

        bool timedOut = maxEpisodeTime > 0f && (Time.time - episodeStartTime) >= maxEpisodeTime;

        if (!aFell && !bFell && !timedOut)
        {
            return;
        }

        roundResolved = true;
        TotalRounds++;

        if (timedOut && !aFell && !bFell)
        {
            // Draw: no reward change, just end the episode.
        }
        else if (aFell && !bFell)
        {
            AgentA.AddReward(-1f);
            AgentB.AddReward(1f);
            if (playerAgent == AgentB) PlayerWins++;
        }
        else if (bFell && !aFell)
        {
            AgentA.AddReward(1f);
            AgentB.AddReward(-1f);
            if (playerAgent == AgentA) PlayerWins++;
        }

        RoundCompleted?.Invoke(aFell, bFell, timedOut, Time.time - episodeStartTime);
        AgentA.EndEpisode();
        AgentB.EndEpisode();
    }

    protected void ResetSingleAgent(RollerAgent agent, Vector3 spawnPos)
    {
        agent.Body.angularVelocity = Vector3.zero;
        agent.Body.velocity = Vector3.zero;
        agent.transform.localPosition = spawnPos;
    }

    protected Vector3 RandomSpawnPosition()
    {
        return new Vector3(
            Random.Range(-arenaHalfSize, arenaHalfSize),
            spawnY,
            Random.Range(-arenaHalfSize, arenaHalfSize)
        );
    }

    protected Vector3 SymmetricSpawnPosition(Vector3 reference)
    {
        return new Vector3(-reference.x, spawnY, -reference.z);
    }
}
