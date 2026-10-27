using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TaiwanRider.Editor
{
    /// <summary>Reference-informed visual study, not a surveyed reconstruction. All dimensions are metres.</summary>
    public static class IntersectionDetailSetup
    {
        public const string RootName = "Intersection detail • reference-informed v1";
        const string AssetRoot = "Assets/TaiwanRider";
        const string Generated = AssetRoot + "/Art/IntersectionDetail";
        [Serializable] sealed class Building { public string name; public int side; public float center, width, height; }
        [Serializable] sealed class Layout { public Building[] items; }
        [Serializable] sealed class LaneSurvey { public Vector3[] lanePoints; }
        const string ContextPrefix = "LaneContext_";
        public const string ContextRootName = "Unverified lane context • original procedural buildings along OSM curve";
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Batch> batches = new Dictionary<string, Batch>();
        static Transform root;
        static Matrix4x4 frame;
        static Font font;
        static string batchPrefix = "";

        sealed class Batch
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
            public Material material;
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int n = vertices.Count;
                vertices.Add(frame.MultiplyPoint3x4(a)); vertices.Add(frame.MultiplyPoint3x4(b));
                vertices.Add(frame.MultiplyPoint3x4(c)); vertices.Add(frame.MultiplyPoint3x4(d));
                // Metric planar UVs keep rectangular ceramic courses horizontal on both road sides.
                Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                Vector2 UV(Vector3 p) => Mathf.Abs(normal.y)>.7f ? new Vector2(p.x,p.z)/2 :
                    Mathf.Abs(normal.x)>.7f ? new Vector2(p.z,p.y)/2 : new Vector2(p.x,p.y)/2;
                uv.Add(UV(a)); uv.Add(UV(b)); uv.Add(UV(c)); uv.Add(UV(d));
                triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
                triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 3);
            }
        }

        static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out Color c); return c; }
        static Material Mat(string name, string color, float metal = 0, float smooth = .22f, string texture = null)
        {
            string path = Generated + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", Hex(color)); material.SetFloat("_Metallic", metal);
            material.SetFloat("_Smoothness", smooth); material.enableInstancing = true;
            if (texture != null)
            {
                string folder = AssetRoot + "/Art/Textures/" + texture + "/";
                var diffuse=AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "diff.png");
                // URP's scaled detail layer retains grain without multiplying the facade by a
                // charcoal tile atlas or covering every wall with the source stucco's heavy mould.
                bool architectural=texture!="asphalt_02";
                material.SetTexture("_BaseMap",architectural?null:diffuse);
                material.SetTexture("_DetailAlbedoMap",architectural?diffuse:null);
                material.SetFloat("_DetailAlbedoMapScale",texture=="concrete_wall_003"?.12f:.18f);
                material.SetFloat("_DetailNormalMapScale",0);
                material.DisableKeyword("_DETAIL_MULX2");
                if(architectural)material.EnableKeyword("_DETAIL_SCALED");else material.DisableKeyword("_DETAIL_SCALED");
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "nor_gl.png"));
                material.SetFloat("_BumpScale",architectural?.16f:.32f); material.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(material); materials[name] = material; return material;
        }
        static void Box(string material, Vector3 p, Vector3 size, Quaternion rotation = default)
        {
            if (rotation.x==0f && rotation.y==0f && rotation.z==0f && rotation.w==0f) rotation = Quaternion.identity;
            string batchKey=batchPrefix+material;
            if (!batches.TryGetValue(batchKey, out Batch b)) batches[batchKey] = b = new Batch { material = materials[material] };
            Vector3 s = size * .5f;
            Vector3 P(float x, float y, float z) => p + rotation * new Vector3(x * s.x, y * s.y, z * s.z);
            b.Quad(P(-1,-1,-1), P(-1,1,-1), P(1,1,-1), P(1,-1,-1));
            b.Quad(P(-1,-1,1), P(1,-1,1), P(1,1,1), P(-1,1,1));
            b.Quad(P(-1,-1,-1), P(1,-1,-1), P(1,-1,1), P(-1,-1,1));
            b.Quad(P(-1,1,-1), P(-1,1,1), P(1,1,1), P(1,1,-1));
            b.Quad(P(-1,-1,-1), P(-1,-1,1), P(-1,1,1), P(-1,1,-1));
            b.Quad(P(1,-1,-1), P(1,1,-1), P(1,1,1), P(1,-1,1));
        }
        static void Rod(string material, Vector3 a, Vector3 b, float radius, int sides = 8)
        {
            string batchKey=batchPrefix+material;
            if (!batches.TryGetValue(batchKey, out Batch batch)) batches[batchKey] = batch = new Batch { material = materials[material] };
            Quaternion q = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            for (int i = 0; i < sides; i++)
            {
                float t = 2 * Mathf.PI * i / sides, t1 = 2 * Mathf.PI * (i + 1) / sides;
                Vector3 p = q * new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t)) * radius;
                Vector3 p1 = q * new Vector3(Mathf.Cos(t1), 0, Mathf.Sin(t1)) * radius;
                batch.Quad(a + p, b + p, b + p1, a + p1);
            }
        }
        static void SurfaceBox(string material, Vector3 origin, Quaternion q, Vector3 center, Vector3 size)
            => Box(material, origin + q * center, size, q);

        static void Window(Vector3 origin, Quaternion q, float width, float height, bool grille, bool shade)
        {
            SurfaceBox("Recess", origin, q, Vector3.zero, new Vector3(width + .16f, height + .16f, .025f));
            SurfaceBox(shade ? "CurtainGlass" : "WindowGlass", origin, q, new Vector3(0, 0, .025f), new Vector3(width, height, .03f));
            foreach (float x in new[] { -width / 2, 0f, width / 2 })
                SurfaceBox("Aluminium", origin, q, new Vector3(x, 0, .075f), new Vector3(.045f, height + .045f, .065f));
            foreach (float y in new[] { -height / 2, height / 2 })
                SurfaceBox("Aluminium", origin, q, new Vector3(0, y, .075f), new Vector3(width + .09f, .045f, .065f));
            SurfaceBox("StoneTrim", origin, q, new Vector3(0, -height / 2 - .08f, .115f), new Vector3(width + .24f, .10f, .24f));
            if (shade) SurfaceBox("CurtainBand", origin, q, new Vector3(-width * .21f, 0, .045f), new Vector3(width * .34f, height - .04f, .012f));
            if (!grille) return;
            int bars = Mathf.RoundToInt(width / .24f);
            for (int i = 0; i <= bars; i++)
                SurfaceBox("WeatheredSteel", origin, q, new Vector3(-width / 2 + width * i / bars, 0, .28f), new Vector3(.025f, height + .02f, .027f));
            foreach (float y in new[] { -height / 2, height / 2 })
                SurfaceBox("WeatheredSteel", origin, q, new Vector3(0, y, .28f), new Vector3(width + .08f, .027f, .027f));
            foreach (float x in new[] { -width / 2, width / 2 })
                SurfaceBox("WeatheredSteel", origin, q, new Vector3(x, -height / 2, .17f), new Vector3(.025f, .025f, .25f));
        }
        static void Aircon(Vector3 p, Quaternion q, float minimumPipeHeight = float.NegativeInfinity)
        {
            SurfaceBox("Appliance", p, q, Vector3.zero, new Vector3(.74f, .47f, .35f));
            SurfaceBox("Recess", p, q, new Vector3(-.12f,0,.181f), new Vector3(.38f,.34f,.012f));
            for (int i = 0; i < 9; i++) SurfaceBox("Aluminium", p,q,new Vector3(-.12f,-.15f+i*.038f,.20f),new Vector3(.37f,.012f,.015f));
            foreach (float x in new[] { -.27f, .27f })
                SurfaceBox("WeatheredSteel", p,q,new Vector3(x,-.275f,-.03f),new Vector3(.045f,.045f,.47f));
            Vector3 pipeEnd=p+q*new Vector3(.35f,-1.0f,-.06f);pipeEnd.y=Mathf.Max(pipeEnd.y,minimumPipeHeight);
            Rod("RainPipe",p+q*new Vector3(.35f,-.13f,-.05f),pipeEnd,.016f);
        }

        static void Sign(string text, Vector3 p, Quaternion q, float width, string plate)
        {
            SurfaceBox("WeatheredSteel", p,q,Vector3.zero,new Vector3(width+.10f,.54f,.14f));
            SurfaceBox(plate,p,q,new Vector3(0,0,.081f),new Vector3(width,.46f,.035f));
            var go = new GameObject("Fictional shop name • " + text); go.layer = 2; go.transform.SetParent(root,false);
            go.transform.position = frame.MultiplyPoint3x4(p + q * new Vector3(0,0,.105f));
            // TextMesh's readable face is local -Z.
            go.transform.rotation = frame.rotation * q * Quaternion.Euler(0,180,0);
            var label = go.AddComponent<TextMesh>(); label.font=font; label.fontSize=64; label.characterSize=.039f;
            label.anchor=TextAnchor.MiddleCenter; label.alignment=TextAlignment.Center; label.text=text;
            label.color=plate=="SignGreen"?Hex("#DDD7BB"):Hex("#38392F");
            go.GetComponent<MeshRenderer>().sharedMaterial=font.material;
        }

        static void Storefront(Building b, int index, bool upperOnly = false)
        {
            int s=b.side; float w=b.width; string wall = new[] {"IvoryTile","WarmPlaster","GreyTile","FadedGreen","WarmPlaster"}[index%5];
            Box(wall,new Vector3(s*12,(b.height+3.15f)/2,0),new Vector3(8.7f,b.height-3.15f,w-.12f));
            Quaternion front = Quaternion.LookRotation(Vector3.left*s,Vector3.up);
            if (!upperOnly)
            {
            Box("ArcadeConcrete",new Vector3(s*9.30f,3.13f,0),new Vector3(3.55f,.22f,w-.10f));
            Box("ShopWall",new Vector3(s*10.98f,1.67f,0),new Vector3(.30f,3.05f,w-.14f));
            Box("StoneTrim",new Vector3(s*9.28f,.169f,0),new Vector3(3.4f,.033f,w-.15f));
            for(float z=-w/2+.2f;z<=w/2;z+=w-.4f)
            {
                Box(wall,new Vector3(s*7.76f,1.67f,z),new Vector3(.44f,3.03f,.44f));
                Box("ColumnBase",new Vector3(s*7.76f,.38f,z),new Vector3(.51f,.45f,.51f));
                Box("StoneTrim",new Vector3(s*7.76f,.63f,z),new Vector3(.49f,.045f,.49f));
                for(int j=0;j<5;j++)Box("Grout",new Vector3(s*7.527f,.85f+j*.40f,z),new Vector3(.006f,.009f,.44f));
            }
            // Low shop panels sit on the original shop-wall line, behind the walkable arcade.
            Vector3 shop = new Vector3(s*10.75f,1.44f,0);
            float bay = (w-.95f)/2;
            for(int k=0;k<2;k++)
            {
                Vector3 origin=shop+new Vector3(0,0,(k==0?-1:1)*(bay+.1f)/2);
                bool shutter=(index+k)%4==0;
                SurfaceBox("Recess",origin,front,Vector3.zero,new Vector3(bay,2.48f,.025f));
                if(shutter)
                {
                    SurfaceBox("Shutter",origin,front,Vector3.zero,new Vector3(bay-.09f,2.40f,.055f));
                    for(int j=0;j<20;j++)SurfaceBox("ShutterLip",origin,front,new Vector3(0,-1.14f+j*.12f,.065f),new Vector3(bay-.12f,.025f,.022f));
                    SurfaceBox("WeatheredSteel",origin,front,new Vector3(0,-1.14f,.057f),new Vector3(.33f,.05f,.028f));
                }
                else
                {
                    SurfaceBox("ShopGlass",origin,front,Vector3.zero,new Vector3(bay-.09f,2.40f,.035f));
                    SurfaceBox("CurtainBand",origin,front,new Vector3(-bay*.23f,-.65f,.023f),new Vector3(bay*.39f,.81f,.012f));
                    for(int j=-1;j<=1;j++) SurfaceBox("Aluminium",origin,front,new Vector3(j*(bay-.08f)/2,0,.071f),new Vector3(.045f,2.46f,.063f));
                    foreach(float y in new[]{-1.20f,.60f,1.20f})SurfaceBox("Aluminium",origin,front,new Vector3(0,y,.074f),new Vector3(bay-.02f,.045f,.06f));
                    SurfaceBox("WeatheredSteel",origin,front,new Vector3(.11f,-.10f,.12f),new Vector3(.025f,.31f,.04f));
                }
                foreach(float x in new[]{-bay/2,bay/2})SurfaceBox("StoneTrim",origin,front,new Vector3(x,0,.055f),new Vector3(.078f,2.57f,.14f));
            }
            // One restrained fascia per building; names are deliberately fictional.
            string[] names={"日常食堂","生活電器","街角茶舖","文具・影印","老街麵館","機車維修","日常小舖","巷口茶行","生活雜貨"};
            Sign(names[index],new Vector3(s*7.51f,3.34f,0),front,Mathf.Min(w-1.0f,4.5f),index%3==0?"SignGreen":"SignCream");
            // Slightly pitched sheet-metal/canvas canopy, with exposed frame and drip gutter.
            float canopyW=w-.8f;
            string canopy=index%3==0?"AwningWine":index%3==1?"AwningOlive":"AwningGrey";
            Box(canopy,new Vector3(s*7.37f,2.85f,0),new Vector3(1.65f,.055f,canopyW),Quaternion.Euler(0,0,s*6));
            Box(canopy,new Vector3(s*6.55f,2.70f,0),new Vector3(.05f,.20f,canopyW));
            Box("WeatheredSteel",new Vector3(s*6.53f,2.81f,0),new Vector3(.10f,.10f,canopyW+.1f));
            for(float z=-canopyW/2+.15f;z<canopyW/2;z+=1.2f)
            {
                Rod("WeatheredSteel",new Vector3(s*8.1f,2.53f,z),new Vector3(s*6.60f,2.78f,z),.018f);
                Rod("WeatheredSteel",new Vector3(s*8.05f,2.94f,z),new Vector3(s*6.6f,2.78f,z),.018f);
            }
            // Small ceiling fixtures and surface conduits reveal arcade depth.
            Box("Appliance",new Vector3(s*9.18f,2.987f,0),new Vector3(.15f,.055f,1.24f));
            Box("LampDiffuser",new Vector3(s*9.18f,2.953f,0),new Vector3(.115f,.018f,1.16f));
            Rod("RainPipe",new Vector3(s*10.57f,.3f,w*.42f),new Vector3(s*10.57f,3.0f,w*.42f),.023f);
            Box("MeterBox",new Vector3(s*10.50f,1.9f,w*.42f),new Vector3(.16f,.50f,.28f));
            for(int i=0;i<2;i++)Box("WindowGlass",new Vector3(s*10.413f,1.84f+i*.17f,w*.42f),new Vector3(.008f,.095f,.19f));
            }
            // Residential stories have varied windows, curtain panels and sparse service equipment.
            int floors=Mathf.RoundToInt((b.height-3.3f)/2.9f), count=Mathf.Max(2,Mathf.FloorToInt(w/2.6f));
            for(int f=0;f<floors;f++)
            {
                float y=4.90f+f*2.9f;
                for(int j=0;j<count;j++)
                {
                    float z=Mathf.Lerp(-w*.34f,w*.34f,count==1?.5f:(float)j/(count-1));
                    float windowWidth=1.28f+.14f*((index+j)%4),windowHeight=1.36f+.10f*((index+f)%3);
                    Window(new Vector3(s*7.585f,y,z),front,windowWidth,windowHeight,(index+j+f)%4==0,(index+j+f)%3==0);
                    if((index+j+f)%3==1)Aircon(new Vector3(s*7.28f,y-1.15f,z+.25f),front,upperOnly?3.3f:float.NegativeInfinity);
                    if((index+j+f)%5==2)Box("WaterStain",new Vector3(s*7.645f,y-1.18f,z-.70f),new Vector3(.005f,.55f,.085f));
                }
                Box("StoneTrim",new Vector3(s*7.59f,y+1.09f,0),new Vector3(.19f,.10f,w-.08f));
            }
            float pipeZ=-w*.45f;
            Rod("RainPipe",new Vector3(s*7.40f,upperOnly?3.3f:.35f,pipeZ),new Vector3(s*7.40f,b.height+.17f,pipeZ),.04f);
            for(float y=1.0f;y<b.height;y+=2.9f)if(!upperOnly || y>=3.3f)Box("WeatheredSteel",new Vector3(s*7.415f,y,pipeZ),new Vector3(.10f,.042f,.12f));
            Box("StoneTrim",new Vector3(s*7.90f,b.height+.18f,0),new Vector3(.40f,.36f,w-.07f));
            Box("RoofCoping",new Vector3(s*7.90f,b.height+.39f,0),new Vector3(.48f,.075f,w-.03f));
            // The two alley-facing returns replace the former uninterrupted dark side walls.
            if(b.name=="Building_1_40" || b.name=="Building_1_41")
            {
                int end=b.name=="Building_1_40"?1:-1;
                Quaternion side=Quaternion.LookRotation(Vector3.forward*end,Vector3.up);
                float z=end*(w/2-.025f);
                for(int f=0;f<floors;f++)
                {
                    float y=4.9f+f*2.9f;
                    foreach(float x in new[]{10.0f,13.25f})Window(new Vector3(x,y,z),side,1.55f,1.50f,f%2==0,true);
                    Box("StoneTrim",new Vector3(12,y+1.09f,z),new Vector3(8.78f,.10f,.16f));
                    if(f%2==0)Aircon(new Vector3(14.5f,y-1.15f,z+end*.22f),side,upperOnly?3.3f:float.NegativeInfinity);
                }
                Rod("RainPipe",new Vector3(15.62f,upperOnly?3.3f:.35f,z+end*.09f),new Vector3(15.62f,b.height+.18f,z+end*.09f),.047f);
                // Small rear side-wall threshold, not a barrier in the 9 m alley.
                if (!upperOnly)
                {
                Box("ShopWall",new Vector3(13.45f,1.65f,z-end*.14f),new Vector3(5.9f,3.0f,.18f));
                Window(new Vector3(12.2f,1.6f,z+end*.03f),side,1.65f,1.6f,true,false);
                SurfaceBox("Shutter",new Vector3(14.5f,1.4f,z),side,Vector3.zero,new Vector3(1.2f,2.4f,.035f));
                }
            }
        }

        static void Pavement(RoadRoute route)
        {
            for(int s=-1;s<=1;s+=2)
            {
                for(float z=-23;z<23;z+=.9f)
                {
                    if(s>0 && z>-7.5f && z<6.5f)continue;
                    frame=Matrix4x4.TRS(route.Position(z),route.Heading(z),Vector3.one);
                    // Expansion joints, subtle edge erosion and repaired paving. No raised collision geometry.
                    Box("Grout",new Vector3(s*8.0f,.190f,0),new Vector3(3.35f,.005f,.012f));
                    Box("Gutter",new Vector3(s*5.985f,.012f,0),new Vector3(.14f,.005f,.87f));
                    if(Mathf.RoundToInt(z*10)%3==0)Box("CurbWear",new Vector3(s*6.098f,.162f,.11f),new Vector3(.145f,.009f,.12f));
                }
                foreach(float z in new[]{-20.5f,-11.5f,8.5f,18.5f})
                {
                    frame=Matrix4x4.TRS(route.Position(z),route.Heading(z),Vector3.one);
                    Box("Recess",new Vector3(s*5.91f,.017f,0),new Vector3(.40f,.011f,.66f));
                    foreach(float x in new[]{-.19f,.19f})Box("DrainSteel",new Vector3(s*5.91f+x,.026f,0),new Vector3(.025f,.02f,.69f));
                    for(int j=0;j<11;j++)Box("DrainSteel",new Vector3(s*5.91f,.028f,-.31f+j*.062f),new Vector3(.40f,.015f,.020f));
                    Box("PavingRepair",new Vector3(s*6.66f,.17f,.7f),new Vector3(.65f,.027f,1.15f));
                    for(int j=0;j<4;j++)Box("Grout",new Vector3(s*6.66f,.186f,.25f+j*.30f),new Vector3(.65f,.006f,.009f));
                }
            }
            frame=Matrix4x4.identity;
        }

        static void AlleyBackdrop()
        {
            // Visual-only continuation of the existing prototype alley. This is an inferred backdrop,
            // not extra playable space or a claim about individual real buildings on Lane 90.
            frame=Matrix4x4.identity;
            Box("BackdropAsphalt",new Vector3(54,-.025f,0),new Vector3(44,.05f,9));
            Box("PavingRepair",new Vector3(45,-.15f,0),new Vector3(76,.20f,60));
            foreach(int s in new[]{-1,1})
            {
                Box("StoneTrim",new Vector3(46,.09f,s*5.60f),new Vector3(59.4f,.18f,2.20f));
                Box("ColumnBase",new Vector3(46,.085f,s*4.53f),new Vector3(59.4f,.17f,.12f));
                for(int i=0;i<7;i++)
                    ContextBuilding(s,i,20.5f+i*7.8f);
            }
            // A stepped block at the far end screens the finite mesh edge behind fog and perspective.
            Box("GreyTile",new Vector3(79,8.1f,0),new Vector3(6,16.2f,31));
            Quaternion west=Quaternion.LookRotation(Vector3.left,Vector3.up);
            for(int f=0;f<5;f++)for(int j=-4;j<=4;j++)
                Window(new Vector3(75.98f,2.9f+f*2.6f,j*3.1f),west,1.35f,1.45f,false,(j+f)%3==0);
        }

        // The same unverified building template is used by the original straight backdrop
        // and by the optional curved context. A whole building gets one rigid frame.
        static void ContextBuilding(int s,int i,float x)
        {
                    Quaternion facing=Quaternion.LookRotation(Vector3.back*s,Vector3.up);
                    float height=10.2f+(i%3)*2.8f;
                    string wall=new[]{"WarmPlaster","GreyTile","IvoryTile","FadedGreen"}[(i+(s>0?1:0))%4];
                    Box(wall,new Vector3(x,height/2,s*10.75f),new Vector3(7.70f,height,8.1f));
                    Box("RoofCoping",new Vector3(x,height+.12f,s*6.73f),new Vector3(7.75f,.25f,.27f));
                    for(int f=0;f<3+(i%3);f++)
                    {
                        float y=4.2f+f*2.6f;if(y+1>height)continue;
                        foreach(float dx in new[]{-2.15f,1.65f})
                            Window(new Vector3(x+dx,y,s*6.665f),facing,1.48f,1.40f,(i+f)%3==0,(i+f)%2==0);
                        Box("StoneTrim",new Vector3(x,y+1.05f,s*6.62f),new Vector3(7.73f,.08f,.15f));
                        if(f==0 && i%2==0)Aircon(new Vector3(x+2.0f,y-.98f,s*6.42f),facing);
                    }
                    for(int j=0;j<2;j++)
                    {
                        Vector3 p=new Vector3(x+(j==0?-1.8f:1.8f),1.35f,s*6.66f);
                        SurfaceBox("Shutter",p,facing,Vector3.zero,new Vector3(2.95f,2.4f,.03f));
                        for(int k=0;k<16;k++)SurfaceBox("ShutterLip",p,facing,new Vector3(0,-1.10f+k*.15f,.06f),new Vector3(2.91f,.025f,.022f));
                    }
                    Box(i%2==0?"AwningOlive":"AwningGrey",new Vector3(x,2.72f,s*6.08f),new Vector3(7.3f,.075f,1.20f),Quaternion.Euler(s*-5,0,0));
                    Rod("RainPipe",new Vector3(x+3.4f,.2f,s*6.55f),new Vector3(x+3.4f,height,s*6.55f),.035f);
        }

        static Vector3[] ReadContextLane()
        {
            var survey=JsonUtility.FromJson<LaneSurvey>(File.ReadAllText(AssetRoot+"/Data/RouteSurvey.json"));
            var points=survey==null?null:survey.lanePoints;
            if(points==null || points.Length<2)throw new InvalidOperationException("Lane 90 survey is missing.");
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];
                if(float.IsNaN(p.x)||float.IsInfinity(p.x)||float.IsNaN(p.z)||float.IsInfinity(p.z)||p.y!=0 || (i>0 && p.x<=points[i-1].x))
                    throw new InvalidOperationException("Lane context requires a finite eastward ground-level centreline.");
            }
            return points;
        }
        static Matrix4x4 ContextFrame(Vector3[] points,float x)
        {
            int end=1;while(end<points.Length-1 && x>points[end].x)end++;
            Vector3 a=points[end-1],b=points[end];
            // Only context beyond the final node is extrapolated, never the surveyed road.
            Vector3 position=Vector3.LerpUnclamped(a,b,(x-a.x)/(b.x-a.x));
            return Matrix4x4.TRS(position,Quaternion.FromToRotation(Vector3.right,(b-a).normalized),Vector3.one);
        }
        static void CurvedLaneContext(Vector3[] points)
        {
            batchPrefix=ContextPrefix;
            try
            {
                frame=Matrix4x4.identity;
                float lastX=points[points.Length-1].x;
                // Neutral visual ground below every roadway. This is inferred view fill,
                // not asphalt, a measured parcel, a lane extension or additional playable space.
                float farX=lastX+80f;
                Box("PavingRepair",new Vector3((7f+farX)/2,-.16f,points[points.Length-1].z/2),new Vector3(farX-7f,.16f,120f));
                var cuts=new List<float>{16.3f};
                cuts.AddRange(points.Where(p=>p.x>16.3f).Select(p=>p.x));cuts.Add(lastX+25f);
                for(int i=1;i<cuts.Count;i++)
                {
                    Vector3 a=ContextFrame(points,cuts[i-1]).MultiplyPoint3x4(Vector3.zero),b=ContextFrame(points,cuts[i]).MultiplyPoint3x4(Vector3.zero);
                    frame=Matrix4x4.TRS((a+b)/2,Quaternion.FromToRotation(Vector3.right,(b-a).normalized),Vector3.one);
                    float length=Vector3.Distance(a,b)+.03f;
                    foreach(int s in new[]{-1,1})
                    {
                        Box("StoneTrim",new Vector3(0,.09f,s*5.75f),new Vector3(length,.18f,2.20f));
                        Box("ColumnBase",new Vector3(0,.085f,s*4.60f),new Vector3(length,.17f,.12f));
                    }
                }
                int count=Mathf.CeilToInt((lastX+12f-20.5f)/7.8f)+1;
                foreach(int s in new[]{-1,1})for(int i=0;i<count;i++)
                {
                    frame=ContextFrame(points,20.5f+i*7.8f);
                    ContextBuilding(s,i,0);
                }
                // Intentionally no BackdropAsphalt and no terminal block across the lane.
            }
            finally{frame=Matrix4x4.identity;batchPrefix="";}
        }

        static void PrepareMaterials()
        {
            Mat("IvoryTile","#D6D1BE",0,.20f,"concrete_tile_facade");
            Mat("WarmPlaster","#D6C9B3",0,.16f,"concrete_wall_003");
            Mat("GreyTile","#BDC6C3",0,.22f,"concrete_tile_facade");
            Mat("FadedGreen","#B7BEAB",0,.18f,"concrete_wall_003");
            Mat("ArcadeConcrete","#B9B7A8",0,.14f,"concrete_wall_003");
            Mat("ShopWall","#CDC8B6",0,.17f,"concrete_wall_003");
            Mat("StoneTrim","#ACA99B",0,.23f,"concrete_floor_worn_001");
            Mat("ColumnBase","#807F70",0,.23f,"concrete_floor_worn_001");
            Mat("RoofCoping","#9A9C92",0,.18f,"concrete_wall_003");
            Mat("PavingRepair","#ABA898",0,.12f,"concrete_floor_worn_001");
            Mat("Recess","#28312F",0,.08f); Mat("WindowGlass","#4E6667",.16f,.63f);
            Mat("CurtainGlass","#76837C",.08f,.45f); Mat("CurtainBand","#B7B4A1",0,.15f);
            Mat("ShopGlass","#5D706A",.14f,.55f); Mat("Aluminium","#B5BCB5",.58f,.46f);
            Mat("WeatheredSteel","#626A64",.43f,.24f); Mat("RainPipe","#8E9186",.14f,.19f);
            Mat("Appliance","#C4C4B4",.1f,.3f); Mat("MeterBox","#9C9D8C",.4f,.28f);
            Mat("Shutter","#808A82",.30f,.36f); Mat("ShutterLip","#657269",.35f,.35f);
            Mat("AwningWine","#69534D",0,.13f); Mat("AwningOlive","#69756B",0,.13f);
            Mat("AwningGrey","#8A8F83",.24f,.24f); Mat("SignGreen","#345D50",0,.3f);
            Mat("SignCream","#CDC7AC",0,.25f); Mat("Grout","#41483F",0,.10f);
            Mat("Gutter","#515B53",0,.12f); Mat("CurbWear","#929286",0,.12f);
            Mat("DrainSteel","#727D75",.45f,.23f); Mat("WaterStain","#989C8C",0,.12f);
            Mat("BackdropAsphalt","#AAAFA6",0,.12f,"asphalt_02");
            var lamp=Mat("LampDiffuser","#D6D5BC",0,.32f); lamp.EnableKeyword("_EMISSION"); lamp.SetColor("_EmissionColor",Hex("#D6D5BC")*.13f);
        }
        static Building[] ReadBuildings() => JsonUtility.FromJson<Layout>("{\"items\":"+File.ReadAllText("Sources/building-layout.json")+"}").items.Where(b=>Mathf.Abs(b.center)<22).ToArray();
        static Transform FindOriginal(string name) => SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(g=>g.name.StartsWith("GongguanBuildings",StringComparison.Ordinal))
            .SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name==name);

        // Bounds refer to building-layout.json center values, not clipping planes.
        // This shared predicate keeps facade and street-dressing selection identical.
        public static bool ExcludesBuilding(int side, float center, int excludedSide, float minBuildingCenter, float maxBuildingCenter)
            => excludedSide!=0 && side==excludedSide && center>=minBuildingCenter && center<=maxBuildingCenter;
        public static void ValidateExclusion(int excludedSide, float minBuildingCenter, float maxBuildingCenter)
        {
            if(excludedSide!=0 && excludedSide!=-1 && excludedSide!=1)throw new ArgumentOutOfRangeException(nameof(excludedSide),"Use -1 for left, +1 for right, or 0 for no exclusion.");
            if(float.IsNaN(minBuildingCenter)||float.IsInfinity(minBuildingCenter)||float.IsNaN(maxBuildingCenter)||float.IsInfinity(maxBuildingCenter)||minBuildingCenter>maxBuildingCenter)
                throw new ArgumentException("Building-center bounds must be finite and ordered.");
        }

        [MenuItem("Taiwan Rider/Realism/Apply intersection detail")]
        public static void Apply() => Apply(0,0,0);

        /// <summary>Rebuilds the existing nine detail buildings, optionally omitting a side/center range.
        /// Originals remain hidden even when their detail is omitted. Upper-only retains the
        /// unmeasured residential mass above 3.15 m; it does not claim a site-matched upper facade.</summary>
        public static void Apply(int excludedSide, float minBuildingCenter, float maxBuildingCenter,
            bool includeAlleyBackdrop = true, bool keepExcludedUpperFloors = false, bool curveContextToLane = false)
        {
            ValidateExclusion(excludedSide,minBuildingCenter,maxBuildingCenter);
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=AssetRoot+"/Scenes/GongguanRide.unity")throw new InvalidOperationException("Open GongguanRide outside Play mode first.");
            var route=UnityEngine.Object.FindFirstObjectByType<RoadRoute>();
            if(!route)throw new InvalidOperationException("RoadRoute missing.");
            Vector3[] contextLane=curveContextToLane?ReadContextLane():null;
            Building[] buildings=ReadBuildings();
            foreach(var b in buildings)if(!FindOriginal(b.name))throw new InvalidOperationException("Original building missing: "+b.name);
            font=AssetDatabase.LoadAssetAtPath<Font>(AssetRoot+"/Art/Fonts/NotoSansTC.otf");
            if(!font)throw new InvalidOperationException("NotoSansTC font missing.");
            Directory.CreateDirectory(Generated);AssetDatabase.Refresh();materials.Clear();batches.Clear();batchPrefix="";PrepareMaterials();
            int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Refine Gongguan intersection visuals");
            var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(old)Undo.DestroyObjectImmediate(old);
            root=new GameObject(RootName).transform;root.gameObject.layer=2;Undo.RegisterCreatedObjectUndo(root.gameObject,"Intersection detail");
            for(int i=0;i<buildings.Length;i++)
            {
                var b=buildings[i];var original=FindOriginal(b.name);Undo.RecordObject(original.gameObject,"Preserve original building behind detail layer");original.gameObject.SetActive(false);
                bool excluded=ExcludesBuilding(b.side,b.center,excludedSide,minBuildingCenter,maxBuildingCenter);
                if(excluded && !keepExcludedUpperFloors)continue;
                frame=Matrix4x4.TRS(route.Position(b.center),route.Heading(b.center),Vector3.one);Storefront(b,i,excluded);
            }
            Pavement(route);
            if(curveContextToLane)CurvedLaneContext(contextLane);else if(includeAlleyBackdrop)AlleyBackdrop();
            Transform contextRoot=null;
            if(curveContextToLane)
            {
                contextRoot=new GameObject(ContextRootName).transform;contextRoot.gameObject.layer=2;contextRoot.SetParent(root,false);
            }
            foreach(var pair in batches)
            {
                bool context=pair.Key.StartsWith(ContextPrefix,StringComparison.Ordinal);
                string materialKey=context?pair.Key.Substring(ContextPrefix.Length):pair.Key;
                var b=pair.Value;string name="Intersection_"+pair.Key,path=Generated+"/"+name+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool existing=mesh;
                // Mutating the retained Mesh directly invalidates native render buffers.
                // CopySerialized alone can leave the old facade visible with GPU resident drawing.
                if(existing)mesh.Clear();else mesh=new Mesh();mesh.name=name;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(b.vertices);mesh.SetUVs(0,b.uv);mesh.SetTriangles(b.triangles,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                if(existing)EditorUtility.SetDirty(mesh);
                else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject(context?"Unverified lane context • "+materialKey:"Buildings_and_pavement_"+pair.Key);go.layer=2;go.transform.SetParent(context?contextRoot:root,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=b.material;
                // Millimetre-scale trim cannot cast stable shadows into the existing street shadow
                // atlas. Buildings/canopies retain their shadows; fine grilles and ribs use lighting only.
                bool fine=materialKey=="Aluminium" || materialKey=="WeatheredSteel" || materialKey=="ShutterLip" || materialKey=="Grout" || materialKey=="WaterStain";
                renderer.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;renderer.receiveShadows=!fine;
                b.material.SetFloat("_ReceiveShadows",fine?0:1);
                if(fine)b.material.EnableKeyword("_RECEIVE_SHADOWS_OFF");else b.material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                EditorUtility.SetDirty(b.material);
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
            Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            int excludedCount=buildings.Count(b=>ExcludesBuilding(b.side,b.center,excludedSide,minBuildingCenter,maxBuildingCenter));
            Debug.Log("Intersection detail applied: "+(buildings.Length-excludedCount)+" complete reference-informed facades, "+excludedCount+(keepExcludedUpperFloors?" upper-only facades":" omitted facades")+", straight backdrop "+(includeAlleyBackdrop&&!curveContextToLane)+", unverified curved lane context "+curveContextToLane+", "+batches.Count+" material batches, 0 added colliders. Scene remains unsaved for review.");
        }

        [MenuItem("Taiwan Rider/Realism/Remove intersection detail")]
        public static void Remove()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");
            var scene=SceneManager.GetActiveScene();var detail=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);
            if(detail)Undo.DestroyObjectImmediate(detail);
            foreach(var b in ReadBuildings()) { var original=FindOriginal(b.name);if(original){Undo.RecordObject(original.gameObject,"Restore original building");original.gameObject.SetActive(true);} }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
