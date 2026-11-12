<#
 share_publish.ps1 — 메인 저장소의 커밋을 "공개 공유 저장소"에 스냅샷으로 올리고,
                     공유 폴더의 APK를 GitHub Release 자산으로 올린다.

 사용 예 (PowerShell, gh 로그인 된 상태):
   .\tools\share_publish.ps1 -Owner adminhroov -Tag A0.3.1-vc56_B0.4.0-vc63 -ShareDir "$env:USERPROFILE\Desktop\SouvenirHunt_공유_20261029"
   .\tools\share_publish.ps1 -Owner adminhroov -SourceRef a-line -ShareBranch a-line   # 다른 갈래(A 원본)를 공유 저장소의 같은 이름 갈래로(체크아웃 없이 git archive)
   .\tools\share_publish.ps1 -Owner adminhroov -NoPush          # 커밋까지만 만들어 보고 push/release 생략(점검용)

 하는 일:
   1) 메인 저장소 작업 트리가 깨끗한지 확인(SourceRef가 HEAD일 때)
   2) 공유 저장소를 %LOCALAPPDATA%\SouvenirHunt_<Repo> 에 복제/갱신
   3) 공유 저장소 내용을 비우고 SourceRef의 "추적 파일만" git archive 로 풀어 넣음 (이력 없음, 무시 파일 없음)
      → 내부 문서(-Exclude, 기본 docs/·beampro_last_ip.txt)는 뺌
   4) 메인 작업 트리의 CHANGES.md(공개용 변경 내용)를 넣고 루트 README.md 생성 → 커밋 → push
   5) -Tag 가 있으면 ShareDir 의 *.apk 와 그 APK들로 새로 만든 SHA256SUMS.txt 를 Release 자산으로 업로드 (같은 태그가 있으면 덮어씀)
   (-WithDocs 를 주면 예전처럼 ShareDir 문서를 share/ 에 복사 — 공개 저장소에는 쓰지 말 것)

 주의: 공유 저장소는 공개다. 경품 토큰(prizeToken)은 기기 config.txt 에만 있고 저장소에는 없다. 토큰 값 모양이 보이면 중단한다.
#>
param(
  [Parameter(Mandatory = $true)][string]$Owner,
  [string]$Repo = "souvenirhunt-public",
  [string]$MainRepoName = "souvenirhunt",
  [string]$Tag,
  [string]$ShareDir,
  [string]$Notes,
  [string]$SourceRef = "HEAD",     # 스냅샷으로 올릴 메인 저장소 커밋/갈래(기본: 지금 체크아웃된 HEAD)
  [string]$ShareBranch = "main",   # 공유 저장소에서 받을 갈래
  [string[]]$Exclude = @("docs", "tools/beampro_last_ip.txt"),   # 공개하지 않을 경로(내부 문서·사내 IP)
  [switch]$WithDocs,
  [switch]$NoPush
)
$ErrorActionPreference = "Stop"

function Invoke-Checked([string]$Label, [scriptblock]$Block) {
  # git/gh는 진행 메시지를 stderr로 낸다 — 호출 쪽이 2>&1로 묶어도 Stop으로 멈추지 않게 이 구간만 Continue, 성공 여부는 종료 코드로 판단
  $prev = $ErrorActionPreference; $ErrorActionPreference = "Continue"
  try { & $Block 2>&1 | ForEach-Object { "$_" } } finally { $ErrorActionPreference = $prev }
  if ($LASTEXITCODE -ne 0) { throw "$Label failed (exit $LASTEXITCODE)" }
}

$gh = "C:\Program Files\GitHub CLI\gh.exe"
if (-not (Test-Path $gh)) { $gh = "gh" }
$tar = Join-Path $env:SystemRoot "System32\tar.exe"
if (-not (Test-Path $tar)) { throw "tar.exe not found (Windows 10 1803+ required)" }
$utf8 = New-Object System.Text.UTF8Encoding $false

$main = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Write-Host "[share] main repo: $main -> $Owner/$Repo ($ShareBranch)"

# 0) gh 로그인 확인
& $gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) { throw "gh is not logged in. Run: gh auth login -h github.com -p https -w" }

