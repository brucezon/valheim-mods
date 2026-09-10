# usage: tools\dll-has-string.ps1 <file.dll> <text> [<text> ...]
# Reports whether each text occurs in the file as UTF-16LE (how .NET stores string literals in the
# #US heap, at ANY byte alignment) or as ASCII/UTF-8 (resource names, manifest strings).
# A whole-file Unicode decode from offset 0 misses odd-aligned literals; this scans byte patterns.
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Texts
)
$bytes = [IO.File]::ReadAllBytes($Path)
function Find-Bytes([byte[]]$hay, [byte[]]$needle) {
    $last = $hay.Length - $needle.Length
    for ($i = 0; $i -le $last; $i++) {
        if ($hay[$i] -ne $needle[0]) { continue }
        $ok = $true
        for ($k = 1; $k -lt $needle.Length; $k++) { if ($hay[$i + $k] -ne $needle[$k]) { $ok = $false; break } }
        if ($ok) { return $i }
    }
    return -1
}
foreach ($t in $Texts) {
    $u16 = Find-Bytes $bytes ([Text.Encoding]::Unicode.GetBytes($t))
    $u8 = Find-Bytes $bytes ([Text.Encoding]::UTF8.GetBytes($t))
    $where = @(); if ($u16 -ge 0) { $where += "utf16@$u16" }; if ($u8 -ge 0) { $where += "utf8@$u8" }
    "{0,-6} {1}  {2}" -f ($(if ($where.Count) { 'FOUND' } else { 'absent' })), $t, ($where -join ' ')
}
