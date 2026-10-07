#!/usr/bin/env python3
"""Local TLS Base fixture for the real C# and Java clients; never a public endpoint."""
import argparse,hashlib,hmac,json,secrets,socket,ssl,struct,subprocess,tempfile,threading,time,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def exactly(s,n):
 b=b''
 while len(b)<n:
  chunk=s.recv(n-len(b))
  if not chunk:raise EOFError('partial')
  b+=chunk
 return b
def read(s,key):
 header=exactly(s,49);device,op,nonce,stamp,n=struct.unpack('!HBIQH',header[:17])
 if n>16384:raise ValueError('size')
 payload=exactly(s,n)
 if not hmac.compare_digest(header[17:],hmac.digest(key,header[:17]+payload,'sha256')):raise ValueError('auth')
 if op!=201 or abs(int(time.time()*1000)-stamp)>5000:raise ValueError('protocol')
 return device,json.loads(payload)
def frame(key,message,nonce,stamp=None):
 payload=json.dumps(message,ensure_ascii=False,separators=(',',':')).encode()
 prefix=struct.pack('!HBIQH',1,201,nonce,stamp or int(time.time()*1000),len(payload))
 return prefix+hmac.digest(key,prefix+payload,'sha256')+payload
def serve(listener,context,key,mode,errors):
 try:
  raw,_=listener.accept()
  with raw,context.wrap_socket(raw,server_side=True) as stream:
   stream.settimeout(12);previous=None
   for step in range(3):
    device,request=read(stream,key)
    if device not in (2,3):raise ValueError('device')
    kind=request['type']
    data={'challenge':request['data']['challenge']} if kind=='session.prove' else {'types':['status']} if kind=='capabilities' else {'online':True}
    response={'version':4,'type':'session.proved' if kind=='session.prove' else kind+'.result','requestId':str(uuid.uuid4()),'correlationId':request['requestId'],'expiresAt':int(time.time()*1000)+30000,'data':data}
    if step==2 and mode=='correlation':response['correlationId']=str(uuid.uuid4())
    if step==2 and mode=='revoked':response['type']='error';response['data']={'code':'revoked'}
    encoded=frame(key,response,step+1,int(time.time()*1000)-6000 if step==2 and mode=='timestamp' else None)
    if step==2 and mode=='replay':encoded=previous
    for i in range(0,len(encoded),7):stream.sendall(encoded[i:i+7])
    previous=encoded
 except (ssl.SSLError,EOFError,ConnectionError,OSError) as e:
  if mode!='pin':errors.append(str(e))
 finally:listener.close()
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--dotnet',default='dotnet');parser.add_argument('--java',default='java');parser.add_argument('--javac',default='javac');args=parser.parse_args()
 with tempfile.TemporaryDirectory(prefix='sx4-tls-') as tmp:
  tmp=Path(tmp);cert=tmp/'cert.pem';keyfile=tmp/'key.pem'
  subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-nodes','-days','1','-subj','/CN=localhost','-keyout',str(keyfile),'-out',str(cert)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
  der=ssl.PEM_cert_to_DER_cert(cert.read_text());pin=hashlib.sha256(der).hexdigest();context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);context.minimum_version=ssl.TLSVersion.TLSv1_2;context.load_cert_chain(cert,keyfile)
  java=ROOT/'phone-android/app/src/main/java/pl/sentinelx/phone'
  subprocess.run([args.javac,'-d',str(tmp/'classes'),*[str(java/n) for n in ['Sx4.java','StrictJson.java','BaseClient.java']],str(ROOT/'verification/java/pl/sentinelx/phone/KernelContract.java')],check=True)
  subprocess.run([args.dotnet,'build',str(ROOT/'verification/Build1/Build1.csproj'),'-c','Release','--nologo'],check=True)
  for language in ['Java','C#']:
   for mode in ['ok','pin','correlation','replay','timestamp','revoked']:
    key=secrets.token_bytes(32);listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(1);listener.settimeout(20);port=listener.getsockname()[1];errors=[]
    thread=threading.Thread(target=serve,args=(listener,context,key,mode,errors),daemon=True);thread.start()
    command=[args.java,'-cp',str(tmp/'classes'),'pl.sentinelx.phone.KernelContract'] if language=='Java' else [args.dotnet,str(ROOT/'verification/Build1/bin/Release/net10.0/Build1.dll')]
    subprocess.run(command+[mode,str(port),'0'*64 if mode=='pin' else pin,key.hex()],check=True,timeout=35)
    thread.join(20)
    if thread.is_alive() or errors:raise RuntimeError(errors or ['TLS server stuck'])
    print('PASS '+language+' TLS '+mode,flush=True)
 print('PASS 12 real-client TLS cases')
if __name__=='__main__':main()
