# ============================================================================
# Contoso Retail - Deployment Script for Azure Cloud Shell
# Multi-Agent Workshop  |  Plan: Flex Consumption (FC1 / Linux)
# ============================================================================
#
# PREREQUISITES in Cloud Shell:
#   1. Clone the repository (first time only):
#        git clone https://github.com/warnov/multi-agentic-workshop.git
#   2. Run this script:
#        cd multi-agentic-workshop/en/labs/foundry/setup/op-flex
#        pwsh ./deployFromAzure.ps1
#
# Usage:
#   ./deployFromAzure.ps1
#   ./deployFromAzure.ps1 -FabricWarehouseSqlEndpoint "<endpoint>" -FabricWarehouseDatabase "<db>"
#   ./deployFromAzure.ps1 -Location "eastus" -FabricWarehouseSqlEndpoint "<endpoint>" -FabricWarehouseDatabase "<db>"
# TenantName is optional: shown on screen only, does not affect created resources.
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "Optional descriptive label (e.g. attendee tenant number). Displayed on screen only.")]
    [string]$TenantName = "",

    [Parameter(Mandatory = $false, HelpMessage = "Azure region (default: eastus).")]
    [string]$Location = "eastus",

    [Parameter(Mandatory = $false, HelpMessage = "Resource Group name (default: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Fabric Warehouse SQL endpoint (no protocol). E.g.: xyz.datawarehouse.fabric.microsoft.com")]
    [string]$FabricWarehouseSqlEndpoint = "",

    [Parameter(Mandatory = $false, HelpMessage = "Fabric Warehouse database name.")]
    [string]$FabricWarehouseDatabase = "",

    [Parameter(Mandatory = $false, HelpMessage = "GPT deployment capacity in thousands of tokens per minute (default: 30).")]
    [int]$GptDeploymentCapacity = 30
)

$ErrorActionPreference = "Stop"

Write-Host "Press Enter for default." -ForegroundColor DarkGray

if (-not $PSBoundParameters.ContainsKey('Location')) {
    $locationInput = Read-Host "Location [$Location]"
    if (-not [string]::IsNullOrWhiteSpace($locationInput)) {
        $Location = $locationInput.Trim()
    }
}

if (-not $PSBoundParameters.ContainsKey('ResourceGroupName')) {
    $resourceGroupInput = Read-Host "ResourceGroupName [$ResourceGroupName]"
    if (-not [string]::IsNullOrWhiteSpace($resourceGroupInput)) {
        $ResourceGroupName = $resourceGroupInput.Trim()
    }
}

if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $configureFabricNow = Read-Host "Do you want to configure the Fabric SQL connection for Lab04 now? (y/N)"
    if ($configureFabricNow -match '^(y|yes)$') {
        $FabricWarehouseSqlEndpoint = (Read-Host "FabricWarehouseSqlEndpoint (no protocol, no port)").Trim()
        $FabricWarehouseDatabase = (Read-Host "FabricWarehouseDatabase").Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseDatabase = (Read-Host "Missing FabricWarehouseDatabase. Enter the value or leave empty to skip").Trim()
}

if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseSqlEndpoint = (Read-Host "Missing FabricWarehouseSqlEndpoint. Enter the value or leave empty to skip").Trim()
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Multi-Agent Workshop - Deployment" -ForegroundColor Cyan
Write-Host " Plan: Flex Consumption (FC1 / Linux)" -ForegroundColor Cyan
Write-Host " Mode: Azure Cloud Shell" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tenant:         $TenantName" -ForegroundColor Yellow
Write-Host "  Location:       $Location" -ForegroundColor Yellow
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor Yellow
Write-Host "  Fabric SQL:     $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)) { '<omitted>' } else { $FabricWarehouseSqlEndpoint })" -ForegroundColor Yellow
Write-Host "  Fabric DB:      $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) { '<omitted>' } else { $FabricWarehouseDatabase })" -ForegroundColor Yellow
Write-Host ""

$hasFabricSql = -not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)
$hasFabricDb = -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)
$hasCompleteFabricConfig = $hasFabricSql -and $hasFabricDb
$FabricWarehouseConnectionString = ""

if ($hasFabricSql -xor $hasFabricDb) {
    Write-Warning "Only one of the Fabric parameters was provided. Both will be omitted to avoid an incomplete connection."
    $FabricWarehouseSqlEndpoint = ""
    $FabricWarehouseDatabase = ""
    $hasCompleteFabricConfig = $false
}

if (-not $hasCompleteFabricConfig) {
    Write-Warning "SQL connection for Lab04 will not be configured in this deployment. You will need to set it manually afterwards."
}

