"""Fetch licensed source assets and generate original sign/dial textures. No Unity dependencies."""
import json, math, os, pathlib, struct, urllib.request, urllib.parse, wave
from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[1]
ART = ROOT / 'Assets' / 'TaiwanRider' / 'Art'
SOURCES = ROOT / 'Sources'
ART.mkdir(parents=True, exist_ok=True)
SOURCES.mkdir(exist_ok=True)
HEADERS={'User-Agent':'GongguanRidingStudy/1.0 (local noncommercial prototype)'}
def fetch(url, dest=None):
    req=urllib.request.Request(url,headers=HEADERS)
    with urllib.request.urlopen(req,timeout=35) as r: data=r.read()
    if dest:
        dest=pathlib.Path(dest);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
    return data

manifest=[]
for asset in ['asphalt_02','concrete_wall_003','concrete_floor_worn_001','concrete_tile_facade']:
    folder=ART/'Textures'/asset;folder.mkdir(parents=True,exist_ok=True)
    try:
        cache=SOURCES/(asset+'.json')
        info=json.loads(cache.read_text(encoding='utf-8')) if cache.exists() else json.loads(fetch('https://api.polyhaven.com/files/'+asset))
        (SOURCES/(asset+'.json')).write_text(json.dumps(info,indent=2),encoding='utf-8')
        for channel,source_channel in [('diff','Diffuse'),('nor_gl','nor_gl'),('rough','Rough')]:
            entries=info.get(source_channel,{}).get('2k',info.get(source_channel,{}).get('1k',{}))
            fmt='png' if 'png' in entries else 'jpg'
            entry=entries.get(fmt)
            if not entry: continue
            file=folder/(channel+'.'+fmt)
            if not file.exists():fetch(entry['url'],file)
        if (folder/'rough.png').exists() or (folder/'rough.jpg').exists():
            rough=Image.open(next(folder.glob('rough.*'))).convert('L')
            metallic=Image.new('RGBA',rough.size,(0,0,0,255))
            metallic.putalpha(rough.point(lambda x:255-x));metallic.save(folder/'metallic_smoothness.png')
        manifest.append({'asset':asset,'source':'https://polyhaven.com/a/'+asset,'license':'CC0','maps':'diffuse, OpenGL normal, roughness; roughness inverted into Unity smoothness alpha'})
        print('MATERIAL',asset,flush=True)
    except Exception as e: print('MATERIAL ERROR',asset,str(e),flush=True)

fontdir=ART/'Fonts';fontdir.mkdir(exist_ok=True)
fontpath=fontdir/'NotoSansTC.otf'
if not fontpath.exists():
    fetch('https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/OTF/TraditionalChinese/NotoSansCJKtc-Regular.otf',fontpath)
font=lambda size:ImageFont.truetype(str(fontpath),size)
manifest.append({'asset':'Noto Sans TC','source':'https://github.com/google/fonts/tree/main/ofl/notosanstc','license':'SIL Open Font License 1.1'})

signs=[('公館眼鏡',(19,81,78)),('益昌機車',(26,50,78)),('古早味豆花',(183,55,39)),('巷口咖啡',(69,57,45)),('老地方便當',(179,65,37)),('生活百貨',(36,76,114)),('台大書坊',(49,99,78)),('美而香早餐',(220,159,43)),('水源茶行',(30,84,67)),('日常藥局',(38,112,92)),('福記麵食',(172,42,35)),('自助洗衣',(61,110,135)),('金玉銀樓',(133,49,37)),('純剪理髮',(39,57,75)),('手機維修',(44,78,115)),('手作飯糰',(170,79,36))]
texdir=ART/'Signs';texdir.mkdir(exist_ok=True)
for index,(name,color) in enumerate(signs):
    for vertical in [False,True]:
        size=(256,1024) if vertical else (1024,256)
        im=Image.new('RGB',size,color);d=ImageDraw.Draw(im)
        d.rectangle((8,8,size[0]-9,size[1]-9),outline=(220,212,173),width=3)
        d.rectangle((16,16,size[0]-17,size[1]-17),outline=tuple(min(255,x+20) for x in color),width=2)
        if vertical:
            fs=font(173)
            for j,ch in enumerate(name):
                box=d.textbbox((0,0),ch,font=fs);d.text(((256-(box[2]-box[0]))/2,53+j*219-box[1]),ch,font=fs,fill=(246,234,193))
            d.text((63,942),'公 館',font=font(33),fill=(232,225,202))
        else:
            fs=font(148);box=d.textbbox((0,0),name,font=fs);d.text(((1024-(box[2]-box[0]))/2,22-box[1]),name,font=fs,fill=(248,237,210))
            d.text((43,199),'GONGGUAN  /  TAIPEI',font=font(23),fill=(217,209,186))
            d.text((788,194),'歡迎光臨',font=font(29),fill=(239,224,194))
        im.save(texdir/f'Sign_{index:02d}_{"V" if vertical else "H"}.png')

