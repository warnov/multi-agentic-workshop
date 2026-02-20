# ============================================================================
# Contoso Retail - Script de Despliegue (Consumption Y1 / Windows)
# Taller Multi-Agéntico
# ============================================================================
# Uso:
#   .\deploy.ps1 -TenantName "mi-tenant-temporal"
#   .\deploy.ps1 -TenantName "mi-tenant-temporal" -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" -FabricWarehouseDatabase "<database>"
#   .\deploy.ps1 -TenantName "mi-tenant-temporal" -Location "eastus" -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" -FabricWarehouseDatabase "<database>"
# ============================================================================

param(
    [Parameter(Mandatory = $true, HelpMessage = "Nombre del tenant temporal asignado al attendee.")]
    [string]$TenantName,

    [Parameter(Mandatory = $false, HelpMessage = "Región de Azure (default: eastus).")]
    [string]$Location = "eastus",

    [Parameter(Mandatory = $false, HelpMessage = "Nombre del Resource Group (default: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Endpoint SQL del Warehouse de Fabric (sin protocolo). Ej: xyz.datawarehouse.fabric.microsoft.com")]
    [string]$FabricWarehouseSqlEndpoint = "",

    [Parameter(Mandatory = $false, HelpMessage = "Nombre de la base de datos del Warehouse de Fabric.")]
    [string]$FabricWarehouseDatabase = ""
)

$ErrorActionPreference = "Stop"

# --- Verificar PowerShell 7+ ---
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host ""
    Write-Host "ERROR: Este script requiere PowerShell 7 o superior." -ForegroundColor Red
    Write-Host "  Version detectada: PowerShell $($PSVersionTable.PSVersion)" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Descarga PowerShell 7:" -ForegroundColor Yellow
    Write-Host "    Windows : https://aka.ms/powershell-release?tag=stable  (MSI installer)" -ForegroundColor Cyan
    Write-Host "             o ejecuta:  winget install Microsoft.PowerShell" -ForegroundColor Gray
    Write-Host "    Linux   : https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux" -ForegroundColor Cyan
    Write-Host "    macOS   : https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos" -ForegroundColor Cyan
    Write-Host "             o ejecuta:  brew install powershell/tap/powershell" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Una vez instalado, abre una terminal 'pwsh' (no 'powershell') y vuelve a ejecutar el script." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# Forzar UTF-8 en la consola
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# --- Verificar ExecutionPolicy ---
$execPolicy = Get-ExecutionPolicy -Scope CurrentUser
if ($execPolicy -eq 'Restricted' -or $execPolicy -eq 'Undefined') {
    $systemPolicy = Get-ExecutionPolicy -Scope LocalMachine
    if ($systemPolicy -eq 'Restricted' -or $systemPolicy -eq 'Undefined') {
        Write-Host ""
        Write-Host "ERROR: La ExecutionPolicy no permite ejecutar scripts." -ForegroundColor Red
        Write-Host "  Policy actual (CurrentUser): $execPolicy" -ForegroundColor Red
        Write-Host "  Policy actual (LocalMachine): $systemPolicy" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Ejecuta este comando en pwsh y vuelve a intentar:" -ForegroundColor Yellow
        Write-Host "    Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser" -ForegroundColor Cyan
        Write-Host ""
        exit 1
    }
}

Write-Host "Presiona Enter para default." -ForegroundColor DarkGray

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
    $configureFabricNow = Read-Host "¿Deseas configurar ahora la conexión SQL de Fabric para Lab04? (s/N)"
    if ($configureFabricNow -match '^(s|si|sí|y|yes)$') {
        $FabricWarehouseSqlEndpoint = (Read-Host "FabricWarehouseSqlEndpoint (sin protocolo, sin puerto)").Trim()
        $FabricWarehouseDatabase = (Read-Host "FabricWarehouseDatabase").Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseDatabase = (Read-Host "Falta FabricWarehouseDatabase. Ingresa el valor o deja vacío para omitir").Trim()
}

if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseSqlEndpoint = (Read-Host "Falta FabricWarehouseSqlEndpoint. Ingresa el valor o deja vacío para omitir").Trim()
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Taller Multi-Agéntico - Despliegue" -ForegroundColor Cyan
Write-Host " Plan: Consumption (Y1 / Windows)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Tenant:         $TenantName" -ForegroundColor Yellow
Write-Host "  Location:       $Location" -ForegroundColor Yellow
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor Yellow
Write-Host "  Fabric SQL:     $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)) { '<omitido>' } else { $FabricWarehouseSqlEndpoint })" -ForegroundColor Yellow
Write-Host "  Fabric DB:      $(if ([string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) { '<omitido>' } else { $FabricWarehouseDatabase })" -ForegroundColor Yellow
Write-Host ""

$hasFabricSql = -not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint)
$hasFabricDb = -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)
$hasCompleteFabricConfig = $hasFabricSql -and $hasFabricDb
$FabricWarehouseConnectionString = ""

if ($hasFabricSql -xor $hasFabricDb) {
    Write-Warning "Se recibió solo uno de los parámetros de Fabric. Se omitirán ambos para no configurar una conexión incompleta."
    $FabricWarehouseSqlEndpoint = ""
    $FabricWarehouseDatabase = ""
    $hasCompleteFabricConfig = $false
}

if (-not $hasCompleteFabricConfig) {
    Write-Warning "No se configurará conexión SQL para Lab04 en este despliegue. Deberás ajustarla manualmente luego."
}

# --- 1. Verificar Azure CLI ---
Write-Host "[1/5] Verificando Azure CLI..." -ForegroundColor Green
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Host "  Azure CLI v$($azVersion.'azure-cli') detectado." -ForegroundColor Gray
    Write-Host "  Registrando provider Microsoft.Bing (si aplica)..." -ForegroundColor Gray
    az provider register --namespace Microsoft.Bing --output none 2>$null
} catch {
    Write-Error "Azure CLI no está instalado. Instálalo desde https://aka.ms/installazurecli"
    exit 1
}

