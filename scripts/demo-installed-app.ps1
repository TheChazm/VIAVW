param([Parameter(Mandatory)][ValidateSet('Word','VLC','7Zip')][string]$Application,[switch]$Smoke)
$ErrorActionPreference='Stop'
$appName=if($Application -eq '7Zip'){'7zip'}else{$Application.ToLowerInvariant()}
$demoArguments=@((Join-Path $PSScriptRoot 'demo-installed-app.py'),'--app',$appName)
if($Smoke){$demoArguments+='--smoke'}
& python @demoArguments
if($LASTEXITCODE -ne 0){throw 'Installed application demonstration did not pass; inspect the printed evidence.'}
