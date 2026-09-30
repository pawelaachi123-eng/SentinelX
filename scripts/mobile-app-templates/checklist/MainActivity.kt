package com.sentinelx.generated

import android.app.Activity
import android.os.Bundle
import android.view.ViewGroup
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Toast
import org.json.JSONArray
import org.json.JSONObject

class MainActivity : Activity() {
    private data class Entry(val title: String, var done: Boolean)
    private val entries = mutableListOf<Entry>()
    private lateinit var rows: LinearLayout
    private lateinit var input: EditText

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        loadEntries()
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(20, 20, 20, 20)
        }
        input = EditText(this).apply { hint = "Nowe zadanie" }
        root.addView(input)
        root.addView(Button(this).apply {
            text = "Dodaj"
            setOnClickListener {
                val title = input.text.toString().trim()
                if (title.isEmpty()) {
                    Toast.makeText(this@MainActivity, "Wpisz treść zadania", Toast.LENGTH_SHORT).show()
                } else if (title.length > 120) {
                    Toast.makeText(this@MainActivity, "Maksymalnie 120 znaków", Toast.LENGTH_SHORT).show()
                } else if (entries.size >= 100) {
                    Toast.makeText(this@MainActivity, "Lista jest pełna (maksymalnie 100 zadań)", Toast.LENGTH_SHORT).show()
                } else {
                    entries.add(Entry(title, false))
                    input.text.clear()
                    saveAndRender()
                }
            }
        })
        rows = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(ScrollView(this).apply { addView(rows) }, LinearLayout.LayoutParams(-1, 0, 1f))
        root.addView(Button(this).apply {
            text = "Usuń ukończone"
            setOnClickListener {
                entries.removeAll { it.done }
                saveAndRender()
            }
        })
        setContentView(root)
        render()
    }

    private fun loadEntries() {
        val raw = getSharedPreferences("local_checklist", MODE_PRIVATE).getString("entries", "[]") ?: "[]"
        try {
            val data = JSONArray(raw)
            for (index in 0 until minOf(data.length(), 100)) {
                val row = data.optJSONObject(index) ?: continue
                val title = row.optString("title").take(120)
                if (title.isNotBlank()) entries.add(Entry(title, row.optBoolean("done")))
            }
        } catch (_: Exception) {
            entries.clear()
        }
    }

    private fun saveAndRender() {
        val data = JSONArray()
        entries.take(100).forEach { entry ->
            data.put(JSONObject().put("title", entry.title).put("done", entry.done))
        }
        getSharedPreferences("local_checklist", MODE_PRIVATE).edit().putString("entries", data.toString()).apply()
        render()
    }

    private fun render() {
        if (!::rows.isInitialized) return
        rows.removeAllViews()
        entries.forEachIndexed { index, entry ->
            val row = CheckBox(this).apply {
                text = entry.title
                isChecked = entry.done
                setOnCheckedChangeListener { _, checked ->
                    entries.getOrNull(index)?.done = checked
                    saveAndRender()
                }
            }
            rows.addView(row, ViewGroup.LayoutParams(-1, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
    }
}
