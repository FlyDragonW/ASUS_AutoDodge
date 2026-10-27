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
    /// <summary>
    /// Facade study: operator image has unknown capture date; 156 updated from 2025 author evidence.
    /// https://www.jyes.com.tw/data/editor/files/FBm20250306-0201CO.jpg
    /// Origin is pavement level at the three-shop frontage centre. Local +Z faces the street.
    /// Seen from the street, photograph-right is local -X: Daming +X / Jyes centre / MiaoGuanYin -X.
    /// Width, height and depth below are estimated. Only the photographed upper-wall crop is built.
    /// Build does not relocate roads, old buildings, colliders, parked vehicles or gameplay objects.
    /// </summary>
    public static class SiteFacadeSetup
    {
        public const string RootName = "Site facade study • Tingzhou 156–158-1 • 2025 evidence revision";
        public const float EstimatedWidth = 11.0f;
        public const float VisibleHeight = 6.85f;
        const string Art = "Assets/TaiwanRider/Art/SiteMatch";
        const string FontPath = "Assets/TaiwanRider/Art/Fonts/NotoSansTC.otf";
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Geometry> geometry = new Dictionary<string, Geometry>();
        static readonly List<Lettering> lettering = new List<Lettering>();
        static Font font;

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> indices = new List<int>();
            public readonly List<Color32> colors = new List<Color32>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int n = vertices.Count; Vector3 normal = Vector3.Cross(b-a,c-a).normalized;
                foreach (Vector3 p in new[] { a,b,c,d })
                {
                    vertices.Add(p);
                    uv.Add(Mathf.Abs(normal.y)>.7f ? new Vector2(p.x,p.z) : Mathf.Abs(normal.x)>.7f ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y));
                }
                indices.AddRange(new[] { n,n+1,n+2,n,n+2,n+3 });
            }
        }
        sealed class Lettering
        {
            public string text; public Vector3 position; public float height, width, yaw; public Color color;
        }
        static Color Hex(string code) { ColorUtility.TryParseHtmlString(code,out var c); return c; }
        static Geometry G(string key)
        { if(!geometry.TryGetValue(key,out var value)) geometry[key]=value=new Geometry(); return value; }
        static Material M(string key,string color,float metallic=0f,float smoothness=.24f)
        {
            string path=Art+"/"+key+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m) { m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path); }
            m.name=key;m.SetColor("_BaseColor",Hex(color));m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
            materials[key]=m;EditorUtility.SetDirty(m);return m;
        }
        static void Box(string key,Vector3 p,Vector3 size,Quaternion? orientation=null)
        {
            var q=orientation??Quaternion.identity;var g=G(key);Vector3 h=size*.5f;
            Vector3 P(float x,float y,float z)=>p+q*Vector3.Scale(new Vector3(x,y,z),h);
            g.Quad(P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),P(1,-1,-1));
            g.Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1));
            g.Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));
            g.Quad(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));
            g.Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1));
            g.Quad(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1));
        }
        static void Rod(string key,Vector3 a,Vector3 b,float radius,int sides=8)
        {
            var g=G(key);var q=Quaternion.FromToRotation(Vector3.up,b-a);
            for(int i=0;i<sides;i++)
            {
                float t=i*2*Mathf.PI/sides,t1=(i+1)*2*Mathf.PI/sides;
                Vector3 d=q*new Vector3(Mathf.Cos(t)*radius,0,Mathf.Sin(t)*radius),e=q*new Vector3(Mathf.Cos(t1)*radius,0,Mathf.Sin(t1)*radius);
                g.Quad(a+d,b+d,b+e,a+e);g.Quad(b,b+e,b+d,b);g.Quad(a,a+d,a+e,a);
            }
        }
        static void Label(string text,Vector3 p,float height,string color,float width=0f,float yaw=0f)
        { lettering.Add(new Lettering{text=text,position=p,height=height,width=width,yaw=yaw,color=Hex(color)}); }
        static void PaintedTexture()
        {
            const int n=256;var image=new Texture2D(n,n,TextureFormat.RGB24,false);
            var pixels=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float grain=Mathf.PerlinNoise(x*.39f,y*.39f)*.025f;
                float uneven=Mathf.PerlinNoise(x*.023f,y*.029f)*.055f;
                float damp=Mathf.Pow(Mathf.PerlinNoise(x*.045f,2.4f),5f)*Mathf.Max(0,.40f-y/(float)n)*.14f;
                float a=.93f+grain+uneven-damp;pixels[y*n+x]=new Color(a*.99f,a,a*.98f);
            }
            image.SetPixels(pixels);image.Apply();File.WriteAllBytes(Art+"/OriginalPlasterGrain.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(Art+"/OriginalPlasterGrain.png",ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(Art+"/OriginalPlasterGrain.png");importer.wrapMode=TextureWrapMode.Repeat;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/OriginalPlasterGrain.png");
            foreach(string key in new[]{"AgedWall","WallRepair","Concrete","Floor"})
            { materials[key].SetTexture("_BaseMap",texture);materials[key].SetTextureScale("_BaseMap",Vector2.one*.7f);EditorUtility.SetDirty(materials[key]); }
        }
        static void Materials()
        {
            M("AgedWall","#878D87",0,.14f);M("WallRepair","#ADB1A8",0,.13f);M("Concrete","#757A75",0,.17f);
            M("Floor","#9E9F94",0,.21f);M("Joint","#696D65",0,.10f);M("Metal","#6A726F",.52f,.37f);
            M("PaleMetal","#A0A8A2",.45f,.4f);M("DarkMetal","#333E3B",.35f,.22f);M("Shadow","#343A35",0,.12f);
            M("Window","#3D5250",.18f,.50f);M("WindowLight","#596760",.10f,.46f);M("Curtain","#827F70",0,.10f);
            M("DamingWood","#9B612F",0,.20f);M("DamingGrain","#89552C",0,.19f);M("DamingGold","#D9BC7B",.10f,.29f);
            M("Shutter","#9C9E97",.35f,.24f);M("ShutterRib","#797D78",.45f,.22f);
            M("JyesBlue","#135BA0",0,.30f);M("JyesEdge","#184D85",0,.30f);M("JyesYellow","#D9C235",0,.29f);
            M("JyesPale","#D3D7BE",0,.25f);M("Paper","#D2D1BD",0,.12f);M("PaperGrey","#969D99",0,.15f);
            M("PosterPink","#AE837A",0,.14f);M("PosterCyan","#65929B",0,.19f);M("PosterInk","#455665",0,.21f);
            M("WarmInside","#B4B3A1",0,.15f);M("Display","#BCBFAE",.1f,.3f);
            M("RamenSign","#B7A34F",0,.21f);M("RamenWood","#64554B",0,.15f);M("Lantern","#AF3327",0,.18f);M("LanternRib","#903126",0,.17f);
            var glass=M("Glass","#93AAA6",0,.73f);glass.SetFloat("_Surface",1);glass.SetFloat("_Blend",0);glass.SetColor("_BaseColor",new Color(.58f,.66f,.63f,.11f));
            glass.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);glass.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);glass.SetFloat("_ZWrite",0);glass.SetFloat("_Cull",0);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");glass.SetOverrideTag("RenderType","Transparent");glass.renderQueue=(int)RenderQueue.Transparent;EditorUtility.SetDirty(glass);
            PaintedTexture();
        }
        static void WindowBay(float x,float width,float bottom,float height,bool bars,bool light=false)
        {
            float cy=bottom+height/2;
            Box("Shadow",new Vector3(x,cy,-.13f),new Vector3(width,height,.035f));
            int panes=Mathf.Max(2,Mathf.RoundToInt(width/.69f));float unit=width/panes;
            for(int i=0;i<panes;i++)
            {
                float px=x-width/2+unit*(i+.5f);
                Box((light&&i%3==0)?"Curtain":i%3==1?"WindowLight":"Window",new Vector3(px,cy,-.07f),new Vector3(unit-.043f,height-.055f,.02f));
                Box("PaleMetal",new Vector3(px-unit/2,cy,.016f),new Vector3(.029f,height,.04f));
            }
            foreach(float edge in new[]{-1f,1f})
            {
                Box("Metal",new Vector3(x+edge*width/2,cy,.014f),new Vector3(.048f,height+.08f,.075f));
                Box("PaleMetal",new Vector3(x,cy+edge*height/2,.018f),new Vector3(width+.055f,.044f,.078f));
            }
            Box("Concrete",new Vector3(x,bottom-.069f,.075f),new Vector3(width+.12f,.12f,.30f));
            if(!bars)return;
            float strip=Mathf.Min(width,.86f),start=x+width/2-strip;
            for(int i=0;i<=7;i++)Box("PaleMetal",new Vector3(start+strip*i/7,cy,.12f),new Vector3(.022f,height+.05f,.029f));
            foreach(float y in new[]{bottom+.04f,bottom+height-.04f})Box("Metal",new Vector3(start+strip/2,y,.12f),new Vector3(strip+.025f,.027f,.045f));
        }
        static void Aircon(Vector3 p,float width=.72f)
        {
            Box("PaleMetal",p,new Vector3(width,.43f,.31f));
            Box("DarkMetal",p+new Vector3(-width*.12f,0,.167f),new Vector3(width*.61f,.31f,.024f));
            for(int i=0;i<7;i++)Box("Metal",p+new Vector3(-width*.12f,-.133f+i*.044f,.186f),new Vector3(width*.59f,.014f,.024f));
            foreach(float x in new[]{-.26f,.26f})Box("DarkMetal",p+new Vector3(x,-.24f,-.035f),new Vector3(.036f,.035f,.42f));
            Rod("PaleMetal",p+new Vector3(width*.4f,-.05f,-.04f),p+new Vector3(width*.4f,-.50f,-.04f),.018f);
        }
        static void UpperCrop()
        {
            // Continuous older frontage, with actual gaps for its photographed window bands.
            foreach(var band in new[]{new Vector2(3.22f,3.57f),new Vector2(4.49f,5.83f)})
                Box("AgedWall",new Vector3(0,(band.x+band.y)/2,-.12f),new Vector3(EstimatedWidth,band.y-band.x,.25f));
            foreach(float x in new[]{-5.40f,-1.78f,1.84f,5.38f})Box("Concrete",new Vector3(x,5.00f,-.12f),new Vector3(.16f,3.70f,.26f));
            Box("WallRepair",new Vector3(.02f,5.05f,.015f),new Vector3(3.30f,.89f,.028f));
            Box("WallRepair",new Vector3(3.60f,5.16f,.008f),new Vector3(3.13f,.80f,.020f));
            WindowBay(3.57f,3.35f,3.58f,.86f,true,true);WindowBay(.02f,3.40f,3.58f,.86f,true);
            WindowBay(-3.62f,3.30f,3.58f,.86f,false);
            // Ramen upper window has broad dark louvres rather than another identical pane layout.
            for(int i=0;i<8;i++)Box("DarkMetal",new Vector3(-4.36f,3.66f+i*.092f,.055f),new Vector3(1.32f,.034f,.044f));
            for(int i=0;i<8;i++)Box("DarkMetal",new Vector3(-2.94f,3.66f+i*.092f,.055f),new Vector3(1.23f,.034f,.044f));
            WindowBay(3.57f,3.35f,5.86f,.92f,true);WindowBay(.02f,3.40f,5.86f,.92f,false);WindowBay(-3.62f,3.30f,5.86f,.92f,true);
            Aircon(new Vector3(-.73f,5.48f,.10f),.74f);Aircon(new Vector3(-2.24f,3.58f,.25f),.86f);
            // In the crop, an AC appliance and pots occupy the top right window line.
            Aircon(new Vector3(-2.80f,6.16f,.17f),.67f);
            Box("RamenWood",new Vector3(-3.60f,5.94f,.13f),new Vector3(.38f,.19f,.25f));
            Rod("DarkMetal",new Vector3(1.89f,3.21f,.10f),new Vector3(1.89f,VisibleHeight,.10f),.024f);
            Rod("DarkMetal",new Vector3(-1.80f,3.18f,.08f),new Vector3(-1.80f,VisibleHeight,.08f),.020f);
            Rod("DarkMetal",new Vector3(-5.45f,3.60f,.27f),new Vector3(-1.82f,3.27f,.24f),.014f);
            Rod("DarkMetal",new Vector3(-5.45f,3.55f,.25f),new Vector3(-1.82f,3.23f,.21f),.010f);
            Box("Concrete",new Vector3(0,VisibleHeight,-.09f),new Vector3(EstimatedWidth,.08f,.30f));
        }
        static void SharedGround()
        {
            Box("Concrete",new Vector3(0,.055f,-.40f),new Vector3(EstimatedWidth,.11f,2.34f));
            Box("Floor",new Vector3(0,.126f,-.41f),new Vector3(EstimatedWidth,.033f,2.28f));
            for(int i=0;i<=27;i++)Box("Joint",new Vector3(-5.5f+i*.40f,.145f,-.40f),new Vector3(.006f,.004f,2.24f));
            for(int i=0;i<6;i++)Box("Joint",new Vector3(0,.145f,-1.52f+i*.40f),new Vector3(EstimatedWidth,.004f,.006f));
            // The photo has a shallow lintel above the signs. A lower projecting beam would
            // physically hide the upper strokes of both Daming and Jyes lettering.
            Box("Shadow",new Vector3(0,3.29f,-.62f),new Vector3(EstimatedWidth,.10f,1.45f));
            foreach(float x in new[]{-5.40f,-1.78f,1.84f,5.38f})Box("Concrete",new Vector3(x,1.66f,-.04f),new Vector3(.15f,3.07f,.20f));
        }
        static void Daming()
        {
            const float x=3.62f,w=3.42f;
            Box("DamingWood",new Vector3(x,2.85f,.09f),new Vector3(w,.77f,.22f));
            for(int i=0;i<7;i++)Box("DamingGrain",new Vector3(x,2.49f+i*.114f,.208f),new Vector3(w-.05f,.009f,.005f));
            Label("大茗",new Vector3(x,2.97f,.217f),.48f,"#D9C999",1.80f);
            Label("DAMING",new Vector3(x,2.62f,.217f),.15f,"#DED1AD",1.44f);
            Box("DarkMetal",new Vector3(x,1.38f,-.075f),new Vector3(w,2.22f,.07f));
            Box("Shutter",new Vector3(x,1.47f,-.010f),new Vector3(w-.12f,1.78f,.06f));
            for(int i=0;i<24;i++)Box("ShutterRib",new Vector3(x,.59f+i*.075f,.033f),new Vector3(w-.13f,.024f,.018f));
            Box("DamingWood",new Vector3(x,.41f,.035f),new Vector3(w-.11f,.41f,.038f));
            for(int i=0;i<6;i++)Box("DamingGrain",new Vector3(x,.225f+i*.068f,.060f),new Vector3(w-.12f,.015f,.012f));
            foreach(float side in new[]{-1f,1f})Box("DamingWood",new Vector3(x+side*w*.485f,1.38f,.10f),new Vector3(.12f,2.20f,.17f));
            Box("DarkMetal",new Vector3(x,2.38f,.05f),new Vector3(w-.15f,.05f,.12f));
            // Short shutter ventilation slots visible close to its upper edge.
            for(int row=0;row<2;row++)for(int i=0;i<14;i++)Box("Shadow",new Vector3(x-w*.445f+i*.226f,2.21f-row*.095f,.046f),new Vector3(.12f,.028f,.009f));
        }
        static void Poster(float x,float y,float width,float height,int variant)
        {
            const float z=.102f;
            Box("Paper",new Vector3(x,y,z),new Vector3(width,height,.009f));
            Box(variant%3==0?"JyesYellow":variant%3==1?"PosterCyan":"PosterPink",new Vector3(x,y+height*.39f,z+.007f),new Vector3(width*.96f,height*.17f,.005f));
            float phoneW=Mathf.Min(.14f,width*.26f),phoneH=Mathf.Min(.23f,height*.47f);
            for(int i=0;i<(variant%3==1?1:2);i++)
            {
                float px=x+(i-.5f)*width*.39f;
                Box("PosterInk",new Vector3(px,y-.02f,z+.009f),new Vector3(phoneW,phoneH,.006f));
                Box("PaperGrey",new Vector3(px,y-.012f,z+.014f),new Vector3(phoneW*.84f,phoneH*.76f,.003f));
            }
            if(width>.42f&&height>.35f)Label(variant%2==0?"手機優惠":"門號專案",new Vector3(x,y+height*.385f,z+.017f),height*.090f,"#344C69",width*.89f);
            Box("PosterInk",new Vector3(x,y-height*.34f,z+.009f),new Vector3(width*.64f,.009f,.004f));
        }
        static void Jyes()
        {
            const float x=.02f,w=3.38f;
            Box("WarmInside",new Vector3(x,1.49f,-1.26f),new Vector3(w,2.73f,.12f));
            foreach(float side in new[]{-1f,1f})Box("JyesPale",new Vector3(x+side*w/2,1.45f,-.61f),new Vector3(.095f,2.66f,1.25f));
            Box("Display",new Vector3(-.76f,.68f,-.87f),new Vector3(1.30f,1.08f,.53f));
            Box("PosterInk",new Vector3(-.76f,1.24f,-.86f),new Vector3(1.35f,.07f,.57f));
            Box("JyesBlue",new Vector3(x,2.86f,.13f),new Vector3(w,.56f,.22f));
            Box("JyesYellow",new Vector3(x,2.515f,.152f),new Vector3(w,.13f,.22f));
            Label("傑昇通信",new Vector3(-.30f,2.88f,.257f),.41f,"#D9E0CD",2.53f);
            Label("挑戰手機市場最低價",new Vector3(x,2.514f,.275f),.075f,"#6B6346",2.15f);
            // Reconstructed emblem silhouette, independent geometry rather than copied logo pixels.
            var emblem=G("JyesPale");emblem.Quad(new Vector3(1.22f,3.075f,.255f),new Vector3(.89f,2.666f,.255f),new Vector3(1.55f,2.666f,.255f),new Vector3(1.22f,3.075f,.255f));
            Rod("JyesYellow",new Vector3(.94f,2.77f,.270f),new Vector3(1.49f,2.94f,.270f),.024f,8);
            foreach(float side in new[]{-1f,1f})Box("JyesPale",new Vector3(x+side*(w/2-.04f),1.32f,.03f),new Vector3(.08f,2.40f,.068f));
            foreach(float y in new[]{.16f,2.47f})Box("JyesPale",new Vector3(x,y,.03f),new Vector3(w,.08f,.068f));
            // Glazing is a thin plane with visible shop depth through unpapered areas.
            G("Glass").Quad(new Vector3(x-w/2+.08f,.19f,.071f),new Vector3(x+w/2-.08f,.19f,.071f),new Vector3(x+w/2-.08f,2.43f,.071f),new Vector3(x-w/2+.08f,2.43f,.071f));
            foreach(float px in new[]{-1.50f,-.47f,.50f,1.52f})Box("PaleMetal",new Vector3(px,1.33f,.11f),new Vector3(.035f,2.32f,.055f));
            Box("PaleMetal",new Vector3(x,1.13f,.11f),new Vector3(w-.10f,.034f,.06f));
            Poster(1.17f,2.06f,.57f,.54f,0);Poster(.44f,2.10f,.71f,.46f,1);Poster(-.45f,2.10f,.86f,.47f,2);Poster(-1.18f,2.05f,.46f,.56f,0);
            Poster(1.19f,1.47f,.55f,.43f,2);Poster(.55f,1.51f,.55f,.57f,0);Poster(-.16f,1.52f,.71f,.56f,1);Poster(-1.02f,1.48f,.80f,.53f,0);
            Poster(1.20f,.93f,.53f,.50f,1);Poster(.53f,.92f,.63f,.47f,0);Poster(-.19f,.91f,.61f,.45f,2);Poster(-.97f,.88f,.84f,.51f,0);
            Poster(.78f,.42f,1.07f,.27f,1);Poster(-.56f,.42f,1.19f,.27f,0);
            foreach(float px in new[]{-.39f,-.54f})Box("Metal",new Vector3(px,1.05f,.178f),new Vector3(.022f,.30f,.028f));
            foreach(float px in new[]{-1.76f,1.77f})
            {
                Box("JyesBlue",new Vector3(px,1.39f,.152f),new Vector3(.19f,2.35f,.13f));
                Label(px<0?"空\n機\n超\n低\n價":"原\n廠\n保\n固",new Vector3(px,1.43f,.225f),.123f,"#DAD8C3",.16f);
            }
            // The photograph's projecting sign is at Jyes' right edge, high on the building.
            const float sx=-1.63f,sz=.67f;
            Box("JyesEdge",new Vector3(sx,4.22f,sz),new Vector3(.18f,2.45f,.59f));
            foreach(float side in new[]{-1f,1f})
            {
                Box("JyesBlue",new Vector3(sx+side*.102f,4.22f,sz),new Vector3(.027f,2.39f,.55f));
                Label("傑\n昇\n通\n信",new Vector3(sx+side*.128f,4.17f,sz),.32f,"#E0DECA",.45f,side*90f);
                Box("JyesYellow",new Vector3(sx+side*.12f,3.085f,sz),new Vector3(.012f,.10f,.53f));
            }
            foreach(float y in new[]{3.43f,5.06f})Rod("Metal",new Vector3(sx,y,.02f),new Vector3(sx,y,sz+.09f),.025f);
        }
        static void Lantern(Vector3 p,float width=.26f,float height=.43f)
        {
            var g=G("Lantern");const int sides=16,rings=8;
            Vector3 P(float t,float a)=>p+new Vector3(Mathf.Sin(t)*Mathf.Cos(a)*width/2,Mathf.Cos(t)*height/2,Mathf.Sin(t)*Mathf.Sin(a)*width/2);
            for(int j=0;j<rings;j++)for(int i=0;i<sides;i++)
            {float t=j*Mathf.PI/rings,t1=(j+1)*Mathf.PI/rings,a=i*2*Mathf.PI/sides,a1=(i+1)*2*Mathf.PI/sides;g.Quad(P(t,a),P(t,a1),P(t1,a1),P(t1,a));}
            foreach(float y in new[]{-1f,1f})Rod("DarkMetal",p+Vector3.up*(y*height*.47f),p+Vector3.up*(y*height*.53f),width*.19f,10);
            Rod("DarkMetal",p+Vector3.up*height*.52f,p+Vector3.up*height*.81f,.010f,6);
            Label("麵",p+Vector3.forward*(width*.51f),.115f,"#443728",.16f);
        }
        static void MiaoGuanYin()
        {
            // 2025-05-03 original author photograph, exact capture day unknown.
            // https://g.udn.com.tw/upfiles/B_CO/coffeebar/PSN_PHOTO/259/f_28822259_1.jpg
            const float x=-3.61f,w=3.38f;
            M("MiaoGreen","#788D78",0,.16f);M("MiaoWood","#A98554",0,.20f);
            M("MiaoPale","#C4C7AD",0,.18f);M("MiaoBanner","#943C32",0,.14f);
            Box("WarmInside",new Vector3(x,1.25f,-1.45f),new Vector3(w,2.22f,.10f));
            Box("MiaoWood",new Vector3(x,1.2f,-1.15f),new Vector3(w-.20f,2.05f,.09f));
            foreach(float side in new[]{-1f,1f})Box("MiaoWood",new Vector3(x+side*1.57f,1.24f,.01f),new Vector3(.16f,2.20f,.16f));
            Box("MiaoGreen",new Vector3(x,2.81f,.14f),new Vector3(w,.85f,.20f));
            foreach(float side in new[]{-1f,1f}){
                Box("PaleMetal",new Vector3(x+side*w*.49f,2.81f,.25f),new Vector3(.032f,.88f,.034f));
                Box("PaleMetal",new Vector3(x,2.81f+side*.42f,.25f),new Vector3(w,.031f,.034f));
            }
            for(int c=0;c<3;c++){
                float cx=x+.52f-c*.52f;var g=G("MiaoPale");
                for(int i=0;i<36;i++){
                    float a=i*2*Mathf.PI/36,b=(i+1)*2*Mathf.PI/36;
                    Vector3 P(float t)=>new Vector3(cx+Mathf.Cos(t)*.225f,2.82f+Mathf.Sin(t)*.24f,.254f);
                    g.Quad(new Vector3(cx,2.82f,.254f),P(a),P(b),new Vector3(cx,2.82f,.254f));
                }
                Label("妙觀音"[c].ToString(),new Vector3(cx,2.82f,.261f),.265f,"#425448",.32f);
            }
            Box("MiaoBanner",new Vector3(x,2.23f,.12f),new Vector3(w-.05f,.27f,.035f));
            Label("妙觀音 素食",new Vector3(x,2.24f,.145f),.15f,"#E1D8BF",2.34f);
            Box("MiaoWood",new Vector3(x+.63f,.73f,-.37f),new Vector3(1.49f,1.07f,.68f));
            Box("MiaoPale",new Vector3(x+.63f,1.29f,-.36f),new Vector3(1.59f,.08f,.76f));
            for(int i=0;i<20;i++)Box("RamenWood",new Vector3(x-.08f+i*.075f,.77f,.002f),new Vector3(.022f,.94f,.022f));
            Box("Shadow",new Vector3(x-.78f,1.29f,-.08f),new Vector3(.74f,1.77f,.07f));
            Box("MiaoPale",new Vector3(x-.91f,1.20f,-.025f),new Vector3(.63f,1.48f,.024f));
            for(int i=0;i<4;i++){
                Box("Paper",new Vector3(x-.91f,1.76f-i*.35f,-.006f),new Vector3(.58f,.30f,.013f));
                Box("MiaoWood",new Vector3(x-.91f,1.78f-i*.35f,.005f),new Vector3(.47f,.055f,.006f));
            }
            Label("素食",new Vector3(x-.91f,1.80f,.014f),.10f,"#5B6047",.43f);
        }
        static void BakeLettering()
        {
            font.RequestCharactersInTexture(string.Concat(lettering.Select(l=>l.text)),64,FontStyle.Normal);var g=G("Lettering");
            foreach(var item in lettering)
            {
                var lines=item.text.Split('\n');const float lineHeight=73f;float bottom=float.PositiveInfinity,top=float.NegativeInfinity,longest=0;
                for(int line=0;line<lines.Length;line++)
                {
                    float advance=0;foreach(char c in lines[line])if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal)){bottom=Mathf.Min(bottom,ch.minY-line*lineHeight);top=Mathf.Max(top,ch.maxY-line*lineHeight);advance+=ch.advance;}
                    longest=Mathf.Max(longest,advance);
                }
                if(float.IsInfinity(bottom))continue;float scale=item.height/64f;if(item.width>0&&longest*scale>item.width)scale=item.width/longest;
                var rotation=Quaternion.Euler(0,180f+item.yaw,0);float middle=(bottom+top)/2;
                for(int line=0;line<lines.Length;line++)
                {
                    float width=0;foreach(char c in lines[line])if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))width+=ch.advance;float pen=-width/2;
                    foreach(char c in lines[line])
                    {
                        if(!font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))continue;
                        if(!char.IsWhiteSpace(c))
                        {
                            Vector3 P(float px,float py)=>item.position+rotation*new Vector3(px*scale,(py-line*lineHeight-middle)*scale,0);
                            int n=g.vertices.Count;g.vertices.Add(P(pen+ch.minX,ch.minY));g.vertices.Add(P(pen+ch.minX,ch.maxY));g.vertices.Add(P(pen+ch.maxX,ch.maxY));g.vertices.Add(P(pen+ch.maxX,ch.minY));
                            // StreetLabelDepth outputs vertex RGB directly, so sRGB swatches
                            // need conversion in a Linear project to avoid grey/washed-out ink.
                            Color ink=QualitySettings.activeColorSpace==ColorSpace.Linear?item.color.linear:item.color;
                            g.uv.AddRange(new[]{ch.uvBottomLeft,ch.uvTopLeft,ch.uvTopRight,ch.uvBottomRight});for(int i=0;i<4;i++)g.colors.Add(ink);g.indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                        }
                        pen+=ch.advance;
                    }
                }
            }
            var atlas=font.material.mainTexture;if(!atlas)throw new InvalidOperationException("Noto font atlas is missing.");
            var rt=RenderTexture.GetTemporary(atlas.width,atlas.height,0,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;var copy=new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,false);
            string path=Art+"/SiteLetteringAtlas.png";
            try{Graphics.Blit(atlas,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);copy.Apply();File.WriteAllBytes(path,copy.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);ti.alphaIsTransparency=true;ti.sRGBTexture=false;ti.mipmapEnabled=true;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("Install the existing StreetLabelDepth.shader before building this facade.");
            var mat=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,Art+"/Lettering.mat");}
            mat.name="Lettering";mat.shader=shader;mat.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));mat.SetFloat("_Cutoff",.23f);materials["Lettering"]=mat;EditorUtility.SetDirty(mat);
        }
        static void SaveMeshes(Transform root)
        {
            var fineSurfaces=new HashSet<string>{"Lettering","Glass","Metal","PaleMetal","DarkMetal","Shutter","ShutterRib","DamingGrain","Joint","WallRepair","Paper","PaperGrey","PosterInk","PosterCyan","PosterPink","JyesYellow","Window","WindowLight","Curtain","LanternRib"};
            foreach(var pair in geometry)
            {
                var g=pair.Value;string path=Art+"/SiteMatch_"+pair.Key+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool exists=mesh;
                if(exists)mesh.Clear();else mesh=new Mesh();mesh.name="SiteMatch_"+pair.Key;mesh.indexFormat=IndexFormat.UInt32;
                mesh.SetVertices(g.vertices);mesh.SetUVs(0,g.uv);if(g.colors.Count==g.vertices.Count)mesh.SetColors(g.colors);mesh.SetTriangles(g.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();mesh.UploadMeshData(false);
                if(exists)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("Site facade batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                bool fine=fineSurfaces.Contains(pair.Key);var mat=materials[pair.Key];
                // URP controls shadow receiving on the material; renderer.receiveShadows alone
                // is insufficient. Thin coplanar trim should not feed or receive noisy shadow maps.
                if(mat.HasProperty("_ReceiveShadows")){mat.SetFloat("_ReceiveShadows",fine?0f:1f);if(fine)mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");else mat.DisableKeyword("_RECEIVE_SHADOWS_OFF");EditorUtility.SetDirty(mat);}
                var mr=go.AddComponent<MeshRenderer>();mr.sharedMaterial=mat;mr.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;mr.receiveShadows=!fine;
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
        /// <summary>Builds a separate, collider-free asset in the active scene. Does not save the scene.</summary>
        public static GameObject Build(Transform parent,Vector3 origin,Quaternion rotation)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Build the facade outside Play mode.");
            font=AssetDatabase.LoadAssetAtPath<Font>(FontPath);if(!font)throw new InvalidOperationException("Existing NotoSansTC font was not found.");
            if(!Shader.Find("TaiwanRider/StreetLabelDepth"))throw new InvalidOperationException("Existing StreetLabelDepth shader was not found.");
            Directory.CreateDirectory(Art);AssetDatabase.Refresh();materials.Clear();geometry.Clear();lettering.Clear();
            var old=parent?parent.Cast<Transform>().FirstOrDefault(t=>t.name==RootName||t.name=="Site facade study • Tingzhou 156–158-1 • 2025 photo")?.gameObject:SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==RootName||g.name=="Site facade study • Tingzhou 156–158-1 • 2025 photo");
            if(old)Undo.DestroyObjectImmediate(old);var result=new GameObject(RootName);result.layer=2;result.transform.SetParent(parent,false);result.transform.SetPositionAndRotation(origin,rotation);
            Undo.RegisterCreatedObjectUndo(result,"Build photographed Tingzhou shop frontage");Materials();SharedGround();UpperCrop();Daming();Jyes();MiaoGuanYin();BakeLettering();SaveMeshes(result.transform);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(result.scene);
            Debug.Log("Built photographed Tingzhou 156–158-1 frontage: 2025 source image, estimated dimensions, upper crop only, no colliders.");return result;
        }
    }
}
