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
   String packed=Base64.encodeToString(SecretCipher.encrypt(name,value,key()),Base64.NO_WRAP);
   if(!preferences.edit().putString(name,packed).commit())throw new IllegalStateException("secret_write");
  }catch(Exception e){throw new IllegalStateException("secret_store",e);}
 }
 synchronized String get(String name){
  String packed=preferences.getString(name,"");if(packed.isEmpty())return "";
  try{
   if(packed.length()>65536)throw new IllegalStateException("secret_format");
   return SecretCipher.decrypt(name,Base64.decode(packed,Base64.NO_WRAP),key());
  }catch(Exception e){preferences.edit().remove(name).commit();return "";}
 }
 synchronized void remove(String name){if(!preferences.edit().remove(name).commit())throw new IllegalStateException("secret_write");}
}
