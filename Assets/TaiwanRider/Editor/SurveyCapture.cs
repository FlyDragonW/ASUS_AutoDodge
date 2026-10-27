using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TaiwanRider.Editor
{
    /// <summary>Render review views over real editor/render frames without changing the riding camera.</summary>
    public static class SurveyCapture
    {
        sealed class Shot
        {
            public string name;
            public Func<Vector3> position,look;
            public float fov;
            public bool hideSleeves;
            public int pairHalf=-1;
        }

        public static bool Busy {get;private set;}
        public static string Status {get;private set;}="idle";
        public static string LastOutput {get;private set;}="";
        public static string LastError {get;private set;}="";
        public static bool UsedRenderRequestFallback {get;private set;}
        static GameObject cameraObject;
        static Camera reviewCamera;
        static RenderTexture target;
        static Texture2D texture,pairedTexture;
        [Serializable] sealed class FrameInfo {public string file,direction;public Vector3 position,look;public float fov;}
        [Serializable] sealed class Manifest {public string timeUtc;public int finalImageCount;public List<FrameInfo> frames;}
        static List<FrameInfo> capturedFrames;
        static Renderer[] sleeves;
        static bool[] sleeveEnabled;
        static Shot[] shots;
        static int shotIndex,updateNumber,preparedUpdate,lastRenderedUpdate,renderedUpdates,completedFrames,preparedFrame;
        static double preparedAt,lastRequestAt;

        public static string CaptureAsync(string label="after",string onlyShot=null)
        {
            if(Busy)throw new InvalidOperationException("Review capture is already running: "+Status);
            if(string.IsNullOrWhiteSpace(label) || label.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || label=="." || label=="..")
                throw new ArgumentException("Use a single folder name for the capture label.",nameof(label));
            var rider=UnityEngine.Object.FindFirstObjectByType<ScooterController>();
            var route=UnityEngine.Object.FindFirstObjectByType<RoadRoute>();
            if(!rider || !rider.riderCamera || !rider.visualBody || !route)
                throw new InvalidOperationException("Open the riding scene before requesting a review capture.");

            LastOutput=Path.GetFullPath("Validation/StreetSurvey/"+label);LastError="";UsedRenderRequestFallback=false;
            Directory.CreateDirectory(LastOutput);
            Busy=true;
            try
            {
                cameraObject=new GameObject("Survey street review camera (temporary)"){hideFlags=HideFlags.HideAndDontSave};
                reviewCamera=cameraObject.AddComponent<Camera>();reviewCamera.enabled=false;reviewCamera.CopyFrom(rider.riderCamera);
                reviewCamera.cullingMask=~0;reviewCamera.rect=new Rect(0,0,1,1);
                var data=reviewCamera.GetUniversalAdditionalCameraData();data.renderType=CameraRenderType.Base;
                // Review poses jump between unrelated storefronts. Spatial AA avoids
                // stale temporal text silhouettes bleeding from the preceding camera pose.
                data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality=AntialiasingQuality.High;
                target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32){name="Survey street review target",hideFlags=HideFlags.HideAndDontSave};
                target.Create();reviewCamera.targetTexture=target;
                texture=new Texture2D(1600,900,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                sleeves=rider.visualBody.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("Jacket sleeve")||r.name.Contains("Riding glove")||r.name.Contains("Gloved finger")).ToArray();
                sleeveEnabled=sleeves.Select(r=>r.enabled).ToArray();
                var survey=GameObject.Find("Gongguan surveyed street • 2025 visual reconstruction");
                if(!survey)throw new InvalidOperationException("Apply the surveyed street reconstruction before capturing.");
                var shotList=new List<Shot>();
                var stations=new[]{-100f,-65f,-25f,20f,60f,100f};
                for(int i=0;i<stations.Length;i++)
                {
                    float station=stations[i];string name=$"{i+1:00}-street-z-{station:+000;-000}-both-directions";
                    foreach(int direction in new[]{1,-1})
                    {
                        int facing=direction;
                        shotList.Add(new Shot{name=name,pairHalf=facing>0?0:1,
                            position=()=>route.Position(station,facing*2.35f,1.70f),
                            look=()=>route.Position(station+facing*35f,0,2.65f),fov=70,hideSleeves=true});
                    }
                }
                shotList.Add(new Shot{name="07-high-street-overview",position=()=>new Vector3(75,105,-135),look=()=>new Vector3(0,8,0),fov=66,hideSleeves=true});
                shotList.Add(new Shot{name="08-high-reverse-overview",position=()=>new Vector3(-80,82,118),look=()=>new Vector3(7,10,-20),fov=66,hideSleeves=true});
                shotList.Add(new Shot{name="09-shuiyuan-from-tingzhou",position=()=>new Vector3(-4,18,-40),look=()=>new Vector3(20,20,-40),fov=78,hideSleeves=true});
                var parked=GameObject.Find("Parked scooters");
                var vehicle=parked?parked.transform.Cast<Transform>().Where(t=>t.gameObject.activeInHierarchy&&t.name.StartsWith("Parked scooter "))
                    .OrderBy(t=>Mathf.Abs(t.position.z+25)).FirstOrDefault():null;
                if(!vehicle)throw new InvalidOperationException("No active parked scooter exists for the ground-level vehicle view.");
                float bank=Mathf.Sign(vehicle.position.x-route.CenterX(vehicle.position.z));
                shotList.Add(new Shot{name="10-ground-vehicle-and-street",position=()=>vehicle.position+new Vector3(-bank*2.6f,1.3f,-2.5f),look=()=>vehicle.position+Vector3.up*.8f,fov=62,hideSleeves=true});
                shotList.Add(new Shot{name="16-market-intersection-view",position=()=>new Vector3(-12,12,-4),look=()=>new Vector3(20,18,-38),fov=70,hideSleeves=true});
                shotList.Add(new Shot{name="17-west-166-168-shopfronts",position=()=>new Vector3(.2f,2.7f,-23.8f),look=()=>new Vector3(-10.6f,3.8f,-16.1f),fov=70,hideSleeves=true});
                shotList.Add(new Shot{name="18-east-225-237-row",position=()=>new Vector3(-.5f,2.5f,61f),look=()=>new Vector3(9,2.4f,68),fov=75,hideSleeves=true});
                shots=shotList.ToArray();
                capturedFrames=new List<FrameInfo>();
                var extraShots=new[]{
                    new Shot{name="11-SF01-ground-facade",position=()=>new Vector3(1.8f,1.65f,59.85f),look=()=>new Vector3(9,1.7f,59.85f),fov=62,hideSleeves=true},
                    new Shot{name="12-SF03-ground-facade",position=()=>new Vector3(-26.5f,1.65f,-104),look=()=>new Vector3(-32,-.1f,-102.8f),fov=58,hideSleeves=true},
                    new Shot{name="13-three-shop-frontages",position=()=>new Vector3(3.5f,2,19.3f),look=()=>new Vector3(-6.2f,3,19.3f),fov=66,hideSleeves=true},
                    new Shot{name="14-shuiyuan-side",position=()=>new Vector3(24,18,10),look=()=>new Vector3(30,20,-10),fov=72,hideSleeves=true},
                    new Shot{name="15-shuiyuan-complete-tingzhou-facade",position=()=>new Vector3(-42,19,-40),look=()=>new Vector3(24,20,-40),fov=60,hideSleeves=true}
                };
                shots=shots.Concat(extraShots).ToArray();
                // Each newly researched frontage gets a reproducible street-side review view.
                var shopShots=new List<Shot>();
                foreach(var reservation in SurveyStreetscapeSetup.LoadAdditionalFrontages())
                {
                    var group=survey.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==reservation.sceneName);
                    if(!group)continue;
                    var shop=group;var item=reservation;
                    bool walkShop=item.id.Contains("25");
                    // Wide arcades need a wider lens rather than a camera inside the
                    // opposite building across this narrow street.
                    float distance=Mathf.Max(walkShop?5.8f:4.4f,Mathf.Min(10.5f,item.width*.86f));
                    float shopFov=Mathf.Max(66,2*Mathf.Atan(item.width*.58f/distance)*Mathf.Rad2Deg);
                    shopShots.Add(new Shot{name="shop-"+item.id,position=()=>shop.position+shop.forward*distance+Vector3.up*Mathf.Min(2.6f,item.height*.44f),
                        look=()=>shop.position+Vector3.up*(walkShop?2.55f:item.height*.45f),fov=shopFov,hideSleeves=true});
                }
                shots=shots.Concat(shopShots).ToArray();
                shots=shots.Concat(new[]{
                    new Shot{name="19-walk-north-row",position=()=>new Vector3(2,2.7f,-24),look=()=>new Vector3(-10.5f,3,-10.4f),fov=70,hideSleeves=true},
                    new Shot{name="20-walk-south-row",position=()=>new Vector3(-2,2.7f,-42),look=()=>new Vector3(-14.6f,3.1f,-55),fov=72,hideSleeves=true},
                    new Shot{name="21-january-northeast-row",position=()=>new Vector3(-2,2.5f,99),look=()=>new Vector3(9.5f,2.6f,87),fov=73,hideSleeves=true},
                    new Shot{name="22-january-northwest-row",position=()=>new Vector3(3,2.5f,103),look=()=>new Vector3(-9,2.8f,93),fov=73,hideSleeves=true},
                    new Shot{name="23-january-middle-east",position=()=>new Vector3(-1,2.4f,56),look=()=>new Vector3(10.5f,2.4f,43),fov=74,hideSleeves=true},
                    new Shot{name="24-january-middle-west",position=()=>new Vector3(2,2.6f,76),look=()=>new Vector3(-8,2.8f,65),fov=74,hideSleeves=true},
                    new Shot{name="25-january-south-retail",position=()=>new Vector3(-13,2.5f,-63),look=()=>new Vector3(-18,3.1f,-86),fov=78,hideSleeves=true},
                    new Shot{name="26-january-north-extension",position=()=>new Vector3(3,2.6f,137),look=()=>new Vector3(-9,2.8f,123),fov=76,hideSleeves=true},
                    new Shot{name="27-january-north-phone-row",position=()=>new Vector3(-3,2.5f,115),look=()=>new Vector3(7,2.6f,102),fov=73,hideSleeves=true},
                    new Shot{name="28-january-west-infill",position=()=>new Vector3(2,2.6f,56),look=()=>new Vector3(-8,2.8f,46),fov=73,hideSleeves=true},
                    new Shot{name="29-january-south-continuation",position=()=>new Vector3(-24,2.8f,-89),look=()=>new Vector3(-15,3.1f,-111),fov=76,hideSleeves=true},
                    new Shot{name="30-january-cinema-corner",position=()=>new Vector3(-31,2.8f,-133),look=()=>new Vector3(-19,3,-125),fov=74,hideSleeves=true},
                    new Shot{name="31-january-under-armour-corner",position=()=>new Vector3(-29,3.2f,-120),look=()=>new Vector3(-39,3.5f,-129),fov=72,hideSleeves=true},
                    new Shot{name="32-january-anonymous-north-gaps",position=()=>new Vector3(1,2.5f,90),look=()=>new Vector3(-10.5f,2.4f,83.5f),fov=72,hideSleeves=true},
                    new Shot{name="33-january-north-sports-entry",position=()=>new Vector3(-3,2.8f,126),look=()=>new Vector3(7.8f,2.7f,117),fov=74,hideSleeves=true},
                    new Shot{name="34-january-la-villa-row",position=()=>new Vector3(-2.5f,2.6f,132),look=()=>new Vector3(7.55f,2.7f,123.5f),fov=72,hideSleeves=true}
                }).ToArray();
                if(onlyShot=="shops")shots=shots.Where(s=>s.name.StartsWith("shop-")).ToArray();
                else if(onlyShot=="walk")shots=shots.Where(s=>s.name.StartsWith("shop-W25")||s.name.StartsWith("19-walk")||s.name.StartsWith("20-walk")).ToArray();
                else if(onlyShot=="january")shots=shots.Where(s=>s.name.Contains("-january-")||(s.name.StartsWith("shop-")&&!s.name.StartsWith("shop-W25")&&!s.name.StartsWith("shop-E0")&&!s.name.StartsWith("shop-NW0"))).ToArray();
                else if(onlyShot=="north-sports")shots=shots.Where(s=>s.name=="shop-NS2501"||s.name=="33-january-north-sports-entry").ToArray();
                else if(onlyShot=="la-villa")shots=shots.Where(s=>s.name=="shop-LV2501"||s.name=="34-january-la-villa-row"||s.name=="33-january-north-sports-entry").ToArray();
                else if(!string.IsNullOrEmpty(onlyShot))shots=shots.Where(s=>s.name==onlyShot).ToArray();
                if(shots.Length==0)throw new ArgumentException("Unknown review shot: "+onlyShot);
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
                        File.WriteAllText(Path.Combine(LastOutput,"capture-manifest.json"),JsonUtility.ToJson(new Manifest{timeUtc=DateTime.UtcNow.ToString("O"),finalImageCount=shots.Select(s=>s.name).Distinct().Count(),frames=capturedFrames},true));
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
                var shot=shots[shotIndex];
                capturedFrames.Add(new FrameInfo{file=shot.name+".png",direction=shot.pairHalf==0?"+Z northwest":shot.pairHalf==1?"-Z southeast":"single",position=reviewCamera.transform.position,look=shot.look(),fov=shot.fov});
                if(shot.pairHalf<0)File.WriteAllBytes(Path.Combine(LastOutput,shot.name+".png"),texture.EncodeToPNG());
                else
                {
                    if(shot.pairHalf==0)
                    {
                        if(pairedTexture)UnityEngine.Object.DestroyImmediate(pairedTexture);
                        pairedTexture=new Texture2D(3200,900,TextureFormat.RGB24,false){hideFlags=HideFlags.HideAndDontSave};
                    }
                    if(!pairedTexture)throw new InvalidOperationException("The first half of a bidirectional street capture is missing.");
                    pairedTexture.SetPixels32(shot.pairHalf*1600,0,1600,900,texture.GetPixels32());
                    if(shot.pairHalf==1)
                    {
                        pairedTexture.Apply();File.WriteAllBytes(Path.Combine(LastOutput,shot.name+".png"),pairedTexture.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(pairedTexture);pairedTexture=null;
                    }
                }
            }
            finally{RenderTexture.active=previous;}
        }
        static void PlayModeChanged(PlayModeStateChange change){Cancel();}
        static void Fail(Exception error)
        {
            LastError=error.ToString();Status="failed: "+error.Message;
            try{Debug.LogError("Survey street review capture: "+error.Message);}
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
                    if(pairedTexture)UnityEngine.Object.DestroyImmediate(pairedTexture);
                    if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);
                    target=null;texture=null;pairedTexture=null;capturedFrames=null;cameraObject=null;reviewCamera=null;sleeves=null;sleeveEnabled=null;shots=null;
                }
            }
        }
    }
}
