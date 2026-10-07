$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
try {
    if (!(Test-Path 'megaSpeedyAPIDotnet.dll') -or !(Test-Path 'megaSpeedyAPI.dll') -or !(Test-Path 'Temp') -or !(Test-Path 'speedyAPI_config.json')) {
        Add-Type -AssemblyName System.Windows.Forms
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Select the extracted SpeedyAPI_CS folder (not the .7z file).'
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'No official SDK folder selected.' }
        $component = Join-Path $picker.SelectedPath 'component'
        if (!(Test-Path $component)) { $component = $picker.SelectedPath }
        foreach ($name in @('megaSpeedyAPIDotnet.dll', 'megaSpeedyAPI.dll')) {
            $source = Join-Path $component ('x86\' + $name)
            if (!(Test-Path $source)) { throw ('Official SDK file missing: ' + $name) }
            if (!(Test-Path $name)) { Copy-Item -LiteralPath $source -Destination $name }
        }
        foreach ($name in @('Temp', 'speedyAPI_config.json')) {
            $source = Join-Path $component $name
            if (!(Test-Path $source)) { throw ('Official SDK file missing: ' + $name) }
            if (!(Test-Path $name)) { Copy-Item -LiteralPath $source -Destination $name -Recurse }
        }
    }
    $targetExe = Join-Path $PSScriptRoot 'Connector.exe'
    $running = @(Get-Process -Name Connector -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $targetExe } catch { $false } })
    if ($running.Count -gt 0) { throw 'Connector is already running. Use Alt+Tab to find it, or close it before restarting.' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    if (!(Test-Path $compiler)) { throw '.NET Framework compiler missing.' }
    & $compiler /nologo /codepage:65001 /target:winexe /platform:x86 /out:Connector.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:megaSpeedyAPIDotnet.dll Connector.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed. Please send a screenshot without credentials.' }
    Start-Process -FilePath '.\Connector.exe' -WorkingDirectory $PSScriptRoot
} catch {
    Write-Host $_.Exception.Message
    exit 1
}
