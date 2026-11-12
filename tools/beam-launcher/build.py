"""Build the Unity Android bridge and ordinary Beam Pro launcher using Unity's SDK.

Uses the same local debug certificate as the Unity APK for the signature permission.
For release signing pass --keystore and set SOUVENIR_KS_PASS / SOUVENIR_KEY_PASS;
never commit a release key or password. Rebuild both APKs with the same certificate.
"""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import argparse, os, subprocess, tempfile

# Keep the ASCII Dev junction: aapt2 on Windows cannot read some non-ASCII paths.
ROOT = Path(__file__).absolute().parents[2]
SRC = Path(__file__).absolute().parent
parser = argparse.ArgumentParser()
parser.add_argument('--android-player', default=r'C:\Program Files\Unity\Hub\Editor\6000.0.79f1\Editor\Data\PlaybackEngines\AndroidPlayer')
parser.add_argument('--keystore', default=str(Path.home()/'.android/debug.keystore'))
args = parser.parse_args()
# 11-10 출력 파일 이름의 버전 = 실행 앱 매니페스트 versionName(언어 선택 화면 추가로 1.0 → 1.1)
import re as _re
LAUNCHER_VERSION = _re.search(r'android:versionName="([^"]+)"', (SRC/'launcher/AndroidManifest.xml').read_text(encoding='utf-8')).group(1)
player = Path(args.android_player)
sdk = player/'SDK'
java_home = player/'OpenJDK'
bt = max((sdk/'build-tools').iterdir(), key=lambda p: tuple(map(int,p.name.split('.'))))
android = max((sdk/'platforms').glob('android-*'),key=lambda p:int(p.name.split('-')[-1]))/'android.jar'
work_parent = ROOT/'Temp/beam-launcher'
work_parent.mkdir(parents=True,exist_ok=True)
work = Path(tempfile.mkdtemp(dir=work_parent))
builds = ROOT/'Builds'; builds.mkdir(exist_ok=True)

def run(cmd):
    result = subprocess.run([str(v) for v in cmd],capture_output=True,text=True,encoding='utf-8',errors='replace')
    if result.returncode:
        raise RuntimeError(result.stdout+'\n'+result.stderr)
    if result.stdout.strip(): print(result.stdout.strip())

def classes(group):
    output=work/group; output.mkdir()
    run([java_home/'bin/javac.exe','-encoding','UTF-8','--release','8',
         '-classpath',android,'-d',output,*sorted((SRC/group).glob('*.java'))])
    jar=work/(group+'.jar')
    with ZipFile(jar,'w',ZIP_DEFLATED) as archive:
        for f in output.rglob('*.class'): archive.write(f,f.relative_to(output).as_posix())
    return jar

bridge=classes('bridge')
plugin=ROOT/'Assets/Plugins/Android/SouvenirBeamEntry.aar'
plugin.parent.mkdir(parents=True,exist_ok=True)
with ZipFile(plugin,'w',ZIP_DEFLATED) as archive:
    archive.write(bridge,'classes.jar')
    archive.write(SRC/'bridge/AndroidManifest.xml','AndroidManifest.xml')
print('Bridge:',plugin)

launcher=classes('launcher')
resources=work/'resources.zip'
run([bt/'aapt2.exe','compile','--dir',SRC/'launcher/res','-o',resources])
apk=work/'unsigned.apk'
run([bt/'aapt2.exe','link','--manifest',SRC/'launcher/AndroidManifest.xml','-I',android,'-o',apk,resources])
dex=work/'dex';dex.mkdir()
run([java_home/'bin/java.exe','-cp',bt/'lib/d8.jar','com.android.tools.r8.D8','--lib',android,'--min-api','31','--output',dex,launcher])
with ZipFile(apk,'a',ZIP_DEFLATED) as archive:
    for f in dex.glob('*.dex'): archive.write(f,f.name)
aligned=work/'aligned.apk'
run([bt/'zipalign.exe','-f','4',apk,aligned])
out=builds/f'SouvenirHunt_Launcher_{LAUNCHER_VERSION}.apk'
env=os.environ.copy()
env.setdefault('SOUVENIR_KS_PASS','android')
env.setdefault('SOUVENIR_KEY_PASS','android')
cmd=[java_home/'bin/java.exe','-jar',bt/'lib/apksigner.jar','sign','--ks',args.keystore,
     '--ks-pass','env:SOUVENIR_KS_PASS','--key-pass','env:SOUVENIR_KEY_PASS','--out',out,aligned]
subprocess.run([str(v) for v in cmd],check=True,env=env)
run([java_home/'bin/java.exe','-jar',bt/'lib/apksigner.jar','verify','--verbose',out])
print('Launcher:',out)
