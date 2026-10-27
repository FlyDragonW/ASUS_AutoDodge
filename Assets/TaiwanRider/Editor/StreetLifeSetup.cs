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
    /// <summary>Original, reference-informed street dressing. No traffic, collision or runtime changes.</summary>
    public static class StreetLifeSetup
    {
        public const string RootName="Gongguan street life • visual study";
        const string Assets="Assets/TaiwanRider";
        const string Art=Assets+"/Art/StreetLife";
        [Serializable] sealed class Building { public string name;public int side;public float center,width,height; }
        [Serializable] sealed class Layout { public Building[] items; }
        static readonly Dictionary<string,Material> mats=new Dictionary<string,Material>();
        static readonly Dictionary<string,Batch> batches=new Dictionary<string,Batch>();
        sealed class LabelSpec { public string text;public Vector3 position;public float size;public Color color;public Matrix4x4 frame;public Quaternion rotation; }
        static readonly List<LabelSpec> labels=new List<LabelSpec>();
        static Matrix4x4 frame;
        static Transform root;
        static Font font;
        sealed class Batch
        {
            public List<Vector3> vertices=new List<Vector3>();public List<Vector2> uv=new List<Vector2>();public List<int> indices=new List<int>();public List<Color32> colors=new List<Color32>();
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,bool stamp=false)
            {
                int n=vertices.Count;foreach(var p in new[]{a,b,c,d})vertices.Add(frame.MultiplyPoint3x4(p));
                Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                Vector2 UV(Vector3 p)=>Mathf.Abs(normal.y)>.7f?new Vector2(p.x,p.z)/2:Mathf.Abs(normal.x)>.7f?new Vector2(p.z,p.y)/2:new Vector2(p.x,p.y)/2;
                if(stamp)uv.AddRange(new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)});
                else foreach(var p in new[]{a,b,c,d})uv.Add(UV(p));
                indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
        }
        static Batch Mesh(string key){if(!batches.TryGetValue(key,out var b))batches[key]=b=new Batch();return b;}
        static Color Hex(string value){ColorUtility.TryParseHtmlString(value,out var c);return c;}
        static Material Mat(string key,string color,float metallic=0,float smooth=.24f)
        {
            string path=Art+"/"+key+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.name="StreetLife_"+key;m.SetColor("_BaseColor",Hex(color));m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smooth);
            m.enableInstancing=true;mats[key]=m;EditorUtility.SetDirty(m);return m;
        }
        static void Transparent(Material m)
        {
            m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.SetOverrideTag("RenderType","Transparent");m.renderQueue=(int)RenderQueue.Transparent;
        }
        static void SaveTexture(string name,Texture2D texture,bool normal=false)
        {
            string path=Art+"/"+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
            importer.sRGBTexture=!normal && !name.EndsWith("_mask",StringComparison.Ordinal);importer.mipmapEnabled=true;importer.anisoLevel=8;
            importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.wrapMode=TextureWrapMode.Repeat;importer.maxTextureSize=512;importer.SaveAndReimport();
        }
        // Eight by sixteen ceramic tiles in one 1.2 m repeat: 150 x 75 mm tiles and 3 mm grout.
        // Variation follows each physical tile, with restrained micrograin rather than arbitrary dirt boxes.
        static void Ceramic(string name,string tint,bool floor=false)
        {
            const int n=512;var color=new Texture2D(n,n,TextureFormat.RGBA32,false);var normal=new Texture2D(n,n,TextureFormat.RGBA32,false);var mask=new Texture2D(n,n,TextureFormat.RGBA32,false);
            Color baseColor=Hex(tint),grout=Color.Lerp(baseColor,Hex("#555950"),.36f);var pixels=new Color32[n*n];var norms=new Color32[n*n];var masks=new Color32[n*n];
            int tileX=floor?128:64,tileY=floor?128:32;
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                int rx=x%tileX,ry=y%tileY,cx=x/tileX,cy=y/tileY;
                float edge=Mathf.Min(rx,tileX-1-rx,ry,tileY-1-ry),joint=Mathf.SmoothStep(0,1,Mathf.Clamp01((edge-.2f)/1.6f));
                float cell=(Mathf.Sin(cx*91.7f+cy*173.3f)*43758.5453f)%1;cell=Mathf.Abs(cell)-.5f;
                float grain=(Mathf.PerlinNoise(x*.39f+8,y*.39f+13)-.5f)*.027f;
                Color c=Color.Lerp(grout,baseColor*(1+cell*.085f+grain),joint);c.a=1;pixels[y*n+x]=c;
                float dx=rx<3?-.16f:rx>tileX-4?.16f:0,dy=ry<3?-.16f:ry>tileY-4?.16f:0;
                Vector3 v=new Vector3(dx,dy,1).normalized;norms[y*n+x]=new Color(v.x*.5f+.5f,v.y*.5f+.5f,v.z*.5f+.5f,1);
                masks[y*n+x]=new Color(0,0,0,Mathf.Lerp(.08f,floor?.24f:.38f,joint)+grain);
            }
            color.SetPixels32(pixels);color.Apply();normal.SetPixels32(norms);normal.Apply();mask.SetPixels32(masks);mask.Apply();
            SaveTexture(name+"_albedo",color);SaveTexture(name+"_normal",normal,true);SaveTexture(name+"_mask",mask);
            var m=Mat(name,"#FFFFFF",0,1);m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/"+name+"_albedo.png"));
            m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/"+name+"_normal.png"));m.SetFloat("_BumpScale",.45f);m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/"+name+"_mask.png"));m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTextureScale("_BaseMap",Vector2.one*(2f/1.2f));EditorUtility.SetDirty(m);
        }
        static void StainTexture()
        {
            const int n=256;var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);var pixels=new Color32[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float u=(x+.5f)/n,v=(y+.5f)/n,edge=Mathf.SmoothStep(0,1,Mathf.Min(u,1-u)*10);
                float runs=Mathf.Pow(Mathf.PerlinNoise(u*12+23,v*1.4f+3),3)*1.45f;
                float top=Mathf.SmoothStep(0,1,v)*Mathf.SmoothStep(0,1,(1-v)*14),a=edge*top*runs*.28f;
                pixels[y*n+x]=new Color(.24f,.27f,.21f,a);
            }
            tex.SetPixels32(pixels);tex.Apply();SaveTexture("DrainPatina",tex);
            var m=Mat("Damp","#FFFFFF");m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/DrainPatina.png"));Transparent(m);
        }
        static void Materials()
        {
            Ceramic("MosaicSand","#ACA18A");Ceramic("MosaicSage","#929D90");Ceramic("MosaicRose","#A18E7F");Ceramic("ArcadeTile","#888879",true);
            Mat("Plaster","#A7A398");Mat("Ceiling","#9C9D91");Mat("DarkInterior","#4E5850");Mat("OchreInterior","#8D8065");
            Mat("Steel","#7D8981",.65f,.38f);Mat("BlackSteel","#454F49",.4f,.20f);Mat("Shutter","#7C887E",.25f,.25f);
            Mat("Timber","#8A6442",0,.22f);Mat("Plywood","#A58E66",0,.2f);Mat("Cream","#C6C3A9",0,.20f);
            Mat("RicePaper","#D2CEB5",0,.08f);Mat("RedSign","#995E49",0,.27f);Mat("GreenSign","#385D4D",0,.29f);Mat("BlueSign","#506B75",0,.28f);
            Mat("CrateBlue","#557A87",0,.26f);Mat("CrateRed","#935849",0,.23f);Mat("Bottle","#647762",.08f,.55f);Mat("Cap","#C1BDB0",.1f,.4f);
            Mat("Bread","#A97941",0,.10f);Mat("CeramicCup","#C7C2AE",0,.5f);Mat("Terracotta","#906C53",0,.15f);Mat("Soil","#545548",0,.05f);
            Mat("Leaves","#687B53",0,.13f);Mat("LeavesLight","#859161",0,.17f);Mat("Rubber","#424D47",0,.1f);
            var glow=Mat("WarmLight","#DCD3B8",0,.35f);glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",Hex("#CAB68A")*.34f);
            var glass=Mat("ClearGlass","#A8C6BC",.15f,.65f);var tint=glass.GetColor("_BaseColor");tint.a=.13f;glass.SetColor("_BaseColor",tint);Transparent(glass);
            StainTexture();
        }
        static void Box(string key,Vector3 p,Vector3 size,Quaternion rotation=default)
        {
            if(rotation.x==0f&&rotation.y==0f&&rotation.z==0f&&rotation.w==0f)rotation=Quaternion.identity;var b=Mesh(key);Vector3 s=size*.5f;
            Vector3 P(float x,float y,float z)=>p+rotation*new Vector3(x*s.x,y*s.y,z*s.z);
            b.Quad(P(-1,-1,-1),P(-1,1,-1),P(1,1,-1),P(1,-1,-1));b.Quad(P(-1,-1,1),P(1,-1,1),P(1,1,1),P(-1,1,1));
            b.Quad(P(-1,-1,-1),P(1,-1,-1),P(1,-1,1),P(-1,-1,1));b.Quad(P(-1,1,-1),P(-1,1,1),P(1,1,1),P(1,1,-1));
            b.Quad(P(-1,-1,-1),P(-1,-1,1),P(-1,1,1),P(-1,1,-1));b.Quad(P(1,-1,-1),P(1,1,-1),P(1,1,1),P(1,-1,1));
        }
        static void Rod(string key,Vector3 a,Vector3 b,float radius,int sides=12,float endRadius=-1)
        {
            var batch=Mesh(key);Quaternion q=Quaternion.FromToRotation(Vector3.up,(b-a).normalized);if(endRadius<0)endRadius=radius;
            for(int i=0;i<sides;i++)
            {
                float t=i*Mathf.PI*2/sides,t1=(i+1)*Mathf.PI*2/sides;Vector3 d=q*new Vector3(Mathf.Cos(t),0,Mathf.Sin(t)),d1=q*new Vector3(Mathf.Cos(t1),0,Mathf.Sin(t1));
                batch.Quad(a+d*radius,b+d*endRadius,b+d1*endRadius,a+d1*radius);
                batch.Quad(b,b+d1*endRadius,b+d*endRadius,b);batch.Quad(a,a+d*radius,a+d1*radius,a);
            }
        }
        static void Ellipsoid(string key,Vector3 center,Vector3 scale,Quaternion rotation=default)
        {
            if(rotation.x==0f&&rotation.y==0f&&rotation.z==0f&&rotation.w==0f)rotation=Quaternion.identity;var b=Mesh(key);
            Vector3 P(float t,float a)=>center+rotation*Vector3.Scale(new Vector3(Mathf.Sin(t)*Mathf.Cos(a),Mathf.Cos(t),Mathf.Sin(t)*Mathf.Sin(a)),scale);
            for(int j=0;j<5;j++)for(int i=0;i<10;i++)
            {float t=j*Mathf.PI/5,t1=(j+1)*Mathf.PI/5,a=i*Mathf.PI*.2f,a1=(i+1)*Mathf.PI*.2f;b.Quad(P(t,a),P(t,a1),P(t1,a1),P(t1,a));}
        }
        static void Stamp(string material,Vector3 center,float width,float height)
        {Vector3 x=Vector3.right*width/2,y=Vector3.up*height/2;Mesh(material).Quad(center-x-y,center+x-y,center+x+y,center-x+y,true);}
        static void Label(string text,Vector3 p,float size,Color color,Quaternion rotation=default)
        {
            // Unity Quaternion.operator== compares the dot product, so default != default.
            // Detect the optional all-zero value explicitly before composing the facing rotation.
            if(rotation.x==0f&&rotation.y==0f&&rotation.z==0f&&rotation.w==0f)rotation=Quaternion.identity;
            labels.Add(new LabelSpec{text=text,position=p,size=size,color=color,frame=frame,rotation=rotation*Quaternion.Euler(0,180,0)});
        }
        static void BakeLettering()
        {
            // TextMesh's default GUI font material can render through walls and show both faces.
            // Bake only these original signs into immutable geometry/atlas, with depth-tested faces.
            string characters=string.Concat(labels.Select(l=>l.text));font.RequestCharactersInTexture(characters,64,FontStyle.Normal);
            var batch=Mesh("Lettering");
            foreach(var label in labels)
            {
                string[] lines=label.text.Split('\n');float lineHeight=70f,minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
                for(int line=0;line<lines.Length;line++)foreach(char c in lines[line])
                    if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal)){minY=Mathf.Min(minY,ch.minY-line*lineHeight);maxY=Mathf.Max(maxY,ch.maxY-line*lineHeight);}
                if(float.IsInfinity(minY))continue;float centerY=(minY+maxY)/2;
                for(int line=0;line<lines.Length;line++)
                {
                    float width=0;foreach(char c in lines[line])if(font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))width+=ch.advance;
                    float pen=-width/2;
                    foreach(char c in lines[line])
                    {
                        if(!font.GetCharacterInfo(c,out var ch,64,FontStyle.Normal))continue;
                        if(!char.IsWhiteSpace(c))
                        {
                            float y=-line*lineHeight-centerY,scale=label.size*.1f;
                            Vector3 P(float x,float dy)=>label.frame.MultiplyPoint3x4(label.position+label.rotation*new Vector3(x*scale,dy*scale,0));
                            int n=batch.vertices.Count;
                            batch.vertices.Add(P(pen+ch.minX,y+ch.minY));batch.vertices.Add(P(pen+ch.minX,y+ch.maxY));
                            batch.vertices.Add(P(pen+ch.maxX,y+ch.maxY));batch.vertices.Add(P(pen+ch.maxX,y+ch.minY));
                            batch.uv.AddRange(new[]{ch.uvBottomLeft,ch.uvTopLeft,ch.uvTopRight,ch.uvBottomRight});
                            for(int j=0;j<4;j++)batch.colors.Add(label.color);
                            batch.indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
                        }
                        pen+=ch.advance;
                    }
                }
            }
            Texture atlas=font.material.mainTexture;if(!atlas)throw new InvalidOperationException("Font atlas is missing.");
            var target=RenderTexture.GetTemporary(atlas.width,atlas.height,0,RenderTextureFormat.ARGB32);var active=RenderTexture.active;
            var baked=new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,false);
            try{Graphics.Blit(atlas,target);RenderTexture.active=target;baked.ReadPixels(new Rect(0,0,atlas.width,atlas.height),0,0);baked.Apply();File.WriteAllBytes(Art+"/StreetLetteringAtlas.png",baked.EncodeToPNG());}
            finally{RenderTexture.active=active;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(baked);}
            string path=Art+"/StreetLetteringAtlas.png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=4096;importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();
            var shader=Shader.Find("TaiwanRider/StreetLabelDepth");if(!shader)throw new InvalidOperationException("StreetLabelDepth shader is missing.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Lettering.mat");
            if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,Art+"/Lettering.mat");}
            material.name="StreetLife_Lettering";material.shader=shader;material.SetTexture("_MainTex",AssetDatabase.LoadAssetAtPath<Texture2D>(path));material.SetFloat("_Cutoff",.23f);mats["Lettering"]=material;EditorUtility.SetDirty(material);
        }
        static void Crate(Vector3 p,bool red,bool bottles=true)
        {
            string key=red?"CrateRed":"CrateBlue";Box(key,p+new Vector3(0,.025f,0),new Vector3(.47f,.05f,.34f));
            foreach(float x in new[]{-.22f,.22f})foreach(float z in new[]{-.15f,.15f})Box(key,p+new Vector3(x,.13f,z),new Vector3(.033f,.23f,.033f));
            for(int j=0;j<3;j++)
            {
                foreach(float z in new[]{-.15f,.15f})Box(key,p+new Vector3(0,.065f+j*.065f,z),new Vector3(.45f,.03f,.025f));
                foreach(float x in new[]{-.22f,.22f})Box(key,p+new Vector3(x,.065f+j*.065f,0),new Vector3(.025f,.03f,.32f));
            }
            if(!bottles)return;
            for(int i=0;i<4;i++)for(int j=0;j<2;j++)
            {var b=p+new Vector3(-.16f+i*.105f,.05f,-.07f+j*.14f);Rod("Bottle",b,b+Vector3.up*.18f,.036f,8);Rod("Bottle",b+Vector3.up*.18f,b+Vector3.up*.24f,.028f,8,.015f);Rod("Cap",b+Vector3.up*.24f,b+Vector3.up*.26f,.018f,8);}
        }
        static void Pot(Vector3 p,float scale=1)
        {
            Rod("Terracotta",p,p+Vector3.up*.34f*scale,.15f*scale,14,.21f*scale);Rod("Soil",p+Vector3.up*.33f*scale,p+Vector3.up*.34f*scale,.184f*scale,14);
            for(int i=0;i<7;i++)
            {
                float a=i*Mathf.PI*2/7;Vector3 tip=p+new Vector3(Mathf.Cos(a)*.20f,.55f+(i%3)*.055f,Mathf.Sin(a)*.20f)*scale;
                Rod("Leaves",p+Vector3.up*.32f*scale,tip,.008f*scale,6);
                Ellipsoid(i%2==0?"Leaves":"LeavesLight",tip,new Vector3(.075f,.16f,.025f)*scale,Quaternion.Euler(25,a*Mathf.Rad2Deg,30));
            }
        }
        static void Stool(Vector3 p)
        {
            Rod("CrateRed",p+Vector3.up*.39f,p+Vector3.up*.43f,.17f,14);
            foreach(float x in new[]{-.1f,.1f})foreach(float z in new[]{-.1f,.1f})Rod("BlackSteel",p+new Vector3(x*1.25f,0,z*1.25f),p+new Vector3(x,.39f,z),.013f,6);
        }
        static void Menu(string text,Vector3 p,string plate="GreenSign")
        {
            Box("Timber",p,new Vector3(.63f,.92f,.05f));Box(plate,p+Vector3.forward*.031f,new Vector3(.56f,.84f,.02f));
            foreach(float x in new[]{-.27f,.27f})Rod("Timber",p+new Vector3(x,-.75f,-.2f),p+new Vector3(x,.44f,0),.018f,6);
            Label(text,p+Vector3.forward*.05f,.0124f,Hex("#DAD6BD"));
        }
        static void Counter(Vector3 p,float width,bool food)
        {
            Box(food?"Steel":"Timber",p+new Vector3(0,.42f,0),new Vector3(width,.80f,.58f));
            Box("Steel",p+new Vector3(0,.85f,0),new Vector3(width+.07f,.055f,.64f));
            Box(food?"Cream":"GreenSign",p+new Vector3(0,.50f,.303f),new Vector3(width-.1f,.50f,.018f));
            foreach(float x in new[]{-width*.44f,width*.44f})Rod("Steel",p+new Vector3(x,.87f,-.15f),p+new Vector3(x,1.37f,-.15f),.018f,8);
            Box("ClearGlass",p+new Vector3(0,1.15f,.08f),new Vector3(width-.1f,.46f,.012f));
            Box("Steel",p+new Vector3(0,1.40f,-.10f),new Vector3(width+.06f,.035f,.53f));
            for(int i=0;i<3;i++)
            {
                Vector3 v=p+new Vector3((i-1)*width*.27f,.90f,-.02f);Box("Steel",v,new Vector3(width*.23f,.045f,.34f));
                if(food)for(int j=0;j<3;j++)Ellipsoid("Bread",v+new Vector3(-width*.065f+j*width*.065f,.065f,0),new Vector3(.066f,.045f,.092f));
                else {Rod("CeramicCup",v,v+Vector3.up*.18f,.07f,12);Rod("Cap",v+Vector3.up*.18f,v+Vector3.up*.20f,.073f,12);}
            }
        }
        static void Shop(Building b,int index,RoadRoute route)
        {
            Quaternion facing=Quaternion.LookRotation(Vector3.left*b.side,Vector3.up);
            frame=Matrix4x4.TRS(route.Position(b.center),route.Heading(b.center),Vector3.one)*Matrix4x4.TRS(new Vector3(b.side*10.53f,0,0),facing,Vector3.one);
            float w=b.width-.90f;int kind=index%4;
            string[] names={"日常食堂","生活電器","街角茶舖","文具・影印","老街麵館","機車維修","日常小舖","巷口茶行","生活雜貨"};
            string[] sub={"手作早餐  家常便當","家電保養・到府服務","原葉茶  現點現泡","文具用品・黑白彩印","湯麵  小菜  外帶","保養  換油  輪胎","米食・飲品・生活用品","紅茶  綠茶  冷泡茶","日常五金  生活雜貨"};
            string sign=index==0||index==4?"RedSign":index==1||index==3?"BlueSign":"GreenSign";
            // These 0.8 m deep displays sit in front of the existing recessed shop-wall collider.
            Box(index%3==0?"OchreInterior":"DarkInterior",new Vector3(0,1.43f,0),new Vector3(w,2.60f,.038f));
            foreach(float x in new[]{-w/2,w/2})Box("Plaster",new Vector3(x,1.48f,.46f),new Vector3(.12f,2.62f,.98f));
            Box("Ceiling",new Vector3(0,2.73f,.45f),new Vector3(w,.12f,1.02f));
            Box("WarmLight",new Vector3(-.4f,2.64f,.40f),new Vector3(1.25f,.035f,.16f));
            // A partly rolled-up steel shutter leaves the working counter and doorway open.
            Rod("Shutter",new Vector3(-w/2,2.49f,1.02f),new Vector3(w/2,2.49f,1.02f),.15f,16);
            for(int j=0;j<3;j++)Box("Shutter",new Vector3(0,2.29f-j*.055f,1.07f),new Vector3(w-.16f,.043f,.025f));
            for(int j=0;j<3;j++)
            {
                float y=.61f+j*.52f;Box("Plywood",new Vector3(-w*.23f,y,.24f),new Vector3(w*.42f,.055f,.43f));
                for(int k=0;k<4;k++)
                {
                    float x=-w*.40f+k*w*.115f;
                    if(index==1){Box("Cream",new Vector3(x,y+.17f,.13f),new Vector3(.28f,.30f,.24f));Box("DarkInterior",new Vector3(x,y+.19f,.27f),new Vector3(.21f,.18f,.025f));}
                    else {Rod(k%2==0?"Bottle":"CeramicCup",new Vector3(x,y+.028f,.23f),new Vector3(x,y+.26f,.23f),.067f,10);Rod("Cap",new Vector3(x,y+.26f,.23f),new Vector3(x,y+.28f,.23f),.071f,10);}
                }
            }
            if(index==0||index==2||index==4||index==7)Counter(new Vector3(-w*.22f,.18f,1.11f),Mathf.Min(2.3f,w*.49f),index==0||index==4);
            else if(index==6||index==8)
            {
                Counter(new Vector3(-w*.22f,.18f,1.02f),Mathf.Min(2.0f,w*.42f),false);
                Crate(new Vector3(-w*.34f,.20f,1.72f),false);Crate(new Vector3(-w*.34f,.45f,1.72f),true);
                Box("Plywood",new Vector3(-w*.1f,.40f,1.75f),new Vector3(.42f,.40f,.38f));
            }
            else if(index==5)
            {
                Box("Steel",new Vector3(-w*.20f,.94f,.87f),new Vector3(2.2f,.08f,.65f));
                foreach(float x in new[]{-w*.20f-.85f,-w*.20f+.85f})Box("BlackSteel",new Vector3(x,.55f,.87f),new Vector3(.045f,.75f,.48f));
                for(int j=0;j<6;j++)Box("CrateRed",new Vector3(-w*.34f+j*.23f,1.14f,.80f),new Vector3(.14f,.30f,.13f));
                Box("BlueSign",new Vector3(-w*.19f,1.86f,.07f),new Vector3(2.2f,.57f,.025f));Label("保養・機油・輪胎",new Vector3(-w*.19f,1.87f,.10f),.021f,Hex("#DBD9C3"));
            }
            else Box("Timber",new Vector3(-w*.24f,.65f,.95f),new Vector3(w*.46f,.94f,.55f));
            // A narrow glazed door with a visible shadowed opening adds a second depth plane.
            float door=w*.30f;
            foreach(float x in new[]{door-.48f,door+.48f})Box("Steel",new Vector3(x,1.34f,1.08f),new Vector3(.05f,2.27f,.075f));
            Box("Steel",new Vector3(door,2.48f,1.08f),new Vector3(1.0f,.05f,.075f));
            Box("ClearGlass",new Vector3(door+.12f,1.37f,.83f),new Vector3(.68f,2.1f,.018f),Quaternion.Euler(0,-18,0));
            Box("Steel",new Vector3(door-.06f,1.24f,.94f),new Vector3(.025f,.32f,.04f));
            Label("營業中",new Vector3(door,2.10f,1.145f),.014f,Hex("#D4CFB4"));
            // Replace the former tiny fascia text with original, properly sized Traditional Chinese.
            float fascia=Mathf.Min(w-.1f,4.5f);Box(sign,new Vector3(0,3.34f,3.16f),new Vector3(fascia,.50f,.035f));
            Label(names[index],new Vector3(0,3.41f,3.19f),.057f,Hex("#DED7BC"));
            Label(sub[index],new Vector3(0,3.19f,3.19f),.012f,Hex("#D1CAB5"));
            if(index==0||index==2||index==5||index==6||index==7)
            {
                float x=-w*.40f;string vertical=index==0?"早\n餐\n便\n當":index==5?"機\n車\n保\n養":index==6?"生\n活\n小\n舖":"現\n泡\n好\n茶";
                // The blade sign faces riders approaching along the road, not the shop wall.
                const float bladeDepth=3.60f;Quaternion blade=Quaternion.Euler(0,90,0);
                Box("BlackSteel",new Vector3(x,3.96f,bladeDepth),new Vector3(.10f,1.48f,.58f));
                Box(sign,new Vector3(x-.061f,3.96f,bladeDepth),new Vector3(.025f,1.42f,.52f));
                Label(vertical,new Vector3(x-.09f,3.96f,bladeDepth),.042f,Hex("#DAD4B8"),Quaternion.Euler(0,-90,0));
                Box(sign,new Vector3(x+.061f,3.96f,bladeDepth),new Vector3(.025f,1.42f,.52f));
                Label(vertical,new Vector3(x+.09f,3.96f,bladeDepth),.042f,Hex("#DAD4B8"),blade);
                Rod("BlackSteel",new Vector3(x,4.56f,2.83f),new Vector3(x,4.56f,bladeDepth+.15f),.022f,8);
            }
            string menu=index==0?"今日早餐\n蛋餅　35\n飯糰　40\n豆漿　25":index==2||index==7?"現泡茶飲\n紅茶　30\n奶茶　55\n冷泡　45":index==5?"定期保養\n機油更換\n輪胎檢查":"生活用品\n米食飲品\n歡迎選購";
            Menu(menu,new Vector3(w*.41f,1.07f,1.92f));
            if(index==0||index==4){Stool(new Vector3(-.75f,.18f,2.1f));Stool(new Vector3(.02f,.18f,2.05f));}
            if(index%2==0)Pot(new Vector3(-w*.43f,.18f,2.15f),index==6?.78f:1);
            if(index==2||index==7)Crate(new Vector3(-w*.42f,.20f,1.44f),false);
            // Wall flyers and a small enamel unit number use plausible dimensions, without real addresses.
            Box("RicePaper",new Vector3(w*.43f,1.72f,.98f),new Vector3(.24f,.36f,.014f));Label("外帶\n歡迎",new Vector3(w*.43f,1.72f,1.00f),.014f,Hex("#534B40"));
            // Patina is local to the downpipe and sill, with feathered alpha rather than rectangular dirt.
            Matrix4x4 shopFrame=frame;
            frame=Matrix4x4.TRS(route.Position(b.center),route.Heading(b.center),Vector3.one)*Matrix4x4.TRS(new Vector3(b.side*7.50f,0,0),facing,Vector3.one);
            // Attach ground-level patina to an actual column face, never to the open arcade span.
            if(index%3!=1)Stamp("Damp",new Vector3(-b.side*(b.width/2-.20f),.85f,.008f),.36f,1.25f);
            frame=Matrix4x4.TRS(route.Position(b.center),route.Heading(b.center),Vector3.one)*Matrix4x4.TRS(new Vector3(b.side*7.635f,0,0),facing,Vector3.one);
            Stamp("Damp",new Vector3(w*.21f,3.92f,.008f),.95f,.87f);
            frame=shopFrame;
        }

        static void ReplaceFacadeMaterials(Transform detail)
        {
            var map=new Dictionary<string,string>{{"IvoryTile","MosaicSand"},{"GreyTile","MosaicSage"},{"FadedGreen","MosaicSage"},{"WarmPlaster","MosaicRose"},{"ArcadeConcrete","Ceiling"},{"ShopWall","Plaster"},{"StoneTrim","ArcadeTile"},{"ColumnBase","MosaicSage"},{"PavingRepair","ArcadeTile"}};
            foreach(var renderer in detail.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(renderer.name.StartsWith("Fictional shop name •",StringComparison.Ordinal)){Undo.RecordObject(renderer.gameObject,"Replace fascia lettering");renderer.gameObject.SetActive(false);continue;}
                const string prefix="Buildings_and_pavement_";if(!renderer.name.StartsWith(prefix,StringComparison.Ordinal))continue;
                string key=renderer.name.Substring(prefix.Length);if(!map.TryGetValue(key,out string replacement))continue;
                Undo.RecordObject(renderer,"Use metric ceramic finish");renderer.sharedMaterial=mats[replacement];
            }
        }
        static void TransitionBuildings(Building[] all)
        {
            var buildings=SceneManager.GetActiveScene().GetRootGameObjects().First(g=>g.name.StartsWith("GongguanBuildings",StringComparison.Ordinal));
            var wallNames=new HashSet<string>{"Concrete","FacadeTile","OldCream","OldGreen","OldRose"};
            foreach(var b in all.Where(v=>Mathf.Abs(v.center)>=22 && Mathf.Abs(v.center)<44))
            {
                var t=buildings.GetComponentsInChildren<Transform>(true).FirstOrDefault(v=>v.name==b.name);if(!t)continue;
                foreach(var r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material[] current=r.sharedMaterials;bool changed=false;
                    for(int i=0;i<current.Length;i++)
                    {
                        if(!current[i])continue;string source=current[i].name.Replace("StreetLife_Transition_","");if(!wallNames.Contains(source))continue;
                        string key="Transition_"+source;if(!mats.TryGetValue(key,out var material))
                        {
                            material=Mat(key,source=="OldRose"?"#A89B8A":source=="OldGreen"?"#919B8D":"#A3A498");
                            var diffuse=AssetDatabase.LoadAssetAtPath<Texture2D>(Assets+"/Art/Textures/concrete_wall_003/diff.png");
                            material.SetTexture("_DetailAlbedoMap",diffuse);material.SetFloat("_DetailAlbedoMapScale",.16f);material.SetFloat("_DetailNormalMapScale",0);material.EnableKeyword("_DETAIL_SCALED");EditorUtility.SetDirty(material);
                        }
                        current[i]=material;changed=true;
                    }
                    if(changed){Undo.RecordObject(r,"Blend adjacent facade tones");r.sharedMaterials=current;}
                }
            }
        }
        static void SaveBatches()
        {
            foreach(var pair in batches)
            {
                var b=pair.Value;string name="StreetLife_"+pair.Key,path=Art+"/"+name+".asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);bool existing=mesh;
                // Mesh setters notify native render buffers; CopySerialized alone can leave a
                // previously rendered asset showing old vertices despite correct serialized data.
                if(existing)mesh.Clear();else mesh=new Mesh();
                mesh.name=name;mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(b.vertices);mesh.SetUVs(0,b.uv);if(b.colors.Count==b.vertices.Count)mesh.SetColors(b.colors);mesh.SetTriangles(b.indices,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
                mesh.UploadMeshData(false);if(existing)EditorUtility.SetDirty(mesh);else AssetDatabase.CreateAsset(mesh,path);
                var go=new GameObject("StreetLife batch • "+pair.Key);go.layer=2;go.transform.SetParent(root,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=mats[pair.Key];bool fine=pair.Key=="Damp"||pair.Key=="ClearGlass"||pair.Key=="Steel"||pair.Key=="BlackSteel"||pair.Key=="Lettering";
                r.shadowCastingMode=fine?ShadowCastingMode.Off:ShadowCastingMode.On;r.receiveShadows=pair.Key!="Damp";
                GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);
            }
        }
        [MenuItem("Taiwan Rider/Realism/Apply street life refinement")]
        public static void Apply() => Apply(0,0,0);

        /// <summary>Use the same side and building-center range as IntersectionDetailSetup.Apply.
        /// Whole shops are omitted; remaining shops keep their original index and design.</summary>
        public static void Apply(int excludedSide, float minBuildingCenter, float maxBuildingCenter)
        {
            IntersectionDetailSetup.ValidateExclusion(excludedSide,minBuildingCenter,maxBuildingCenter);
            var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Assets+"/Scenes/GongguanRide.unity")throw new InvalidOperationException("Open GongguanRide outside Play mode.");
            var detail=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==IntersectionDetailSetup.RootName);if(!detail)throw new InvalidOperationException("Apply IntersectionDetailSetup first.");
            var route=UnityEngine.Object.FindFirstObjectByType<RoadRoute>();if(!route)throw new InvalidOperationException("RoadRoute missing.");
            font=AssetDatabase.LoadAssetAtPath<Font>(Assets+"/Art/Fonts/NotoSansTC.otf");if(!font)throw new InvalidOperationException("Noto Sans TC missing.");
            var all=JsonUtility.FromJson<Layout>("{\"items\":"+File.ReadAllText("Sources/building-layout.json")+"}").items;
            Directory.CreateDirectory(Art);AssetDatabase.Refresh();mats.Clear();batches.Clear();labels.Clear();Materials();
            int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Gongguan street life refinement");var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);if(old)Undo.DestroyObjectImmediate(old);
            root=new GameObject(RootName).transform;root.gameObject.layer=2;Undo.RegisterCreatedObjectUndo(root.gameObject,"Street life details");
            ReplaceFacadeMaterials(detail.transform);TransitionBuildings(all);int index=0,created=0;
            foreach(var b in all.Where(v=>Mathf.Abs(v.center)<22))
            {
                int originalIndex=index++;
                if(IntersectionDetailSetup.ExcludesBuilding(b.side,b.center,excludedSide,minBuildingCenter,maxBuildingCenter))continue;
                Shop(b,originalIndex,route);created++;
            }
            BakeLettering();SaveBatches();Undo.CollapseUndoOperations(group);EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            Debug.Log("Street life study applied: "+created+" original shops, "+(index-created)+" omitted shops, "+batches.Count+" mesh batches, generated metric tile maps, no added colliders. Scene remains unsaved for review.");
        }
        [MenuItem("Taiwan Rider/Realism/Remove street life refinement")]
        public static void Remove()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode first.");var scene=SceneManager.GetActiveScene();
            var old=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==RootName);if(old)Undo.DestroyObjectImmediate(old);
            var detail=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==IntersectionDetailSetup.RootName);
            if(detail)foreach(var r in detail.GetComponentsInChildren<MeshRenderer>(true))
            {
                if(r.name.StartsWith("Fictional shop name •",StringComparison.Ordinal)){r.gameObject.SetActive(true);continue;}
                const string prefix="Buildings_and_pavement_";if(!r.name.StartsWith(prefix,StringComparison.Ordinal))continue;
                var m=AssetDatabase.LoadAssetAtPath<Material>(Assets+"/Art/IntersectionDetail/"+r.name.Substring(prefix.Length)+".mat");if(m)r.sharedMaterial=m;
            }
            foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)))
            {
                var materials=r.sharedMaterials;bool changed=false;
                for(int i=0;i<materials.Length;i++)if(materials[i]&&materials[i].name.StartsWith("StreetLife_Transition_",StringComparison.Ordinal))
                {var m=AssetDatabase.LoadAssetAtPath<Material>(Assets+"/Materials/"+materials[i].name.Substring("StreetLife_Transition_".Length)+".mat");if(m){materials[i]=m;changed=true;}}
                if(changed)r.sharedMaterials=materials;
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
