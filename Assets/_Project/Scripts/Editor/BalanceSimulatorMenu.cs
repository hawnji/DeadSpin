using System;
using System.IO;
using System.Text;
using DeadSpin.Core.Simulation;
using DeadSpin.Runtime.Bootstrap;
using UnityEditor;
using UnityEngine;

namespace DeadSpin.EditorTools
{
    /// <summary>
    /// Menu: Dead Spin > Simulate Runs. Plays whole runs headless with scripted bots (no scene, no
    /// rendering) and writes a balance report: win rate, endings, rounds survived and any invariant
    /// problems the simulator caught. Use it after changing rules, prices or payouts.
    /// Also: Dead Spin > Delete Save Files, for testing a fresh first launch.
    /// </summary>
    public static class BalanceSimulatorMenu
    {
        public const string ReportPath = "Docs/BALANCE_REPORT.md";

        [MenuItem("Dead Spin/Simulate Runs (100 per style)", priority = 20)]
        public static void SimulateHundred() { Simulate(100); }

        [MenuItem("Dead Spin/Simulate Runs (500 per style)", priority = 21)]
        public static void SimulateFiveHundred() { Simulate(500); }

        private static void Simulate(int perStyle)
        {
            var report = new StringBuilder();
            report.AppendLine("# Dead Spin balance report");
            report.AppendLine();
            report.AppendLine("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " by Dead Spin > Simulate Runs, " + perStyle + " runs per bot style.");
            report.AppendLine();
            int problems = 0;
            try
            {
                BotStyle[] styles = (BotStyle[])Enum.GetValues(typeof(BotStyle));
                for (int s = 0; s < styles.Length; s++)
                {
                    BotStyle style = styles[s];
                    if (EditorUtility.DisplayCancelableProgressBar("Dead Spin", "Simulating " + style + " runs...", (float)s / styles.Length)) break;
                    SimulationReport result = RunSimulator.SimulateMany(perStyle, 1000UL + (ulong)s * 100000UL, style);
                    report.AppendLine("## " + style);
                    report.AppendLine();
                    report.AppendLine("```");
                    report.AppendLine(result.Describe());
                    report.AppendLine("```");
                    report.AppendLine();
                    foreach (SimulatedRun run in result.Runs)
                    {
                        foreach (string problem in run.Problems)
                        {
                            problems++;
                            if (problems <= 50) Debug.LogWarning("[Simulator] Seed " + run.Seed + " (" + style + "): " + problem);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            report.AppendLine(problems == 0 ? "No invariant problems." : problems + " invariant problems (see the Console).");

            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString());
            Debug.Log("[Dead Spin] Balance report written to " + ReportPath + (problems == 0 ? "." : ". " + problems + " problems found."));
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Dead Spin/Delete Save Files", priority = 40)]
        public static void DeleteSaves()
        {
            if (!EditorUtility.DisplayDialog("Dead Spin", "Delete the saved run and all unlocks and Marks?", "Delete", "Cancel")) return;
            foreach (string file in new[] { GameSession.RunSaveFile, GameSession.MetaSaveFile })
            {
                string path = Path.Combine(Application.persistentDataPath, file);
                if (File.Exists(path)) File.Delete(path);
            }
            Debug.Log("[Dead Spin] Save files deleted from " + Application.persistentDataPath + ".");
        }
    }
}
