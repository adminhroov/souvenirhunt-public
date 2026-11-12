"""Build the Unity Android bridge and ordinary Beam Pro launcher using Unity's SDK.

Uses the same local debug certificate as the Unity APK for the signature permission.
For release signing pass --keystore and set SOUVENIR_KS_PASS / SOUVENIR_KEY_PASS;
never commit a release key or password. Rebuild both APKs with the same certificate.

10-29 버전 B(별도 패키지 — A와 Beam Pro에 동시 설치): --variant B 이면 실행 앱 com.hroov.souvenirhunt.launcher.b
("B SouvenirHunt 실행", 대상 게임 com.hroov.souvenirhunt.b) -> Builds/SouvenirHunt_Launcher_B_1.0.apk.
브리지 AAR은 A·B 공용(매니페스트의 ${applicationId}로 권한·taskAffinity가 게임 패키지마다 따로 만들어진다).
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
parser.add_argument('--variant', choices=['A', 'B'], default='A',
                    help='A = com.hroov.souvenirhunt.launcher (기본) / B = com.hroov.souvenirhunt.launcher.b (버전 B 게임 실행)')
args = parser.parse_args()
# 10-29 변형별 실행 앱 패키지·표시 이름·대상 게임 패키지·출력 파일(소스 매니페스트는 A 값 — B는 글자 바꿔 씀)
GAME_A, LAUNCHER_A, LABEL_A = 'com.hroov.souvenirhunt', 'com.hroov.souvenirhunt.launcher', 'SouvenirHunt 실행'
# 11-10 출력 파일 이름의 버전 = 실행 앱 매니페스트 versionName(언어 선택 화면 추가로 1.0 → 1.1)
import re as _re
LAUNCHER_VERSION = _re.search(r'android:versionName="([^"]+)"', (SRC/'launcher/AndroidManifest.xml').read_text(encoding='utf-8')).group(1)
if args.variant == 'B':
    game, launcher_pkg, label, out_name = GAME_A + '.b', LAUNCHER_A + '.b', 'B SouvenirHunt 실행', f'SouvenirHunt_Launcher_B_{LAUNCHER_VERSION}.apk'  # 10-29(2) 표시명 B 접두
else:
    game, launcher_pkg, label, out_name = GAME_A, LAUNCHER_A, LABEL_A, f'SouvenirHunt_Launcher_{LAUNCHER_VERSION}.apk'
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
    # 10-29 감사: 매니페스트 dataExtractionRules가 참조하는 bridge/res(AAR 원본 리소스) + 심볼 목록 R.txt
    symbols=[]
    for f in sorted((SRC/'bridge/res').rglob('*')):
        if not f.is_file(): continue
        archive.write(f,'res/'+f.relative_to(SRC/'bridge/res').as_posix())
        symbols.append(f'int {f.parent.name.split("-")[0]} {f.stem} 0x7f{len(symbols)+1:02x}0000')
    archive.writestr('R.txt','\n'.join(symbols)+'\n')
print('Bridge:',plugin)

launcher=classes('launcher')
# 10-29 실행 앱 매니페스트: 패키지·대상 게임 권한·패키지 조회·표시 이름을 변형 값으로(LauncherActivity는 자기 패키지에서 대상 게임을 구한다)
manifest_text=(SRC/'launcher/AndroidManifest.xml').read_text(encoding='utf-8')
for old,new in ((f'package="{LAUNCHER_A}"',f'package="{launcher_pkg}"'),
                (f'"{GAME_A}.permission.CONTROL"',f'"{game}.permission.CONTROL"'),
                (f'<package android:name="{GAME_A}" />',f'<package android:name="{game}" />'),
                (f'android:label="{LABEL_A}"',f'android:label="{label}"')):
    if manifest_text.count(old)!=1: raise RuntimeError(f'launcher manifest: {old!r} not found exactly once')
    manifest_text=manifest_text.replace(old,new)
manifest=work/'AndroidManifest.xml'
manifest.write_text(manifest_text,encoding='utf-8')
resources=work/'resources.zip'
run([bt/'aapt2.exe','compile','--dir',SRC/'launcher/res','-o',resources])
apk=work/'unsigned.apk'
run([bt/'aapt2.exe','link','--manifest',manifest,'-I',android,'-o',apk,resources])
dex=work/'dex';dex.mkdir()
run([java_home/'bin/java.exe','-cp',bt/'lib/d8.jar','com.android.tools.r8.D8','--lib',android,'--min-api','31','--output',dex,launcher])
with ZipFile(apk,'a',ZIP_DEFLATED) as archive:
    for f in dex.glob('*.dex'): archive.write(f,f.name)
aligned=work/'aligned.apk'
run([bt/'zipalign.exe','-f','4',apk,aligned])
out=builds/out_name
env=os.environ.copy()
env.setdefault('SOUVENIR_KS_PASS','android')
env.setdefault('SOUVENIR_KEY_PASS','android')
cmd=[java_home/'bin/java.exe','-jar',bt/'lib/apksigner.jar','sign','--ks',args.keystore,
     '--ks-pass','env:SOUVENIR_KS_PASS','--key-pass','env:SOUVENIR_KEY_PASS','--out',out,aligned]
subprocess.run([str(v) for v in cmd],check=True,env=env)
run([java_home/'bin/java.exe','-jar',bt/'lib/apksigner.jar','verify','--verbose',out])
print('Launcher:',out,f'({launcher_pkg} -> {game}, "{label}")')
