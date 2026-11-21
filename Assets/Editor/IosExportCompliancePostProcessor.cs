#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace GravityPuzzle.Editor
{
    /// <summary>
    /// Declares standard/exempt encryption in the generated iOS Info.plist so
    /// App Store Connect / TestFlight builds do not stall in "Missing Compliance".
    /// </summary>
    internal sealed class IosExportCompliancePostProcessor : IPostprocessBuildWithReport
    {
        private const string ExportComplianceKey = "ITSAppUsesNonExemptEncryption";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.iOS)
                return;

            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[IosBuild] Info.plist not found at '{plistPath}'; skipping {ExportComplianceKey}.");
                return;
            }

            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean(ExportComplianceKey, false);
            plist.WriteToFile(plistPath);

            Debug.Log($"[IosBuild] Set {ExportComplianceKey} = false in '{plistPath}'.");
        }
    }
}
#endif
