"""Validate a complete client archive, then sign a channel using the publisher's Windows key.

No upload and no credentials. Publish the matching GitHub Release asset first.
"""
from pathlib import Path
import argparse, hashlib, json, re, subprocess, zipfile, tempfile, urllib.parse, urllib.request, time

HERE=Path(__file__).resolve().parent

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('archive',type=Path)
    parser.add_argument('--tag',required=True)
    parser.add_argument('--version',required=True)
    parser.add_argument('--sequence',required=True,type=int)
    parser.add_argument('--asset',required=True,help='Exact asset name on GitHub, after GitHub normalizes spaces.')
    parser.add_argument('--previous-channel',type=Path,help='For offline preparation only: a previously verified channel snapshot. Default: read the published signed channel.')
    args=parser.parse_args()
    if not re.fullmatch(r'[a-zA-Z0-9._,-]+',args.tag) or not re.fullmatch(r'[a-zA-Z0-9._-]+',args.asset):
        raise ValueError('Use a simple GitHub tag and exact asset name.')
    with tempfile.TemporaryDirectory(prefix='another-client-channel-')as temporary:
        previous=Path(temporary)/'previous.json'
        if args.previous_channel:feed=args.previous_channel.read_bytes()
        else:
            url='https://raw.githubusercontent.com/chuletas123/another-client/main/channels/alpha.json?publish='+str(time.time_ns())
            request=urllib.request.Request(url,headers={'User-Agent':'AnotherClient-Publisher','Cache-Control':'no-cache'})
            with urllib.request.urlopen(request,timeout=20)as response:feed=response.read(65537)
        if len(feed)>65536:raise ValueError('Published update channel is too large.')
        previous.write_bytes(feed)
        verified=subprocess.run([str(HERE/'build/Publisher.exe'),'read-channel',str(previous),str(HERE/'assets/update-public.xml')],capture_output=True,text=True,check=True)
        current=json.loads(verified.stdout)
        if args.sequence<=current['sequence']:raise ValueError('New sequence must exceed the published sequence '+str(current['sequence'])+'. Renaming a ZIP does not activate an update.')
    roots={'Another Client.exe','Another Client.exe.config','AnotherClient.Accounts.dll','AnotherClient.Capture.dll','AnotherClient.Avatar.dll','AnotherClient.Discord.dll','AnotherClient.Registration.dll','discord-presence.json','LEEME.txt','ac-player.json','distribution-components.json','remote-services.json','client-manifest.json'}
    capture={'scripts/capture/AnotherClientCapture.exe','scripts/capture/ScreenRecorderLib.LICENSE.txt','scripts/capture/ScreenRecorderLib.dll','scripts/capture/manifest.json'}
    with zipfile.ZipFile(args.archive) as archive:
        items=[item for item in archive.infolist() if not item.is_dir()]
        folders={item.filename.split('/')[0] for item in items}
        if len(folders)!=1:raise ValueError('Expected one top-level client folder.')
        top=folders.pop();prefix=top+'/'
        files={item.filename[len(prefix):]:item for item in items}
        if len(files)!=len(items):raise ValueError('Duplicate archive entries.')
        raw=archive.read(prefix+'client-manifest.json');manifest=json.loads(raw)
        if args.sequence>=3 and manifest.get('updateChannelSequence')!=args.sequence:raise ValueError('Client embedded update identity must match the channel sequence.')
        if manifest.get('accountsCopied') is not False or manifest.get('privateKeysCopied') is not False:raise ValueError('Privacy flags must explicitly exclude accounts and private keys.')
        with tempfile.TemporaryDirectory(prefix='another-client-release-identity-')as temporary:
            folder=Path(temporary);client=folder/'Another Client.exe';external=folder/'client-manifest.json'
            client.write_bytes(archive.read(prefix+'Another Client.exe'));external.write_bytes(raw)
            (folder/'ac-player.json').write_bytes(archive.read(prefix+'ac-player.json'))
            subprocess.run([str(HERE/'build/Publisher.exe'),'verify-client',str(client),str(external)],check=True)
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
                  archiveUrl='https://github.com/chuletas123/another-client/releases/download/'+urllib.parse.quote(args.tag,safe='-._~')+'/'+args.asset,
                  archiveSha256=digest,archiveBytes=args.archive.stat().st_size,uncompressedBytes=sum(i.file_size for i in items),fileCount=len(items),topFolder=top,
                  manifestSha256=expected['client-manifest.json'],exeSha256=expected['Another Client.exe'])
    payload=HERE/'release-candidate.json';payload.write_text(json.dumps(spec,separators=(',',':')),encoding='utf-8')
    subprocess.run([str(HERE/'build/Publisher.exe'),'sign',str(payload),str(HERE/'channel-candidate.json')],check=True)
    print('Validated and signed '+str(len(items))+' files. Review channel-candidate.json before publishing.')

if __name__=='__main__':main()
