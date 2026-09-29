plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}
android {
    namespace = "pl.sentinelx.companion"
    compileSdk = 35
    defaultConfig {
        applicationId = "pl.sentinelx.companion"
        minSdk = 28
        targetSdk = 35
        versionCode = 1
        versionName = "0.99"
    }
    buildTypes { release { isMinifyEnabled = false } }
    compileOptions { sourceCompatibility = JavaVersion.VERSION_17; targetCompatibility = JavaVersion.VERSION_17 }
    kotlinOptions { jvmTarget = "17" }
}
dependencies { implementation("androidx.core:core-ktx:1.13.1") }