# 1) 메인 작업 트리 확인
if ($SourceRef -eq "HEAD") {
  $dirty = git -C $main status --porcelain
  if ($dirty) { throw "Main repo working tree is not clean. Commit first." }
}
$head = git -C $main rev-parse --short "$SourceRef^{commit}"
if ($LASTEXITCODE -ne 0 -or -not $head) { throw "SourceRef not found: $SourceRef" }
$head = $head.Trim()
$branch = if ($SourceRef -eq "HEAD") { (git -C $main rev-parse --abbrev-ref HEAD).Trim() } else { $SourceRef }
$date   = Get-Date -Format "yyyy-MM-dd HH:mm"
Write-Host "[share] source: $Owner/$MainRepoName $branch@$head"

# 2) 공유 저장소 복제/갱신
$work = Join-Path $env:LOCALAPPDATA "SouvenirHunt_$Repo"
if (-not (Test-Path (Join-Path $work ".git"))) {
  Invoke-Checked "gh repo clone" { & $gh repo clone "$Owner/$Repo" $work }
}
Invoke-Checked "git fetch" { git -C $work fetch origin }
$remoteBranch = git -C $work ls-remote --heads origin $ShareBranch
if ($remoteBranch) {
  Invoke-Checked "git checkout" { git -C $work checkout -q -B $ShareBranch "origin/$ShareBranch" }
} else {
  # 새 갈래(빈 저장소의 첫 main 포함)는 이력 없이 시작(다른 갈래 이력과 섞지 않음)
  Invoke-Checked "git checkout" { git -C $work checkout -q --orphan $ShareBranch }
}

# 커밋 작성자: 메인 저장소의 설정을 그대로 사용 (전역 설정이 없어도 커밋되게)
$uName  = git -C $main config user.name
$uEmail = git -C $main config user.email
if ($uName)  { git -C $work config user.name  "$uName" }
if ($uEmail) { git -C $work config user.email "$uEmail" }

# 3) 내용 비우고 스냅샷 풀기 → 비공개 경로 제거
Get-ChildItem -LiteralPath $work -Force | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force
$snap = Join-Path $env:TEMP "souvenirhunt_snapshot.tar"
Invoke-Checked "git archive" { git -C $main archive --format=tar -o $snap "$SourceRef^{commit}" }
Invoke-Checked "tar extract" { & $tar -xf $snap -C $work }
Remove-Item -LiteralPath $snap -Force
foreach ($x in $Exclude) {
  $p = Join-Path $work $x
  if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force; Write-Host "[share] excluded: $x" }
}

# 4) 공개용 변경 내용 + (선택) 공유 문서
$changes = Join-Path $main "CHANGES.md"
if (Test-Path -LiteralPath $changes) { Copy-Item -LiteralPath $changes -Destination (Join-Path $work "CHANGES.md") -Force }
if ($WithDocs -and $ShareDir) {
  $dest = Join-Path $work "share"
  New-Item -ItemType Directory -Force -Path $dest | Out-Null
  Get-ChildItem -LiteralPath $ShareDir -File | Where-Object { $_.Extension -in ".md", ".html", ".txt" } | Copy-Item -Destination $dest -Force
  Write-Host "[share] docs copied to share/"
}
# 토큰 값이 섞여 있으면 중단 (실제 값 모양: 16자 이상 영숫자)
$leak = Get-ChildItem -LiteralPath $work -Recurse -File -Force |
  Where-Object { $_.FullName -notlike "*\.git\*" -and $_.Extension -in ".md", ".html", ".txt", ".cs", ".json", ".java", ".ps1", ".py" } |
  Select-String -Pattern 'prizeToken\s*[=:]\s*"?[A-Za-z0-9_\-]{16,}' -List
if ($leak) { throw "prizeToken value found: " + (($leak | ForEach-Object { $_.Path }) -join ", ") }
$fileCount = (Get-ChildItem -LiteralPath $work -Recurse -File -Force | Where-Object { $_.FullName -notlike "*\.git\*" }).Count
Write-Host "[share] snapshot files: $fileCount"

