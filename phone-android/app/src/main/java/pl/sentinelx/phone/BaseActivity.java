package pl.sentinelx.phone;
import android.app.*;
import android.os.*;
import android.net.*;
import android.content.Context;
import android.text.InputType;
import android.view.*;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;
/** Native Base dashboard: never depends on the PC web server. */
public final class BaseActivity extends Activity {
 private final Handler ui=new Handler(Looper.getMainLooper());
 private ExecutorService worker;
 private volatile BaseClient client;
 private BaseSession session;
 private volatile boolean active,blocked;
 private final ConnectionLifecycle lifecycle=new ConnectionLifecycle();private int connectingEpoch=-1;
 private TextView status,result;
 private EditText host,port,pin,secret,device,base,target,appId,seconds;
 private Spinner mode,operation;
 private ConnectivityManager connectivity;
 private ConnectivityManager.NetworkCallback network;
 private final Runnable poll=()->submitQuery("status");
 @Override public void onCreate(Bundle state){
  super.onCreate(state);session=new BaseSession(this);blocked=session.blocked();worker=Executors.newSingleThreadExecutor();
  ScrollView scroll=new ScrollView(this);LinearLayout box=new LinearLayout(this);box.setOrientation(1);box.setPadding(22,24,22,24);box.setBackgroundColor(0xff0d0f14);scroll.addView(box);setContentView(scroll);
  label(box,"Sentinel Base · SX4 · 1.0.0",23);status=label(box,"Nie sparowano",16);result=label(box,"Porównaj fingerprint z Base. Sekret jest osobny dla telefonu.",14);result.setTextIsSelectable(true);
  host=field(box,"Adres Base / VPN / relay",InputType.TYPE_CLASS_TEXT);
  port=field(box,"Port TLS",InputType.TYPE_CLASS_NUMBER);port.setText("443");
  pin=field(box,"Fingerprint SHA-256 z fizycznej Base",InputType.TYPE_CLASS_TEXT);
  secret=field(box,"Sekret urządzenia (64 hex)",InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD);
  device=field(box,"ID telefonu",InputType.TYPE_CLASS_NUMBER);device.setText("3");
  base=field(box,"ID Base",InputType.TYPE_CLASS_NUMBER);base.setText("1");
  mode=spinner(box,new String[]{"lan","vpn","relay"});
  button(box,"Szukaj w LAN",()->discover());
  button(box,"Sparuj i sprawdź tożsamość",()->pair());
  button(box,"Odłącz urządzenie",()->confirm("Usunąć parowanie telefonu?",()->{blocked=true;disconnect();session.forget();status.setText("Odłączono. Unieważnij sekret także w Base.");}));
  Map<String,Object> saved=session.configuration();if(saved!=null){
   host.setText(String.valueOf(saved.get("host")));port.setText(String.valueOf(saved.get("port")));pin.setText(String.valueOf(saved.get("fingerprint")));
   device.setText(String.valueOf(saved.get("deviceId")));base.setText(String.valueOf(saved.get("baseId")));
   String m=String.valueOf(saved.get("mode"));mode.setSelection(m.equals("relay")?2:m.equals("vpn")?1:0);
  }
  for(String type:new String[]{"status","pc.state","devices","scenes","notifications","diagnostics","logs","tasks"})button(box,type,()->submitQuery(type));
  target=field(box,"ID docelowego PC lub sceny",InputType.TYPE_CLASS_TEXT);
  button(box,"Wake-on-LAN przez Base",()->action("wol",StrictJson.map("pcId",target()),false));
  button(box,"Uruchom scenę",()->action("scenes.run",StrictJson.map("id",target(),"expiresSeconds",30),true));
  operation=spinner(box,new String[]{"lock","sentinel.show","ollama.ensure","sleep","restart","shutdown","steam.run","steam.install"});
  appId=field(box,"Numeryczny Steam AppID",InputType.TYPE_CLASS_NUMBER);
  button(box,"Wyślij zadanie",()->task());
  seconds=field(box,"Timer: 1–300 sekund",InputType.TYPE_CLASS_NUMBER);seconds.setText("30");
  button(box,"Timer blokady PC",()->{
   int delay=Integer.parseInt(seconds.getText().toString());if(delay<1||delay>300)throw new IllegalArgumentException();
   action("timers.create",StrictJson.map("pcId",target(),"operation","lock","seconds",delay),false);
  });
  connectivity=(ConnectivityManager)getSystemService(Context.CONNECTIVITY_SERVICE);
  network=new ConnectivityManager.NetworkCallback(){
   @Override public void onAvailable(Network n){ui.post(()->{if(active&&!blocked){disconnect();connect();}});}
   @Override public void onLost(Network n){ui.post(()->{if(active){disconnect();status.setText("Sieć zmieniona · ponawiam uwierzytelnienie");scheduleReconnect();}});}
  };
 }
 private TextView label(LinearLayout box,String text,int size){TextView v=new TextView(this);v.setText(text);v.setTextSize(size);v.setTextColor(0xffe8ecf4);v.setPadding(0,10,0,10);box.addView(v);return v;}
 private EditText field(LinearLayout box,String hint,int input){EditText v=new EditText(this);v.setHint(hint);v.setContentDescription(hint);v.setInputType(input);v.setSingleLine(true);v.setTextColor(0xffe8ecf4);v.setHintTextColor(0xffaab4cc);box.addView(v);return v;}
 private Spinner spinner(LinearLayout box,String[] values){Spinner v=new Spinner(this);v.setAdapter(new ArrayAdapter<>(this,android.R.layout.simple_spinner_dropdown_item,values));box.addView(v);return v;}
 private void button(LinearLayout box,String title,Runnable action){Button b=new Button(this);b.setText(title);b.setAllCaps(false);b.setOnClickListener(v->{try{action.run();}catch(Exception e){result.setText("Niepoprawne dane. Sprawdź ID, AppID i zakres timera.");}});box.addView(b);}
 private String target(){String id=target.getText().toString().trim();if(!id.matches("[A-Za-z0-9_-]{1,64}"))throw new IllegalArgumentException();return id;}
 private void confirm(String message,Runnable action){
  long end=SystemClock.elapsedRealtime()+30000;
  AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Potwierdzenie").setMessage(message+"\nZgoda wygasa za 30 sekund.").setNegativeButton("Anuluj",null)
   .setPositiveButton("Zatwierdź",(d,w)->{if(active&&SystemClock.elapsedRealtime()<end)action.run();}).create();
  dialog.show();ui.postDelayed(dialog::dismiss,30000);
 }
 private void task(){
  String op=operation.getSelectedItem().toString();String value=op.startsWith("steam.")?appId.getText().toString():"";
  if(op.startsWith("steam.")&&(!value.matches("[1-9][0-9]{0,9}")||Long.parseLong(value)>4294967295L))throw new IllegalArgumentException();
  action("tasks.create",StrictJson.map("pcId",target(),"operation",op,"target",value,"expiresSeconds",60),Arrays.asList("sleep","restart","shutdown","steam.install").contains(op));
 }
 private void action(String type,Map<String,Object> data,boolean risk){
  Runnable send=()->request(type,data,false);if(risk)confirm("Wykonać "+type+" dla "+data.getOrDefault("pcId",data.get("id"))+"?",send);else send.run();
 }
 private void pair(){
  Map<String,Object> c=StrictJson.map("host",host.getText().toString().trim(),"port",Integer.parseInt(port.getText().toString()),"fingerprint",pin.getText().toString().trim(),
   "deviceId",Integer.parseInt(device.getText().toString()),"baseId",Integer.parseInt(base.getText().toString()),"secret",secret.getText().toString().trim(),"mode",mode.getSelectedItem().toString());
  disconnect();int epoch=lifecycle.epoch();status.setText("Sprawdzam podpis i challenge…");
  worker.execute(()->{
   try(BaseClient verify=BaseSession.create(c)){
    client=verify;verify.connect();
    if(!active||epoch!=lifecycle.epoch())return;
    session.save(c);ui.post(()->{if(active&&epoch==lifecycle.epoch()){secret.setText("");blocked=false;lifecycle.paired();lifecycle.connected();disconnect();connect();AlertJobService.schedule(this);}});
   }catch(Exception e){ui.post(()->{if(active&&epoch==lifecycle.epoch())showError(e);});}
   finally{if(epoch==lifecycle.epoch())client=null;}
  });
 }
 private void discover(){
  status.setText("Szukam w LAN…");int epoch=lifecycle.epoch();
  worker.execute(()->{
   try{List<Map<String,Object>> found=BaseDiscovery.discover();ui.post(()->{
    if(!active||epoch!=lifecycle.epoch())return;if(found.isEmpty()){result.setText("Brak zgodnych odpowiedzi. Możesz wpisać adres ręcznie.");return;}
    String[] names=new String[found.size()];for(int i=0;i<names.length;i++)names[i]=String.valueOf(found.get(i).get("host"));
    new AlertDialog.Builder(this).setTitle("Wybierz Base").setItems(names,(d,w)->{
     Map<String,Object> c=found.get(w);host.setText(String.valueOf(c.get("host")));port.setText(String.valueOf(c.get("port")));base.setText(String.valueOf(c.get("baseId")));
     result.setText("Kandydat fingerprint: "+c.get("fingerprint")+"\nPorównaj go z fizyczną Base i wpisz powyżej.");mode.setSelection(0);
    }).show();
   });}catch(Exception e){ui.post(()->{if(active&&epoch==lifecycle.epoch())showError(e);});}
  });
 }
 private void connect(){
  if(!active||blocked||session.configuration()==null||connectingEpoch==lifecycle.epoch())return;int epoch=lifecycle.epoch();connectingEpoch=epoch;status.setText("Łączenie · "+session.configuration().get("mode"));
  worker.execute(()->{
   try{
    BaseClient next=session.open();if(!active||epoch!=lifecycle.epoch()){next.close();return;}client=next;next.connect();
    ui.post(()->{if(active&&epoch==lifecycle.epoch()){connectingEpoch=-1;lifecycle.connected();status.setText("Base połączona · SX4 · "+networkLabel());submitQuery("status");}});
   }catch(Exception e){ui.post(()->{if(active&&epoch==lifecycle.epoch()){connectingEpoch=-1;showError(e);scheduleReconnect();}});}
  });
 }
 private String networkLabel(){Network n=connectivity.getActiveNetwork();NetworkCapabilities c=connectivity.getNetworkCapabilities(n);return c!=null&&c.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)?"Wi-Fi · "+mode.getSelectedItem():"remote · "+mode.getSelectedItem();}
 private void scheduleReconnect(){if(!active||blocked)return;ui.removeCallbacks(poll);int epoch=lifecycle.epoch();long delay=lifecycle.retryDelay(ThreadLocalRandom.current().nextInt(1000));ui.postDelayed(()->{if(active&&epoch==lifecycle.epoch()){disconnect();connect();}},delay);}
 private void submitQuery(String type){request(type,StrictJson.map(),true);}
 private void request(String type,Map<String,Object> data,boolean read){
  BaseClient connection=client;int epoch=lifecycle.epoch();if(!active||connection==null||blocked){result.setText("Base offline. Sparuj urządzenie lub poczekaj na połączenie.");return;}
  if(!read)result.setText("Wysyłam jednorazowo…");
  worker.execute(()->{
   try{Map<String,Object> reply=connection.request(type,data);String text=StrictJson.encode(redact(reply.get("data")));ui.post(()->{
    if(!active||epoch!=lifecycle.epoch())return;result.setText(text);status.setText("Base połączona · "+networkLabel());
    if(type.equals("status")){ui.removeCallbacks(poll);ui.postDelayed(poll,15000);}
   });}catch(Exception e){ui.post(()->{if(active&&epoch==lifecycle.epoch()){showError(e);if(!(e instanceof Sx4.Error)){disconnect();scheduleReconnect();}}});}
  });
 }
 private static Object redact(Object value){
  if(value instanceof Map){Map<String,Object> safe=new LinkedHashMap<>();for(Map.Entry<?,?> e:((Map<?,?>)value).entrySet()){String k=String.valueOf(e.getKey());safe.put(k,k.toLowerCase(Locale.ROOT).matches(".*(secret|token|password|key).*")?"[redacted]":redact(e.getValue()));}return safe;}
  if(value instanceof List){List<Object> safe=new ArrayList<>();for(Object x:(List<?>)value)safe.add(redact(x));return safe;}return value;
 }
 private void showError(Exception e){
  if(e instanceof Sx4.Error){String code=((Sx4.Error)e).code;if(!code.equals("unsupported")&&!code.equals("not_paired")){blocked=true;lifecycle.revoke();session.setBlocked(true);disconnect();}result.setText("SX4: "+code+(blocked?" · sprawdź tożsamość i sparuj ponownie":""));}
  else result.setText("Połączenie przerwane. Komendy nie są automatycznie powtarzane.");
 }
 private void disconnect(){lifecycle.networkChanged();connectingEpoch=-1;ui.removeCallbacks(poll);BaseClient c=client;client=null;if(c!=null)c.close();}
 @Override protected void onResume(){super.onResume();active=true;blocked=session.blocked();lifecycle.resume(blocked);connectivity.registerDefaultNetworkCallback(network);connect();}
 @Override protected void onStop(){active=false;lifecycle.stop();disconnect();ui.removeCallbacksAndMessages(null);try{connectivity.unregisterNetworkCallback(network);}catch(IllegalArgumentException ignored){}super.onStop();}
 @Override protected void onDestroy(){disconnect();worker.shutdownNow();super.onDestroy();}
}
