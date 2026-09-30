package com.sentinelx.phone

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews

/** One-tap shortcuts open the lightweight controller; they do not run an unreviewed background service. */
class SentinelWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, ids: IntArray) {
        ids.forEach { id ->
            val views = RemoteViews(context.packageName, R.layout.sentinel_widget)
            views.setOnClickPendingIntent(R.id.widget_pc_on, action(context, id, "PC_WAKE", null, 1))
            views.setOnClickPendingIntent(R.id.widget_pc_status, action(context, id, "PC_STATUS", null, 2))
            views.setOnClickPendingIntent(R.id.widget_tv_on, action(context, id, "TV_POWER_ON", "tv", 3))
            views.setOnClickPendingIntent(R.id.widget_tv_mute, action(context, id, "TV_MUTE", "tv", 4))
            manager.updateAppWidget(id, views)
        }
    }

    private fun action(context: Context, widgetId: Int, skill: String, target: String?, slot: Int): PendingIntent {
        val intent = Intent(context, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
            .putExtra(MainActivity.EXTRA_WIDGET_INTENT, skill)
            .putExtra(MainActivity.EXTRA_WIDGET_TARGET, target)
        return PendingIntent.getActivity(context, widgetId * 10 + slot, intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
    }
}
