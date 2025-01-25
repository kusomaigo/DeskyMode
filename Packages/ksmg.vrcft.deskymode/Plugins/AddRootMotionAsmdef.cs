#if UNITY_EDITOR
using System.IO;
using UnityEditor;

[InitializeOnLoad]
public class AddRootMotionAsmdef
{
    static AddRootMotionAsmdef()
    {
        //EditorUtility.DisplayDialog("Missing Final IK", "Make sure Final IK or Final IK Stub is installed correctly first", "Ok");

        string rootMotionPath = "Assets/Plugins/RootMotion/";
        bool anythingInLocation = Directory.Exists(rootMotionPath);
        if (!anythingInLocation)
        {
            EditorUtility.DisplayDialog("Missing Final IK", "Make sure Final IK or Final IK Stub is installed correctly first", "Ok");
        }
        else
        {
            // hack to create asmdef for RootMotion lmao
            string asmdefPath = string.Concat(rootMotionPath, "RootMotion.asmdef");
            if (System.IO.File.Exists(asmdefPath)) return;
            StreamWriter writer = new StreamWriter(asmdefPath, true);
            writer.WriteLine("{\n\t\"name\": \"RootMotion\"\n}");
            writer.Close();
            AssetDatabase.ImportAsset(asmdefPath);
        }
    }

    //[MenuItem("Tools/DeskyMode/Refresh Scripts")]
    //static void ForceRefresh()
    //{
    //    new AddRootMotionAsmdef();
    //    //AssetDatabase.Refresh();
    //}
}

#endif