# --- 1. Verify active session ---
# In Cloud Shell the session is usually active, but we verify just in case.
Write-Host "[1/5] Verifying Azure session..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No active session. Starting login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Subscription: $($account.name) ($($account.id))" -ForegroundColor Gray
Write-Host "  Registering provider Microsoft.Bing (if applicable)..." -ForegroundColor Gray
az provider register --namespace Microsoft.Bing --output none 2>$null

# --- 2. Create Resource Group ---
Write-Host "[2/5] Creating Resource Group '$ResourceGroupName'..." -ForegroundColor Green
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "  Resource Group ready." -ForegroundColor Gray

# Unique suffix used only to probe for an existing Function App name before deployment.
# NOTE: Bicep derives the real suffix via uniqueString(subscriptionId), which produces a different value.
$suffixForNames = $account.id.Replace('-', '').Substring(0, 5).ToLower()

if (-not $hasCompleteFabricConfig -and -not [string]::IsNullOrWhiteSpace($suffixForNames)) {
    $existingFunctionAppName = "func-contosoretail-$suffixForNames"
    $existingConnection = az functionapp config appsettings list `
        --resource-group $ResourceGroupName `
        --name $existingFunctionAppName `
        --query "[?name=='FabricWarehouseConnectionString'].value | [0]" `
        --output tsv 2>$null

    if (-not [string]::IsNullOrWhiteSpace($existingConnection) -and $existingConnection -ne "null") {
        $FabricWarehouseConnectionString = $existingConnection
        Write-Host "  Existing FabricWarehouseConnectionString in Function App will be preserved." -ForegroundColor Yellow
    }
}

# --- 3. Deploy Bicep ---
Write-Host "[3/5] Deploying infrastructure..." -ForegroundColor Green

Write-Host "" -ForegroundColor Gray
Write-Host "  This may take ~5 minutes." -ForegroundColor Yellow
Write-Host ""

$scriptDir = $PSScriptRoot
$templateFile = Join-Path $scriptDir "main.bicep"
$deploymentName = "main"

if (-not (Test-Path $templateFile)) {
    Write-Error "main.bicep not found in '$scriptDir'. Make sure to run the script from the op-flex folder of the cloned repository."
    exit 1
}

# Launch deployment in background (--no-wait)
az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $templateFile `
    --parameters location=$Location tenantName=$TenantName gptDeploymentCapacity=$GptDeploymentCapacity fabricWarehouseSqlEndpoint=$FabricWarehouseSqlEndpoint fabricWarehouseDatabase=$FabricWarehouseDatabase fabricWarehouseConnectionString="$FabricWarehouseConnectionString" `
    --name $deploymentName `
    --no-wait `
    --output none

if ($LASTEXITCODE -ne 0) {
    Write-Error "Could not start the deployment. Verify there are no soft-deleted resources (az cognitiveservices account list-deleted)."
    exit 1
}

# Wait for the deployment to appear in ARM
$retries = 0
do {
    Start-Sleep -Seconds 3
    $retries++
    $depState = az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.provisioningState' `
        --output tsv 2>$null
} while (-not $depState -and $retries -lt 10)

if (-not $depState) {
    Write-Error "Deployment '$deploymentName' was not registered in Azure. Check for validation errors."
    exit 1
}

# Track resource by resource
$completedOps = @{}
$deployFailed = $false

while ($true) {
    Start-Sleep -Seconds 3

    $opsJson = az deployment operation group list `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --output json 2>$null

    if (-not $opsJson) { continue }
    $ops = $opsJson | ConvertFrom-Json

    foreach ($op in $ops) {
        $resType = $op.properties.targetResource.resourceType
        $resName = $op.properties.targetResource.resourceName
        $status  = $op.properties.provisioningState

        if (-not $resType -or -not $resName) { continue }

        $key = "$resType/$resName"
        $prevStatus = $completedOps[$key]
        if ($prevStatus -ne $status) {
            $completedOps[$key] = $status
            $shortType = $resType -replace '^Microsoft\.', '' -replace '/providers/.*', ''
            switch ($status) {
                'Running'   { Write-Host "  ⏳ $shortType/$resName ..." -ForegroundColor Gray }
                'Succeeded' { Write-Host "  ✅ $shortType/$resName" -ForegroundColor Green }
                'Failed'    { Write-Host "  ❌ $shortType/$resName" -ForegroundColor Red; $deployFailed = $true }
            }
        }
    }

    $depJson = az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.provisioningState' `
        --output tsv 2>$null

    if ($depJson -eq 'Succeeded' -or $depJson -eq 'Failed' -or $depJson -eq 'Canceled') {
        break
    }
}

if ($depJson -ne 'Succeeded') {
    Write-Host ""
    az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.error' `
        --output json
    Write-Error "Deployment failed. Review the errors above."
    exit 1
}

