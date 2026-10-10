"""Keep attribution and reproducible source data beside the project and player."""
import json,pathlib,hashlib
root=pathlib.Path(__file__).resolve().parents[1]
sources=root/'Sources'
manifest=json.loads((sources/'asset-manifest.json').read_text(encoding='utf-8'))
manifest=[x for x in manifest if x['asset']!='Gongguan road centre lines']
manifest.append(dict(asset='Gongguan road centre lines',source='https://www.openstreetmap.org/way/316698035',license='ODbL 1.0',attribution='© OpenStreetMap contributors',license_url='https://www.openstreetmap.org/copyright',source_file='gongguan-osm.json',derived_file='Assets/TaiwanRider/Data/RouteSurvey.json',details='OSM centreline and junction used. Width 12.4 m is an estimate; street elevation, facades, markings and shop identities are reconstructed.'))
manifest.append(dict(asset='Noto Sans CJK TC Regular static font',source='https://github.com/notofonts/noto-cjk/tree/main/Sans/OTF/TraditionalChinese',license='SIL Open Font License 1.1',file='Assets/TaiwanRider/Art/Fonts/NotoSansTC.otf'))
manifest.append(dict(asset='Original metric scooter, sedan, building meshes, signs, sky shader and synthetic engine',source='Tools/model_assets.py and Tools/prepare_assets.py',license='Original project work',details='Scooter dimensions based on publicly stated Yamaha AXIS Z specifications; original generic mesh with no Yamaha logo. Sedan is fictional. Shop names and placement are illustrative, not a survey.'))
(sources/'asset-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
credits=json.loads((sources/'References'/'credits.json').read_text(encoding='utf-8'))
lines=['RideReflex — asset notices','', 'Road centreline: © OpenStreetMap contributors, ODbL 1.0. https://www.openstreetmap.org/copyright', 'Source and derived route data are provided in this project. Width and street-level details are inferred.', '', 'PBR surfaces: Poly Haven, CC0. https://polyhaven.com/license', 'asphalt_02; concrete_wall_003; concrete_floor_worn_001; concrete_tile_facade.', '', 'Font: Noto Sans TC / Noto Sans CJK TC, SIL Open Font License 1.1. See FONT_LICENSE.txt / Art/Fonts/OFL.txt.', '', 'Model dimensions reference: Yamaha AXIS Z 125 (2025). https://www.yamaha-motor.com.tw/news/news-202508-axis-z', 'Original model; no affiliation or endorsement. Simplified assisted riding, not a validated dynamics model.', '', 'Reference photographs below are included only in Sources/References, not used as in-game textures:']
for c in credits:
 m=c['metadata'];lines += ['',c['title'],c['source'],'Author: '+str(m.get('Artist',{}).get('value','See source page')),'License: '+str(m.get('LicenseShortName',{}).get('value','See source page'))+' '+str(m.get('LicenseUrl',{}).get('value',''))]
(sources/'THIRD_PARTY_NOTICES.txt').write_text('\n'.join(lines),encoding='utf-8')
checks={str(p.relative_to(root)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in (root/'Assets'/'TaiwanRider'/'Art').rglob('*') if p.is_file() and p.suffix!='.meta'}
(sources/'asset-sha256.json').write_text(json.dumps(checks,indent=2),encoding='utf-8')
print('Attribution and SHA-256 manifest written')
