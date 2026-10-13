$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
try {
    foreach ($asset in @('index.html', 'large-order-alerts.js', 'dashboard-live.js', 'phone.html', 'phone.js', 'vendor\qrcode.js')) {
        if (!(Test-Path (Join-Path (Split-Path $PSScriptRoot -Parent) $asset))) { throw 'Dashboard files missing. Extract the whole repository ZIP and keep windows-connector inside it.' }
    }
    if (!(Test-Path 'megaSpeedyAPIDotnet.dll') -or !(Test-Path 'megaSpeedyAPI.dll') -or !(Test-Path 'Temp') -or !(Test-Path 'speedyAPI_config.json')) {
        function Get-SdkLayout([string] $candidate) {
            if (!(Test-Path -LiteralPath $candidate -PathType Container)) { return $null }
            $component = Join-Path $candidate 'component'
            if (!(Test-Path -LiteralPath $component -PathType Container)) { $component = $candidate }
            $dllFolder = Join-Path $component 'x86'
            if (!(Test-Path -LiteralPath $dllFolder -PathType Container)) { $dllFolder = $component }
            if ((Test-Path (Join-Path $dllFolder 'megaSpeedyAPIDotnet.dll')) -and (Test-Path (Join-Path $dllFolder 'megaSpeedyAPI.dll')) -and (Test-Path (Join-Path $component 'Temp')) -and (Test-Path (Join-Path $component 'speedyAPI_config.json'))) { return @{ Component = $component; DllFolder = $dllFolder } }
            return $null
        }
        $layout = $null
        $roots = @([Environment]::GetFolderPath('DesktopDirectory'), (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads'), (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)) | Where-Object { $_ } | Select-Object -Unique
        foreach ($root in $roots) {
            if (!(Test-Path -LiteralPath $root -PathType Container)) { continue }
            foreach ($repo in @(Get-ChildItem -LiteralPath $root -Directory -Filter 'tanyasite*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)) {
                $candidates = @((Join-Path $repo.FullName 'windows-connector'))
                foreach ($nested in @(Get-ChildItem -LiteralPath $repo.FullName -Directory -Filter 'tanyasite*' -ErrorAction SilentlyContinue)) { $candidates += Join-Path $nested.FullName 'windows-connector' }
                foreach ($candidate in $candidates) { $layout = Get-SdkLayout $candidate; if ($layout) { break } }
                if ($layout) { break }
            }
            if ($layout) { break }
        }
        if (!$layout) {
            Add-Type -AssemblyName System.Windows.Forms
            $picker = New-Object System.Windows.Forms.FolderBrowserDialog
            $picker.Description = 'Select extracted SpeedyAPI_CS OR your old working windows-connector folder.'
            if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'No official SDK folder selected.' }
            $layout = Get-SdkLayout $picker.SelectedPath
            if (!$layout) { throw 'Selected folder does not contain the complete official x86 SDK files.' }
        }
        Write-Host 'Reusing existing local SDK files. The broker SDK configuration stays on this computer.'
        foreach ($name in @('megaSpeedyAPIDotnet.dll', 'megaSpeedyAPI.dll')) {
            if (!(Test-Path $name)) { Copy-Item -LiteralPath (Join-Path $layout.DllFolder $name) -Destination $name }
        }
        foreach ($name in @('Temp', 'speedyAPI_config.json')) {
            if (!(Test-Path $name)) { Copy-Item -LiteralPath (Join-Path $layout.Component $name) -Destination $name -Recurse }
        }
    }
    $targetExe = Join-Path $PSScriptRoot 'Connector.exe'
    $running = @(Get-Process -Name Connector -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $targetExe } catch { $false } })
    if ($running.Count -gt 0) { throw 'Connector is already running. Use Alt+Tab to find it, or close it before restarting.' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    if (!(Test-Path $compiler)) { throw '.NET Framework compiler missing.' }
    & $compiler /nologo /codepage:65001 /target:winexe /platform:x86 /out:Connector.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:megaSpeedyAPIDotnet.dll Connector.cs MobileServer.cs OptionChain.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed. Please send a screenshot without credentials.' }
    Start-Process -FilePath '.\Connector.exe' -WorkingDirectory $PSScriptRoot
} catch {
    Write-Host $_.Exception.Message
    exit 1
}
