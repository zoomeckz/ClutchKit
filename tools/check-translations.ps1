# Checks src\Translations.cs against the texts used in the code:
#  - texts in L.T("...") / L.F("...") / L.N("...") that have no translation row
#  - rows that aren't used anymore
#  - rows with the wrong number of languages, or whose {0} {1} parts differ from English
#   powershell -ExecutionPolicy Bypass -File tools\check-translations.ps1
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root 'src'
$lit = '"((?:[^"\\]|\\.)*)"'

# texts used in the code
$used = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($f in Get-ChildItem $src -Filter *.cs) {
    if ($f.Name -in 'Translations.cs', 'Lang.cs') { continue }
    $c = [IO.File]::ReadAllText($f.FullName)
    foreach ($m in [regex]::Matches($c, 'L\.[TFN]\(' + $lit)) { if ($m.Groups[1].Value.Length -gt 0) { [void]$used.Add($m.Groups[1].Value) } }
}

# translation rows: R("en", "es", ...) - may span several lines
$t = [IO.File]::ReadAllText((Join-Path $src 'Translations.cs'))
$rows = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)   # case-sensitive, like the app
$problems = 0
foreach ($m in [regex]::Matches($t, 'R\(((?:\s*' + $lit + '\s*,?)+)\)')) {
    $cells = @([regex]::Matches($m.Groups[1].Value, $lit) | ForEach-Object { $_.Groups[1].Value })
    $en = $cells[0]
    if ($cells.Count -ne 8) { "WRONG LENGTH ($($cells.Count)): $en"; $problems++ }
    if ($rows.ContainsKey($en)) { "DUPLICATE ROW: $en"; $problems++ }
    $rows[$en] = $cells
    $want = (@([regex]::Matches($en, '\{\d\}') | ForEach-Object { $_.Value }) | Sort-Object) -join ','
    for ($i = 1; $i -lt $cells.Count; $i++) {
        $got = (@([regex]::Matches($cells[$i], '\{\d\}') | ForEach-Object { $_.Value }) | Sort-Object) -join ','
        if ($got -ne $want) { "PLACEHOLDERS DIFFER (column $i): $en  ->  $($cells[$i])"; $problems++ }
    }
}

$ignore = @('+1', '-1')
foreach ($k in $used) { if (-not $rows.ContainsKey($k) -and $ignore -notcontains $k) { "NO TRANSLATION: $k"; $problems++ } }
foreach ($k in $rows.Keys) { if (-not $used.Contains($k)) { "UNUSED ROW: $k" } }
"$($used.Count) texts in the code, $($rows.Count) rows, $problems problem(s)"
if ($problems -gt 0) { exit 1 }
