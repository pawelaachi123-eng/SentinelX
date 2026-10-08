package pl.sentinelx.phone;
import java.nio.charset.StandardCharsets;
import java.util.*;
public final class KernelContract {
 static int checks;
 interface Throws {void run()throws Exception;}
 static void check(boolean pass,String name){if(!pass)throw new AssertionError(name);checks++;System.out.println("PASS "+name);}
 static void reject(Throws action,String code)throws Exception{try{action.run();throw new AssertionError("accepted "+code);}catch(Sx4.Error e){check(e.code.equals(code),"reject "+code);}}
 public static void run() throws Exception {
  byte[] key=new byte[32];new java.security.SecureRandom().nextBytes(key);long now=System.currentTimeMillis();
  byte[] payload="{\"test\":\"Zażółć 🛰\"}".getBytes(StandardCharsets.UTF_8);
  byte[] raw=Sx4.encode(new Sx4.Frame(7,201,-1,now,payload),key);
  check(Sx4.utf8(Sx4.decode(raw,key).payload).contains("Zażółć"),"binary unicode");
  try(Sx4.Authority authority=new Sx4.Authority(7,key)){authority.verify(raw,now);reject(()->authority.verify(raw,now),"replay");}
  try(Sx4.Authority authority=new Sx4.Authority(7,key)){reject(()->authority.verify(raw,now+5001),"timestamp");}
  try(Sx4.Authority authority=new Sx4.Authority(8,key)){reject(()->authority.verify(raw,now),"device");}
  byte[] changed=raw.clone();changed[changed.length-1]^=1;reject(()->Sx4.decode(changed,key),"auth");
  reject(()->Sx4.decode(Arrays.copyOf(raw,raw.length-1),key),"size");
  reject(()->Sx4.decode(new byte[1],key),"format");
  reject(()->Sx4.encode(new Sx4.Frame(1,201,1,now,new byte[16385]),key),"format");
  reject(()->Sx4.utf8(new byte[]{(byte)255}),"utf8");
  Sx4.Authority revoked=new Sx4.Authority(7,key);revoked.close();reject(()->revoked.verify(raw,now),"revoked");
  try(Sx4.Authority authority=new Sx4.Authority(7,key)){for(int i=0;i<120;i++)authority.verify(Sx4.encode(new Sx4.Frame(7,201,i,now,payload),key),now);reject(()->authority.verify(Sx4.encode(new Sx4.Frame(7,201,121,now,payload),key),now),"rate_limit");}
  reject(()->StrictJson.parse("{\"test\":1,\"TEST\":2}"),"duplicate_key");
  reject(()->StrictJson.parse("{\"nested\":{\"a\":1,\"A\":2}}"),"duplicate_key");
  check(StrictJson.object(StrictJson.parse("{\"version\":4}")).get("version") instanceof Long,"version is integral Long");
  check(StrictJson.parse(StrictJson.encode(StrictJson.map("test","Zażółć 🛰","array",Arrays.asList(1,2)))) instanceof Map,"JSON roundtrip");
  reject(()->new BaseClient("localhost",443,new String(new char[64]).replace('\0','0'),3,3,new String(new char[64]).replace('\0','0')),"configuration");
  javax.crypto.KeyGenerator generator=javax.crypto.KeyGenerator.getInstance("AES");generator.init(256);javax.crypto.SecretKey aes=generator.generateKey();
  byte[] one=SecretCipher.encrypt("base.identity","sensitive 🛰",aes),two=SecretCipher.encrypt("base.identity","sensitive 🛰",aes);
  check(!Arrays.equals(one,two),"Keystore cipher random IV");check(SecretCipher.decrypt("base.identity",one,aes).equals("sensitive 🛰"),"Keystore cipher roundtrip");
  one[one.length-1]^=1;try{SecretCipher.decrypt("base.identity",one,aes);throw new AssertionError("tamper");}catch(javax.crypto.AEADBadTagException e){check(true,"Keystore tamper rejected");}
  try{SecretCipher.decrypt("pc.token",two,aes);throw new AssertionError("AAD");}catch(javax.crypto.AEADBadTagException e){check(true,"Keystore name binding");}
  ConnectionLifecycle life=new ConnectionLifecycle();int epoch=life.resume(false);check(life.current(epoch),"lifecycle resume");
  life.networkChanged();check(!life.current(epoch),"Wi-Fi/LTE cancels old epoch");epoch=life.epoch();life.stop();check(!life.current(epoch),"stop blocks stale callbacks");
  epoch=life.resume(true);check(!life.current(epoch),"revocation survives resume");life.paired();epoch=life.epoch();check(life.current(epoch),"repair pairing restores connection");
  long delay=0;for(int i=0;i<10;i++){long next=life.retryDelay(0);check(next>=delay&&next<=60000,"bounded exponential reconnect");delay=next;}
  check(ConnectionLifecycle.fatalLink("auth"),"fatal auth blocks");
  check(ConnectionLifecycle.fatalLink("revoked"),"fatal revoked blocks");
  check(!ConnectionLifecycle.fatalLink("size"),"transient size reconnects");
  check(!ConnectionLifecycle.fatalLink("unsupported"),"unsupported is steady state");
  check(!ConnectionLifecycle.fatalLink("not_paired"),"not_paired is steady state");
  System.out.println("PASS "+checks+" Java checks");
 }
 public static void main(String[] args)throws Exception {
  if(args.length==0){run();return;}
  String mode=args[0];int port=Integer.parseInt(args[1]);String pin=args[2],secret=args[3];
  try(BaseClient client=new BaseClient("127.0.0.1",port,pin,3,1,secret)){
   try{client.connect();if(mode.equals("ok")){
    check(client.supports("status"),"capability");Map<String,Object> reply=client.request("status",StrictJson.map());check(Boolean.TRUE.equals(StrictJson.object(reply.get("data")).get("online")),"TLS Base status");
    reject(()->client.request("shell",StrictJson.map()),"unsupported");
   }else{client.request("status",StrictJson.map());throw new AssertionError("bad server accepted");}}
   catch(Exception e){if(mode.equals("ok"))throw e;if(e instanceof Sx4.Error)check(((Sx4.Error)e).code.equals(mode)||(mode.equals("pin")&&((Sx4.Error)e).code.equals("auth")),"TLS reject "+mode);else if(mode.equals("pin"))check(true,"TLS pin rejected");else throw e;}
  }
 }
}
