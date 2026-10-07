package pl.sentinelx.phone;

import android.app.job.JobInfo;
import android.app.job.JobParameters;
import android.app.job.JobScheduler;
import android.app.job.JobService;
import android.content.ComponentName;
import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.IOException;

/** Every ~15 minutes (the Android minimum) asks the PC for new alerts and shows them as notifications — no account, no cloud, no foreground service. */
public class AlertJobService extends JobService {
    private static final int JOB_ID = 94;
    private volatile long generation;private volatile BaseClient jobClient;private Thread jobThread;

    static void schedule(Context context) {
        try {
            JobScheduler scheduler = (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
            if (scheduler == null || scheduler.getPendingJob(JOB_ID) != null) return;
            JobInfo job = new JobInfo.Builder(JOB_ID, new ComponentName(context, AlertJobService.class))
                    .setPeriodic(15 * 60 * 1000L)
                    .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                    .setPersisted(true)
                    .build();
            scheduler.schedule(job);
        } catch (Exception ignored) {
            // alerts are a convenience; the app works without them
        }
    }

    @Override
    public boolean onStartJob(JobParameters params) {
        final Context context = getApplicationContext();
        final long epoch=++generation;
        jobThread=new Thread(() -> {
            try {
                pollBase(context,epoch);
                if(epoch==generation)pollOnce(context,()->epoch==generation);
            } catch (Exception ignored) {
                // next run will try again
            } finally {
                if(epoch==generation)jobFinished(params, false);
            }
        }, "sentinelx-alerts");jobThread.start();
        return true;
    }

    @Override
    public boolean onStopJob(JobParameters params) { generation++;BaseClient c=jobClient;if(c!=null)c.close();if(jobThread!=null)jobThread.interrupt();return true; }

    /** One polling round; also used when the app is opened. The first round only remembers the current state, so old alerts are not replayed. */
    static void pollOnce(Context context) throws Exception {pollOnce(context,()->true);}
    private static void pollOnce(Context context,java.util.function.BooleanSupplier current) throws Exception {
        Session session = new Session(context);
        if (!session.hasPc() || !session.hasToken()) return;
        String body;
        try {
            body = fetch(session);
        } catch (PinnedTls.HttpStatusException unauthorized) {
            if (unauthorized.status == 401) session.clearToken(); // the phone was disconnected on the PC
            return;
        } catch (IOException unreachable) {
            // the PC may have a new address: find it (only the PC with the pinned certificate counts) and try once more
            PcLocator.Pc found = PcLocator.discover(session.fingerprint());
            if (found == null) return;
            session.savePc(found.host, found.port, found.fingerprint, found.name);
            body = fetch(session);
        }
        JSONObject json = new JSONObject(body);
        JSONArray alerts = json.optJSONArray("alerts");
        long previous = session.lastAlert();
        if (alerts != null && previous != 0L) {
            for (int i = 0; i < alerts.length(); i++) {
                JSONObject alert = alerts.getJSONObject(i);
                long id = alert.optLong("id");
                if (id > previous && current.getAsBoolean()) Notifier.post(context, id, alert.optString("level", "info"), alert.optString("title", ""), alert.optString("text", ""));
            }
        }
        if(current.getAsBoolean())session.setLastAlert(json.optLong("last", previous));
    }

    private void pollBase(Context context,long epoch) throws Exception {
        BaseSession session=new BaseSession(context);if(session.configuration()==null||session.blocked())return;
        try(BaseClient connection=session.open()){
            jobClient=connection;if(epoch!=generation)return;connection.connect();
            if(!connection.supports("notifications")||epoch!=generation)return;
            java.util.Map<String,Object> response=StrictJson.object(connection.request("notifications",StrictJson.map("after",session.lastAlert())).get("data"));
            Object alerts=response.get("alerts");long previous=session.lastAlert(),last=previous;
            if(alerts instanceof java.util.List){if(((java.util.List<?>)alerts).size()>32)throw new Sx4.Error("size");
                for(Object item:(java.util.List<?>)alerts){java.util.Map<String,Object> a=StrictJson.object(item);long id=((Number)a.get("id")).longValue();last=Math.max(last,id);
                    if(epoch==generation&&previous!=0&&id>previous)Notifier.post(context,id,String.valueOf(a.getOrDefault("level","info")),String.valueOf(a.getOrDefault("title","Base")),String.valueOf(a.getOrDefault("text","")));
                }
            }
            if(response.get("last") instanceof Number)last=Math.max(last,((Number)response.get("last")).longValue());
            if(epoch==generation)session.lastAlert(last);
        }catch(Sx4.Error e){if(!e.code.equals("unsupported")&&epoch==generation)session.setBlocked(true);}
        catch(IOException ignored){/* next periodic job reconnects; no command retries */}
        finally{jobClient=null;}
    }

    private static String fetch(Session session) throws IOException {
        return PinnedTls.getText(session.baseUrl() + "api/alerts?after=" + session.lastAlert() + "&wait=0", session.fingerprint(), session.token(), 8000);
    }
}
