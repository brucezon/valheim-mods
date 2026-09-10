# usage: tools\fix-mojibake.ps1 <file> [<file> ...]
# Reverses UTF-8 double-encoding ("mojibake") produced by reading a UTF-8 file as ANSI (Windows-1252)
# and writing it back as UTF-8. Applies as many reversal rounds as needed. ASCII-only script on purpose.
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Paths)
$utf8 = New-Object System.Text.UTF8Encoding($false)
$cp1252 = [Text.Encoding]::GetEncoding(1252)
# Signatures of double-encoded common characters: em dash, check mark, o-umlaut, middle dot, arrow.
$sig = [string]::Join('|', @(
    ([char]0x00E2 + [char]0x20AC),          # "a-circumflex euro" prefix of em dash / arrows / approx
    ([char]0x00E2 + [char]0x0153 + [char]0x201D), # check mark
    ([char]0x00C3 + [char]0x00B6),          # o-umlaut
    ([char]0x00C2 + [char]0x00B7),          # middle dot
    ([char]0x00C3 + [char]0x2014)           # multiplication sign
))
foreach ($path in $Paths) {
    $rounds = 0
    while ($rounds -lt 8) {
        $s = $utf8.GetString([IO.File]::ReadAllBytes($path))
        if ($s -notmatch $sig) { break }
        $s = $s.Replace([string][char]0xFEFF, '')   # a UTF-8 BOM would otherwise become a literal '?'
        $s = $utf8.GetString($cp1252.GetBytes($s))
        [IO.File]::WriteAllBytes($path, $utf8.GetBytes($s))
        $rounds++
    }
    $final = $utf8.GetString([IO.File]::ReadAllBytes($path))
    $dashes = ([regex]::Matches($final, [string][char]0x2014)).Count
    $checks = ([regex]::Matches($final, [string][char]0x2714)).Count
    $left = ([regex]::Matches($final, $sig)).Count
    "{0}: reversed {1} round(s); em-dashes={2} check-marks={3} leftovers={4}" -f (Split-Path $path -Leaf), $rounds, $dashes, $checks, $left
}
