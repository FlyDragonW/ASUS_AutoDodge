"""Fetch a bounded public OSM dataset; no scene or source asset mutations."""
import datetime, hashlib, json, pathlib, urllib.parse, urllib.request

HERE = pathlib.Path(__file__).resolve().parent
BBOX = (25.0118, 121.5325, 25.0153, 121.5365)  # south, west, north, east
box = ','.join(map(str, BBOX))
QUERY = f'''[out:json][timeout:25];
(
 nwr["building"]({box});
 nwr["building:part"]({box});
 way["highway"]({box});
 nwr["name"]({box});
 nwr["addr:housenumber"]({box});
 nwr["waterway"]({box});
);
out meta geom;
'''
meta = {'requestedAtUTC': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'query': QUERY, 'bboxSWNE': BBOX, 'license': 'OpenStreetMap contributors, ODbL 1.0',
        'licenseUrl': 'https://www.openstreetmap.org/copyright', 'attempts': []}
for endpoint in ('https://overpass-api.de/api/interpreter', 'https://overpass.kumi.systems/api/interpreter'):
    try:
        request = urllib.request.Request(endpoint, data=urllib.parse.urlencode({'data': QUERY}).encode(),
                                         headers={'User-Agent': 'TaiwanRider-site-audit/1.0 (bounded read-only research)'})
        with urllib.request.urlopen(request, timeout=35) as response:
            raw = response.read()
            status = response.status
        result = json.loads(raw)
        if result.get('remark'):
            raise RuntimeError(result['remark'])
        (HERE / 'site-osm.json').write_bytes(raw)
        meta['attempts'].append({'endpoint': endpoint, 'status': status, 'success': True})
        meta.update({'successfulEndpoint': endpoint, 'responseSHA256': hashlib.sha256(raw).hexdigest(),
                     'bytes': len(raw), 'elementCount': len(result['elements']),
                     'osmTimestamp': result.get('osm3s', {}).get('timestamp_osm_base'), 'success': True})
        break
    except Exception as exc:
        meta['attempts'].append({'endpoint': endpoint, 'success': False, 'error': str(exc)})
else:
    meta['success'] = False
    meta['limitation'] = 'Both bounded public endpoint attempts failed; no further endpoint retries made.'
(HERE / 'site-osm-provenance.json').write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({key: value for key, value in meta.items() if key != 'query'}, ensure_ascii=False, indent=2))
