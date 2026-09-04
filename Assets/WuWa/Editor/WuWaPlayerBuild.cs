using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WuWa.EditorTools
{
    /// Builds the standalone player from inside the running Editor.
    ///
    /// `unity build` spawns a second editor in batch mode, which cannot open a project the running
    /// editor already holds a lock on — and it would rebuild the whole Library from cold. Driving
    /// BuildPipeline here reuses the warm asset database instead.
    public static class WuWaPlayerBuild
    {
        public static string BuildWindows(string outDir, string version)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(version) && PlayerSettings.bundleVersion != version)
            {
                sb.Append("version " + PlayerSettings.bundleVersion + " -> " + version + "\n");
                PlayerSettings.bundleVersion = version;
                AssetDatabase.SaveAssets();
            }

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);
            if (scenes.Count == 0) return "no enabled scenes in build settings";
            sb.Append("scenes: " + string.Join(", ", scenes.ToArray()) + "\n");

            Directory.CreateDirectory(outDir);
            string exe = Path.Combine(outDir, PlayerSettings.productName + ".exe");

            var opts = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s2 = report.summary;
            sb.Append("result=" + s2.result);
            sb.Append(" errors=" + s2.totalErrors + " warnings=" + s2.totalWarnings);
            sb.Append(" time=" + (int)s2.totalTime.TotalSeconds + "s");
            sb.Append(" size=" + (s2.totalSize / (1024 * 1024)) + "MB");
            sb.Append(" out=" + exe);

            if (s2.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            sb.Append("\n  ERR [" + step.name + "] " + msg.content);
            }
            return sb.ToString();
        }
    }
}
