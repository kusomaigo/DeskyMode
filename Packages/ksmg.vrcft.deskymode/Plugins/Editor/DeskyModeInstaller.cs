#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;

//[InitializeOnLoad]
public class DeskyModeInstaller
{
    static string deskyModeInstallLocation = "Assets/Gimmicks/DeskyMode/";

    static DeskyModeInstaller()
    {
        string[] deskyModeInstalledScriptFiles = { string.Concat(deskyModeInstallLocation, "Editor/DeskyModeSetup.cs"),
                                              string.Concat(deskyModeInstallLocation, "Editor/DeskyModeEditor.cs") };
        // see if DeskyMode scripts were setup
        bool deskyModeScriptsInLocation = System.IO.File.Exists(deskyModeInstalledScriptFiles[0]) ||
            System.IO.File.Exists(deskyModeInstalledScriptFiles[1]);
        if (deskyModeScriptsInLocation)
        {
            // clear out the existing DeskyMode scripts
            RemoveFiles(deskyModeInstalledScriptFiles);
        }
        // copied Final IK assembly check from VRLab's FinalIKStubInstaller
        // not foolproof but if you're trying to fool this script... why? 
        bool fikPresent = AppDomain.CurrentDomain.GetAssemblies()
            .Any(x => x.GetTypes().Any(y => y.FullName == "RootMotion.FinalIK.AimIK"));
        if (!fikPresent)
        {

            EditorUtility.DisplayDialog("Missing Final IK", "Please install Final IK or Final IK Stub", "Ok");
        }
        else
        {
            // copy over the scripts
            // copied again from FIK Stub <3
            string thisFilePath = AssetDatabase.FindAssets("DeskyModeInstaller", new[] { "Packages", "Assets" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            string thisPackageDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFilePath), "../../"));
            string[] deskyModeSourceScriptFiles = { string.Concat(thisPackageDirectory, "Editor/DeskyModeSetup.cs.no"),
                                              string.Concat(thisPackageDirectory, "Editor/DeskyModeEditor.cs.no") };
            CopyFiles(deskyModeSourceScriptFiles, deskyModeInstallLocation);

            // check if Final IK was installed or the stub
            // can check by looking for the editor scripts
            // copied Final IK assembly check from VRLab's FinalIKStubInstaller
            bool realFIKPresent = AppDomain.CurrentDomain.GetAssemblies()
                .Any(x => x.GetTypes().Any(y => y.FullName == "RootMotion.FinalIK.IKInspector"));
            if (realFIKPresent)
            {
                // "uncomment" the #define ActualFinalIK in DeskyModeSetup.cs
                StreamReader reader = new StreamReader(deskyModeInstalledScriptFiles[0]);
                string fileText = reader.ReadToEnd();
                reader.Close();
                File.Delete(deskyModeInstalledScriptFiles[0]);
                StreamWriter writer = new StreamWriter(deskyModeInstalledScriptFiles[0]);
                writer.WriteLine("#define ActualFinalIK");
                writer.WriteLine(fileText);
                writer.Close();
                AssetDatabase.ImportAsset(deskyModeInstalledScriptFiles[0]);
            }
        }
    }

    private static void CopyFiles(string[] files, string finalPath)
    {
        foreach (var file in files)
        {
            if (file.EndsWith(".cs.no"))
            {
                string partialPath = file.Substring(file.IndexOf("Editor", StringComparison.Ordinal));
                string directory = Path.GetDirectoryName(partialPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(finalPath + directory);
                }

                File.Copy(file, finalPath + partialPath.Replace(".cs.no", ".cs"));
                File.Copy(file.Replace(".cs.no", ".cs.meta.no"), finalPath + partialPath.Replace(".cs.no", ".cs.meta"));
            }
        }
    }

    private static void RemoveFiles(string[] files)
    {
        foreach (var file in files)
        {
            if (file.EndsWith(".cs"))
            {
                File.Delete(file);
            }
        }
    }

    [MenuItem("Tools/DeskyMode/Refresh Scripts")]
    static void ForceRefresh()
    {
        new DeskyModeInstaller();
    }
}

#endif
