param(
 [Parameter(Mandatory=$true)][string]$Unity,
 [Parameter(Mandatory=$true)][string]$ProjectPath,
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [ValidateSet('baseline','improved')][string]$Controller='improved',
 [ValidateSet('main','dynamic','hold','heldout','timing30','timing90','brake')][string]$Suite='main'
)
$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath $ProjectPath).Path
$editor=(Resolve-Path -LiteralPath $Unity).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a new output directory; existing evidence is never overwritten.'}
$runtime=Join-Path $project 'Assets/TaiwanRider/Runtime/AutomaticAvoidance.cs'
$source=Join-Path $PSScriptRoot ('controllers/'+$Controller+'.cs')
$config=Join-Path $PSScriptRoot ($Suite+'-config.json')
foreach($entry in (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'shared-manifest.json') -Raw | ConvertFrom-Json)) {
 if((Get-FileHash -LiteralPath (Join-Path $project $entry.path)).Hash.ToLowerInvariant() -ne $entry.sha256){throw ('Shared runtime differs: '+$entry.path)}
}
foreach($entry in (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'before-hashes.json') -Raw | ConvertFrom-Json) | Where-Object {$_.path -match 'RadarSensor.cs$|RiderVisionSensor.cs$|GongguanRide.unity$|Scooter125.asset$|\.onnx$'}) {
 if((Get-FileHash -LiteralPath (Join-Path $project $entry.path)).Hash.ToLowerInvariant() -ne $entry.sha256){throw ('Protected input differs: '+$entry.path)}
}
$backup=[IO.File]::ReadAllBytes($runtime)
$candidateHash=(Get-FileHash -LiteralPath $source).Hash
try {
 Copy-Item -LiteralPath $source -Destination $runtime
 New-Item -ItemType Directory -Path $output | Out-Null
 $log=Join-Path $output 'unity.log'
 $arguments='-batchmode -projectPath "'+$project+'" -executeMethod TaiwanRider.Editor.AvoidanceBenchmark.Begin -visionModel v2 -controllerTag '+$Controller+' -benchmarkConfig "'+$config+'" -benchmarkOutput "'+$output+'" -logFile "'+$log+'"'
 $process=Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -PassThru
 $process.WaitForExit()
 if($process.ExitCode -ne 0){throw ('Unity exit '+$process.ExitCode)}
 $report=Get-Content -LiteralPath (Join-Path $output 'report.json') -Raw | ConvertFrom-Json
 if(-not $report.completed -or $report.error -or @($report.cases | Where-Object {-not $_.valid}).Count){throw 'Incomplete or invalid report'}
 @{controller=$Controller;controllerSha256=$candidateHash.ToLowerInvariant();configSha256=(Get-FileHash -LiteralPath $config).Hash.ToLowerInvariant();utc=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'provenance.json') -Encoding utf8
 Write-Output ($report.recorded.ToString()+' valid cases: '+$output)
} finally {
 if((Get-FileHash -LiteralPath $runtime).Hash -eq $candidateHash){[IO.File]::WriteAllBytes($runtime,$backup)}
 else {Write-Warning 'Runtime changed during this run; not overwriting concurrent edits.'}
}
