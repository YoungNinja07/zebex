# Android — Zebex Phone

Native Java app, min SDK24 (Android7), target/compile SDK35, Android Gradle Plugin8.7.3, Gradle8.9, JDK17 or newer. ZXing Android Embedded4.3.0 decodes camera images locally.

From an Android development environment run `gradle :app:assembleDebug`. Set ANDROID_HOME to an SDK containing platforms;android-35 and build-tools;35.0.0. `gradle :app:testDebugUnitTest` runs Robolectric tests; the TLS/PTY case is skipped unless the fixture is configured.

In this cloud workspace run `bash setup-android.sh` from the repository root. It verifies pinned archives, uses the configured proxy and trusted environment CAs without disabling TLS, installs the SDK, builds the release APK and runs lint. Artifact: `artifacts/Zebex-Phone-Android-v0.4.0.apk`.

Release signing reads ZEBEX_ANDROID_KEYSTORE and ZEBEX_ANDROID_PASSWORD_FILE. The script keeps them outside the checkout under /workspace/.zebex-tools/android-signing with restricted permissions. Preserve these private files for compatible updates; do not commit or publish them. A fresh signing key cannot update an APK signed by the previous key. The script reuses the existing key and password.

For the complete Linux test run `python tests/phone-integration.py` from the root after preparing .NET and Android tools. It opens two PTYs, starts the real .NET TLS/serial fixture and runs all10 Android tests, including the actual Link client, certificate pinning, catalog, SQLite scans, duplicate upload and the SUZ helper process. Python is only a development harness; neither product requires it.

Protocol: TLS at TCP18790; POST /v1/pair with expiring QR code and client UUID returns a random bearer token. All subsequent requests require the token. POST /v1/documents accepts an immutable upload ID and decimal quantities as strings. Identical retries return the original receipt. GET /v1/documents/{id} is restricted to that client. GET /v1/products uses offset/limit and revision to reject mixed pages. The APK pins the self-signed leaf SHA256 from QR and does not disable global validation.

SQLite keeps product staging separate from the visible catalog. Interrupted downloads preserve existing products. Uploaded documents remain frozen with their IDs and scan rows. Cloud and device-transfer backups are disabled to keep credentials local. Robolectric does not exercise physical camera hardware or installation on a phone.
