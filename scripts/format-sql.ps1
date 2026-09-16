#requires -Version 7.0
param(
    [string]$Path,
    [Alias('check')][switch]$CheckOnly,
    [switch]$Strict,
    [string]$AssemblyPath = "$PSScriptRoot/lib/Microsoft.SqlServer.TransactSql.ScriptDom.dll"
)
$ErrorActionPreference = 'Stop'
try {
    if (!(Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) { throw 'ScriptDOM is missing. Supply -AssemblyPath or install the pinned dependency (references/formatter.md).' }
    if ((Get-FileHash -LiteralPath $AssemblyPath -Algorithm SHA256).Hash -ne 'fb4581ca0c8b68612d9d8a05cac1194c038c6b0e86b4db41a3072d6f3190592b') { throw 'ScriptDOM does not match the pinned 180.18.1 assembly' }
    if (!('RoySql' -as [type])) {
        Add-Type -LiteralPath $AssemblyPath
        $references = @([IO.Directory]::GetFiles("$PSHOME/ref", '*.dll')) + (Resolve-Path -LiteralPath $AssemblyPath).Path
        Add-Type -Path "$PSScriptRoot/RoySql.cs" -ReferencedAssemblies $references
    }
    $sql = if ($Path) { [IO.File]::ReadAllText($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)) } else { [Console]::In.ReadToEnd() }
    $warning = $null
    $formatted = [RoySql]::Format($sql, [bool]$Strict, [ref]$warning)
    if ($warning) { [Console]::Error.WriteLine($warning) }
    if ($CheckOnly) { if ($formatted -cne $sql) { exit 1 }; exit 0 }
    [Console]::Out.Write($formatted)
} catch {
    [Console]::Error.WriteLine($_.Exception.GetBaseException().Message)
    exit 2
}
