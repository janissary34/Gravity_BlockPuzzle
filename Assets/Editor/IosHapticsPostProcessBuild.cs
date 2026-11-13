#if UNITY_IOS
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Wires the Xcode project for Assets/Plugins/iOS/GravityHaptics.mm:
    /// CoreHaptics is weak-linked because the deployment target is below iOS 13,
    /// and the plugin is compiled with ARC because it stores autoreleased players.
    /// </summary>
    public static class IosHapticsPostProcessBuild
    {
        private const string PluginProjectPath = "Libraries/Plugins/iOS/GravityHaptics.mm";
        private const string CoreHapticsFramework = "CoreHaptics.framework";

        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.iOS)
                return;

            string projectPath = PBXProject.GetPBXProjectPath(buildPath);
            PBXProject project = new PBXProject();
            project.ReadFromFile(projectPath);

            string frameworkTarget = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(frameworkTarget, CoreHapticsFramework, true);

            string pluginGuid = project.FindFileGuidByProjectPath(PluginProjectPath);
            if (string.IsNullOrEmpty(pluginGuid))
                Debug.LogError("[Haptics] " + PluginProjectPath + " was not found in the Xcode project; iOS haptics will not compile with ARC.");
            else
                project.SetCompileFlagsForFile(frameworkTarget, pluginGuid, new List<string> { "-fobjc-arc" });

            File.WriteAllText(projectPath, project.WriteToString());
        }
    }
}
#endif
