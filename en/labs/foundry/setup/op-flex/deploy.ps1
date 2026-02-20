# ============================================================================
# Contoso Retail - Deployment Script (Flex Consumption FC1 / Linux)
# Multi-Agent Workshop
# ============================================================================
# Usage:
#   .\deploy.ps1 -TenantName "my-temporary-tenant"
#   .\deploy.ps1 -TenantName "my-temporary-tenant" -FabricWarehouseSqlEndpoint "<fabric-sql-endpoint>" -FabricWarehouseDatabase "<database>"
#   .\deploy.ps1 -TenantName "my-temporary-tenant" -Location "eastus" -FabricWarehouseSqlEndpoint "<fabric-sql-endpoint>" -FabricWarehouseDatabase "<database>"
# ============================================================================

param(
    [Parameter(Mandatory = $true, HelpMessage = "Temporary tenant name assigned to the attendee.")]
    [string]$TenantName,

    [Parameter(Mandatory = $false, HelpMessage = "Azure region (default: eastus).")]
    [string]$Location = "eastus",

    [Parameter(Mandatory = $false, HelpMessage = "Resource Group name (default: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Fabric Warehouse SQL endpoint (no protocol). E.g.: xyz.datawarehouse.fabric.microsoft.com")]
    [string]$FabricWarehouseSqlEndpoint = "",

    [Parameter(Mandatory = $false, HelpMessage = "Fabric Warehouse database name.")]
    [string]$FabricWarehouseDatabase = ""
)

$ErrorActionPreference = "Stop"

# --- Check PowerShell 7+ ---
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host ""
    Write-Host "ERROR: This script requires PowerShell 7 or higher." -ForegroundColor Red
    Write-Host "  Detected version: PowerShell $($PSVersionTable.PSVersion)" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Download PowerShell 7:" -ForegroundColor Yellow
    Write-Host "    Windows : https://aka.ms/powershell-release?tag=stable  (MSI installer)" -ForegroundColor Cyan
    Write-Host "             or run:  winget install Microsoft.PowerShell" -ForegroundColor Gray
    Write-Host "    Linux   : https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux" -ForegroundColor Cyan
    Write-Host "    macOS   : https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos" -ForegroundColor Cyan
    Write-Host "             or run:  brew install powershell/tap/powershell" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Once installed, open a 'pwsh' terminal (not 'powershell') and run the script again." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# Forzar UTF-8 en la consola
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# --- Check ExecutionPolicy ---
$execPolicy = Get-ExecutionPolicy -Scope CurrentUser
if ($execPolicy -eq 'Restricted' -or $execPolicy -eq 'Undefined') {
    $systemPolicy = Get-ExecutionPolicy -Scope LocalMachine
    if ($systemPolicy -eq 'Restricted' -or $systemPolicy -eq 'Undefined') {
        Write-Host ""
        Write-Host "ERROR: The ExecutionPolicy does not allow running scripts." -ForegroundColor Red
        Write-Host "  Current policy (CurrentUser): $execPolicy" -ForegroundColor Red
        Write-Host "  Current policy (LocalMachine): $systemPolicy" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Run this command in pwsh and try again:" -ForegroundColor Yellow
        Write-Host "    Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser" -ForegroundColor Cyan
        Write-Host ""
        exit 1
    }
}

Write-Host "Press Enter to accept defaults." -ForegroundColor DarkGray

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
    $FabricWarehouseDatabase = (Read-Host "FabricWarehouseDatabase is missing. Enter a value or leave blank to skip").Trim()
}

if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseSqlEndpoint = (Read-Host "FabricWarehouseSqlEndpoint is missing. Enter a value or leave blank to skip").Trim()
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Multi-Agent Workshop - Deployment" -ForegroundColor Cyan
Write-Host " Plan: Flex Consumption (FC1 / Linux)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tenant:         $TenantName" -ForegroundColor Yellow
Write-Host "  Location:       $Location" -ForegroundColor Yellow
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor Yellow
Write-Host "  Fabric SQL:     $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)) { '<skipped>' } else { $FabricWarehouseSqlEndpoint })" -ForegroundColor Yellow
    Write-Host "  Fabric DB:      $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) { '<skipped>' } else { $FabricWarehouseDatabase })" -ForegroundColor Yellow
Write-Host ""

$hasFabricSql = -not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)
$hasFabricDb = -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)
$hasCompleteFabricConfig = $hasFabricSql -and $hasFabricDb
$FabricWarehouseConnectionString = ""

