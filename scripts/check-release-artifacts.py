#!/usr/bin/env python3
import argparse,hashlib,struct,zipfile
from pathlib import Path
def pe(data,machines):
 assert len(data)>128 and data[:2]==b'MZ','Missing executable header'
 offset=struct.unpack_from('<I',data,0x3c)[0]
 assert offset+6<len(data) and data[offset:offset+4]==b'PE\0\0','Invalid PE'
 assert struct.unpack_from('<H',data,offset+4)[0] in machines,'Wrong architecture'
def present(path,minimum):
 path=Path(path);assert path.is_file() and path.stat().st_size>=minimum,'Missing/tiny artifact: '+str(path)
 print(hashlib.sha256(path.read_bytes()).hexdigest()+'  '+path.name)
 return path
def main():
 p=argparse.ArgumentParser();p.add_argument('--windows');p.add_argument('--portable');p.add_argument('--android');a=p.parse_args()
 assert a.windows or a.portable or a.android,'No artifacts supplied'
 if a.windows:
  path=present(a.windows,1048576);assert '1.0.0' in path.name,'Version filename';pe(path.read_bytes(),{0x14c,0x8664})
 if a.portable:
  path=present(a.portable,1048576)
  with zipfile.ZipFile(path) as z:
   names=set(z.namelist());assert {'SentinelX.exe','SentinelX.dll','coreclr.dll','SentinelX.runtimeconfig.json'}<=names,'Runtime/app missing'
   pe(z.read('SentinelX.exe'),{0x8664})
   engine=[n for n in names if n.startswith('Engine/') and n.endswith('/llama-server.exe')];assert len(engine)==1,'Engine missing'
   pe(z.read(engine[0]),{0x8664});assert z.testzip() is None,'ZIP CRC failure'
 if a.android:
  path=present(a.android,20000)
  with zipfile.ZipFile(path) as z:
   assert {'AndroidManifest.xml','classes.dex','resources.arsc'}<=set(z.namelist()),'APK content'
   assert z.testzip() is None,'APK CRC failure'
 print('PASS real release artifact structure')
if __name__=='__main__':main()
