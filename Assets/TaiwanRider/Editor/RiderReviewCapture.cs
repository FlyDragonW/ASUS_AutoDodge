using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace TaiwanRider.Editor
{
    public static class RiderReviewCapture
    {
        public static void Capture()
        {
            Directory.CreateDirectory("Validation/Views");
            var rider=Object.FindAnyObjectByType<ScooterController>();
            var route=Object.FindAnyObjectByType<RoadRoute>();
            var go=new GameObject("Review camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(rider.riderCamera);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            void Shot(string name,Vector3 position,Vector3 look,float fov)
            {
                camera.transform.position=position;camera.transform.LookAt(look);camera.fieldOfView=fov;
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};
                RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1920,1080),0,0);texture.Apply();
                File.WriteAllBytes("Validation/Views/"+name+".png",texture.EncodeToPNG());
            }
            try
            {
                Shot("01-rider-start",rider.riderCamera.transform.position,rider.riderCamera.transform.position+rider.riderCamera.transform.forward*10,64);
                Shot("02-intersection",route.Position(-22,2.65f,1.6f),route.Position(8,1,2.2f),60);
                Shot("03-lane-90",new Vector3(22,1.65f,1),new Vector3(-5,1.6f,0),62);
                Shot("04-arcade-detail",route.Position(-57,5.6f,1.8f),route.Position(-49,8.4f,2.4f),58);
                Shot("05-scooter-model",rider.transform.position+new Vector3(2.2f,1.35f,2.3f),rider.transform.position+Vector3.up*.62f,48);
                Shot("06-route-north",route.Position(69,2.65f,1.6f),route.Position(27,0,2),65);
            }
            finally {RenderTexture.active=previous;Object.DestroyImmediate(texture);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(go);}
            Debug.Log("Six 1920x1080 review views saved to Validation/Views.");
        }
    }
}
