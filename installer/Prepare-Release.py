"""Validate a complete client archive, then sign a channel using the publisher's Windows key.

No upload and no credentials. Publish the matching GitHub Release asset first.
"""
from pathlib import Path
import argparse, hashlib, json, re, subprocess, zipfile

HERE=Path(__file__).resolve().parent

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('archive',type=Path)
    parser.add_argument('--tag',required=True)
    parser.add_argument('--version',required=True)
    parser.add_argument('--sequence',required=True,type=int)
    parser.add_argument('--asset',required=True,help='Exact asset name on GitHub, after GitHub normalizes spaces.')
    args=parser.parse_args()
    if not re.fullmatch(r'[a-zA-Z0-9._-]+',args.tag) or not re.fullmatch(r'[a-zA-Z0-9._-]+',args.asset):
        raise ValueError('Use a simple GitHub tag and exact asset name.')
    roots={'Another Client.exe','Another Client.exe.config','AnotherClient.Accounts.dll','AnotherClient.Capture.dll','LEEME.txt','ac-player.json','distribution-components.json','remote-services.json','client-manifest.json'}
    capture={'scripts/capture/AnotherClientCapture.exe','scripts/capture/ScreenRecorderLib.LICENSE.txt','scripts/capture/ScreenRecorderLib.dll','scripts/capture/manifest.json'}
    with zipfile.ZipFile(args.archive) as archive:
        items=[item for item in archive.infolist() if not item.is_dir()]
        folders={item.filename.split('/')[0] for item in items}
        if len(folders)!=1:raise ValueError('Expected one top-level client folder.')
        top=folders.pop();prefix=top+'/'
        files={item.filename[len(prefix):]:item for item in items}
        if len(files)!=len(items):raise ValueError('Duplicate archive entries.')
        raw=archive.read(prefix+'client-manifest.json');manifest=json.loads(raw)
        if manifest.get('accountsCopied') is not False or manifest.get('privateKeysCopied') is not False:raise ValueError('Privacy flags must explicitly exclude accounts and private keys.')
        expected=dict(manifest['files']);expected['client-manifest.json']=hashlib.sha256(raw).hexdigest()
        if set(expected)!=set(files):raise ValueError('Archive inventory differs from the manifest.')
        for name,item in files.items():
            parts=name.split('/')
            if any(p in ('','..','.') or '\\' in p or ':' in p for p in parts):raise ValueError('Unsafe archive path.')
            if len(parts)==1 and name not in roots:raise ValueError('Unexpected root file: '+name)
            if len(parts)>1 and parts[0] not in ('2026E','assets') and name not in capture:raise ValueError('Unexpected runtime/private directory: '+name)
            if re.search(r'\.(?:dpapi|pfx|p12|key|sqlite|sqlite3)$',name,re.I):raise ValueError('Private material cannot be distributed: '+name)
            if name.lower().endswith('.pem'):
                pem=archive.read(item)
                if name!='2026E/ssl/cacert.pem' or b'PRIVATE KEY' in pem or not re.findall(br'-----BEGIN CERTIFICATE-----',pem) or any(label!=b'CERTIFICATE' for label in re.findall(br'-----BEGIN ([A-Z ]+)-----',pem)):
                    raise ValueError('Only the engine public CA certificate bundle may contain PEM data.')
            with archive.open(item) as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
            if digest!=expected[name]:raise ValueError('File hash differs: '+name)
        with args.archive.open('rb') as stream:digest=hashlib.file_digest(stream,'sha256').hexdigest()
        spec=dict(schemaVersion=1,product='AnotherClient',channel='alpha',platform='windows-x64',version=args.version,sequence=args.sequence,
                  archiveUrl='https://github.com/chuletas123/another-client/releases/download/'+args.tag+'/'+args.asset,
                  archiveSha256=digest,archiveBytes=args.archive.stat().st_size,uncompressedBytes=sum(i.file_size for i in items),fileCount=len(items),topFolder=top,
                  manifestSha256=expected['client-manifest.json'],exeSha256=expected['Another Client.exe'])
    payload=HERE/'release-candidate.json';payload.write_text(json.dumps(spec,separators=(',',':')),encoding='utf-8')
    subprocess.run([str(HERE/'build/Publisher.exe'),'sign',str(payload),str(HERE/'channel-candidate.json')],check=True)
    print('Validated and signed '+str(len(items))+' files. Review channel-candidate.json before publishing.')

if __name__=='__main__':main()