$app = if ($ShareBranch -eq "a-line") { "A 앱(SouvenirHunt, com.hroov.souvenirhunt)" } else { "B 앱(B SouvenirHunt, com.hroov.souvenirhunt.b)" }
$readme = @"
# SouvenirHunt

XREAL One Pro 안경 + Beam Pro로 하는 XR 인형뽑기 게임입니다. 이 갈래(``$ShareBranch``)는 **$app** 의 Unity 프로젝트입니다.

- 변경 내용: [CHANGES.md](CHANGES.md)
- 설치 파일(APK): [Releases](https://github.com/$Owner/$Repo/releases) — 게임 APK와 실행 앱(Launcher) APK를 둘 다 설치하고, 실행 앱으로 시작해 첫 화면에서 한국어 / English를 고릅니다. ``SHA256SUMS.txt`` 로 파일을 확인할 수 있습니다.
- 갈래: ``main`` = B 앱, ``a-line`` = A 앱
- 개발 환경: Unity 6000.0.79f1, XREAL SDK 3.1.0(Packages 안에 포함), 대상 기기 XREAL One Pro + Beam Pro(Android 14)
- 경품 서버 연동 토큰은 들어 있지 않습니다.

스냅샷: 원본 ``$branch`` 커밋 ``$head``, $date 게시. 이 저장소는 자동 생성되므로 여기서 직접 고치지 않습니다.
"@
[IO.File]::WriteAllText((Join-Path $work "README.md"), $readme, $utf8)

Invoke-Checked "git add" { git -C $work add -A }
$pending = git -C $work status --porcelain
if (-not $pending) {
  Write-Host "[share] no changes since last snapshot"
} else {
  Invoke-Checked "git commit" { git -C $work commit -q -m "snapshot $date : $branch@$head" }
  Write-Host "[share] committed snapshot"
}
if ($NoPush) { Write-Host "[share] -NoPush: skipped push/release. Work dir: $work"; exit 0 }
Invoke-Checked "git push" { git -C $work push -u origin $ShareBranch }
Write-Host "[share] pushed: https://github.com/$Owner/$Repo/tree/$ShareBranch"

# 5) Release (APK + 그 APK들의 SHA256SUMS)
if ($Tag) {
  if (-not $ShareDir) { throw "-Tag requires -ShareDir (APK source folder)" }
  $apks = @(Get-ChildItem -LiteralPath $ShareDir -File -Filter *.apk)
  if ($apks.Count -eq 0) { throw "No APK found in $ShareDir" }
  $sumDir = Join-Path $env:TEMP "souvenirhunt_release_sums"
  New-Item -ItemType Directory -Force -Path $sumDir | Out-Null
  $sums = Join-Path $sumDir "SHA256SUMS.txt"
  $lines = $apks | ForEach-Object { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLower() + "  " + $_.Name }
  [IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), $utf8)
  $assets = @($apks | ForEach-Object { $_.FullName }) + $sums
  if (-not $Notes) { $Notes = "게시 $date. 변경 내용은 CHANGES.md, 설치는 README.md를 보세요." }
  $existing = @(& $gh release list --repo "$Owner/$Repo" --limit 200 --json tagName --jq ".[].tagName")
  if ($existing -contains $Tag) {
    Invoke-Checked "gh release upload" { & $gh release upload $Tag @assets --repo "$Owner/$Repo" --clobber }
  } else {
    # 설명은 파일로 넘김 — Windows PowerShell 5.1은 큰따옴표가 든 인자를 native exe에 깨뜨려 넘긴다
    $notesFile = Join-Path $env:TEMP "souvenirhunt_release_notes.md"
    [IO.File]::WriteAllText($notesFile, $Notes, $utf8)
    try { Invoke-Checked "gh release create" { & $gh release create $Tag @assets --repo "$Owner/$Repo" --title $Tag --notes-file $notesFile --target $ShareBranch } }
    finally { Remove-Item -LiteralPath $notesFile -Force -ErrorAction SilentlyContinue }
  }
  Remove-Item -LiteralPath $sumDir -Recurse -Force -ErrorAction SilentlyContinue
  Write-Host "[share] release: https://github.com/$Owner/$Repo/releases/tag/$Tag ($($assets.Count) assets)"
}
Write-Host "[share] done"
