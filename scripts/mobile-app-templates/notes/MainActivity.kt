package com.sentinelx.generated

import android.app.Activity
import android.os.Bundle
import android.text.InputFilter
import android.text.InputType
import android.view.Gravity
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast

class MainActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val preferences = getSharedPreferences("local_notes", MODE_PRIVATE)
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(24, 24, 24, 24)
        }
        root.addView(TextView(this).apply {
            text = "Notatki zapisują się tylko na tym urządzeniu."
            textSize = 18f
        })
        val editor = EditText(this).apply {
            hint = "Wpisz notatkę"
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_FLAG_CAP_SENTENCES
            gravity = Gravity.TOP or Gravity.START
            minLines = 8
            filters = arrayOf(InputFilter.LengthFilter(10_000))
            setText(preferences.getString("body", "").orEmpty().take(10_000))
        }
        root.addView(editor, LinearLayout.LayoutParams(-1, 0, 1f))
        root.addView(Button(this).apply {
            text = "Zapisz notatkę"
            setOnClickListener {
                preferences.edit().putString("body", editor.text.toString()).apply()
                Toast.makeText(this@MainActivity, "Zapisano lokalnie", Toast.LENGTH_SHORT).show()
            }
        })
        setContentView(root)
    }
}