# Get outputs from successful deployment
$result = az deployment group show `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --output json | ConvertFrom-Json

$outputs = $result.properties.outputs
$functionAppName = $outputs.functionAppName.value

Write-Host ""
Write-Host "  Unique suffix (Bicep): $($outputs.suffix.value)" -ForegroundColor Cyan
Write-Host ""

# --- 4. Build and publish Function App code ---
Write-Host "[4/5] Building FxContosoRetail..." -ForegroundColor Green

$projectDir = Join-Path $scriptDir "../../code/api/FxContosoRetail"
$projectDir = (Resolve-Path $projectDir).Path
$publishDir = Join-Path $projectDir "bin/publish"

$publishOutput = dotnet publish $projectDir --configuration Release --output $publishDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "===== Build error detail =====" -ForegroundColor Red
    $publishOutput | ForEach-Object { Write-Host $_ }
    Write-Host "==============================" -ForegroundColor Red
    Write-Error "Error building the project. Check the code."
    exit 1
}

# Create zip for deployment
# FIX: Compress-Archive in PowerShell 7 on Linux excludes hidden directories (dotfiles)
# such as .azurefunctions, which Flex Consumption validates as mandatory.
# The system zip command is used instead, as '.' includes all files without exception.
$zipPath = "/tmp/fxcontosoretail-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Push-Location $publishDir
& zip -r $zipPath . | Out-Null
Pop-Location

# Wait for SCM endpoint to become available
# Note: in Cloud Shell (Linux) [System.Net.Dns] is used since Resolve-DnsName is not available.
$scmHost = "$functionAppName.scm.azurewebsites.net"
Write-Host "[5/5] Waiting for SCM endpoint to become available..." -ForegroundColor Green
$dnsReady = $false
for ($i = 0; $i -lt 30; $i++) {
    try {
        [System.Net.Dns]::GetHostAddresses($scmHost) | Out-Null
        $dnsReady = $true
        break
    } catch {
        Start-Sleep -Seconds 10
    }
}
if (-not $dnsReady) {
    Write-Warning "  DNS for $scmHost did not resolve after 5 minutes. Attempting deploy anyway..."
}

# Deploy with retries
$maxRetries = 3
$deploySuccess = $false
for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
    Write-Host "  Deploying to $functionAppName (attempt $attempt/$maxRetries)..." -ForegroundColor Gray
    az functionapp deployment source config-zip `
        --resource-group $ResourceGroupName `
        --name $functionAppName `
        --src $zipPath `
        --timeout 600 `
        --output none 2>&1 | Out-Null

    if ($LASTEXITCODE -eq 0) {
        $deploySuccess = $true
        break
    }

    if ($attempt -lt $maxRetries) {
        $waitSecs = $attempt * 30
        Write-Host "  ⚠️  Attempt $attempt failed. Retrying in $waitSecs seconds..." -ForegroundColor Yellow
        Start-Sleep -Seconds $waitSecs
    }
}

if (-not $deploySuccess) {
    Write-Error "Error publishing code after $maxRetries attempts. You can retry manually with: az functionapp deployment source config-zip --resource-group $ResourceGroupName --name $functionAppName --src '$zipPath'"
    exit 1
}

# Clean up temporary files
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  ✅ Code published successfully." -ForegroundColor Green

# --- Final summary ---
$functionAppUrl = $outputs.functionAppUrl.value
$apiUrl = "$functionAppUrl/api/OrdersReporter"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Deployment complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Unique suffix:               $($outputs.suffix.value)" -ForegroundColor White
Write-Host "  Storage Account:             $($outputs.storageAccountName.value)" -ForegroundColor White
Write-Host "  Function App:                $functionAppName" -ForegroundColor White
Write-Host "  Function App Base URL:       $functionAppUrl/api" -ForegroundColor White
Write-Host "  API OrdersReporter:          $apiUrl" -ForegroundColor White
Write-Host "  Foundry Project Endpoint:    $($outputs.foundryProjectEndpoint.value)" -ForegroundColor White
Write-Host "  Bing Grounding Resource:     $($outputs.bingGroundingName.value)" -ForegroundColor White
Write-Host "  Bing Connection Name:        $($outputs.bingConnectionName.value)" -ForegroundColor White
Write-Host "  Bing Connection Id (Julie):  $($outputs.bingConnectionName.value)" -ForegroundColor White
if ($hasCompleteFabricConfig) {
    Write-Host "  Fabric SQL Connection:       updated from parameters" -ForegroundColor White
}
elseif (-not [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Fabric SQL Connection:       preserved from existing configuration" -ForegroundColor White
}
else {
    Write-Host "  Fabric SQL Connection:       not configured" -ForegroundColor Yellow
}
if (-not $hasCompleteFabricConfig -and [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Lab04 notice:                SQL connection (FabricWarehouseConnectionString) was not configured. Set it manually in the Function App." -ForegroundColor Yellow
}
Write-Host ""
