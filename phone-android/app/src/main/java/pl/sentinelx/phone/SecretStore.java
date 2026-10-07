package pl.sentinelx.phone;
import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

final class SecretStore {
 private final SharedPreferences preferences;
 private static final String ALIAS="sentinelx.device.secrets.v1";
 SecretStore(Context context){preferences=context.getApplicationContext().getSharedPreferences("sentinelx.secrets",Context.MODE_PRIVATE);}
 private SecretKey key() throws Exception {
  KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
  if(!store.containsAlias(ALIAS)){
   KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
   generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT)
    .setKeySize(256).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setRandomizedEncryptionRequired(true).build());
   generator.generateKey();
  }
  return (SecretKey)store.getKey(ALIAS,null);
 }
 synchronized void put(String name,String value){
  try{
   Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());cipher.updateAAD(name.getBytes(StandardCharsets.UTF_8));
   byte[] encrypted=cipher.doFinal(value.getBytes(StandardCharsets.UTF_8));String packed=Base64.encodeToString(cipher.getIV(),Base64.NO_WRAP)+":"+Base64.encodeToString(encrypted,Base64.NO_WRAP);
   if(!preferences.edit().putString(name,packed).commit())throw new IllegalStateException("secret_write");
  }catch(Exception e){throw new IllegalStateException("secret_store",e);}
 }
 synchronized String get(String name){
  String packed=preferences.getString(name,"");if(packed.isEmpty())return "";
  try{
   String[] parts=packed.split(":",-1);if(parts.length!=2||packed.length()>16384)throw new IllegalStateException("secret_format");
   Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Base64.decode(parts[0],Base64.NO_WRAP)));
   cipher.updateAAD(name.getBytes(StandardCharsets.UTF_8));return new String(cipher.doFinal(Base64.decode(parts[1],Base64.NO_WRAP)),StandardCharsets.UTF_8);
  }catch(Exception e){preferences.edit().remove(name).commit();return "";}
 }
 synchronized void remove(String name){if(!preferences.edit().remove(name).commit())throw new IllegalStateException("secret_write");}
}
