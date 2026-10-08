package pl.sentinelx.phone;
import android.content.Context;
import java.util.Map;
final class BaseSession {
 private final SecretStore store;private final android.content.SharedPreferences state;
 BaseSession(Context context){store=new SecretStore(context);state=context.getSharedPreferences("base.state",Context.MODE_PRIVATE);}
 Map<String,Object> configuration(){try{String saved=store.get("base.identity");return saved.isEmpty()?null:StrictJson.object(StrictJson.parse(saved));}catch(Exception e){return null;}}
 BaseClient open() throws Exception {if(blocked())throw new Sx4.Error("revoked");Map<String,Object> c=configuration();if(c==null)throw new Sx4.Error("not_paired");return create(c);}
 static BaseClient create(Map<String,Object> c) throws Exception {
  Object mode=c.get("mode");if(!("lan".equals(mode)||"vpn".equals(mode)||"relay".equals(mode)))throw new Sx4.Error("mode");
  return new BaseClient((String)c.get("host"),((Number)c.get("port")).intValue(),(String)c.get("fingerprint"),((Number)c.get("deviceId")).intValue(),((Number)c.get("baseId")).intValue(),(String)c.get("secret"));
 }
 void save(Map<String,Object> c) throws Sx4.Error{store.put("base.identity",StrictJson.encode(c));setBlocked(false);state.edit().remove("lastAlert").apply();}
 void forget(){store.remove("base.identity");setBlocked(false);state.edit().remove("lastAlert").apply();}
 boolean blocked(){return state.getBoolean("blocked",false);}
 void setBlocked(boolean value){state.edit().putBoolean("blocked",value).apply();}
 long lastAlert(){return state.getLong("lastAlert",0);}
 void lastAlert(long id){state.edit().putLong("lastAlert",id).apply();}
}
