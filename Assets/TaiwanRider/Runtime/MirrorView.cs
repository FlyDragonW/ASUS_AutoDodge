using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace TaiwanRider
{
    public sealed class MirrorView : MonoBehaviour
    {
        public Camera mirrorCamera;
        public Renderer surface;
        RenderTexture target;
        Material mirrorMaterial;
        float next;
        void Start()
        {
            target=new RenderTexture(256,256,16,RenderTextureFormat.ARGB32);target.name="Live motorcycle mirror";
            mirrorCamera.targetTexture=target;mirrorCamera.enabled=false;
            // A Resources material keeps the unlit shader and its required variants in Player builds.
            var template=Resources.Load<Material>("RiderMirror");
            if(!template){Debug.LogError("Missing RiderMirror material. Run DemoAssetSetup.Prepare().");enabled=false;return;}
            mirrorMaterial=new Material(template);
            mirrorMaterial.SetTexture("_BaseMap",target);mirrorMaterial.SetTextureScale("_BaseMap",new Vector2(-1,1));mirrorMaterial.SetTextureOffset("_BaseMap",new Vector2(1,0));
            surface.material=mirrorMaterial;
        }
        void LateUpdate()
        {
            if(Time.unscaledTime<next)return;next=Time.unscaledTime+1f/15f;
            var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};
            if(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(mirrorCamera,request))UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(mirrorCamera,request);
        }
        void OnDestroy(){if(target){target.Release();Destroy(target);}if(mirrorMaterial)Destroy(mirrorMaterial);}
    }
}
