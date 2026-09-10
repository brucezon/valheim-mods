# Downloads candidate building-damage mods, runs the 1.0 call-site sweep, and prints README/CHANGELOG excerpts.
$W = 'C:\Users\cooki\agent-projects\valheim-modding'
$S = "$W\refs\1.0\_stage\buildmods"
New-Item -ItemType Directory -Force $S | Out-Null
$managed = "$W\refs\1.0\gamepath\valheim_Data\Managed"

$targets = @(
  @{ site='hexium';       ns='Azumatt';  name='AzuWearNTearPatches'; ver='1.0.8' },
  @{ site='thunderstore'; ns='SHK';      name='BuildingDamageMod';   ver='4.6.4' },
  @{ site='thunderstore'; ns='Landoria'; name='StructureProtection'; ver='1.0.5' },
  @{ site='thunderstore'; ns='Azumatt';  name='ResinGuard';          ver='1.2.6' }
)
foreach ($t in $targets) {
  $dir = "$S\$($t.ns)-$($t.name)-$($t.ver)"
  New-Item -ItemType Directory -Force $dir | Out-Null
  if ($t.site -eq 'hexium') {
    $url = (Invoke-RestMethod "https://hexium.gg/api/experimental/package/$($t.ns)/$($t.name)/$($t.ver)/").download_url
  } else {
    $url = "https://thunderstore.io/package/download/$($t.ns)/$($t.name)/$($t.ver)/"
  }
  "`n=============== $($t.ns)-$($t.name)-$($t.ver)  [$($t.site)] ==============="
  Invoke-WebRequest -Uri $url -OutFile "$dir\pkg.zip" -UseBasicParsing
  Expand-Archive -Path "$dir\pkg.zip" -DestinationPath $dir -Force
  foreach ($dll in Get-ChildItem $dir -Recurse -Filter *.dll) {
    "-- sweep $($dll.Name)"
    Push-Location "$W\tools\call-retarget"
    dotnet run -c Release --no-build -- $dll.FullName "$dir\$($dll.BaseName).checked.dll" $managed 2>&1 | Select-Object -Last 1
    Pop-Location
  }
  $cl = Get-ChildItem $dir -Recurse -Filter CHANGELOG.md | Select-Object -First 1
  if ($cl) { "-- CHANGELOG (top 25 lines)"; Get-Content $cl.FullName -TotalCount 25 }
  $rm = Get-ChildItem $dir -Recurse -Filter README.md | Select-Object -First 1
  if ($rm) { "-- README (first 60 lines)"; Get-Content $rm.FullName -TotalCount 60 | ForEach-Object { $_.Substring(0, [Math]::Min(170, $_.Length)) } }
}
