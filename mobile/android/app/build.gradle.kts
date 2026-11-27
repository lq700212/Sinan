plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

android {
    namespace = "com.sinan.sinan_mobile"

    // SDK 口径显式锁定，不跟随 flutter.* 默认值漂移（Flutter 升级改默认值时不至于
    // 悄悄抬高最低版本，把现场旧手持机踢出安装范围）：
    // - compileSdk 36 = Android 16，与 Flutter 3.50 引擎的编译目标一致；
    // - targetSdk 36 = 按 Android 16 行为适配（edge-to-edge、16 KB 页等），
    //   这是"适配到哪一版"，不是"最高只能装到哪一版"；
    // - minSdk 24 = Android 7.0，Flutter 3.50 引擎的最低要求，覆盖主流现场手机。
    // Android 没有 maxSdkVersion（<uses-sdk android:maxSdkVersion> 早已废弃且被 Play 忽略），
    // 装机上限由设备厂商的 Android 版本决定，App 侧只能声明下限与适配目标。
    compileSdk = 36
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        // TODO: Specify your own unique Application ID (https://developer.android.com/studio/build/application-id.html).
        applicationId = "com.sinan.sinan_mobile"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = 24
        targetSdk = 36
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    // 现场端只提供中文界面（英文仅作 Material 组件兜底），
    // 但 AndroidX / image_picker 等依赖自带几十种语言的字符串资源。
    // 裁剪到 zh + en，release 包里不再携带用不到的语言，减小 APK。
    // AGP 9 的公开 DSL 是 androidResources.localeFilters（旧 resourceConfigurations 已废弃）。
    androidResources {
        localeFilters += listOf("zh", "en")
    }

    packaging {
        // Flutter 默认把 libflutter.so / libapp.so 以“未压缩”方式存进 APK
        // （启动时直接 mmap，省一次解压）。现场端主要靠 U 盘/微信分发 APK，
        // 传输体积比首次启动快几十毫秒更重要，因此改为压缩存储：
        // APK 内 .so 压掉约一半，安装时由系统解压，运行时行为不变。
        jniLibs {
            useLegacyPackaging = true
        }
    }

    buildTypes {
        release {
            // TODO: Add your own signing config for the release build.
            // Signing with the debug keys for now, so `flutter run --release` works.
            signingConfig = signingConfigs.getByName("debug")
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}
