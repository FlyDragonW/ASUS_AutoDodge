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
    public static class SiteMatchCapture
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

            LastOutput=Path.GetFullPath("Validation/SiteMatch/"+label);LastError="";UsedRenderRequestFallback=false;
            Directory.CreateDirectory(LastOutput);
            Busy=true;
            try
            {
                cameraObject=new GameObject("Site match review camera (temporary)"){hideFlags=HideFlags.HideAndDontSave};
                reviewCamera=cameraObject.AddComponent<Camera>();reviewCamera.enabled=false;reviewCamera.CopyFrom(rider.riderCamera);
                reviewCamera.cullingMask=~0;reviewCamera.rect=new Rect(0,0,1,1);
                var data=reviewCamera.GetUniversalAdditionalCameraData();data.renderType=CameraRenderType.Base;
                data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.TemporalAntiAliasing;
                target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32){name="Site match review target",hideFlags=HideFlags.HideAndDontSave};
                target.Create();reviewCamera.targetTexture=target;
                texture=new Texture2D(1600,900,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                sleeves=rider.visualBody.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("Jacket sleeve")||r.name.Contains("Riding glove")||r.name.Contains("Gloved finger")).ToArray();
                sleeveEnabled=sleeves.Select(r=>r.enabled).ToArray();
                var frontage=GameObject.Find("Gongguan 2025 site match");
                if(!frontage)throw new InvalidOperationException("Apply the site match before capturing.");
                Vector3 anchor=frontage.transform.position;
                shots=new[]
                {
                    new Shot{name="01-photo-frontage",position=()=>anchor+new Vector3(15.5f,2.0f,0),look=()=>anchor+new Vector3(0,4.15f,0),fov=56},
                    new Shot{name="02-frontage-approach",position=()=>route.Position(-5,1f,1.65f),look=()=>anchor+new Vector3(0,3.1f,0),fov=65},
                    new Shot{name="03-lane-90",position=()=>new Vector3(-3,2.2f,2),look=()=>new Vector3(48,1.7f,-3),fov=62},
                    new Shot{name="04-intersection",position=()=>route.Position(-20,2.5f,1.65f),look=()=>route.Position(4,0,2.4f),fov=68},
                    new Shot{name="05-jyes-detail",position=()=>anchor+new Vector3(7.8f,1.8f,0),look=()=>anchor+new Vector3(0,2.4f,0),fov=51}
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
            try{Debug.LogError("Site match review capture: "+error.Message);}
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