if ($hasFabricSql -xor $hasFabricDb) {
    Write-Warning "Only one of the Fabric parameters was provided. Both will be skipped to avoid an incomplete connection."
    $FabricWarehouseSqlEndpoint = ""
    $FabricWarehouseDatabase = ""
    $hasCompleteFabricConfig = $false
}

if (-not $hasCompleteFabricConfig) {
    Write-Warning "SQL connection for Lab04 will not be configured in this deployment. You will need to set it manually later."
}

# --- 1. Check Azure CLI ---
Write-Host "[1/5] Checking Azure CLI..." -ForegroundColor Green
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Host "  Azure CLI v$($azVersion.'azure-cli') detected." -ForegroundColor Gray
    Write-Host "  Registering Microsoft.Bing provider (if needed)..." -ForegroundColor Gray
    az provider register --namespace Microsoft.Bing --output none 2>$null
} catch {
    Write-Error "Azure CLI is not installed. Install it from https://aka.ms/installazurecli"
    exit 1
}

# --- 2. Check active session ---
Write-Host "[2/5] Checking Azure session..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No active session found. Starting login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Subscription: $($account.name) ($($account.id))" -ForegroundColor Gray

# --- 3. Create Resource Group ---
Write-Host "[3/5] Creating Resource Group '$ResourceGroupName'..." -ForegroundColor Green
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "  Resource Group ready." -ForegroundColor Gray

# Attempt to preserve existing configuration (requires the RG to already exist)
$suffixForNames = $null
if (-not [string]::IsNullOrWhiteSpace($TenantName)) {
    $suffixTemplateForPreserve = @'
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
  "contentVersion": "1.0.0.0",
  "parameters": { "t": { "type": "string" } },
  "resources": [],
  "outputs": { "s": { "type": "string", "value": "[substring(uniqueString(parameters('t')),0,5)]" } }
}
'@
    $suffixTempFileForPreserve = Join-Path $env:TEMP "suffix-calc-preserve.json"
    [System.IO.File]::WriteAllText($suffixTempFileForPreserve, $suffixTemplateForPreserve, [System.Text.UTF8Encoding]::new($false))
    $suffixForNames = az deployment group create `
        --resource-group $ResourceGroupName `
        --template-file $suffixTempFileForPreserve `
        --parameters t=$TenantName `
        --name "suffix-calc-preserve" `
        --query 'properties.outputs.s.value' `
        --output tsv 2>$null
    Remove-Item $suffixTempFileForPreserve -Force -ErrorAction SilentlyContinue
}

if (-not $hasCompleteFabricConfig -and -not [string]::IsNullOrWhiteSpace($suffixForNames)) {
    $existingFunctionAppName = "func-contosoretail-$suffixForNames"
    $existingConnection = az functionapp config appsettings list `
        --resource-group $ResourceGroupName `
        --name $existingFunctionAppName `
        --query "[?name=='FabricWarehouseConnectionString'].value | [0]" `
        --output tsv 2>$null

    if (-not [string]::IsNullOrWhiteSpace($existingConnection) -and $existingConnection -ne "null") {
        $FabricWarehouseConnectionString = $existingConnection
        Write-Host "  Existing FabricWarehouseConnectionString from the Function App will be preserved." -ForegroundColor Yellow
    }
}

# --- 4. Deploy Bicep ---
Write-Host "[4/5] Deploying infrastructure..." -ForegroundColor Green

# Calculate and display the suffix before deploying
$suffixTemplate = @'
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
  "contentVersion": "1.0.0.0",
  "parameters": { "t": { "type": "string" } },
  "resources": [],
  "outputs": { "s": { "type": "string", "value": "[substring(uniqueString(parameters('t')),0,5)]" } }
}
'@
$suffixTempFile = Join-Path $env:TEMP "suffix-calc.json"
[System.IO.File]::WriteAllText($suffixTempFile, $suffixTemplate, [System.Text.UTF8Encoding]::new($false))
$suffixResult = az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $suffixTempFile `
    --parameters t=$TenantName `
    --name "suffix-calc" `
    --query 'properties.outputs.s.value' `
    --output tsv 2>$null
Remove-Item $suffixTempFile -Force -ErrorAction SilentlyContinue
Write-Host "  Suffix:         $suffixResult" -ForegroundColor Yellow

