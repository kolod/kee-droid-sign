import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
}

// Release signing key: CI writes keystore.properties from the repository secrets exported by
// KeeDroidSign; both it and the keystore are git-ignored.
val keystoreProperties = Properties().apply {
    val propsFile = file("keystore.properties")
    if (propsFile.exists()) propsFile.inputStream().use { load(it) }
}
val hasReleaseSigning = keystoreProperties.containsKey("storeFile")

android {
    namespace = "io.github.kolod.keedroidsign.sample"
    compileSdk = 37

    defaultConfig {
        applicationId = "io.github.kolod.keedroidsign.sample"
        minSdk = 26
        targetSdk = 37
        // CI passes -PappVersionCode/-PappVersionName; local builds fall back to these.
        versionCode = (findProperty("appVersionCode") as String?)?.toIntOrNull() ?: 1
        versionName = findProperty("appVersionName") as String? ?: "1.0"
    }

    if (hasReleaseSigning) {
        signingConfigs {
            create("release") {
                storeFile = file(keystoreProperties.getProperty("storeFile"))
                storePassword = keystoreProperties.getProperty("storePassword")
                keyAlias = keystoreProperties.getProperty("keyAlias")
                keyPassword = keystoreProperties.getProperty("keyPassword")
            }
        }
    }

    buildTypes {
        release {
            // Deliberately no fallback to the debug key: this app exists to prove that the
            // exported secrets sign the build, so signing with any other key must fail loudly.
            signingConfig = signingConfigs.findByName("release")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

val verifyReleaseSigning by tasks.registering {
    description = "Fails the release build when keystore.properties is missing."
    val configured = hasReleaseSigning
    doLast {
        if (!configured) {
            throw GradleException("Release signing is not configured: keystore.properties not found")
        }
    }
}

tasks.matching { it.name == "packageRelease" || it.name == "assembleRelease" }.configureEach {
    dependsOn(verifyReleaseSigning)
}
