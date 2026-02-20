# ============================================================================
# Contoso Retail - Unlock Storage Account
# Multi-Agent Workshop
# ============================================================================
# When a subscription policy disables public network access on the
# Storage Account, the Function App fails to start (503 error) because
# the Functions host cannot reach its own backing storage.
#
# This script identifies the attendee's Storage Account from the suffix
# assigned during the initial setup and re-enables public network access.
#
# Usage:
#   .\unlock-storage.ps1
#   .\unlock-storage.ps1 -ResourceGroupName "rg-contoso-retail"
#   .\unlock-storage.ps1 -Suffix "sytao"
#   .\unlock-storage.ps1 -FunctionAppName "func-contosoretail-sytao"
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "5-character suffix assigned to the attendee during initial setup. If not provided, it is detected automatically from the Function App.")]
    [ValidatePattern('^[a-z0-9]{5}$')]
    [string]$Suffix,

    [Parameter(Mandatory = $false, HelpMessage = "Resource Group name (default: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Exact Function App name. If provided, used to derive the suffix.")]
    [string]$FunctionAppName
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Multi-Agent Workshop" -ForegroundColor Cyan
Write-Host " Unlock Storage Account" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Suffix:          $(if ([string]::IsNullOrWhiteSpace($Suffix)) { '<auto>' } else { $Suffix })" -ForegroundColor Yellow
Write-Host "  Storage Account: $(if ([string]::IsNullOrWhiteSpace($Suffix)) { '<auto>' } else { "stcontosoretail$Suffix" })" -ForegroundColor Yellow
Write-Host "  Resource Group:  $ResourceGroupName" -ForegroundColor Yellow
Write-Host ""

# --- 1. Check Azure CLI ---
Write-Host "[1/4] Checking Azure CLI..." -ForegroundColor Green
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Host "  Azure CLI v$($azVersion.'azure-cli') detected." -ForegroundColor Gray
} catch {
    Write-Error "Azure CLI is not installed. Install it from https://aka.ms/installazurecli"
    exit 1
}

# --- 2. Check active session ---
Write-Host "[2/4] Checking Azure session..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No active session found. Starting login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Subscription: $($account.name) ($($account.id))" -ForegroundColor Gray

if ([string]::IsNullOrWhiteSpace($Suffix)) {
    Write-Host "  Detecting suffix automatically..." -ForegroundColor Yellow

    if (-not [string]::IsNullOrWhiteSpace($FunctionAppName)) {
        if ($FunctionAppName -notmatch '^func-contosoretail-([a-z0-9]{5})$') {
            Write-Error "FunctionAppName '$FunctionAppName' does not match the expected format 'func-contosoretail-<suffix>'."
            exit 1
        }

        $Suffix = $Matches[1]
    }
    else {
        $functionAppsTsv = az functionapp list `
            --resource-group $ResourceGroupName `
            --query "[?starts_with(name, 'func-contosoretail-')].name" `
            --output tsv 2>$null

        if (-not $functionAppsTsv) {
            Write-Error "No Function Apps with prefix 'func-contosoretail-' were found in Resource Group '$ResourceGroupName'. Use -Suffix or -FunctionAppName."
            exit 1
        }

        $functionApps = @($functionAppsTsv -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

        if ($functionApps.Count -gt 1) {
            Write-Host "  Multiple candidate Function Apps found:" -ForegroundColor Yellow
            $functionApps | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
            Write-Error "Specify -Suffix or -FunctionAppName to avoid ambiguity."
            exit 1
        }

        $FunctionAppName = $functionApps[0]

        if ($FunctionAppName -notmatch '^func-contosoretail-([a-z0-9]{5})$') {
            Write-Error "Could not derive the suffix from Function App '$FunctionAppName'."
            exit 1
        }

        $Suffix = $Matches[1]
    }
}

$storageAccountName = "stcontosoretail$Suffix"
Write-Host "  Suffix resolved:  $Suffix" -ForegroundColor Gray
Write-Host "  Storage resolved: $storageAccountName" -ForegroundColor Gray

# --- 3. Check and unlock Storage Account ---
Write-Host "[3/4] Checking Storage Account '$storageAccountName'..." -ForegroundColor Green

$storageJson = az storage account show `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --query "{publicNetworkAccess:publicNetworkAccess, provisioningState:provisioningState}" `
    --output json 2>$null

if (-not $storageJson) {
    Write-Error "Storage Account '$storageAccountName' not found in Resource Group '$ResourceGroupName'."
    exit 1
}

$storage = $storageJson | ConvertFrom-Json
Write-Host "  Estado actual: publicNetworkAccess = $($storage.publicNetworkAccess)" -ForegroundColor Gray

if ($storage.publicNetworkAccess -eq "Enabled") {
    Write-Host ""
    Write-Host "  Storage Account already has public network access enabled. No action required." -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host "  Enabling public network access..." -ForegroundColor Yellow
az storage account update `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --public-network-access Enabled `
    --output none

Write-Host "  Public network access enabled." -ForegroundColor Green

# --- 4. Restart Function App so it reconnects to storage ---
$functionAppName = if (-not [string]::IsNullOrWhiteSpace($FunctionAppName)) { $FunctionAppName } else { "func-contosoretail-$Suffix" }
Write-Host "[4/4] Restarting Function App '$functionAppName'..." -ForegroundColor Green

az functionapp restart `
    --name $functionAppName `
    --resource-group $ResourceGroupName `
    --output none 2>$null

if ($LASTEXITCODE -eq 0) {
    Write-Host "  Function App restarted." -ForegroundColor Green
} else {
    Write-Host "  Could not restart the Function App (it may not exist yet). Continuing..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Storage Account unlocked" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Storage Account '$storageAccountName' now has public" -ForegroundColor Gray
Write-Host "  network access enabled and the Function App was restarted." -ForegroundColor Gray
Write-Host ""
