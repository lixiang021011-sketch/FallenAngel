# 「删掉 generated 后重建」验收：先备份，再从 authored 重算，最后逐份比哈希。
#
# 用法（在 mgmcs_validator 目录下）：
#   powershell -ExecutionPolicy Bypass -File tools\docs\rebuild_and_verify.ps1
#
# 顺序就是 README 里那条链。第 0 步（from_listening_ai）与第 1 步的机器网格都在这条链里；
# madmom 一首约 20–35 秒，30 首大约 15–20 分钟，所以它会跑很久。

$ErrorActionPreference = "Continue"
$env:PYTHONIOENCODING = "utf-8"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)      # mgmcs_validator
Set-Location $root

$docs = Join-Path $root "static\docs"
$backup = "F:\vibe transcription\_mgmcs_index\backup_docs_before_rebuild"
$phone = "F:\vibe transcription\runs\phone_listening3\listening"
# 必须是 merge_phone_taps.py 读的那个目录（它顶部写死的 PHONE_DOCS），
# 否则第 3 步会拿旧的会话文档去并，"从零重建"就是假的。
$tmp = "F:\vibe transcription\runs\phone_docs"
$bp = "D:\fa_tools\bp_venv\Scripts\python.exe"
$py = "C:\Python314\python.exe"

function Hash-Docs($dir) {
    Get-ChildItem $dir -Filter "*.mgmcs.json" | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16), $_.Name
    }
}

Write-Output "0/5 备份现文档 → $backup"
New-Item -ItemType Directory -Force -Path $backup | Out-Null
Copy-Item "$docs\*.mgmcs.json" $backup -Force
$before = Hash-Docs $docs
$before | Out-File -Encoding utf8 (Join-Path $backup "hashes_before.txt")
Write-Output ("     现文档 {0} 份" -f $before.Count)

Write-Output "1/5 删掉 generated（只删文档，不动 coordinate_map 与 audio）"
Get-ChildItem $docs -Filter "*.mgmcs.json" | Remove-Item -Force

Write-Output "2/5 手机会话 → 验证器输入（一会话一份）"
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
& $bp tools\from_listening_ai.py $phone --out $tmp | Out-File -Encoding utf8 "$env:TEMP\rebuild_step2.log"

Write-Output "3/5 每首歌并机器网格（慢，15–20 分钟）"
& $py tools\docs\merge_phone_taps.py | Out-File -Encoding utf8 "$env:TEMP\rebuild_step3.log"

Write-Output "4/5 未吸附变体 + health 标注 + 歌单页"
& $py tools\docs\un_snap.py | Out-File -Encoding utf8 "$env:TEMP\rebuild_step4a.log"
& $py tools\docs\mark_health.py | Out-File -Encoding utf8 "$env:TEMP\rebuild_step4b.log"
& $py tools\docs\build_song_index.py | Out-File -Encoding utf8 "$env:TEMP\rebuild_step4c.log"

Write-Output "5/5 比对"
$after = Hash-Docs $docs
$after | Out-File -Encoding utf8 (Join-Path $backup "hashes_after.txt")
$diff = Compare-Object $before $after
if (-not $diff) {
    Write-Output ("     OK：重建后 {0} 份，逐份哈希与重建前完全一致" -f $after.Count)
} else {
    Write-Output "     有差异（前 → 后）："
    $diff | ForEach-Object { "       {0} {1}" -f $_.SideIndicator, $_.InputObject }
}
