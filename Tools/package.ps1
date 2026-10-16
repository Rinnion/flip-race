param(
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '../Builds/WebGL'),
    [string]$Output = (Join-Path $PSScriptRoot '../Releases/flip-race-v3-webgl.zip')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$buildRoot = (Resolve-Path -LiteralPath $BuildDirectory).Path.TrimEnd('\', '/')
$archivePath = [System.IO.Path]::GetFullPath($Output)
if ($archivePath.StartsWith($buildRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The output ZIP must be outside the public build directory.'
}
if ([System.IO.Path]::GetExtension($archivePath) -ne '.zip') { throw 'Output must have the .zip extension.' }
$outputDirectory = [System.IO.Path]::GetDirectoryName($archivePath)
[void][System.IO.Directory]::CreateDirectory($outputDirectory)
$manifestPath = $archivePath + '.manifest.json'
& node (Join-Path $PSScriptRoot 'verify-build.mjs') $buildRoot --json $manifestPath
if ($LASTEXITCODE -ne 0) { throw 'Build verification failed; ZIP was not created.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$temporaryArchive = Join-Path $outputDirectory ('.package-' + [Guid]::NewGuid().ToString('N') + '.zip')

try {
    $zip = [System.IO.Compression.ZipFile]::Open($temporaryArchive, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $manifest.files) {
            $source = Join-Path $buildRoot $file.path
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $file.path, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $zip.Dispose() }

    # Reopen and hash the actual archive entries. This verifies both the root
    # layout and byte-for-byte fidelity without extracting untrusted paths.
    $zip = [System.IO.Compression.ZipFile]::OpenRead($temporaryArchive)
    try {
        if ($zip.Entries.Count -ne $manifest.fileCount) { throw 'ZIP entry count differs from the verified build.' }
        if ($null -eq $zip.GetEntry('index.html')) { throw 'index.html is not at ZIP root.' }
        foreach ($file in $manifest.files) {
            $entry = $zip.GetEntry($file.path)
            if ($null -eq $entry -or $entry.Length -ne $file.bytes) { throw "ZIP entry missing or wrong size: $($file.path)" }
            $stream = $entry.Open()
            $hasher = [System.Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $hasher.Dispose(); $stream.Dispose() }
            if ($hash -ne $file.sha256) { throw "ZIP SHA-256 mismatch: $($file.path)" }
        }
    } finally { $zip.Dispose() }

    [System.IO.File]::Copy($temporaryArchive, $archivePath, $true)
    $archive = Get-Item -LiteralPath $archivePath
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest | Add-Member -NotePropertyName archive -NotePropertyValue ([ordered]@{
        path = $archivePath; bytes = $archive.Length
        miB = [Math]::Round($archive.Length / 1MB, 3); sha256 = $archiveHash
        entriesVerified = $true; indexAtRoot = $true
    })
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Output "Ready: $archivePath"
    Write-Output "ZIP: $($archive.Length) bytes ($([Math]::Round($archive.Length / 1MB, 3)) MiB)"
    Write-Output "SHA-256: $archiveHash"
    Write-Output "Verified entries: $($manifest.fileCount), index.html at root, all file hashes match."
    Write-Output "Manifest: $manifestPath"
} finally {
    if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
}