Write-Host "" -ForegroundColor Gray
Write-Host "  This may take ~5 minutes." -ForegroundColor Yellow
Write-Host ""
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$templateFile = Join-Path $scriptDir "main.bicep"
$deploymentName = "main"

# Lanzar despliegue en background (--no-wait)
az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $templateFile `
    --parameters tenantName=$TenantName location=$Location fabricWarehouseSqlEndpoint=$FabricWarehouseSqlEndpoint fabricWarehouseDatabase=$FabricWarehouseDatabase fabricWarehouseConnectionString="$FabricWarehouseConnectionString" `
    --name $deploymentName `
    --no-wait `
    --output none

if ($LASTEXITCODE -ne 0) {
    Write-Error "Could not start the deployment. Check that there are no soft-deleted resources (az cognitiveservices account list-deleted)."
    exit 1
}

# Esperar a que el deployment aparezca en ARM (~5 segundos)
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

# Seguimiento recurso a recurso
$completedOps = @{}
$spinChars = @('|', '/', '-', '\\')
$spinIdx = 0
$deployFailed = $false

while ($true) {
    Start-Sleep -Seconds 3

    # Obtener operaciones del deployment
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

        # Mostrar solo transiciones nuevas
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

    # Check if the deployment has finished
    $depJson = az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.provisioningState' `
        --output tsv 2>$null

    if ($depJson -eq 'Succeeded' -or $depJson -eq 'Failed' -or $depJson -eq 'Canceled') {
        break
    }

    $spinIdx = ($spinIdx + 1) % $spinChars.Count
}

if ($depJson -ne 'Succeeded') {
    Write-Host ""
    # Show detailed error
    az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.error' `
        --output json
    Write-Error "Deployment failed. Review the errors above."
    exit 1
}

# Obtener outputs del deployment exitoso
$result = az deployment group show `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --output json | ConvertFrom-Json

$outputs = $result.properties.outputs
$functionAppName = $outputs.functionAppName.value

# --- 5. Publish Function App code ---
Write-Host "[5/5] Publishing FxContosoRetail code..." -ForegroundColor Green
$projectDir = Join-Path (Join-Path (Join-Path (Join-Path $scriptDir "..") "..") "code") "api"
$projectDir = Join-Path $projectDir "FxContosoRetail"
$publishDir = Join-Path (Join-Path $projectDir "bin") "publish"

Write-Host "  Building project..." -ForegroundColor Gray
$publishOutput = dotnet publish $projectDir --configuration Release --output $publishDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "" 
    Write-Host "===== Build error details =====" -ForegroundColor Red
    $publishOutput | ForEach-Object { Write-Host $_ }
    Write-Host "===============================" -ForegroundColor Red
    Write-Error "Error building the project. Check the code."
    exit 1
}

# Crear zip para deployment
$zipPath = Join-Path $env:TEMP "fxcontosoretail-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# Wait for the SCM endpoint to be resolvable via DNS (Flex Consumption takes a moment)
$scmHost = "$functionAppName.scm.azurewebsites.net"
Write-Host "  Waiting for the SCM endpoint to become available..." -ForegroundColor Gray
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

# Deploy con reintentos (hasta 3 intentos con espera incremental)
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
    Write-Error "Error publishing the code after $maxRetries attempts. You can retry manually with: az functionapp deployment source config-zip --resource-group $ResourceGroupName --name $functionAppName --src `"$zipPath`""
    exit 1
}

# Limpiar archivos temporales
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
Write-Host "  Unique suffix:       $($outputs.suffix.value)" -ForegroundColor White
Write-Host "  Storage Account:     $($outputs.storageAccountName.value)" -ForegroundColor White
Write-Host "  Function App:        $functionAppName" -ForegroundColor White
Write-Host "  Function App Base URL:      $functionAppUrl/api" -ForegroundColor White
Write-Host "  API OrdersReporter:          $apiUrl" -ForegroundColor White
Write-Host "  Foundry Project Endpoint:    $($outputs.foundryProjectEndpoint.value)" -ForegroundColor White
Write-Host "  Bing Grounding Resource:     $($outputs.bingGroundingName.value)" -ForegroundColor White
Write-Host "  Bing Connection Name:        $($outputs.bingConnectionName.value)" -ForegroundColor White
Write-Host "  Bing Connection ID (Julie):  $($outputs.bingConnectionId.value)" -ForegroundColor White
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
