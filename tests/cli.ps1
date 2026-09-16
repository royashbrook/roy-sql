#requires -Version 7.0
param([Parameter(Mandatory)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'roy-sql-cli-' + [guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($root)
$inputFile = Join-Path $root 'input.sql'
$outputFile = Join-Path $root 'output.sql'
$badFile = Join-Path $root 'bad.sql'
$utf8 = [Text.UTF8Encoding]::new($false)
$sample = "SELECT N'café 日本語' AS Label FROM dbo.Items WHERE Kind != 'X' AND Id BETWEEN 1 AND 4"
[IO.File]::WriteAllText($inputFile, $sample, $utf8)
[IO.File]::WriteAllText($badFile, 'not the assembly', $utf8)
function Run([string[]]$Arguments, [string]$InputText = '') {
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })))
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardInputEncoding = $utf8
    $info.StandardOutputEncoding = $utf8
    $info.StandardErrorEncoding = $utf8
    foreach ($arg in @('-NoLogo', '-NoProfile', '-File', "$PSScriptRoot/../scripts/format-sql.ps1") + $Arguments) { $info.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $process.StandardInput.Write($InputText)
        $process.StandardInput.Close()
        $out = $process.StandardOutput.ReadToEndAsync()
        $err = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(30000)) { $process.Kill($true); throw 'CLI timed out' }
        [pscustomobject]@{ code = $process.ExitCode; stdout = $out.GetAwaiter().GetResult(); stderr = $err.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}
try {
    $file = Run @('-AssemblyPath', $AssemblyPath, '-Path', $inputFile)
    if ($file.code -ne 0 -or $file.stderr -or !$file.stdout.Contains('café 日本語')) { throw 'file/Unicode output failed' }
    $stdin = Run @('-AssemblyPath', $AssemblyPath) $sample
    if ($stdin.code -ne 0 -or $stdin.stderr -or $stdin.stdout -cne $file.stdout) { throw 'stdin/file mismatch' }
    [IO.File]::WriteAllText($outputFile, $file.stdout, $utf8)
    foreach ($case in @(@($inputFile, '1'), @($outputFile, '0'))) {
        $check = Run @('-AssemblyPath', $AssemblyPath, '-Path', $case[0], '-Check')
        if ($check.code -ne [int]$case[1] -or $check.stdout -or $check.stderr) { throw 'check exit/output contract failed' }
    }
    $repeat = Run @('-AssemblyPath', $AssemblyPath, '-Path', $outputFile)
    if ($repeat.code -ne 0 -or $repeat.stdout -cne $file.stdout) { throw 'CLI not idempotent' }
    $refused = Run @('-AssemblyPath', $AssemblyPath, '-Strict') 'select a into newtable from t'
    if ($refused.code -ne 2 -or $refused.stdout -or !$refused.stderr) { throw 'refusal leaked partial output' }
    $loose = Run @('-AssemblyPath', $AssemblyPath) 'select a from t limit 50;'
    if ($loose.code -ne 0 -or !$loose.stdout.Contains('limit') -or $loose.stderr -notmatch 'best-effort') { throw 'default fallback failed' }
    $looseRepeat = Run @('-AssemblyPath', $AssemblyPath) $loose.stdout
    if ($looseRepeat.code -ne 0 -or $looseRepeat.stdout -cne $loose.stdout) { throw 'fallback CLI is not stable' }
    $looseCheck = Run @('-AssemblyPath', $AssemblyPath, '-Check') $loose.stdout
    if ($looseCheck.code -ne 0 -or $looseCheck.stdout -or $looseCheck.stderr -notmatch 'best-effort') { throw 'fallback check contract failed' }
    $strict = Run @('-AssemblyPath', $AssemblyPath, '-Strict') 'select a from t limit 50;'
    if ($strict.code -ne 2 -or $strict.stdout -or $strict.stderr -notmatch 'T-SQL parse at 1:' -or $strict.stderr -notmatch 'Incorrect syntax' -or $strict.stderr -match 'Exception calling') { throw 'strict diagnostic failed' }
    $unclosed = Run @('-AssemblyPath', $AssemblyPath) "select 'unfinished"
    if ($unclosed.code -ne 0 -or $unclosed.stdout -cne "select 'unfinished" -or $unclosed.stderr -notmatch 'preserved unchanged') { throw 'damaged input fallback failed' }
    $missing = Run @('-AssemblyPath', $AssemblyPath, '-Path', (Join-Path $root 'missing.sql'))
    if ($missing.code -ne 2 -or $missing.stdout -or !$missing.stderr) { throw 'missing input was hidden' }
    foreach ($path in @((Join-Path $root 'missing.dll'), $badFile)) {
        $failed = Run @('-AssemblyPath', $path) $sample
        if ($failed.code -ne 2 -or $failed.stdout -or !$failed.stderr) { throw 'dependency refusal failed' }
    }
    if ([IO.File]::ReadAllText($inputFile) -cne $sample) { throw 'input changed' }
    '15 CLI checks passed'
} finally {
    Remove-Item -LiteralPath $inputFile, $outputFile, $badFile -ErrorAction SilentlyContinue
    [IO.Directory]::Delete($root)
}
