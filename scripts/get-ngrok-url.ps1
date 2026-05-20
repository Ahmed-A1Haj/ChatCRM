# Reads the current ngrok HTTPS tunnel URL from the local ngrok dashboard.
# Run this on your machine AFTER `ngrok http 5128` is running.
#
# Usage:  pwsh ./scripts/get-ngrok-url.ps1

try {
    $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -ErrorAction Stop
} catch {
    Write-Host "[ERROR] ngrok isn't running." -ForegroundColor Red
    Write-Host "Start it in another terminal first:  ngrok http 5128" -ForegroundColor Yellow
    exit 1
}

$https = $tunnels.tunnels | Where-Object { $_.public_url -like "https://*" } | Select-Object -First 1

if (-not $https) {
    Write-Host "[ERROR] No HTTPS tunnel found. Is ngrok configured for HTTPS?" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "==> Your ngrok HTTPS URL:" -ForegroundColor Cyan
Write-Host "    $($https.public_url)" -ForegroundColor Green
Write-Host ""
Write-Host "Paste it into ChatCRM.MVC/appsettings.Development.json:" -ForegroundColor Yellow
Write-Host "  ""Evolution"": {"
Write-Host "    ""WebhookPublicBaseUrl"": ""$($https.public_url)"""
Write-Host "  }"
Write-Host ""
