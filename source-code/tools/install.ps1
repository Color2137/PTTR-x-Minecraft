<#
  PTRCraft: Minecraft inside Paint the Town Red. Installs (or updates) everything on this PC.

    powershell -ExecutionPolicy Bypass -File tools\install.ps1            # install / update
    powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Uninstall # remove it again

  What it does:
   1. BepInEx 6 (bleeding edge 788, IL2CPP x64, from builds.bepinex.dev) into Paint the Town Red's folder,
      if it isn't there yet. Its console window is turned off.
   2. The PTRCraft plugin into PTR's BepInEx\plugins\PTRCraft.
   3. A Prism Launcher instance "PTRCraft" (Minecraft 26.3, Fabric Loader 0.19.5, Fabric API 0.162.0+26.3
      from Fabric's maven, and the ptrcraft mod). It never touches your other instances.
  Builds first if the plugin or mod jar is missing (needs the .NET SDK and JDK 25).
#>
param(
	[string]$PtrDir = "",
	[string]$PrismDir = "$env:APPDATA\PrismLauncher",
	[switch]$Uninstall
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = if ($env:PTRCRAFT_OUT) { $env:PTRCRAFT_OUT } else { Join-Path $env:TEMP "ptrmc" }

function Find-Ptr {
	if ($PtrDir) { return $PtrDir }
	$steam = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
	$libs = @()
	if ($steam) {
		$libs += $steam
		$vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
		if (Test-Path $vdf) { $libs += (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches).Matches | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' } }
	}
	foreach ($l in $libs) {
		$acf = Join-Path $l "steamapps\appmanifest_337320.acf"
		if (Test-Path $acf) {
			$dir = ((Select-String -Path $acf -Pattern '"installdir"\s+"([^"]+)"').Matches[0].Groups[1].Value)
			return Join-Path $l "steamapps\common\$dir"
		}
	}
	throw "Paint the Town Red (Steam app 337320) not found. Pass -PtrDir."
}

$ptr = Find-Ptr
$instance = Join-Path $PrismDir "instances\PTRCraft"
Write-Host "Paint the Town Red: $ptr"
Write-Host "Prism instance:     $instance"

if ($Uninstall) {
	# exactly what this script added (docs\UNINSTALL-BepInEx.txt)
	foreach ($p in "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "dotnet", "BepInEx") {
		$t = Join-Path $ptr $p
		if (Test-Path $t) { Remove-Item -Recurse -Force $t; Write-Host "removed $t" }
	}
	if (Test-Path $instance) { Remove-Item -Recurse -Force $instance; Write-Host "removed $instance (its Minecraft world goes with it)" }
	Write-Host "Done. Paint the Town Red is back to plain PTR."
	exit 0
}

# 1. BepInEx
if (-not (Test-Path (Join-Path $ptr "BepInEx\core\BepInEx.Unity.IL2CPP.dll"))) {
	$zip = Join-Path $out "BepInEx-be.788.zip"
	New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
	Write-Host "downloading BepInEx 6 be.788 (IL2CPP x64)..."
	Invoke-WebRequest -UseBasicParsing "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip" -OutFile $zip
	$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLower()
	if ($hash -ne "f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a") { throw "BepInEx download checksum mismatch ($hash)" }
	Expand-Archive -Force $zip $ptr
	Write-Host "BepInEx installed. Start PTR once so it generates its interop files (about a minute)."
}
$bepCfg = Join-Path $ptr "BepInEx\config\BepInEx.cfg"
if (Test-Path $bepCfg) {
	$text = Get-Content $bepCfg -Raw
	$text = [regex]::Replace($text, '(\[Logging\.Console\][\s\S]*?\nEnabled = )true', '${1}false')
	Set-Content -Encoding utf8 $bepCfg $text
}

# 2. the plugin
$dll = Join-Path $out "plugin-bin\Release\PTRCraft.dll"
if (-not (Test-Path $dll)) {
	if (-not (Test-Path (Join-Path $ptr "BepInEx\interop\Assembly-CSharp.dll"))) { throw "Start Paint the Town Red once (BepInEx makes its interop files), then run this again." }
	dotnet build (Join-Path $root "ptr-plugin\PTRCraft.csproj") -c Release -nologo -v q "-p:PTR_DIR=$ptr"
}
$plugins = Join-Path $ptr "BepInEx\plugins\PTRCraft"
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item -Force $dll $plugins
Write-Host "plugin -> $plugins"

# 3. the Prism instance
$jar = Join-Path $out "mc-build\libs\ptrcraft-0.1.0.jar"
if (-not (Test-Path $jar)) {
	$env:JAVA_HOME = "C:\Program Files\Java\jdk-25.0.2"
	& (Join-Path $root "minecraft\gradlew.bat") -p (Join-Path $root "minecraft") --project-cache-dir (Join-Path $out "pcache-mc") build -x test
}
$mods = Join-Path $instance "minecraft\mods"
New-Item -ItemType Directory -Force $mods | Out-Null
Get-ChildItem $mods -Filter "ptrcraft-*.jar" | Remove-Item -Force
Copy-Item -Force $jar $mods
$fapi = Join-Path $mods "fabric-api-0.162.0+26.3.jar"
if (-not (Test-Path $fapi)) {
	Invoke-WebRequest -UseBasicParsing "https://maven.fabricmc.net/net/fabricmc/fabric-api/fabric-api/0.162.0%2B26.3/fabric-api-0.162.0%2B26.3.jar" -OutFile $fapi
	$sha1 = (Get-FileHash -Algorithm SHA1 $fapi).Hash.ToLower()
	if ($sha1 -ne "273cd2dcbd92d1559edcc91c9f23fee47f6ff93f") { Remove-Item $fapi; throw "Fabric API checksum mismatch" }
}
$pack = Join-Path $instance "mmc-pack.json"
if (-not (Test-Path $pack)) {
	@'
{
    "components": [
        { "uid": "org.lwjgl3", "version": "3.4.3", "dependencyOnly": true },
        { "uid": "net.minecraft", "version": "26.3", "important": true },
        { "uid": "net.fabricmc.intermediary", "version": "26.3", "dependencyOnly": true },
        { "uid": "net.fabricmc.fabric-loader", "version": "0.19.5" }
    ],
    "formatVersion": 1
}
'@ | Set-Content -Encoding utf8 $pack
}
$cfg = Join-Path $instance "instance.cfg"
if (-not (Test-Path $cfg)) {
	@'
[General]
ConfigVersion=1.3
iconKey=default
name=PTRCraft (Minecraft inside Paint the Town Red)
InstanceType=OneSix
AutomaticJava=true
OverrideJavaArgs=true
JvmArgs=-Dptrcraft.startHidden=true --enable-native-access=ALL-UNNAMED
OverrideMemory=true
MaxMemAlloc=4096
MinMemAlloc=1024
OverrideConsole=true
ShowConsole=false
ShowConsoleOnError=false
AutoCloseConsole=true
notes=Started hidden by Paint the Town Red (BepInEx plugin PTRCraft). Minecraft is the player inside PTR.
'@ | Set-Content -Encoding utf8 $cfg
}
Write-Host "Prism instance ready: $instance"
Write-Host "Done. Start Paint the Town Red from Steam: Minecraft starts hidden with it."
