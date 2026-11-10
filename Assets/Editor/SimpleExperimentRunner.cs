using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.Barracuda;

// Runs the original Simple scene without saving temporary model/mode changes.
[InitializeOnLoad]
public static class SimpleExperimentRunner
{
    static StreamWriter writer;
    static int rounds;
    static int wins;
    static int losses;
    static int draws;
    static double startTime;
    static bool finished;
    static SimpleExperimentRunner()
    {
        EditorApplication.playModeStateChanged += OnPlayState;
        EditorApplication.update += Tick;
        startTime = EditorApplication.timeSinceStartup;
    }

    static string Arg(string key, string fallback = "")
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    public static void Run()
    {
        if (Arg("-experiment-mode") == "") throw new Exception("Missing -experiment-mode");
        EditorSceneManager.OpenScene("Assets/Scenes/Simple.unity");
        var player = GameObject.Find("Player").GetComponent<RollerAgent>();
        var enemy = GameObject.Find("Enemy").GetComponent<RollerAgent>();
        var controller = UnityEngine.Object.FindObjectOfType<DeterministicRollerGameController>();
        if (controller == null || enemy.heuristicMode != RollerAgent.HeuristicMode.ConservativeCenter)
            throw new Exception("Unexpected Simple controller/opponent");
        if (controller.fixedSpawnAXZ != new Vector2(2, 0) || controller.fixedSpawnBXZ != Vector2.zero)
            throw new Exception("Unexpected Simple spawn positions");
        foreach (var a in new[] { player, enemy })
            if (a.GetComponent<DecisionRequester>().DecisionPeriod != 10)
                throw new Exception("Decision Period must remain 10");
        enemy.GetComponent<BehaviorParameters>().BehaviorType = BehaviorType.HeuristicOnly;
        var behavior = player.GetComponent<BehaviorParameters>();
        if (Arg("-experiment-mode") == "eval")
        {
            behavior.Model = AssetDatabase.LoadAssetAtPath<NNModel>(Arg("-experiment-model", "Assets/RollingControl.onnx"));
            if (behavior.Model == null) throw new Exception("Model not found");
            behavior.InferenceDevice = InferenceDevice.CPU;
            behavior.BehaviorType = BehaviorType.InferenceOnly;
        }
        else
        {
            behavior.Model = null;
            behavior.BehaviorType = BehaviorType.Default;
        }
        Debug.Log("EXPERIMENT_READY mode=" + Arg("-experiment-mode") + " opponent=" + enemy.heuristicMode);
        EditorApplication.isPlaying = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void SetEvaluationSeed()
    {
        if (Arg("-experiment-mode") != "eval") return;
        int seed = int.Parse(Arg("-experiment-seed", "1"));
        UnityEngine.Random.InitState(seed);
        Academy.Instance.InferenceSeed = seed;
    }

    static void OnPlayState(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || Arg("-experiment-mode") != "eval") return;
        var controller = UnityEngine.Object.FindObjectOfType<RollerGameController>();
        string path = Arg("-experiment-output");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        writer = new StreamWriter(path, false);
        writer.WriteLine("round,outcome,duration_seconds,player_fell,opponent_fell,timed_out,player_reward,player_x,player_z,opponent_x,opponent_z");
        writer.Flush();
        Time.timeScale = 1;
        controller.RoundCompleted += (aFell, bFell, timedOut, duration) =>
        {
            if (finished) return;
            bool playerIsA = controller.playerAgent == controller.AgentA;
            bool pFell = playerIsA ? aFell : bFell;
            bool oFell = playerIsA ? bFell : aFell;
            string outcome = oFell && !pFell ? "W" : pFell && !oFell ? "L" : "D";
            if (outcome == "W") wins++; else if (outcome == "L") losses++; else draws++;
            var p = controller.playerAgent;
            var o = p.Opponent;
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F4},{3},{4},{5},{6:F6},{7:F4},{8:F4},{9:F4},{10:F4}",
                ++rounds, outcome, duration, pFell, oFell, timedOut, p.GetCumulativeReward(),
                p.transform.localPosition.x, p.transform.localPosition.z, o.transform.localPosition.x, o.transform.localPosition.z));
            writer.Flush();
            Debug.Log("EVAL_ROUND " + rounds + " " + outcome + " seconds=" + duration);
            if (rounds >= int.Parse(Arg("-experiment-rounds", "10")))
            {
                finished = true;
                writer.Dispose();
                Debug.Log("EVAL_COMPLETE W=" + wins + " D=" + draws + " L=" + losses);
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            }
        };
    }

    static void Tick()
    {
        if (Arg("-experiment-mode") == "") return;
        string stop = Arg("-experiment-stop");
        if (stop != "" && File.Exists(stop)) EditorApplication.Exit(0);
        if (EditorApplication.timeSinceStartup - startTime > 10800)
        {
            Debug.LogError("Experiment exceeded 3-hour timeout");
            EditorApplication.Exit(2);
        }
    }
}
