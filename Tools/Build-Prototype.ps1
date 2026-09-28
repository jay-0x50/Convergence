param(
    [string]$EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe',
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
    throw 'Unity Editor를 찾을 수 없습니다. -EditorPath로 Unity.exe 경로를 지정하세요.'
}
$artifactDirectory = Join-Path $projectRoot 'Artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$logPath = Join-Path $artifactDirectory 'unity-build.log'
$method = if ($ValidateOnly) { 'Convergence.Editor.PrototypeTools.ValidateProject' } else { 'Convergence.Editor.PrototypeTools.BuildWindows' }
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $projectRoot + '"'), '-executeMethod', $method, '-logFile', ('"' + $logPath + '"'))
$buildProcess = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($buildProcess.ExitCode -ne 0) {
    throw "Unity 작업 실패 (종료 코드 $($buildProcess.ExitCode)). 로그: $logPath"
}
$successMarker = if ($ValidateOnly) { 'CONVERGENCE VALIDATION PASSED' } else { 'CONVERGENCE BUILD PASSED' }
if (-not (Select-String -LiteralPath $logPath -Pattern $successMarker -Quiet)) {
    throw "Unity 완료 메시지를 찾지 못했습니다. 로그: $logPath"
}
Write-Output "Unity 작업 완료. 로그: $logPath"