im=Image.new('RGB',(1024,512),(19,25,27));d=ImageDraw.Draw(im)
d.rounded_rectangle((12,12,1010,499),60,fill=(36,44,46),outline=(127,135,128),width=9)
d.arc((236,-19,786,531),190,350,fill=(228,225,195),width=5)
for k in range(11):
    a=math.radians(195+k*15);cx,cy=511,263
    p=(cx+235*math.cos(a),cy+235*math.sin(a));q=(cx+213*math.cos(a),cy+213*math.sin(a))
    d.line([p,q],fill=(235,230,205),width=5)
    x,y=cx+178*math.cos(a),cy+178*math.sin(a)
    d.text((x,y),str(k*10),font=font(27),anchor='mm',fill=(224,230,211))
d.text((512,332),'km/h',font=font(23),anchor='mm',fill=(192,197,187))
d.rounded_rectangle((398,379,625,440),7,fill=(128,151,133));d.text((512,408),'00125.0',font=font(31),anchor='mm',fill=(27,42,32))
d.text((130,338),'E ━━━ F',font=font(25),anchor='mm',fill=(216,220,199));d.text((890,336),'125',font=font(37),anchor='mm',fill=(217,223,215))
im.save(texdir/'Instrument.png')

im=Image.new('RGB',(512,256),(231,230,211));d=ImageDraw.Draw(im)
d.rounded_rectangle((8,8,502,246),19,outline=(54,60,59),width=6)
d.text((256,53),'臺灣省',font=font(33),anchor='mm',fill=(45,51,49))
d.text((256,157),'KPA-0125',font=font(80),anchor='mm',fill=(35,42,40))
im.save(texdir/'Plate.png')

# Original synthetic engine bed. Not represented as a recording of a real vehicle.
audio=ART/'Audio';audio.mkdir(exist_ok=True)
with wave.open(str(audio/'ScooterEngine.wav'),'w') as wav:
    wav.setnchannels(1);wav.setsampwidth(2);wav.setframerate(44100)
    data=bytearray()
    for i in range(44100*4):
        t=i/44100;v=.3*math.sin(2*math.pi*55*t)+.14*math.sin(2*math.pi*110*t)+.08*math.sin(2*math.pi*165*t)
        v*=.65+.35*math.sin(2*math.pi*27.5*t)**2
        data+=struct.pack('<h',int(v*14000))
    wav.writeframes(data)

query='[out:json][timeout:15];way["name"~"^(汀州路三段|羅斯福路四段90巷)$"](25.0128,121.533,25.0141,121.5354);out geom;'
for endpoint in ([] if (SOURCES/'gongguan-osm.json').exists() else ['https://overpass.private.coffee/api/interpreter','https://overpass-api.de/api/interpreter']):
    try:
        result=fetch(endpoint+'?'+urllib.parse.urlencode({'data':query}));parsed=json.loads(result)
        if parsed.get('elements'):
            (SOURCES/'gongguan-osm.json').write_bytes(result)
            manifest.append({'asset':'Gongguan road centre lines','source':'OpenStreetMap via Overpass','license':'ODbL 1.0','query':query})
            print('OSM',len(parsed['elements']),flush=True);break
    except Exception as e:print('OSM RETRY',str(e),flush=True)
(SOURCES/'asset-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
(SOURCES/'fictional-signs.json').write_text(json.dumps([s[0] for s in signs],ensure_ascii=False,indent=2),encoding='utf-8')
print('ASSETS READY',flush=True)
