<#
.SYNOPSIS
    Generate Gmail OAuth2 refresh token for RadioPad email delivery.

.DESCRIPTION
    This script helps obtain a Gmail OAuth2 refresh token that RadioPad uses
    to send magic-link emails via the Gmail API over HTTPS (port 443).
    This bypasses DigitalOcean's SMTP port block.

.NOTES
    Prerequisites:
    1. Go to https://console.cloud.google.com
    2. Create a project (or select existing)
    3. Enable the Gmail API: APIs & Services > Library > Gmail API > Enable
    4. Create OAuth2 credentials:
       - APIs & Services > Credentials > Create Credentials > OAuth client ID
       - Application type: "Desktop app"  (or "Web application" with http://localhost redirect)
       - Copy the Client ID and Client Secret
    5. Run this script with those values
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ClientId,
    
    [Parameter(Mandatory=$true)]
    [string]$ClientSecret
)

$ErrorActionPreference = "Stop"

$scope = "https://www.googleapis.com/auth/gmail.send"
$redirectUri = "http://localhost:8484"

# Step 1: Generate authorization URL
$authUrl = "https://accounts.google.com/o/oauth2/v2/auth?" +
    "client_id=$ClientId&" +
    "redirect_uri=$([System.Uri]::EscapeDataString($redirectUri))&" +
    "response_type=code&" +
    "scope=$([System.Uri]::EscapeDataString($scope))&" +
    "access_type=offline&" +
    "prompt=consent"

Write-Host ""
Write-Host "=== Gmail OAuth2 Setup for RadioPad ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Step 1: Open this URL in your browser and sign in with manwara575@gmail.com:" -ForegroundColor Yellow
Write-Host ""
Write-Host $authUrl -ForegroundColor Green
Write-Host ""

# Step 2: Start a temporary HTTP listener to capture the redirect
Write-Host "Waiting for Google OAuth callback on http://localhost:8484 ..." -ForegroundColor Yellow
Write-Host "(If your browser doesn't open automatically, paste the URL above manually)" -ForegroundColor Gray
Write-Host ""

# Try to open browser
Start-Process $authUrl -ErrorAction SilentlyContinue

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:8484/")
$listener.Start()

$context = $listener.GetContext()
$authCode = $context.Request.QueryString["code"]
$error_param = $context.Request.QueryString["error"]

# Send response to browser
$responseHtml = "<html><body><h2>Authorization successful!</h2><p>You can close this tab and return to the terminal.</p></body></html>"
if ($error_param) {
    $responseHtml = "<html><body><h2>Authorization failed: $error_param</h2></body></html>"
}
$buffer = [System.Text.Encoding]::UTF8.GetBytes($responseHtml)
$context.Response.ContentLength64 = $buffer.Length
$context.Response.OutputStream.Write($buffer, 0, $buffer.Length)
$context.Response.Close()
$listener.Stop()

if ($error_param) {
    Write-Error "OAuth error: $error_param"
    exit 1
}

if ([string]::IsNullOrWhiteSpace($authCode)) {
    Write-Error "No authorization code received."
    exit 1
}

Write-Host "Authorization code received!" -ForegroundColor Green

# Step 2: Exchange authorization code for refresh token
Write-Host ""
Write-Host "Exchanging authorization code for refresh token..." -ForegroundColor Cyan

$tokenBody = @{
    code          = $authCode
    client_id     = $ClientId
    client_secret = $ClientSecret
    redirect_uri  = $redirectUri
    grant_type    = "authorization_code"
}

try {
    $response = Invoke-RestMethod -Uri "https://oauth2.googleapis.com/token" -Method POST -Body $tokenBody -ContentType "application/x-www-form-urlencoded"
}
catch {
    Write-Error "Token exchange failed: $_"
    exit 1
}

if (-not $response.refresh_token) {
    Write-Error "No refresh_token in response. Make sure you used prompt=consent and access_type=offline."
    Write-Host "Response: $($response | ConvertTo-Json)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== SUCCESS ===" -ForegroundColor Green
Write-Host ""
Write-Host "Refresh Token: $($response.refresh_token)" -ForegroundColor Cyan
Write-Host ""
Write-Host "Add these to your VPS /opt/radiopad/.secrets.env:" -ForegroundColor Yellow
Write-Host ""
Write-Host "RADIOPAD_EMAIL_PROVIDER=gmail"
Write-Host "RADIOPAD_EMAIL_FROM=RadioPad <manwara575@gmail.com>"
Write-Host "RADIOPAD_EMAIL_REPLY_TO=manwara575@gmail.com"
Write-Host "RADIOPAD_GMAIL_CLIENT_ID=$ClientId"
Write-Host "RADIOPAD_GMAIL_CLIENT_SECRET=$ClientSecret"
Write-Host "RADIOPAD_GMAIL_REFRESH_TOKEN=$($response.refresh_token)"
Write-Host ""
Write-Host "Then rebuild containers: docker compose build --no-cache && docker compose up -d" -ForegroundColor Yellow
