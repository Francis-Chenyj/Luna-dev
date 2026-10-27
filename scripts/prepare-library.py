"""Build a resumable, verified Luna offline profile from public APIs. Python 3 stdlib only."""
import argparse, concurrent.futures, datetime, gzip, hashlib, json, os, pathlib, threading, time, urllib.request, urllib.error

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = 'https://www.malzaharai.top'
LANES = ['top', 'jungle', 'middle', 'bottom', 'support']
LABELS = ['上路', '打野', '中路', '下路', '辅助']
lock = threading.Lock()
next_request = 0.0

def stamp():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()

def encoded(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')

def atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_bytes(data)
    os.replace(temporary, path)

def request(url):
    global next_request
    for attempt in range(4):
        with lock:
            wait = max(0, next_request - time.monotonic())
            next_request = max(next_request, time.monotonic()) + 0.55
        time.sleep(wait)
        try:
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({'https':args.proxy})) if args.proxy else urllib.request.build_opener()
            with opener.open(urllib.request.Request(url, headers={'User-Agent':'Luna (offline library maintenance)', 'Accept':'application/json'}), timeout=18) as response:
                raw = response.read(16*1024*1024+1)
            if len(raw) > 16*1024*1024:
                raise ValueError('Response too large')
            data = json.loads(raw)
            if not isinstance(data, dict) or data.get('success') is False:
                raise ValueError('Invalid API response')
            return data
        except (OSError, ValueError) as error:
            if isinstance(error, urllib.error.HTTPError) and error.code in (401, 403):
                raise
            if attempt == 3:
                raise
            time.sleep(min(30, 2**(attempt+1)))

def status():
    data = request(SOURCE + '/api/counter/status')
    fingerprint = data.get('package', {}).get('sha256', '')
    if not data.get('ready') or len(fingerprint) != 64 or not data.get('meta', {}).get('generatedAt'):
        raise ValueError('Source is not ready or has no data fingerprint')
    return data

def valid(record, key, fingerprint):
    _, cid, lane, mode, threshold, sensitivity = key.split('-')
    data = record['data']
    return record.get('key') == key and record.get('fingerprint') == fingerprint and data.get('success') is True and str(data.get('champion', {}).get('championId')) == cid and data.get('lane') == lane and data.get('mode') == mode and isinstance(data.get('sections'), list) and len(data['sections']) <= 7 and all(isinstance(s.get('items'), list) for s in data['sections'])

def main():
    before = status()
    fingerprint = before['package']['sha256']
    work = ROOT / 'research' / 'acquisition' / fingerprint
    work.mkdir(parents=True, exist_ok=True)
    atomic(work / 'status.json', encoded(before))
    catalog_file = work / 'catalog.json'
    if catalog_file.exists():
        catalog = json.loads(catalog_file.read_bytes())
    else:
        try:
            roster = request('https://game.gtimg.cn/images/lol/act/img/js/heroList/hero_list.js')
            heroes = {int(h['heroId']): dict(id=int(h['heroId']), name=h['title'], title=h['name'], en=h['alias'], keywords=h.get('keywords', ''), lanes={}) for h in roster['hero']}
            version = roster.get('version', '')
        except (OSError, ValueError, KeyError):
            old = json.loads((ROOT / 'data/catalog.json').read_text(encoding='utf-8-sig'))['data']
            heroes = {h['id']: dict(h, lanes={}) for h in old['heroes']}
            version = old.get('version', '')
        for lane in LANES:
            data = request(SOURCE + '/api/counter/champions?mode=father&lane=' + lane)
            if not isinstance(data.get('champions'), list) or not data['champions']:
                raise ValueError('Empty lane catalog: ' + lane)
            if len(data['champions']) != next(l['championCount'] for l in before['lanes'] if l['key']==lane):
                raise ValueError('Source lane count changed: ' + lane)
            for h in data['champions']:
                cid = int(h['championId'])
                hero = heroes.setdefault(cid, dict(id=cid, name=h['championNameEn'], title=h['championNameCn'], en=h['championNameEn'], keywords='', lanes={}))
                hero['lanes'][lane] = h.get('displayGames', h.get('games', 0))
                hero['sourceTitle'] = h['championNameCn']
            print('catalog', lane, len(data['champions']), flush=True)
        catalog = dict(data=dict(heroes=list(heroes.values()), lanes=[dict(key=k,label=v) for k,v in zip(LANES,LABELS)], version=version, updatedAt=stamp()), fetchedAt=stamp())
        atomic(catalog_file, encoded(catalog))
    profiles = [(1,5)] if args.profile == 'default' else [(int(args.profile[0]),int(args.profile[2]))] if args.profile != 'all' else [(1,5)] + [(t,s) for t in range(1,6) for s in range(1,6) if (t,s) != (1,5)]
    for threshold, sensitivity in profiles:
        profile = f'{threshold}-{sensitivity}'
        directory = work / profile
        directory.mkdir(exist_ok=True)
        keys = [f'detail-{h["id"]}-{lane}-{mode}-{threshold}-{sensitivity}' for h in catalog['data']['heroes'] for lane in h['lanes'] for mode in ('father','son')]
        def fetch(key):
            path = directory / (key+'.json.gz')
            if path.exists():
                try:
                    if valid(json.loads(gzip.decompress(path.read_bytes())),key,fingerprint):
                        return key, hashlib.sha256(path.read_bytes()).hexdigest(), None
                except (ValueError, OSError, KeyError):
                    pass
            try:
                _, cid, lane, mode, t, s = key.split('-')
                data = request(f'{SOURCE}/api/counter/detail?championId={cid}&lane={lane}&mode={mode}&thresholdLevel={t}&sensitivity={s}')
                record = dict(key=key, fingerprint=fingerprint, fetchedAt=stamp(), data=data)
                if not valid(record, key, fingerprint):
                    raise ValueError('Incorrect response schema or query identity')
                raw = gzip.compress(encoded(record), mtime=0)
                atomic(path,raw)
                return key, hashlib.sha256(raw).hexdigest(), None
            except (OSError, ValueError, KeyError) as error:
                return key, None, str(error)
        entries, failed = {}, {}
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            for count, (key, digest, error) in enumerate(pool.map(fetch, keys), 1):
                if error:
                    failed[key] = error
                else:
                    entries[key] = digest
                if count % 25 == 0 or error or count == len(keys):
                    print(f'{profile}: {count}/{len(keys)}, saved {len(entries)}, failed {len(failed)}', error or '', flush=True)
        atomic(directory / 'failures.json', encoded(failed))
        after = status()
        if after['package']['sha256'] != fingerprint:
            raise ValueError('Source changed during acquisition; rerun for the new edition')
        if failed:
            raise ValueError(f'{len(failed)} missing queries; rerun to resume')
        manifest = dict(schema=1, source=SOURCE+'/counter', profile=profile, fingerprint=fingerprint, sourceGeneratedAt=before['meta']['generatedAt'], builtAt=stamp(), expected=len(keys), entries=entries, catalog=catalog)
        atomic(directory / 'manifest.json',encoded(manifest))
        print('COMPLETE', directory, flush=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--profile', default='default', help='default, all, or threshold-sensitivity, e.g. 3-5')
    parser.add_argument('--proxy', help='Optional HTTPS proxy URL; otherwise use system settings')
    args=parser.parse_args()
    if args.profile not in ('default','all') and not __import__('re').fullmatch('[1-5]-[1-5]',args.profile):
        parser.error('Invalid profile')
    main()