# --- 2. Verificar sesión activa ---
Write-Host "[2/5] Verificando sesión de Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No hay sesión activa. Iniciando login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Suscripción: $($account.name) ($($account.id))" -ForegroundColor Gray

# --- 3. Crear Resource Group ---
Write-Host "[3/5] Creando Resource Group '$ResourceGroupName'..." -ForegroundColor Green
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "  Resource Group listo." -ForegroundColor Gray

# Intentar preservar configuración existente (requiere que el RG ya exista)
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
        Write-Host "  Se preservará FabricWarehouseConnectionString existente en la Function App." -ForegroundColor Yellow
    }
}

# --- 4. Desplegar Bicep ---
Write-Host "[4/5] Desplegando infraestructura..." -ForegroundColor Green

# Calcular y mostrar el sufijo antes de desplegar
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
Write-Host "  Sufijo:         $suffixResult" -ForegroundColor Yellow

Write-Host "" -ForegroundColor Gray
Write-Host "  Esto puede tomar ~5 minutos." -ForegroundColor Yellow
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
    Write-Error "No se pudo iniciar el despliegue. Verifica que no haya recursos soft-deleted (az cognitiveservices account list-deleted)."
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
    Write-Error "El deployment '$deploymentName' no se registró en Azure. Verifica errores de validación."
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

    # Verificar si el deployment terminó
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
    # Mostrar error detallado
    az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.error' `
        --output json
    Write-Error "El despliegue falló. Revisa los errores anteriores."
    exit 1
}

# Obtener outputs del deployment exitoso
$result = az deployment group show `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --output json | ConvertFrom-Json

$outputs = $result.properties.outputs
$functionAppName = $outputs.functionAppName.value

# --- 5. Publicar código de la Function App ---
Write-Host "[5/5] Publicando código de FxContosoRetail..." -ForegroundColor Green
$projectDir = Join-Path (Join-Path (Join-Path (Join-Path $scriptDir "..") "..") "code") "api"
$projectDir = Join-Path $projectDir "FxContosoRetail"
$publishDir = Join-Path (Join-Path $projectDir "bin") "publish"

Write-Host "  Compilando proyecto..." -ForegroundColor Gray
$publishOutput = dotnet publish $projectDir --configuration Release --output $publishDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "===== Detalle de error de compilación =====" -ForegroundColor Red
    $publishOutput | ForEach-Object { Write-Host $_ }
    Write-Host "==========================================" -ForegroundColor Red
    Write-Error "Error al compilar el proyecto. Verifica el código."
    exit 1
}

# Crear zip para deployment
$zipPath = Join-Path $env:TEMP "fxcontosoretail-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# Esperar a que el SCM endpoint esté resolvible por DNS
$scmHost = "$functionAppName.scm.azurewebsites.net"
Write-Host "  Esperando a que el endpoint SCM esté disponible..." -ForegroundColor Gray
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
    Write-Warning "  El DNS de $scmHost no resolvió tras 5 minutos. Intentando deploy de todas formas..."
}

# Deploy con reintentos (hasta 3 intentos con espera incremental)
$maxRetries = 3
$deploySuccess = $false
for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
    Write-Host "  Desplegando a $functionAppName (intento $attempt/$maxRetries)..." -ForegroundColor Gray
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
        Write-Host "  ⚠️  Intento $attempt falló. Reintentando en $waitSecs segundos..." -ForegroundColor Yellow
        Start-Sleep -Seconds $waitSecs
    }
}

if (-not $deploySuccess) {
    Write-Error "Error al publicar el código tras $maxRetries intentos. Puedes reintentar manualmente con: az functionapp deployment source config-zip --resource-group $ResourceGroupName --name $functionAppName --src `"$zipPath`""
    exit 1
}

# Limpiar archivos temporales
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  ✅ Código publicado exitosamente." -ForegroundColor Green

# --- Resumen final ---
$functionAppUrl = $outputs.functionAppUrl.value

$apiUrl = "$functionAppUrl/api/OrdersReporter"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " ¡Despliegue completo!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Sufijo único:        $($outputs.suffix.value)" -ForegroundColor White
Write-Host "  Storage Account:     $($outputs.storageAccountName.value)" -ForegroundColor White
Write-Host "  Function App:        $functionAppName" -ForegroundColor White
Write-Host "  Function App Base URL:      $functionAppUrl/api" -ForegroundColor White
Write-Host "  API OrdersReporter:          $apiUrl" -ForegroundColor White
Write-Host "  Foundry Project Endpoint:    $($outputs.foundryProjectEndpoint.value)" -ForegroundColor White
Write-Host "  Bing Grounding Resource:     $($outputs.bingGroundingName.value)" -ForegroundColor White
Write-Host "  Bing Connection Name:        $($outputs.bingConnectionName.value)" -ForegroundColor White
Write-Host "  Bing Connection ID (Julie):  $($outputs.bingConnectionId.value)" -ForegroundColor White
if ($hasCompleteFabricConfig) {
    Write-Host "  Fabric SQL Connection:       actualizada desde parámetros" -ForegroundColor White
}
elseif (-not [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Fabric SQL Connection:       preservada desde configuración existente" -ForegroundColor White
}
else {
    Write-Host "  Fabric SQL Connection:       no configurada" -ForegroundColor Yellow
}
if (-not $hasCompleteFabricConfig -and [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Aviso Lab04:                 No se configuró la conexión SQL (FabricWarehouseConnectionString). Configúrala manualmente en la Function App." -ForegroundColor Yellow
}
Write-Host ""
