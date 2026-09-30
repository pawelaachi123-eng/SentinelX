plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.sentinelx.generated"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.sentinelx.generated.__TEMPLATE__"
        minSdk = 23
        targetSdk = 35
        versionCode = 1
        versionName = "1.0"
    }
}
