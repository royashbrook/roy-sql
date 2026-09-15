#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$package = 'https://api.nuget.org/v3-flatcontainer/microsoft.sqlserver.transactsql.scriptdom/180.18.1/microsoft.sqlserver.transactsql.scriptdom.180.18.1.nupkg'
$target = "$PSScriptRoot/lib/Microsoft.SqlServer.TransactSql.ScriptDom.dll"
$assemblyHash = 'fb4581ca0c8b68612d9d8a05cac1194c038c6b0e86b4db41a3072d6f3190592b'
if (Test-Path -LiteralPath $target) {
    if ((Get-FileHash -LiteralPath $target).Hash -ne $assemblyHash) { throw 'existing dependency does not match the pin; inspect it before replacing' }
    'ScriptDOM 180.18.1 already installed'
    return
}
$download = [IO.Path]::GetTempFileName()
try {
    Invoke-WebRequest -Uri $package -OutFile $download
    if ((Get-FileHash -LiteralPath $download).Hash -ne 'eaea6048f5d8b9243521188850fa04a88a6b589fe0fa457fa1ecf94f06be12a5') { throw 'package hash mismatch' }
    $zip = [IO.Compression.ZipFile]::OpenRead($download)
    try {
        $entry = $zip.GetEntry('lib/netstandard2.0/Microsoft.SqlServer.TransactSql.ScriptDom.dll')
        if (!$entry) { throw 'pinned assembly missing from package' }
        $null = [IO.Directory]::CreateDirectory("$PSScriptRoot/lib")
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
    } finally { $zip.Dispose() }
    if ((Get-FileHash -LiteralPath $target).Hash -ne $assemblyHash) { throw 'extracted assembly hash mismatch' }
    'installed ScriptDOM 180.18.1 (MIT, Microsoft)'
} finally { Remove-Item -LiteralPath $download }
