using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TaiwanRider.Editor
{
    /// <summary>Render review views over real editor/render frames without changing the riding camera.</summary>
    public static class RealismCapture
    {
        sealed class Shot
        {
            public string name;
            public Func<Vector3> position,look;
            public float fov;
            public bool hideSleeves;
        }

        public static bool Busy {get;private set;}
        public static string Status {get;private set;}="idle";
        public static string LastOutput {get;private set;}="";
        public static string LastError {get;private set;}="";
        public static bool UsedRenderRequestFallback {get;private set;}
        static GameObject cameraObject;
        static Camera reviewCamera;
        static RenderTexture target;
        static Texture2D texture;
        static Renderer[] sleeves;
        static bool[] sleeveEnabled;
        static Shot[] shots;
        static int shotIndex,updateNumber,preparedUpdate,lastRenderedUpdate,renderedUpdates,completedFrames,preparedFrame;
        static double preparedAt,lastRequestAt;

        public static string CaptureAsync(string label="after")
        {
            if(Busy)throw new InvalidOperationException("Review capture is already running: "+Status);
            if(string.IsNullOrWhiteSpace(label) || label.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || label=="." || label=="..")
                throw new ArgumentException("Use a single folder name for the capture label.",nameof(label));
            var rider=UnityEngine.Object.FindFirstObjectByType<ScooterController>();
            var route=UnityEngine.Object.FindFirstObjectByType<RoadRoute>();
            if(!rider || !rider.riderCamera || !rider.visualBody || !route)
                throw new InvalidOperationException("Open the riding scene before requesting a review capture.");

            LastOutput=Path.GetFullPath("Validation/Realism/"+label);LastError="";UsedRenderRequestFallback=false;
            Directory.CreateDirectory(LastOutput);
            Busy=true;
            try
            {
                cameraObject=new GameObject("Refinement review camera (temporary)"){hideFlags=HideFlags.HideAndDontSave};
                reviewCamera=cameraObject.AddComponent<Camera>();reviewCamera.enabled=false;reviewCamera.CopyFrom(rider.riderCamera);
                reviewCamera.cullingMask=~0;reviewCamera.rect=new Rect(0,0,1,1);
                var data=reviewCamera.GetUniversalAdditionalCameraData();data.renderType=CameraRenderType.Base;
                data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.TemporalAntiAliasing;
                target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32){name="Refinement review target",hideFlags=HideFlags.HideAndDontSave};
                target.Create();reviewCamera.targetTexture=target;
                texture=new Texture2D(1600,900,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                sleeves=rider.visualBody.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("Jacket sleeve")||r.name.Contains("Riding glove")||r.name.Contains("Gloved finger")).ToArray();
                sleeveEnabled=sleeves.Select(r=>r.enabled).ToArray();
                shots=new[]
                {
                    new Shot{name="01-intersection",position=()=>route.Position(-20,2.5f,1.65f),look=()=>route.Position(4,3,2.4f),fov=64},
                    new Shot{name="02-corner",position=()=>route.Position(-9,-2f,1.75f),look=()=>route.Position(8,9.6f,4.2f),fov=55},
                    new Shot{name="03-arcade",position=()=>route.Position(-15,5.5f,1.65f),look=()=>route.Position(-6,8.8f,2.4f),fov=65},
                    new Shot{name="04-scooter",position=()=>rider.transform.TransformPoint(new Vector3(2.05f,1.18f,2.18f)),look=()=>rider.transform.TransformPoint(new Vector3(0,.64f,0)),fov=48,hideSleeves=true},
                    new Shot{name="05-cockpit",position=()=>rider.transform.TransformPoint(new Vector3(.02f,1.37f,-.47f)),look=()=>rider.transform.TransformPoint(new Vector3(0,.98f,.45f)),fov=46,hideSleeves=true},
                    new Shot{name="06-parked-scooters",position=()=>route.Position(-30,-2.25f,1.25f),look=()=>route.Position(-27,-5.35f,.7f),fov=52},
                    new Shot{name="07-parked-sedan",position=()=>route.Position(-68,-1.4f,1.65f),look=()=>route.Position(-64,-5.12f,.8f),fov=52},
                    new Shot{name="08-street-frontage",position=()=>route.Position(-43,1f,1.65f),look=()=>route.Position(-30,-7.5f,2.8f),fov=62}
                };
                shotIndex=updateNumber=completedFrames=0;
                EditorApplication.update+=Update;
                EditorApplication.playModeStateChanged+=PlayModeChanged;
                EditorApplication.quitting+=Cancel;
                AssemblyReloadEvents.beforeAssemblyReload+=Cancel;
                RenderPipelineManager.endCameraRendering+=CameraRendered;
                RenderPipelineManager.endFrameRendering+=FrameRendered;
                PrepareShot();
                // Normal URP camera processing registers and updates GPU Resident
                // Drawer culling before any readback or optional render request.
                reviewCamera.enabled=true;
                RequestEditorFrame();
                return "started: "+LastOutput;
            }
            catch(Exception error){Fail(error);throw;}
        }

        static void PrepareShot()
        {
            var shot=shots[shotIndex];
            for(int i=0;i<sleeves.Length;i++)if(sleeves[i])sleeves[i].enabled=shot.hideSleeves?false:sleeveEnabled[i];
            ApplyPose();
            reviewCamera.GetUniversalAdditionalCameraData().resetHistory=true;
            preparedUpdate=updateNumber;preparedFrame=completedFrames;lastRenderedUpdate=-1;renderedUpdates=0;
            preparedAt=EditorApplication.timeSinceStartup;lastRequestAt=preparedAt;
            Status=$"warming {shotIndex+1}/{shots.Length}: {shot.name}";
        }
        static void ApplyPose()
        {
            var shot=shots[shotIndex];reviewCamera.transform.position=shot.position();
            reviewCamera.transform.LookAt(shot.look());reviewCamera.fieldOfView=shot.fov;
        }
        static void RequestEditorFrame()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        static void CameraRendered(ScriptableRenderContext context,Camera camera)
        {
            if(!Busy || camera!=reviewCamera || lastRenderedUpdate==updateNumber)return;
            lastRenderedUpdate=updateNumber;renderedUpdates++;
        }
        static void FrameRendered(ScriptableRenderContext context,Camera[] cameras){if(Busy)completedFrames++;}
        static void Update()
        {
            if(!Busy)return;
            try
            {
                updateNumber++;
                if(!reviewCamera || !target)throw new InvalidOperationException("The temporary review camera or target was removed.");
                if(EditorApplication.isCompiling)throw new InvalidOperationException("Capture interrupted by script compilation.");
                double now=EditorApplication.timeSinceStartup;
                int updatesSincePose=updateNumber-preparedUpdate;
                // Read a completed render from an earlier editor tick, never the
                // same callback that introduced a camera or changed its view.
                if(updatesSincePose>=4 && renderedUpdates>=12 && completedFrames>preparedFrame && now-preparedAt>=.15)
                {
                    SaveShot();shotIndex++;
                    if(shotIndex==shots.Length)
                    {
                        Status="complete: "+LastOutput;
                        Cleanup();return;
                    }
                    PrepareShot();RequestEditorFrame();return;
                }
                if(now-preparedAt>12)
                    throw new TimeoutException($"No stable render for {shots[shotIndex].name}: {renderedUpdates} camera renders over {updatesSincePose} editor updates. Open the Game view and retry.");

                ApplyPose();
                Status=$"warming {shotIndex+1}/{shots.Length}: {shots[shotIndex].name}; camera frames {renderedUpdates}/12; editor updates {updatesSincePose}";
                RequestEditorFrame();
                // An unfocused Edit-mode Game view may not render target cameras
                // automatically. Fall back only after warm-up AND a real SRP frame,
                // submitting at most once per editor tick and reading on a later tick.
                if(now-preparedAt>.8 && updatesSincePose>=4 && completedFrames>preparedFrame && now-lastRequestAt>.12)
                {
                    var request=new UniversalRenderPipeline.SingleCameraRequest{destination=target};
                    if(!RenderPipeline.SupportsRenderRequest(reviewCamera,request))
                        throw new InvalidOperationException("URP does not support the fallback review render request.");
                    UsedRenderRequestFallback=true;lastRequestAt=now;
                    RenderPipeline.SubmitRenderRequest(reviewCamera,request);
                }
            }
            catch(Exception error){Fail(error);}
        }
        static void SaveShot()
        {
            var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();
                File.WriteAllBytes(Path.Combine(LastOutput,shots[shotIndex].name+".png"),texture.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;}
        }
        static void PlayModeChanged(PlayModeStateChange change){Cancel();}
        static void Fail(Exception error)
        {
            LastError=error.ToString();Status="failed: "+error.Message;
            try{Debug.LogError("Refinement review capture: "+error.Message);}
            finally{Cleanup();}
        }
        public static void Cancel()
        {
            if(!Busy)return;
            Status="cancelled";
            Cleanup();
        }
        static void Cleanup()
        {
            Busy=false;
            EditorApplication.update-=Update;
            EditorApplication.playModeStateChanged-=PlayModeChanged;
            EditorApplication.quitting-=Cancel;
            AssemblyReloadEvents.beforeAssemblyReload-=Cancel;
            RenderPipelineManager.endCameraRendering-=CameraRendered;
            RenderPipelineManager.endFrameRendering-=FrameRendered;
            try
            {
                if(reviewCamera){reviewCamera.enabled=false;reviewCamera.targetTexture=null;}
            }
            finally
            {
                try
                {
                    if(sleeves!=null && sleeveEnabled!=null)
                        for(int i=0;i<sleeves.Length;i++)if(sleeves[i])sleeves[i].enabled=sleeveEnabled[i];
                }
                finally
                {
                    if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}
                    if(texture)UnityEngine.Object.DestroyImmediate(texture);
                    if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);
                    target=null;texture=null;cameraObject=null;reviewCamera=null;sleeves=null;sleeveEnabled=null;shots=null;
                }
            }
        }
    }
}
