#!/usr/bin/env bash
set -euo pipefail
TOOLS_ROOT=/workspace/.zebex-tools
mkdir -p "$TOOLS_ROOT"
download_verified() {
  local archive_path="$1" archive_url="$2" archive_hash="$3"
  curl --fail --show-error --location "$archive_url" --output "$archive_path"
  printf '%s  %s\n' "$archive_hash" "$archive_path" | sha256sum --check
}
if [ ! -x "$TOOLS_ROOT/jdk21/bin/javac" ]; then
  download_verified /tmp/zebex-jdk21.tar.gz https://download.oracle.com/java/21/archive/jdk-21.0.12.1_linux-x64_bin.tar.gz 12f870b21301b42292558a3f872ce543affa2b86cb6458591c78388c41ddb111
  mkdir -p "$TOOLS_ROOT/jdk21"
  tar -xzf /tmp/zebex-jdk21.tar.gz --strip-components=1 -C "$TOOLS_ROOT/jdk21"
fi
if [ ! -x "$TOOLS_ROOT/gradle-8.9/bin/gradle" ]; then
  download_verified /tmp/zebex-gradle.zip https://downloads.gradle.org/distributions/gradle-8.9-bin.zip d725d707bfabd4dfdc958c624003b3c80accc03f7037b5122c4b1d0ef15cecab
  unzip -q /tmp/zebex-gradle.zip -d "$TOOLS_ROOT"
fi
if [ ! -x "$TOOLS_ROOT/android/cmdline-tools/bin/sdkmanager" ]; then
  download_verified /tmp/zebex-android-cli.zip https://dl.google.com/android/repository/commandlinetools-linux-16111833_latest.zip 0877a1d048fe4a24efe2eff536ca4223f7adeb58648bb81909d33c446918cfa8
  mkdir -p "$TOOLS_ROOT/android"
  unzip -q /tmp/zebex-android-cli.zip -d "$TOOLS_ROOT/android"
  chmod +x "$TOOLS_ROOT/android/cmdline-tools/bin/"*
fi
export JAVA_HOME="$TOOLS_ROOT/jdk21"
export ANDROID_HOME="$TOOLS_ROOT/android"
export ANDROID_USER_HOME="$TOOLS_ROOT/android-home"
export GRADLE_USER_HOME="$TOOLS_ROOT/gradle-cache"
export PATH="$JAVA_HOME/bin:$PATH"
mkdir -p "$ANDROID_USER_HOME" "$GRADLE_USER_HOME"
# Use the environment's configured proxy and CA certificates, preserving TLS verification.
python - <<'PY'
import os,pathlib,urllib.parse
proxy=os.environ.get('HTTPS_PROXY') or os.environ.get('https_proxy')
lines=[]
if proxy:
    p=urllib.parse.urlsplit(proxy)
    for kind in ('http','https'):
        lines += [f'systemProp.{kind}.proxyHost={p.hostname}', f'systemProp.{kind}.proxyPort={p.port or 80}']
        if p.username: lines.append(f'systemProp.{kind}.proxyUser={urllib.parse.unquote(p.username)}')
        if p.password: lines.append(f'systemProp.{kind}.proxyPassword={urllib.parse.unquote(p.password)}')
file=pathlib.Path(os.environ['GRADLE_USER_HOME'])/'gradle.properties'
file.write_text('\n'.join(lines)+'\n');file.chmod(0o600)
PY
for ca_path in /usr/local/share/ca-certificates/*.crt; do
  [ -f "$ca_path" ] || continue
  ca_alias="zebex-env-$(basename "$ca_path" .crt)"
  if ! keytool -list -alias "$ca_alias" -keystore "$JAVA_HOME/lib/security/cacerts" -storepass changeit > /dev/null 2>&1; then
    keytool -importcert -noprompt -alias "$ca_alias" -file "$ca_path" -keystore "$JAVA_HOME/lib/security/cacerts" -storepass changeit > /dev/null
  fi
done
printf 'y\n%.0s' {1..100} | "$ANDROID_HOME/cmdline-tools/bin/sdkmanager" --sdk_root="$ANDROID_HOME" --licenses > /tmp/zebex-android-licenses.log 2>&1
"$ANDROID_HOME/cmdline-tools/bin/sdkmanager" --sdk_root="$ANDROID_HOME" 'platforms;android-35' 'build-tools;35.0.0' 'platform-tools' > /tmp/zebex-android-sdk.log 2>&1
SIGNING_ROOT="$TOOLS_ROOT/android-signing"
mkdir -p "$SIGNING_ROOT"
if [ ! -f "$SIGNING_ROOT/password" ]; then
  openssl rand -hex 32 > "$SIGNING_ROOT/password"
  chmod 600 "$SIGNING_ROOT/password"
fi
if [ ! -f "$SIGNING_ROOT/zebex-phone.jks" ]; then
  keytool -genkeypair -keystore "$SIGNING_ROOT/zebex-phone.jks" -storepass:file "$SIGNING_ROOT/password" -keypass:file "$SIGNING_ROOT/password" -alias zebex-phone -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Zebex Phone Independent, O=Zebex Phone, C=PL'
  chmod 600 "$SIGNING_ROOT/zebex-phone.jks"
fi
export ZEBEX_ANDROID_KEYSTORE="$SIGNING_ROOT/zebex-phone.jks"
export ZEBEX_ANDROID_PASSWORD_FILE="$SIGNING_ROOT/password"
cd /workspace/zebex/android
"$TOOLS_ROOT/gradle-8.9/bin/gradle" --no-daemon :app:assembleRelease :app:lintRelease
mkdir -p ../artifacts
cp app/build/outputs/apk/release/app-release.apk ../artifacts/Zebex-Phone-Android-v0.4.2.apk
"$ANDROID_HOME/build-tools/35.0.0/apksigner" verify ../artifacts/Zebex-Phone-Android-v0.4.2.apk
