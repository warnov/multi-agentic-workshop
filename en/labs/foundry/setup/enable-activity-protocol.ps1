# ============================================================================
# Contoso Retail - Enable the Activity Protocol on the Anders agent
# Multi-Agentic Workshop
# ============================================================================
# Copilot Studio invokes Foundry agents using the Activity Protocol (the Azure
# Bot Service protocol). A freshly created agent only exposes the Responses API
# protocol, so Copilot Studio fails to connect with:
#   HTTP 400 - Agent Anders endpoint does not support activity.
#
# This script enables the 'activity' protocol on the agent endpoint through the
# Foundry REST API. It is the code equivalent of the manual "publish to
# M365/Teams" step in the portal. It keeps 'responses' and the 'Entra' scheme
# (which Copilot Studio uses) so it does not break SDK/portal interaction.
#
# Preview API: uses api-version=v1 + the Foundry-Features header.
#
# Usage:
#   ./enable-activity-protocol.ps1 -Suffix "sytao"
#   ./enable-activity-protocol.ps1 -ProjectEndpoint "https://ais-...azure.com/api/projects/aip-..." -AgentName "Anders"
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "5-character suffix assigned to the attendee.")]
    [ValidatePattern('^[a-z0-9]{5}$')]
    [string]$Suffix,

    [Parameter(Mandatory = $false, HelpMessage = "Foundry project endpoint. If provided, takes precedence over -Suffix.")]
    [string]$ProjectEndpoint,

    [Parameter(Mandatory = $false, HelpMessage = "Agent name (default: Anders).")]
    [string]$AgentName = "Anders"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Multi-Agentic Workshop" -ForegroundColor Cyan
Write-Host " Enable Activity Protocol ($AgentName)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# --- 1. Resolve the project endpoint ---
if ([string]::IsNullOrWhiteSpace($ProjectEndpoint)) {
    if ([string]::IsNullOrWhiteSpace($Suffix)) {
        Write-Error "You must provide -Suffix or -ProjectEndpoint."
        exit 1
    }
    $ProjectEndpoint = "https://ais-contosoretail-$Suffix.services.ai.azure.com/api/projects/aip-contosoretail-$Suffix"
}
$ProjectEndpoint = $ProjectEndpoint.TrimEnd('/')
Write-Host "  Endpoint: $ProjectEndpoint" -ForegroundColor Yellow
Write-Host "  Agent:    $AgentName" -ForegroundColor Yellow
Write-Host ""

# --- 2. Verify the Azure session ---
Write-Host "[1/3] Verifying Azure session..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No active session. Signing in..." -ForegroundColor Yellow
    az login | Out-Null
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Subscription: $($account.name)" -ForegroundColor Gray

# --- 3. Get a token for the Foundry data plane ---
Write-Host "[2/3] Getting token (audience https://ai.azure.com)..." -ForegroundColor Green
$token = az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "Could not obtain an access token."
    exit 1
}

# --- 4. Enable the 'activity' protocol via PATCH ---
Write-Host "[3/3] Enabling the 'activity' protocol on the agent..." -ForegroundColor Green
$uri = "$ProjectEndpoint/agents/${AgentName}?api-version=v1"
$headers = @{
    Authorization      = "Bearer $token"
    "Foundry-Features" = "AgentEndpoints=V1Preview"
}
$body = @{
    agent_endpoint = @{
        protocol_configuration = @{
            responses = @{}
            activity  = @{}
        }
        authorization_schemes = @(
            @{ type = "Entra" },
            @{ type = "BotServiceRbac" }
        )
    }
} | ConvertTo-Json -Depth 6

try {
    Invoke-RestMethod -Method Patch -Uri $uri -Headers $headers `
        -ContentType "application/merge-patch+json" -Body $body | Out-Null
    Write-Host ""
    Write-Host "  Activity Protocol enabled on '$AgentName'." -ForegroundColor Green
    Write-Host "  You can now connect Anders from Copilot Studio." -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Warning "Could not enable the protocol through the API (remember it is preview)."
    Write-Warning "Detail: $($_.Exception.Message)"
    Write-Host "  Alternative: publish Anders to M365/Teams from the Foundry portal (see lab03)." -ForegroundColor Yellow
    exit 1
}
