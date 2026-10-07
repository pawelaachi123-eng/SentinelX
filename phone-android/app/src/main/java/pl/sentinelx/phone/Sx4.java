package pl.sentinelx.phone;
import java.io.*;
import java.nio.*;
import java.nio.charset.*;
import java.security.*;
import java.util.*;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

public final class Sx4 {
 public static final int HEADER=49,MAX_PAYLOAD=16384,EXTENSION=201;
 public static final class Error extends IOException {
  public final String code;public Error(String code){super(code);this.code=code;}
 }
 public static final class Frame {
  public final int device,opcode,nonce;public final long timestamp;public final byte[] payload;
  public Frame(int device,int opcode,int nonce,long timestamp,byte[] payload){this.device=device;this.opcode=opcode;this.nonce=nonce;this.timestamp=timestamp;this.payload=payload;}
 }
 private static byte[] mac(byte[] key,byte[] input) throws Error {
  try{Mac m=Mac.getInstance("HmacSHA256");m.init(new SecretKeySpec(key,"HmacSHA256"));return m.doFinal(input);}
  catch(GeneralSecurityException e){throw new Error("crypto");}
 }
 public static byte[] encode(Frame f,byte[] key) throws Error {
  if(key.length!=32||f.device<1||f.device>65535||f.opcode<0||f.opcode>255||f.timestamp<0||f.payload.length>MAX_PAYLOAD)throw new Error("format");
  ByteBuffer b=ByteBuffer.allocate(HEADER+f.payload.length).order(ByteOrder.BIG_ENDIAN);
  b.putShort((short)f.device).put((byte)f.opcode).putInt(f.nonce).putLong(f.timestamp).putShort((short)f.payload.length);
  byte[] input=new byte[17+f.payload.length];System.arraycopy(b.array(),0,input,0,17);System.arraycopy(f.payload,0,input,17,f.payload.length);
  b.put(mac(key,input)).put(f.payload);return b.array();
 }
 public static Frame decode(byte[] raw,byte[] key) throws Error {
  if(key.length!=32||raw.length<HEADER)throw new Error("format");
  ByteBuffer b=ByteBuffer.wrap(raw).order(ByteOrder.BIG_ENDIAN);
  int id=b.getShort()&65535,op=b.get()&255,nonce=b.getInt();long ts=b.getLong();int n=b.getShort()&65535;
  if(n>MAX_PAYLOAD||raw.length!=HEADER+n)throw new Error("size");
  byte[] input=new byte[17+n];System.arraycopy(raw,0,input,0,17);System.arraycopy(raw,HEADER,input,17,n);
  if(!MessageDigest.isEqual(mac(key,input),Arrays.copyOfRange(raw,17,49)))throw new Error("auth");
  if(id==0||ts<0)throw new Error("device");
  return new Frame(id,op,nonce,ts,Arrays.copyOfRange(raw,HEADER,raw.length));
 }
 public static final class Authority implements AutoCloseable {
  private final int device;private final byte[] key;private boolean revoked;
  private final Map<Integer,Long> nonces=new HashMap<>();private final ArrayDeque<Long> rate=new ArrayDeque<>();
  public Authority(int device,byte[] key){if(device<1||device>65535||key.length!=32)throw new IllegalArgumentException("device");this.device=device;this.key=key.clone();}
  public synchronized Frame verify(byte[] raw,long now) throws Error {
   if(revoked)throw new Error("revoked");Frame f=decode(raw,key);
   if(f.device!=device)throw new Error("device");
   if(Math.abs(now-f.timestamp)>5000)throw new Error("timestamp");
   nonces.entrySet().removeIf(x->x.getValue()<now-10000);
   if(nonces.containsKey(f.nonce))throw new Error("replay");
   while(!rate.isEmpty()&&rate.peekFirst()<now-60000)rate.removeFirst();
   if(rate.size()>=120||nonces.size()>=2048)throw new Error("rate_limit");
   nonces.put(f.nonce,now);rate.addLast(now);return f;
  }
  @Override public synchronized void close(){revoked=true;Arrays.fill(key,(byte)0);nonces.clear();rate.clear();}
 }
 public static String utf8(byte[] bytes) throws Error {
  try{return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();}
  catch(CharacterCodingException e){throw new Error("utf8");}
 }
 public static String hex(byte[] bytes){StringBuilder b=new StringBuilder();for(byte v:bytes)b.append(String.format(Locale.ROOT,"%02x",v&255));return b.toString();}
 public static byte[] unhex(String value) throws Error {
  if(value.length()%2!=0)throw new Error("hex");byte[] bytes=new byte[value.length()/2];
  for(int i=0;i<bytes.length;i++){int a=Character.digit(value.charAt(2*i),16),b=Character.digit(value.charAt(2*i+1),16);if(a<0||b<0)throw new Error("hex");bytes[i]=(byte)((a<<4)|b);}
  return bytes;
 }
 private Sx4(){}
}
