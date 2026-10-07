param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Void Crew',
    [string]$BepInExDll = ''
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
if (!$BepInExDll) { $BepInExDll = Join-Path $GamePath 'BepInEx\core\BepInEx.dll' }
$managed = Join-Path $GamePath 'Void Crew_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @($BepInExDll, "$managed\Assembly-CSharp.dll", "$managed\UnityEngine.dll", "$managed\UnityEngine.CoreModule.dll", "$managed\UnityEngine.IMGUIModule.dll", "$managed\UnityEngine.InputLegacyModule.dll", "$managed\netstandard.dll")
$references += @('UnityEngine.UIElementsModule.dll', 'PhotonUnityNetworking.dll', 'PhotonRealtime.dll', 'ResourceAssets.dll') | ForEach-Object { Join-Path $managed $_ }
$references += Join-Path $managed 'Unity.InputSystem.dll'
$references += Join-Path $managed 'Photon3Unity3D.dll'
$references += Join-Path $managed 'UnityEngine.JSONSerializeModule.dll'
$references += Join-Path $managed 'UnityEngine.ImageConversionModule.dll'
$references += Join-Path $managed 'Newtonsoft.Json.dll'
$references += Join-Path (Split-Path $BepInExDll) '0Harmony.dll'
foreach ($file in @($compiler) + $references) { if (!(Test-Path -LiteralPath $file)) { throw "Missing required file: $file" } }
$arguments = @('/nologo', '/target:library', '/optimize+', "/out:$outputDir\ProgressEditor.dll")
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += "/resource:$projectRoot\Assets\RankCardBackground.png,ProgressEditor.RankCardBackground.png"
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built $outputDir\ProgressEditor.dll"
