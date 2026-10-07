package pl.sentinelx.phone;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.cert.X509Certificate;
import java.util.*;
import javax.net.ssl.*;

public final class BaseClient implements AutoCloseable {
 private final String host,fingerprint;private final int port,deviceId;private final byte[] key;private final Sx4.Authority authority;
 private final Object socketGate=new Object();private volatile SSLSocket socket;private volatile boolean closed;private int nonce;
 private final Set<String> capabilities=new HashSet<>();
 public BaseClient(String host,int port,String fingerprint,int deviceId,int baseId,String secret) throws Sx4.Error {
  if(host.isEmpty()||host.length()>253||host.matches(".*[\\s/\\\\].*")||port<1||port>65535||!fingerprint.matches("[0-9a-fA-F]{64}")||!secret.matches("[0-9a-fA-F]{64}")||
     deviceId<1||deviceId>65535||baseId<1||baseId>65535||deviceId==baseId)throw new Sx4.Error("configuration");
  this.host=host;this.port=port;this.fingerprint=fingerprint;this.deviceId=deviceId;key=Sx4.unhex(secret);authority=new Sx4.Authority(baseId,key);nonce=new SecureRandom().nextInt();
 }
 public synchronized boolean supports(String type){return capabilities.contains(type);}
 public synchronized void connect() throws Exception {
  if(closed)throw new Sx4.Error("closed");
  TrustManager[] trust={new X509TrustManager(){
   @Override public X509Certificate[] getAcceptedIssuers(){return new X509Certificate[0];}
   @Override public void checkClientTrusted(X509Certificate[] chain,String auth) throws java.security.cert.CertificateException{throw new java.security.cert.CertificateException("client");}
   @Override public void checkServerTrusted(X509Certificate[] chain,String auth) throws java.security.cert.CertificateException{
    try{if(chain.length==0||!MessageDigest.isEqual(MessageDigest.getInstance("SHA-256").digest(chain[0].getEncoded()),Sx4.unhex(fingerprint)))throw new java.security.cert.CertificateException("pin");chain[0].checkValidity();}
    catch(GeneralSecurityException|Sx4.Error e){throw new java.security.cert.CertificateException("pin");}
   }
  }};
  SSLContext context=SSLContext.getInstance("TLS");context.init(null,trust,new SecureRandom());
  SSLSocket next=(SSLSocket)context.getSocketFactory().createSocket();
  synchronized(socketGate){if(closed){next.close();throw new Sx4.Error("closed");}socket=next;}
  try{
   List<String> protocols=new ArrayList<>();for(String p:next.getSupportedProtocols())if(p.equals("TLSv1.2")||p.equals("TLSv1.3"))protocols.add(p);
   next.setEnabledProtocols(protocols.toArray(new String[0]));next.connect(new InetSocketAddress(host,port),10000);next.setSoTimeout(10000);next.startHandshake();
   String challenge=UUID.randomUUID().toString()+UUID.randomUUID();
   Map<String,Object> proof=exchange("session.prove",StrictJson.map("challenge",challenge,"role","controller","deviceId",deviceId),"session.proved");
   if(!challenge.equals(StrictJson.object(proof.get("data")).get("challenge")))throw new Sx4.Error("challenge");
   Map<String,Object> caps=StrictJson.object(exchange("capabilities",StrictJson.map(),"capabilities.result").get("data"));
   Object values=caps.get("types");if(!(values instanceof List)||((List<?>)values).size()>32)throw new Sx4.Error("capabilities");
   for(Object c:(List<?>)values)if(!(c instanceof String)||!((String)c).matches("[A-Za-z0-9._]{1,64}")||!capabilities.add((String)c))throw new Sx4.Error("capabilities");
  }catch(Exception e){close();throw e;}
 }
 public synchronized Map<String,Object> request(String type,Map<String,Object> data) throws Exception {
  if(!supports(type))throw new Sx4.Error("unsupported");return exchange(type,data,type+".result");
 }
 private Map<String,Object> exchange(String type,Map<String,Object> data,String expected) throws Exception {
  if(closed||socket==null)throw new Sx4.Error("offline");
  String id=UUID.randomUUID().toString();long now=System.currentTimeMillis();
  byte[] payload=StrictJson.encode(StrictJson.map("version",4,"type",type,"requestId",id,"correlationId","","expiresAt",now+30000,"data",data)).getBytes(StandardCharsets.UTF_8);
  try{
   socket.getOutputStream().write(Sx4.encode(new Sx4.Frame(deviceId,Sx4.EXTENSION,++nonce,now,payload),key));socket.getOutputStream().flush();
   long end=System.nanoTime()+20000000000L;byte[] header=read(49,end);int n=((header[15]&255)<<8)|(header[16]&255);
   if(n>Sx4.MAX_PAYLOAD)throw new Sx4.Error("size");byte[] raw=Arrays.copyOf(header,49+n);System.arraycopy(read(n,end),0,raw,49,n);
   Sx4.Frame frame=authority.verify(raw,System.currentTimeMillis());if(frame.opcode!=Sx4.EXTENSION)throw new Sx4.Error("opcode");
   Map<String,Object> response=StrictJson.object(StrictJson.parse(Sx4.utf8(frame.payload)));
   Set<String> fields=new HashSet<>(Arrays.asList("version","type","requestId","correlationId","expiresAt","data"));
   if(!response.keySet().equals(fields)||!Long.valueOf(4).equals(response.get("version")))throw new Sx4.Error("version");
   if(!(response.get("requestId") instanceof String)||!((String)response.get("requestId")).matches("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")||!id.equals(response.get("correlationId")))throw new Sx4.Error("correlation");
   Object expires=response.get("expiresAt");long time=System.currentTimeMillis();
   if(!(expires instanceof Long)||((Long)expires)<=time||((Long)expires)>time+300000)throw new Sx4.Error("expiry");
   if("error".equals(response.get("type"))){Object code=StrictJson.object(response.get("data")).get("code");throw new Sx4.Error(code instanceof String&&((String)code).matches("[a-z_]{1,64}")?(String)code:"remote_error");}
   if(!expected.equals(response.get("type")))throw new Sx4.Error("response_type");
   StrictJson.object(response.get("data"));return response;
  }catch(Exception e){close();throw e;}
 }
 private byte[] read(int length,long end) throws IOException {
  byte[] bytes=new byte[length];int offset=0;
  while(offset<length){
   long left=(end-System.nanoTime())/1000000;if(left<=0)throw new SocketTimeoutException("frame_timeout");
   SSLSocket current=socket;if(closed||current==null)throw new Sx4.Error("closed");
   current.setSoTimeout((int)Math.min(20000,Math.max(1,left)));int count=current.getInputStream().read(bytes,offset,length-offset);
   if(count<0)throw new EOFException("partial_frame");offset+=count;
  }
  return bytes;
 }
 @Override public void close(){
  synchronized(socketGate){if(closed)return;closed=true;SSLSocket s=socket;if(s!=null)try{s.close();}catch(IOException ignored){}}
  authority.close();Arrays.fill(key,(byte)0);
 }
}
