dotnet clean
$log = dotnet build 2>&1
$log | Out-File build.log -Encoding utf8
$errors = $log | Where-Object { $_ -match '\berror\b' -or $_ -match '\bwarning\b' }
$errors | Out-File build-errors.txt -Encoding utf8
