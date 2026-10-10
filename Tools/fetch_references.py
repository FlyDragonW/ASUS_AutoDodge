import json,pathlib,urllib.parse,urllib.request
root=pathlib.Path(__file__).resolve().parents[1]/'Sources'/'References';root.mkdir(exist_ok=True)
titles=['File:Roosevelt Road Sec.4 Lane 78, Taipei (near Alley 2).jpg','File:GongGuan Night Market, Taipei 20191008 04.jpg','File:GongGuan Night Market, Taipei 20191008 03.jpg','File:TCPD SWAT ANA-0922 left side on Tingzhou Road 20240907.jpg']
metadata=[]
for index,title in enumerate(titles):
    query={'action':'query','titles':title,'prop':'imageinfo','iiprop':'url|extmetadata','iiurlwidth':'1600','format':'json'}
    try:
        request=urllib.request.Request('https://commons.wikimedia.org/w/api.php?'+urllib.parse.urlencode(query),headers={'User-Agent':'GongguanRidingStudy/1.0'})
        with urllib.request.urlopen(request,timeout=30) as r:data=json.load(r)
        info=next(iter(data['query']['pages'].values()))['imageinfo'][0]
        with urllib.request.urlopen(urllib.request.Request(info.get('thumburl',info['url']),headers={'User-Agent':'GongguanRidingStudy/1.0'}),timeout=30) as r:(root/f'reference_{index}.jpg').write_bytes(r.read())
        metadata.append({'file':f'reference_{index}.jpg','title':title,'source':info['descriptionurl'],'metadata':info.get('extmetadata',{}),'use':'Reference only, not embedded in game textures'})
        print(title,flush=True)
    except Exception as e:print('REFERENCE ERROR',title,e,flush=True)
(root/'credits.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
