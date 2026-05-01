# Reads the current ngrok HTTPS URL from the local ngrok dashboard, then registers
# the webhook on EVERY Evolution API instance owned by this Railway account.
#
# Use this whenever ngrok restarts and gives you a new free subdomain.
#
# Secrets are NOT hardcoded — they're read from ChatCRM.MVC/appsettings.Development.json
# (which is gitignored). If you need to rotate them, edit that file and re-run this
# script to push the new webhook secret to every instance.
#
# Usage:  pwsh ./scripts/reregister-all-webhooks.ps1

$ErrorActionPreference = "Stop"

$configPath = Join-Path $PSScriptRoot "..\ChatCRM.MVC\appsettings.Development.json"
if (-not (Test-Path $configPath)) {
    Write-Host "[ERROR] $configPath not found. Copy appsettings.json and fill in real Evolution credentials." -ForegroundColor Red
    exit 1
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$EvolutionUrl    = $config.Evolution.BaseUrl
$EvolutionApiKey = $config.Evolution.ApiKey
$WebhookSecret   = $config.Evolution.WebhookSecret

if (-not $EvolutionUrl -or -not $EvolutionApiKey -or -not $WebhookSecret) {
    Write-Host "[ERROR] Evolution.BaseUrl / ApiKey / WebhookSecret missing in appsettings.Development.json" -ForegroundColor Red
    exit 1
}

Write-Host "==> Reading ngrok URL from http://127.0.0.1:4040 ..." -ForegroundColor Cyan
try {
    $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -ErrorAction Stop
} catch {
    Write-Host "[ERROR] ngrok isn't running. Start it first:  ngrok http 5128" -ForegroundColor Red
    exit 1
}

$https = $tunnels.tunnels | Where-Object { $_.public_url -like "https://*" } | Select-Object -First 1
if (-not $https) {
    Write-Host "[ERROR] No HTTPS tunnel found." -ForegroundColor Red
    exit 1
}

$webhookUrl = "$($https.public_url)/api/evolution/webhook"
Write-Host "    ngrok: $($https.public_url)" -ForegroundColor Green
Write-Host "    target webhook: $webhookUrl" -ForegroundColor Green

Write-Host ""
Write-Host "==> Fetching all instances ..." -ForegroundColor Cyan
$instances = Invoke-RestMethod -Method Get -Uri "$EvolutionUrl/instance/fetchInstances" `
    -Headers @{ "apikey" = $EvolutionApiKey }

if (-not $instances -or $instances.Count -eq 0) {
    Write-Host "[ERROR] No instances found." -ForegroundColor Red
    exit 1
}

$body = @{
    webhook = @{
        enabled         = $true
        url             = $webhookUrl
        webhookByEvents = $false
        webhookBase64   = $false
        events          = @("MESSAGES_UPSERT")
        headers         = @{ "x-webhook-secret" = $WebhookSecret }
    }
} | ConvertTo-Json -Depth 5

foreach ($instance in $instances) {
    $name = $instance.name
    Write-Host ""
    Write-Host "==> Updating webhook for '$name' ..." -ForegroundColor Cyan
    try {
        Invoke-RestMethod `
            -Method Post `
            -Uri "$EvolutionUrl/webhook/set/$name" `
            -Headers @{ "apikey" = $EvolutionApiKey } `
            -ContentType "application/json" `
            -Body $body | Out-Null
        Write-Host "    OK" -ForegroundColor Green
    } catch {
        Write-Host "    [ERROR] $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done. All instance webhooks now point at $webhookUrl" -ForegroundColor Green
