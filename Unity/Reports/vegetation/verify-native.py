"""One-off ground-cover acceptance run using the game's existing opt-in debug bridge."""
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import time

root = Path(__file__).resolve().parents[3]
out = Path(__file__).resolve().parent
control = out / 'native-debug'
control.mkdir(exist_ok=True)
exe = root / 'Unity/OpenTerritory/Builds/WindowsRelease/BaiaDoSol.exe'
assembly = exe.parent / 'BaiaDoSol_Data/Managed/Assembly-CSharp.dll'
report = {'assemblySha256': hashlib.sha256(assembly.read_bytes()).hexdigest().upper(), 'checks': []}
seq = int(time.time())
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
process = subprocess.Popen([str(exe), '--city-debug', str(control), '-screen-fullscreen', '0',
    '-screen-width', '1440', '-screen-height', '900', '-logFile', str(out / 'native-debug.log')],
    startupinfo=startup, creationflags=subprocess.CREATE_NO_WINDOW)

def command(op, **kwargs):
    global seq
    seq += 1
    temp = control / 'command.tmp'
    temp.write_text(json.dumps(dict(seq=seq, op=op, **kwargs)), encoding='utf-8')
    os.replace(temp, control / 'command.json')
    deadline = time.monotonic() + 40
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f'Player exited: {process.returncode}')
        try:
            state = json.loads((control / 'state.json').read_text())
            if state['seq'] == seq:
                return state
        except (OSError, ValueError):
            pass
        time.sleep(.1)
    raise TimeoutError(f'No response to {op}')

def point(x, y, z):
    return dict(x=x, y=y, z=z)

try:
    initial = command('state')
    command('weather', weather='clear')
    for name, x, z in [('grass', 217.371384, 379.3834), ('shrub', 166.811462, 374.375244)]:
        command('warp', point=point(x, 9.075, z-3), yaw=0)
        command('camera', follow=False, point=point(x, 12, z-9), lookAt=point(x, 10.3, z+3))
        time.sleep(.7)
        start = command('state')
        command('input', moveY=1, seconds=2)
        time.sleep(2.5)
        end = command('state')
        distance = math.hypot(end['position']['x']-start['position']['x'], end['position']['z']-start['position']['z'])
        report['checks'].append(dict(name='walk-through-'+name, passed=distance>3.5 and end['grounded'] and end['errors']==0,
            distance=distance, grounded=end['grounded'], start=start['position'], end=end['position'], fps=end['fps'], errors=end['errors']))
    paused = command('pause', pause=True)
    time.sleep(.8)
    still = command('state')
    resumed = command('pause', pause=False)
    report['checks'].append(dict(name='settings-pause-and-resume', passed=paused['paused'] and still['clock']==paused['clock'] and not resumed['paused']))
    command('camera', follow=False, point=point(174, 12, 360), lookAt=point(166, 10, 375))
    # Hidden native windows do not support ScreenCapture; visual captures are reviewed separately.
    report['passed'] = all(c['passed'] for c in report['checks'])
    report['assemblyUnchanged'] = report['assemblySha256'] == hashlib.sha256(assembly.read_bytes()).hexdigest().upper()
finally:
    try:
        command('quit')
    except (RuntimeError, TimeoutError):
        pass
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.terminate()
    (out/'ground-cover-native-check.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report), flush=True)
