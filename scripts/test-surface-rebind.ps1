param([string]$HostAssembly=(Join-Path $PSScriptRoot '../src/LayerHost/bin/Apps-20261022b/net10.0-windows/LayerHost.dll'))
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($HostAssembly))
$windowType=$assembly.GetType('LayerHost.WindowInfo',$true)
$selection=$assembly.GetType('LayerHost.LayerWindowSelection',$true)
$method=$selection.GetMethod('ReplacementMain',[Reflection.BindingFlags]'Static,NonPublic')
$ctor=$windowType.GetConstructors()|Where-Object{$_.GetParameters().Count -eq 12}|Select-Object -First 1
function Window([long]$handle,[int]$process=77,[long]$owner=0,[bool]$visible=$true,[string]$title='Disposable document',[bool]$enabled=$true){
    $ctor.Invoke([object[]]@($handle,[long]0,$process,'ResearchWindow',$title,$visible,$enabled,0,0,900,650,$owner))
}
function Select-Replacement([object[]]$items){
    $typed=[Array]::CreateInstance($windowType,$items.Count)
    for($index=0;$index -lt $items.Count;$index++){$typed.SetValue($items[$index],$index)}
    $arguments=[object[]]::new(3);$arguments[0]=$typed;$arguments[1]=[long]1;$arguments[2]=[int]77
    $method.Invoke($null,$arguments)
}
$checks=[ordered]@{}
$checks.uniqueReplacement=(Select-Replacement @((Window 2))).Handle -eq 2
$checks.keepExisting=(Select-Replacement @((Window 1),(Window 2))) -eq $null
$checks.ambiguousRejected=(Select-Replacement @((Window 2),(Window 3))) -eq $null
$checks.helperProcessRejected=(Select-Replacement @((Window 2 -process 88))) -eq $null
$checks.ownedPopupRejected=(Select-Replacement @((Window 2 -owner 123))) -eq $null
$checks.hiddenRejected=(Select-Replacement @((Window 2 -visible $false))) -eq $null
$checks.untitledRejected=(Select-Replacement @((Window 2 -title ''))) -eq $null
$checks.disabledMainRemainsDiscoverable=(Select-Replacement @((Window 2 -enabled $false))).Handle -eq 2
$checks.emptyWaits=(Select-Replacement @()) -eq $null
if(@($checks.Values|Where-Object{!$_}).Count -ne 0){$checks|ConvertTo-Json;throw 'Surface replacement checks failed'}
[pscustomobject]@{passed=$true;checks=$checks}|ConvertTo-Json -Depth 3
