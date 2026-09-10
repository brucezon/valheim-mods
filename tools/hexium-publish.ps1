# usage: powershell -File tools\hexium-publish.ps1 <package.zip>
# Uploads a Thunderstore-format zip to Hexium under bruceirons-team using .secrets\hexium_token.
param([Parameter(Mandatory = $true)][string]$Zip)
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

$body = @{ author_name = 'bruceirons-team'; categories = @(); communities = @('valheim'); has_nsfw_content = $false; upload_uuid = $uuid; community_categories = @{ valheim = @() } } | ConvertTo-Json -Depth 4
try {
  $sub = Invoke-RestMethod -Method Post -Uri "$api/submission/submit/" -Headers $headers -ContentType 'application/json' -Body $body
  "published: $($sub.package_version.full_name)  $($sub.package_version.download_url)"
} catch {
  $r = $_.Exception.Response; if ($r) { $sr = New-Object IO.StreamReader($r.GetResponseStream()); "submit failed: " + $sr.ReadToEnd() } else { throw }
}
