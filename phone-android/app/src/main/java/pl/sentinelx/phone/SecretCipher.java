package pl.sentinelx.phone;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;
/** Platform independent AES-GCM envelope; key provider remains Android Keystore. */
public final class SecretCipher {
 public static byte[] encrypt(String name,String value,SecretKey key)throws Exception {
  if(name.isEmpty()||name.length()>64||value.length()>8192)throw new IllegalArgumentException("secret_size");
  Cipher c=Cipher.getInstance("AES/GCM/NoPadding");c.init(Cipher.ENCRYPT_MODE,key);c.updateAAD(name.getBytes(StandardCharsets.UTF_8));
  byte[] iv=c.getIV(),encrypted=c.doFinal(value.getBytes(StandardCharsets.UTF_8));if(iv.length!=12)throw new IllegalStateException("iv");
  byte[] packed=new byte[13+encrypted.length];packed[0]=1;System.arraycopy(iv,0,packed,1,12);System.arraycopy(encrypted,0,packed,13,encrypted.length);return packed;
 }
 public static String decrypt(String name,byte[] packed,SecretKey key)throws Exception {
  if(packed.length<29||packed.length>32768||packed[0]!=1)throw new IllegalArgumentException("secret_format");
  Cipher c=Cipher.getInstance("AES/GCM/NoPadding");c.init(Cipher.DECRYPT_MODE,key,new GCMParameterSpec(128,Arrays.copyOfRange(packed,1,13)));
  c.updateAAD(name.getBytes(StandardCharsets.UTF_8));byte[] plain=c.doFinal(Arrays.copyOfRange(packed,13,packed.length));
  try{return Sx4.utf8(plain);}finally{Arrays.fill(plain,(byte)0);}
 }
 private SecretCipher(){}
}
