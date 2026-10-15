#!/usr/bin/env python3
"""Development test only: Android JVM client -> pinned TLS -> real serial PTY -> SUZ helper.
The Windows EXE and Android APK do not require Python.
"""
import os
import pathlib
import pty
import select
import subprocess
import tempfile
import threading
import tty

repo = pathlib.Path(__file__).resolve().parent.parent
tools = pathlib.Path(os.environ.get('ZEBEX_TOOLS', '/workspace/.zebex-tools'))
dotnet = tools / 'dotnet/dotnet'
env = os.environ.copy()
env.update(DOTNET_ROOT=str(tools / 'dotnet'), DOTNET_CLI_HOME=str(tools / 'dotnet-home'),
           NUGET_PACKAGES=str(tools / 'nuget'), JAVA_HOME=str(tools / 'jdk21'),
           ANDROID_HOME=str(tools / 'android'), ANDROID_USER_HOME=str(tools / 'android-home'),
           GRADLE_USER_HOME=str(tools / 'gradle-cache'))
for project in ('tests/Phone.Checks', 'src/Zebex.SuzBridge'):
    subprocess.run([str(dotnet), 'build', project, '-c', 'Release'], cwd=repo, env=env, check=True)

with tempfile.TemporaryDirectory(prefix='zebex-android-integration-') as directory:
    folder = pathlib.Path(directory)
    masters, slaves, names = [], [], []
    for _ in range(2):
        master, slave = pty.openpty()
        tty.setraw(slave)
        os.set_blocking(master, False)
        masters.append(master)
        slaves.append(slave)
        names.append(os.ttyname(slave))
    stopping = threading.Event()
    def relay():
        while not stopping.is_set():
            for fd in select.select(masters, [], [], 0.1)[0]:
                try:
                    data = os.read(fd, 65536)
                    if data:
                        os.write(masters[1 - masters.index(fd)], data)
                except (BlockingIOError, OSError):
                    pass
    thread = threading.Thread(target=relay, daemon=True)
    thread.start()
    fixture = subprocess.Popen([str(dotnet), str(repo/'tests/Phone.Checks/bin/Release/net8.0/Phone.Checks.dll'),
                                '--fixture', str(folder/'pair.json'), names[0]], env=env,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    try:
        assert fixture.stdout.readline().strip() == 'FIXTURE_READY', 'TLS/serial fixture failed to start'
        env.update(ZEBEX_PHONE_FIXTURE=str(folder/'pair.json'), ZEBEX_TEST_DOTNET=str(dotnet),
                   ZEBEX_BRIDGE_TEST_DLL=str(repo/'src/Zebex.SuzBridge/bin/Release/net8.0/ZebexSuzBridge.dll'),
                   ZEBEX_BRIDGE_TEST_PORT=names[1], ZEBEX_TEST_DOCUMENT=str(folder/'document_Zebex.txt'))
        subprocess.run([str(tools/'gradle-8.9/bin/gradle'), '--no-daemon', ':app:testDebugUnitTest'],
                       cwd=repo/'android', env=env, check=True, timeout=240)
        assert '000123\t0.3' in (folder/'document_Zebex.txt').read_text(encoding='utf-8-sig')
        print('ANDROID_TLS_SERIAL_INTEGRATION_OK', flush=True)
    finally:
        stopping.set()
        fixture.terminate()
        try:
            fixture.wait(timeout=3)
        except subprocess.TimeoutExpired:
            fixture.kill()
        for fd in masters + slaves:
            os.close(fd)
