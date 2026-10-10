using System.IO;
using UnityEngine;
using UnityEditor;
namespace TaiwanRider.Editor
{
    public static class DemoAssetSetup
    {
        [MenuItem("Taiwan Rider/Prepare auto avoidance demo")]
        public static void Prepare()
        {
            const string folder="Assets/TaiwanRider/Resources";
            Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            var shader=Shader.Find("TaiwanRider/DemoRadar");if(!shader)throw new System.Exception("Radar shader was not imported");
            var material=AssetDatabase.LoadAssetAtPath<Material>(folder+"/RiderRadar.mat");
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,folder+"/RiderRadar.mat");}
            else {material.shader=shader;EditorUtility.SetDirty(material);}
            AssetDatabase.SaveAssetIfDirty(material);
            var mirrorShader=Shader.Find("Universal Render Pipeline/Unlit");
            if(!mirrorShader)throw new System.Exception("Mirror shader was not imported");
            var mirror=AssetDatabase.LoadAssetAtPath<Material>(folder+"/RiderMirror.mat");
            if(!mirror){mirror=new Material(mirrorShader);AssetDatabase.CreateAsset(mirror,folder+"/RiderMirror.mat");}
            else {mirror.shader=mirrorShader;EditorUtility.SetDirty(mirror);}
            AssetDatabase.SaveAssetIfDirty(mirror);
        }
    }
}
