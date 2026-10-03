package pl.sentinelx.phone;

import android.Manifest;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;

/** Shows the PC's alerts as normal Android notifications. */
final class Notifier {
    static final String CHANNEL_ALERTS = "alerts";
    static final String CHANNEL_INFO = "info";

    private Notifier() { }

    static void ensureChannels(Context context) {
        if (Build.VERSION.SDK_INT < 26) return;
        NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager == null) return;
        NotificationChannel alerts = new NotificationChannel(CHANNEL_ALERTS, "Alerty z komputera", NotificationManager.IMPORTANCE_HIGH);
        alerts.setDescription("Wysokie obciążenie, mało miejsca na dysku i inne rzeczy, które wymagają uwagi.");
        NotificationChannel info = new NotificationChannel(CHANNEL_INFO, "Przypomnienia i informacje", NotificationManager.IMPORTANCE_DEFAULT);
        info.setDescription("Przypomnienia z komputera i informacje o silniku AI.");
        manager.createNotificationChannel(alerts);
        manager.createNotificationChannel(info);
    }

    static void post(Context context, long id, String level, String title, String text) {
        if (Build.VERSION.SDK_INT >= 33 && context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return;
        ensureChannels(context);
        boolean important = "warn".equals(level) || "error".equals(level);
        Intent open = new Intent(context, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        PendingIntent pending = PendingIntent.getActivity(context, 0, open, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(context, important ? CHANNEL_ALERTS : CHANNEL_INFO)
                : new Notification.Builder(context);
        builder.setSmallIcon(R.drawable.ic_notification)
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(new Notification.BigTextStyle().bigText(text))
                .setContentIntent(pending)
                .setAutoCancel(true);
        if (Build.VERSION.SDK_INT < 26) builder.setPriority(important ? Notification.PRIORITY_HIGH : Notification.PRIORITY_DEFAULT);
        NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager != null) manager.notify((int) (id % Integer.MAX_VALUE), builder.build());
    }
}
