"""Final private release verification: source, stock, scripts, matrix and ZIP.
python ngo_verify_release.py <release folder> <validation folder>
"""
import argparse
import ast
import base64
import hashlib
import importlib.util
import json
from pathlib import Path, PureWindowsPath
import re
import zipfile


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream,'sha256').hexdigest()


def local(root,rel):
    return root.joinpath(*PureWindowsPath(rel).parts)


def load(path):
    return json.loads(path.read_text('utf-8-sig'))


def verify(release,validation):
    tools=Path(__file__).resolve().parent
    spec=importlib.util.spec_from_file_location('ngo_packer_checked',tools/'ngo_pack_release.py')
    packer=importlib.util.module_from_spec(spec); spec.loader.exec_module(packer)
    m=load(release/'manifest.json'); b=load(release/'build-info.json')
    files={p.relative_to(release).as_posix():sha(p) for p in sorted(release.rglob('*')) if p.is_file()}
    assert not any(part.startswith('backup_') for r in files for part in Path(r).parts)
    payload={f['path']:f for f in m['files']}; stock={f['path']:f['sha256'] for f in m['stock_files']}
    assert set(p.relative_to(release/'files').as_posix() for p in (release/'files').rglob('*') if p.is_file())==set(r.replace('\\','/') for r in payload)
    assert set(p.relative_to(release/'original').as_posix() for p in (release/'original').rglob('*') if p.is_file())==set(r.replace('\\','/') for r in stock)
    assert len({r.lower() for r in payload})==len(payload)
    for rel,f in payload.items():
        assert files['files/'+rel.replace('\\','/')]==f['sha256'],rel
        assert local(release/'files',rel).stat().st_size==f['bytes'],rel
        assert not packer.skipped(rel),rel
    originals=[]
    with zipfile.ZipFile(packer.ZIP) as z:
        for rel,digest in stock.items():
            assert sha(local(release/'original',rel))==digest,rel
            with z.open('Package/'+rel.replace('\\','/')) as fi:
                assert hashlib.file_digest(fi,'sha256').hexdigest()==digest,rel
            originals.append({'path':rel,'sha256':digest,'archive_match':True})
    for v in m['previous_versions']:
        r=release.parent/v['name']; old=load(r/'manifest.json')
        assert sha(r/'manifest.json')==v['manifest_sha256']
        assert sorted(v['added_files'])==sorted(f['path'] for f in old['files'] if not f['stock_sha256'])
        for f in old['files']:
            assert sha(local(r/'files',f['path']))==f['sha256']
            if f['stock_sha256']:
                assert stock[f['path']]==f['stock_sha256']
                assert sha(local(r/'original',f['path']))==f['stock_sha256']
    source=[]
    for f in b['source_files']:
        p=local(packer.PKG,f['path']); st=p.stat()
        digest=sha(p)
        assert digest==f['sha256'] and st.st_size==f['bytes'] and st.st_mtime_ns==f['mtime_ns'],f['path']
        if f['path']!=m['settings_path']:
            assert digest==payload[f['path']]['sha256']
        source.append({**f,'unchanged':True})
    assert set(payload)=={f['path'] for f in b['source_files']}
    assert local(release/'files',m['settings_path']).read_bytes()==packer.release_settings(release.parent/'maimai_NGO_skin_20261101')
    for part in ('install','uninstall','common'):
        dst=release/(('release_common' if part=='common' else part)+'.ps1')
        assert dst.read_bytes()==(tools/('ngo_release_'+part+'.ps1')).read_text('ascii').replace('\r\n','\n').replace('\n','\r\n').encode('ascii')
        for text in re.findall(r"'([A-Za-z0-9+/=]{45,})'",dst.read_text('ascii')):
            base64.b64decode(text,validate=True).decode('utf-8')
    for part in ('install','uninstall'):
        (release/(part+'.bat')).read_text('ascii')
    for name in ('ngo_pack_release.py','ngo_test_release.py','ngo_verify_release.py'):
        ast.parse((tools/name).read_text('utf-8-sig'))
    readme=next(p for p in release.glob('*.txt') if p.name not in ('SHA256SUMS.txt',))
    assert readme.read_text('utf-8-sig')==(tools/'ngo_release_readme.txt').read_text('utf-8-sig').format(
        date=b['date'],files=len(payload),replaced=sum(bool(f['stock_sha256']) for f in payload.values()),
        added=sum(not f['stock_sha256'] for f in payload.values()),obsolete=len(b['obsolete_added_files']))
    d2=Path(r'E:\cxref\maimai-ngo\work\lowend\outline\approved_d2_library')
    for f in d2.iterdir():
        if f.is_file():
            assert sha(f)==files['files/LocalAssets/PartnerLive2D/ngo_pair/library/'+f.name],f.name
    results=load(validation/'matrix/results.json')['results']
    assert results[-1]['name']=='complete full matrix' and all(r['status']=='PASS' for r in results)
    # A generic nonzero exit is insufficient evidence for the intended guard.
    for action in ('install','uninstall'):
        for case,reason in [('running','The game in this Package is running'),
                            ('unreadable','Cannot verify a running Sinmai process path')]:
            assert reason in (validation/'matrix'/('guard-'+action+'-'+case+'.log')).read_text('utf-8')
    for case in ('payload','original'):
        assert 'SHA256 mismatch:' in (validation/'matrix'/('guard-corrupt-'+case+'.log')).read_text('utf-8')
    archive=release.with_suffix('.zip')
    digest=sha(archive)
    assert archive.with_suffix('.zip.sha256').read_text('ascii')==f'{digest}  {archive.name}\n'
    with zipfile.ZipFile(archive) as z:
        entries=[i for i in z.infolist() if not i.is_dir()]
        assert len(entries)==len(files) and len({i.filename for i in entries})==len(entries)
        assert set(i.filename for i in entries)=={release.name+'/'+r for r in files}
        for i,info in enumerate(entries,1):
            rel=info.filename[len(release.name)+1:]
            with z.open(info) as fi:
                assert hashlib.file_digest(fi,'sha256').hexdigest()==files[rel],rel
            if info.file_size>100_000_000 or i%80==0:
                print(f'ZIP verified {i}/{len(entries)}: {rel}',flush=True)
    result={'status':'PASS','release':str(release),'payload_files':len(payload),'stock_files':len(stock),
            'release_files':len(files),'payload_bytes':sum(f['bytes'] for f in payload.values()),
            'zip_bytes':archive.stat().st_size,'zip_sha256':digest,'zip_entries':len(files),
            'stock_archive_matches':originals,'source_unchanged':source,'files':files,
            'tool_sha256':{n:sha(tools/n) for n in ['ngo_pack_release.py','ngo_release_common.ps1','ngo_release_install.ps1','ngo_release_uninstall.ps1','ngo_release_readme.txt','ngo_test_release.py','ngo_verify_release.py']},
            'matrix_results':results}
    (validation/'final-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('PASS final source/stock/script/matrix/ZIP verification',digest,flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('release',type=Path); parser.add_argument('validation',type=Path)
    a=parser.parse_args(); verify(a.release.resolve(),a.validation.resolve())
