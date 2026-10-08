package io.github.kolod.keedroidsign.sample

import android.app.Activity
import android.os.Bundle
import android.view.Gravity
import android.widget.TextView

/** The only screen of the signing test fixture. */
class MainActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(
            TextView(this).apply {
                text = getString(R.string.hello)
                textSize = 24f
                gravity = Gravity.CENTER
            },
        )
    }
}
