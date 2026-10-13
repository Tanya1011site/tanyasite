$ErrorActionPreference = 'Stop'
try {
    Write-Host 'Installing Cloudflare tunnel through Windows Package Manager.'
    Write-Host 'Only market data will use the tunnel. Broker credentials stay on your computer.'
    $wingetCommand = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (!$wingetCommand) { throw 'Windows Package Manager is missing. Install/update Microsoft App Installer from Microsoft Store, then retry.' }
    & $wingetCommand.Source install --id Cloudflare.cloudflared --exact --source winget --scope user --accept-source-agreements --accept-package-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw 'Installation failed. Please keep this window open and send an error screenshot without credentials.' }
    exit 0
} catch {
    Write-Host $_.Exception.Message
    Read-Host 'Press Enter to close'
    exit 1
}
