# usage: powershell -File tools\hexium-publish.ps1 <package.zip> [-Categories 'Transportation,Vehicles']
# Uploads a Thunderstore-format zip to Hexium under bruceirons-team using .secrets\hexium_token.
#
# Categories are comma-separated slugs and default to none. Publishing with none is what every
# package did up to 14 Sep 2026, and the cost is that the mod never appears under any category
# filter on the site. Valid slugs come from:
#   curl https://hexium.gg/api/experimental/community/valheim/category/
# Note the listing page's "Installation Type: Client-only" badge is NOT one of these - it is a
# site-wide default Hexium shows on every package, server-only ones included, and nothing we send
# changes it. Say the install target in the package README instead.
param([Parameter(Mandatory = $true)][string]$Zip, [string]$Categories = '')
$ErrorActionPreference = 'Stop'
$W = Split-Path $PSScriptRoot -Parent
$token = (Get-Content "$W\.secrets\hexium_token" -Raw).Trim()
$api = 'https://hexium.gg/api/experimental'
$headers = @{ Authorization = "Bearer $token" }
$zipPath = (Resolve-Path $Zip).Path
$name = Split-Path $zipPath -Leaf
$size = (Get-Item $zipPath).Length

$init = Invoke-RestMethod -Method Post -Uri "$api/usermedia/initiate-upload/" -Headers $headers -ContentType 'application/json' -Body (@{ filename = $name; file_size_bytes = $size } | ConvertTo-Json)
$uuid = $init.user_media.uuid
if ($init.upload_urls.Count -ne 1) { throw "multi-part upload not implemented (size $size)" }
"uuid=$uuid"

$bytes = [IO.File]::ReadAllBytes($zipPath)
$put = Invoke-WebRequest -Method Put -Uri $init.upload_urls[0].url -Body $bytes -ContentType 'application/octet-stream' -UseBasicParsing
$etag = $put.Headers['ETag']
"etag=$etag"

$fin = Invoke-RestMethod -Method Post -Uri "$api/usermedia/$uuid/finish-upload/" -Headers $headers -ContentType 'application/json' -Body (@{ parts = @(@{ ETag = $etag.Trim('"'); PartNumber = 1 }) } | ConvertTo-Json -Depth 4)
"finish: $($fin.status)"

$cats = @($Categories -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
"categories: $(if ($cats.Count) { $cats -join ', ' } else { '(none)' })"
$body = @{ author_name = 'bruceirons-team'; categories = $cats; communities = @('valheim'); has_nsfw_content = $false; upload_uuid = $uuid; community_categories = @{ valheim = $cats } } | ConvertTo-Json -Depth 4
try {
  $sub = Invoke-RestMethod -Method Post -Uri "$api/submission/submit/" -Headers $headers -ContentType 'application/json' -Body $body
  "published: $($sub.package_version.full_name)  $($sub.package_version.download_url)"
} catch {
  $r = $_.Exception.Response; if ($r) { $sr = New-Object IO.StreamReader($r.GetResponseStream()); "submit failed: " + $sr.ReadToEnd() } else { throw }
}
