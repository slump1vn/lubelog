plugins {
    id("com.android.application")
}

// Tên miền HTTPS của web app (xem gradle.properties).
val twaHost: String = providers.gradleProperty("twaHost").get()
val launchUrl = "https://$twaHost/"

// Khoá ký bản phát hành lấy từ biến môi trường (GitHub Actions secrets), không bao giờ nằm trong repo.
val keystorePath: String? = System.getenv("ANDROID_KEYSTORE_PATH")
val hasReleaseKey = !keystorePath.isNullOrBlank() && file(keystorePath).exists()

android {
    namespace = "vn.digifusion.finance"
    compileSdk = 35

    defaultConfig {
        applicationId = "vn.digifusion.finance"
        minSdk = 23
        targetSdk = 35
        versionCode = (System.getenv("GITHUB_RUN_NUMBER") ?: "1").toInt()
        versionName = "0.1.${System.getenv("GITHUB_RUN_NUMBER") ?: "0"}"

        manifestPlaceholders["hostName"] = twaHost
        resValue("string", "launchUrl", launchUrl)
        resValue(
            "string",
            "assetStatements",
            "[{\\\"relation\\\": [\\\"delegate_permission/common.handle_all_urls\\\"], " +
                "\\\"target\\\": {\\\"namespace\\\": \\\"web\\\", \\\"site\\\": \\\"https://$twaHost\\\"}}]"
        )
    }

    signingConfigs {
        if (hasReleaseKey) {
            create("release") {
                storeFile = file(keystorePath!!)
                storePassword = System.getenv("ANDROID_KEYSTORE_PASSWORD")
                keyAlias = System.getenv("ANDROID_KEY_ALIAS")
                keyPassword = System.getenv("ANDROID_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // Chưa có khoá phát hành thì ký bằng khoá debug để vẫn cài thử được.
            signingConfig = if (hasReleaseKey) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }

    buildFeatures {
        resValues = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    // Thư viện chính thức của Google cho Trusted Web Activity (giống mẫu do Bubblewrap tạo).
    implementation("com.google.androidbrowserhelper:androidbrowserhelper:2.5.0")
}
