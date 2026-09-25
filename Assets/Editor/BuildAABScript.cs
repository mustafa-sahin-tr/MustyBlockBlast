using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System.IO;

public static class BuildAABScript
{
    [MenuItem("Build/Build AAB Release")]
    public static void BuildAAB()
    {
        // Log and set buildAppBundle
        Debug.Log("=== AAB BUILD SCRIPT ===");
        Debug.Log("Current buildAppBundle: " + EditorUserBuildSettings.buildAppBundle);
        Debug.Log("Current exportAsGoogleAndroidProject: " + EditorUserBuildSettings.exportAsGoogleAndroidProject);
        
        // Store original state
        bool originalBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
        Debug.Log("SAVED ORIGINAL buildAppBundle: " + originalBuildAppBundle);
        
        // Set for AAB build
        EditorUserBuildSettings.buildAppBundle = true;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        
        Debug.Log("Set buildAppBundle to true");
        Debug.Log("Set exportAsGoogleAndroidProject to false");
        
        // Build path
        string buildPath = "Builds/Android/BlockioBlast.aab";
        
        // Ensure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath));
        
        // Build options
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = buildPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None,
        };
        
        Debug.Log("Building AAB to: " + buildPath);
        Debug.Log("Target: " + options.target);
        
        // Perform the build
        BuildReport report = BuildPipeline.BuildPlayer(options);
        
        if (report.summary.result == BuildResult.Succeeded)
        {
            FileInfo fileInfo = new FileInfo(buildPath);
            Debug.Log("[AAB BUILD SUCCESS] Output: " + buildPath);
            Debug.Log("Build size: " + (fileInfo.Length / (1024.0 * 1024.0)) + " MB");
        }
        else
        {
            Debug.LogError("[AAB BUILD FAILED]");
            Debug.LogError("Build result: " + report.summary.result);
            Debug.LogError("Errors: " + report.summary.totalErrors);
        }
        
        // Restore original state
        Debug.Log("Restoring original buildAppBundle: " + originalBuildAppBundle);
        EditorUserBuildSettings.buildAppBundle = originalBuildAppBundle;
        Debug.Log("buildAppBundle restored to: " + EditorUserBuildSettings.buildAppBundle);
    }
}
