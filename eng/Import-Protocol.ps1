param([string]$ArchivePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$pin = Get-Content (Join-Path $repo 'protocol/protocol.lock.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$cache = [IO.Path]::GetFullPath($cache)
# Refuse a linked artifacts/ before anything, including the download below, writes through it.
if ((Get-Item -LiteralPath $cache -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw "Refusing to import into ${cache}: it is a link."
}

# Each import starts from empty folders, so no file from an earlier import survives.
# Only these two folders directly under artifacts/ are ever cleared, and never through a link.
function Reset-ImportFolder([ValidateSet('protocol', 'protocol-package')][string]$Name) {
    $path = [IO.Path]::GetFullPath((Join-Path $cache $Name))
    if ([IO.Path]::GetDirectoryName($path) -ne $cache -or [IO.Path]::GetFileName($path) -ne $Name) {
        throw "Refusing to clear ${path}: it is not artifacts/$Name."
    }
    foreach ($folder in @($cache, $path)) {
        if ((Test-Path -LiteralPath $folder) -and
            ((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to clear ${path}: $folder is a link."
        }
    }
    if (Test-Path -LiteralPath $path) {
        if (!(Test-Path -LiteralPath $path -PathType Container)) { throw "Refusing to clear ${path}: it is not a folder." }
        # Directory.Delete removes a link inside the folder without following it. It cannot
        # delete the archive, which the import holds open without delete sharing.
        try { [IO.Directory]::Delete($path, $true) }
        catch { throw "Could not clear artifacts/$Name; if the archive is in it, move it out. $($_.Exception.GetBaseException().Message)" }
    }
    New-Item -ItemType Directory -Path $path | Out-Null
    $path
}

if (!('BriosaExamples.RealPath' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BriosaExamples
{
    // Where a file or folder really is: the path Windows reports for an open handle, with
    // every junction, symbolic link, short name and substituted drive resolved.
    public static class RealPath
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);

        public static string OfOpenFile(SafeFileHandle handle)
        {
            var path = new StringBuilder(1024);
            var length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
            if (length >= path.Capacity)
            {
                path.Capacity = (int)length;
                length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0);
            }
            if (length == 0 || length >= path.Capacity) throw new Win32Exception();
            return path.ToString();
        }

        public static string OfFolder(string folder)
        {
            // No access rights are requested; backup semantics let CreateFile open a folder.
            using (var handle = CreateFileW(folder, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception();
                return OfOpenFile(handle);
            }
        }
    }
}
'@
}
# ZipFileExtensions is in a different assembly in Windows PowerShell than in PowerShell 7.
$zipExtensions = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'System.IO.Compression.FileSystem' } else { 'System.IO.Compression.ZipFile' }
Add-Type -AssemblyName System.IO.Compression, $zipExtensions

if (!$ArchivePath) {
    $ArchivePath = Join-Path $cache $pin.file_name
    if (!(Test-Path -LiteralPath $ArchivePath)) {
        if (!$pin.url) { throw "Protocol $($pin.version) has no download URL. Place the archive at $ArchivePath or pass -ArchivePath." }
        Invoke-WebRequest $pin.url -OutFile $ArchivePath
    }
}
$ArchivePath = (Resolve-Path -LiteralPath $ArchivePath).ProviderPath
# The import holds the archive open, sharing it only for reading, until it ends. Nothing can
# delete, rename or change the archive meanwhile, whatever path it uses, and the import reads
# only through this handle.
$archive = [IO.File]::Open($ArchivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    # Decide by where the archive really is, not how its path is spelled: a path through a
    # link can name a file inside a folder the import clears.
    $archiveLocation = [BriosaExamples.RealPath]::OfOpenFile($archive.SafeFileHandle)
    foreach ($name in 'protocol', 'protocol-package') {
        $folder = Join-Path $cache $name
        if ((Test-Path -LiteralPath $folder -PathType Container) -and
            $archiveLocation.StartsWith([BriosaExamples.RealPath]::OfFolder($folder) + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Move the archive out of artifacts/$name; the import clears that folder."
        }
    }
    if ((Get-FileHash -InputStream $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $pin.sha256) {
        throw 'Protocol archive hash mismatch.'
    }
    $archive.Position = 0
    $extracted = Reset-ImportFolder 'protocol-package'
    $zip = [IO.Compression.ZipArchive]::new($archive, [IO.Compression.ZipArchiveMode]::Read, $true)
    try { [IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip, $extracted) } finally { $zip.Dispose() }
    $package = Join-Path $extracted ([IO.Path]::GetFileNameWithoutExtension($pin.file_name))
    $manifest = Get-Content (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.briosa_version -ne $pin.version -or $manifest.source_revision -ne $pin.source_revision -or
        $manifest.spatial_analyzer_target -ne $pin.sa_target) { throw 'Protocol identity mismatch.' }
    if ($manifest.schema_version -ne 3 -or $manifest.compatibility.major -ne 3 -or $manifest.compatibility.revision -lt 0) {
        throw 'A contract-aware protocol artifact is required.'
    }
    foreach ($entry in $manifest.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $package $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
            throw "Protocol file mismatch: $($entry.path)"
        }
    }
    $destination = Reset-ImportFolder 'protocol'
    Copy-Item -LiteralPath (Join-Path $package 'proto') -Destination $destination -Recurse
    # The builds read artifacts/protocol, so it must hold exactly the manifest's proto files.
    $imported = @(Get-ChildItem -LiteralPath $destination -Recurse -File -Force |
        ForEach-Object { $_.FullName.Substring($destination.Length + 1).Replace('\', '/') } | Sort-Object)
    $expected = @($manifest.files.path | Where-Object { $_.StartsWith('proto/') } | Sort-Object)
    if (($imported -join "`n") -cne ($expected -join "`n")) { throw 'Imported protocol files differ from the archive manifest.' }
} finally { $archive.Dispose() }
Write-Host 'Verified protocol imported. Generated C# remains build output.'
