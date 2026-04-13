# ============================================================================
# Contoso Retail - Script de Despliegue para Azure Cloud Shell
# Taller Multi-Agéntico  |  Plan: Flex Consumption (FC1 / Linux)
# ============================================================================
#
# REQUISITOS PREVIOS en Cloud Shell:
#   1. Clona el repositorio (solo la primera vez):
#        git clone https://github.com/<org>/taller-multi-agentic.git
#   2. Ejecuta este script:
#        cd taller-multi-agentic/labs/foundry/setup/op-flex
#        pwsh ./deployFromAzure.ps1 -TenantName "mi-tenant"
#
# Uso:
#   ./deployFromAzure.ps1
#   ./deployFromAzure.ps1 -FabricWarehouseSqlEndpoint "<endpoint>" -FabricWarehouseDatabase "<db>"
#   ./deployFromAzure.ps1 -Location "eastus" -FabricWarehouseSqlEndpoint "<endpoint>" -FabricWarehouseDatabase "<db>"
# TenantName es opcional: solo se muestra en pantalla, no afecta a los recursos creados.
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "Etiqueta descriptiva opcional (ej: número de tenant del attendee). Solo se muestra en pantalla.")]
    [string]$TenantName = "",

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
Write-Host " Plan: Flex Consumption (FC1 / Linux)" -ForegroundColor Cyan
Write-Host " Modo: Azure Cloud Shell" -ForegroundColor Cyan
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

# --- 1. Verificar sesión activa ---
# En Cloud Shell la sesión suele estar activa, pero se verifica por si acaso.
Write-Host "[1/5] Verificando sesión de Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No hay sesión activa. Iniciando login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Suscripción: $($account.name) ($($account.id))" -ForegroundColor Gray
Write-Host "  Registrando provider Microsoft.Bing (si aplica)..." -ForegroundColor Gray
az provider register --namespace Microsoft.Bing --output none 2>$null

# --- 2. Crear Resource Group ---
Write-Host "[2/5] Creando Resource Group '$ResourceGroupName'..." -ForegroundColor Green
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "  Resource Group listo." -ForegroundColor Gray

# Sufijo para intentar localizar una Function App existente antes del deploy.
# NOTA: Bicep deriva el sufijo real vía uniqueString(subscriptionId), que produce un valor diferente.
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
        Write-Host "  Se preservará FabricWarehouseConnectionString existente en la Function App." -ForegroundColor Yellow
    }
}

# --- 3. Desplegar Bicep ---
Write-Host "[3/5] Desplegando infraestructura..." -ForegroundColor Green

Write-Host "" -ForegroundColor Gray
Write-Host "  Esto puede tomar ~5 minutos." -ForegroundColor Yellow
Write-Host ""

$scriptDir = $PSScriptRoot
$templateFile = Join-Path $scriptDir "main.bicep"
$deploymentName = "main"

if (-not (Test-Path $templateFile)) {
    Write-Error "No se encontró main.bicep en '$scriptDir'. Asegúrate de ejecutar el script desde la carpeta op-flex del repositorio clonado."
    exit 1
}

# Lanzar despliegue en background (--no-wait)
az deployment group create `
    --resource-group $ResourceGroupName `
    --template-file $templateFile `
    --parameters location=$Location tenantName=$TenantName fabricWarehouseSqlEndpoint=$FabricWarehouseSqlEndpoint fabricWarehouseDatabase=$FabricWarehouseDatabase fabricWarehouseConnectionString="$FabricWarehouseConnectionString" `
    --name $deploymentName `
    --no-wait `
    --output none

if ($LASTEXITCODE -ne 0) {
    Write-Error "No se pudo iniciar el despliegue. Verifica que no haya recursos soft-deleted (az cognitiveservices account list-deleted)."
    exit 1
}

# Esperar a que el deployment aparezca en ARM
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

# --- 4. Compilar y publicar código de la Function App ---
Write-Host "[4/5] Compilando FxContosoRetail..." -ForegroundColor Green

$projectDir = Join-Path $scriptDir "../../code/api/FxContosoRetail"
$projectDir = (Resolve-Path $projectDir).Path
$publishDir = Join-Path $projectDir "bin/publish"

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
# FIX: Compress-Archive en PowerShell 7 sobre Linux excluye directorios ocultos (dotfiles)
# como .azurefunctions, que Flex Consumption valida como obligatorio.
# Se usa el comando zip del sistema, que con '.' incluye todos los archivos sin excepción.
$zipPath = "/tmp/fxcontosoretail-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Push-Location $publishDir
& zip -r $zipPath . | Out-Null
Pop-Location

# Esperar a que el SCM endpoint esté disponible
# Nota: en Cloud Shell (Linux) se usa [System.Net.Dns] ya que Resolve-DnsName no está disponible.
$scmHost = "$functionAppName.scm.azurewebsites.net"
Write-Host "[5/5] Esperando a que el endpoint SCM esté disponible..." -ForegroundColor Green
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

# Deploy con reintentos
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
    Write-Error "Error al publicar el código tras $maxRetries intentos. Puedes reintentar manualmente con: az functionapp deployment source config-zip --resource-group $ResourceGroupName --name $functionAppName --src '$zipPath'"
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
Write-Host "  Bing Connection Name (Julie): $($outputs.bingConnectionName.value)" -ForegroundColor White
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
