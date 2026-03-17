$main = 'Streamer/MainWindow.xaml'
if (-not (Test-Path $main)) { Write-Error "MainWindow.xaml not found at $main"; exit 1 }
$content = Get-Content $main -Raw
# extract StaticResource keys like {StaticResource BgCard}
$refs = [regex]::Matches($content, '\{StaticResource\s+([A-Za-z0-9_-]+)\}') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
Write-Output "StaticResource keys referenced in MainWindow.xaml:"
$refs | ForEach-Object { Write-Output " - $_" }

$missing = @()
foreach ($r in $refs) {
    $found1 = Select-String -Path .\* -Pattern "x:Key\s*=\s*`"$r`"" -SimpleMatch -Quiet
    $found2 = Select-String -Path .\* -Pattern "x:Key\s*=\s*'$r'" -SimpleMatch -Quiet
    if (-not ($found1 -or $found2)) { $missing += $r }
}

if ($missing.Count -eq 0) {
    Write-Output "All referenced keys appear defined in the repo."
} else {
    Write-Output "Missing resource keys (referenced but not found):"
    $missing | ForEach-Object { Write-Output " - $_" }
    $missing | Out-File missing-resources.txt -Encoding utf8
    Write-Output "Saved missing list to missing-resources.txt"
}