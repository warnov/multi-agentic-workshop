# ============================================================================
# Contoso Retail - Desbloquear Storage Account
# Taller Multi-Agéntico
# ============================================================================
# Cuando una política de suscripción deshabilita el acceso público al
# Storage Account, la Function App no puede arrancar (error 503) porque
# el host de Functions no alcanza su propio almacenamiento de respaldo.
#
# Este script identifica el Storage Account del attendee a partir del
# sufijo asignado durante el setup inicial y re-habilita el acceso
# público de red.
#
# Uso:
#   .\unlock-storage.ps1
#   .\unlock-storage.ps1 -ResourceGroupName "rg-contoso-retail"
#   .\unlock-storage.ps1 -Suffix "sytao"
#   .\unlock-storage.ps1 -FunctionAppName "func-contosoretail-sytao"
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "Sufijo de 5 caracteres asignado al attendee durante el setup inicial. Si no se provee, se detecta automaticamente desde la Function App.")]
    [ValidatePattern('^[a-z0-9]{5}$')]
    [string]$Suffix,

    [Parameter(Mandatory = $false, HelpMessage = "Nombre del Resource Group (default: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Nombre exacto de la Function App. Si se provee, se usa para derivar el sufijo.")]
    [string]$FunctionAppName
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Taller Multi-Agéntico" -ForegroundColor Cyan
Write-Host " Desbloquear Storage Account" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Sufijo:          $(if ([string]::IsNullOrWhiteSpace($Suffix)) { '<auto>' } else { $Suffix })" -ForegroundColor Yellow
Write-Host "  Storage Account: $(if ([string]::IsNullOrWhiteSpace($Suffix)) { '<auto>' } else { "stcontosoretail$Suffix" })" -ForegroundColor Yellow
Write-Host "  Resource Group:  $ResourceGroupName" -ForegroundColor Yellow
Write-Host ""

# --- 1. Verificar Azure CLI ---
Write-Host "[1/4] Verificando Azure CLI..." -ForegroundColor Green
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Host "  Azure CLI v$($azVersion.'azure-cli') detectado." -ForegroundColor Gray
} catch {
    Write-Error "Azure CLI no esta instalado. Instalalo desde https://aka.ms/installazurecli"
    exit 1
}

# --- 2. Verificar sesión activa ---
Write-Host "[2/4] Verificando sesion de Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  No hay sesion activa. Iniciando login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Suscripcion: $($account.name) ($($account.id))" -ForegroundColor Gray

if ([string]::IsNullOrWhiteSpace($Suffix)) {
    Write-Host "  Detectando sufijo automaticamente..." -ForegroundColor Yellow

    if (-not [string]::IsNullOrWhiteSpace($FunctionAppName)) {
        if ($FunctionAppName -notmatch '^func-contosoretail-([a-z0-9]{5})$') {
            Write-Error "FunctionAppName '$FunctionAppName' no cumple el formato esperado 'func-contosoretail-<suffix>'."
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
            Write-Error "No se encontraron Function Apps con prefijo 'func-contosoretail-' en el Resource Group '$ResourceGroupName'. Usa -Suffix o -FunctionAppName."
            exit 1
        }

        $functionApps = @($functionAppsTsv -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

        if ($functionApps.Count -gt 1) {
            Write-Host "  Se encontraron multiples Function Apps candidatas:" -ForegroundColor Yellow
            $functionApps | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
            Write-Error "Especifica -Suffix o -FunctionAppName para evitar ambiguedad."
            exit 1
        }

        $FunctionAppName = $functionApps[0]

        if ($FunctionAppName -notmatch '^func-contosoretail-([a-z0-9]{5})$') {
            Write-Error "No se pudo derivar el sufijo desde la Function App '$FunctionAppName'."
            exit 1
        }

        $Suffix = $Matches[1]
    }
}

$storageAccountName = "stcontosoretail$Suffix"
Write-Host "  Sufijo resuelto: $Suffix" -ForegroundColor Gray
Write-Host "  Storage resuelto: $storageAccountName" -ForegroundColor Gray

# --- 3. Verificar y desbloquear Storage Account ---
Write-Host "[3/4] Verificando Storage Account '$storageAccountName'..." -ForegroundColor Green

$storageJson = az storage account show `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --query "{publicNetworkAccess:publicNetworkAccess, provisioningState:provisioningState}" `
    --output json 2>$null

if (-not $storageJson) {
    Write-Error "No se encontro el Storage Account '$storageAccountName' en el Resource Group '$ResourceGroupName'."
    exit 1
}

$storage = $storageJson | ConvertFrom-Json
Write-Host "  Estado actual: publicNetworkAccess = $($storage.publicNetworkAccess)" -ForegroundColor Gray

if ($storage.publicNetworkAccess -eq "Enabled") {
    Write-Host ""
    Write-Host "  El Storage Account ya tiene acceso publico habilitado. No se requiere accion." -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host "  Habilitando acceso publico de red..." -ForegroundColor Yellow
az storage account update `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --public-network-access Enabled `
    --output none

Write-Host "  Acceso publico habilitado." -ForegroundColor Green

# --- 4. Reiniciar Function App para que reconecte al storage ---
$functionAppName = if (-not [string]::IsNullOrWhiteSpace($FunctionAppName)) { $FunctionAppName } else { "func-contosoretail-$Suffix" }
Write-Host "[4/4] Reiniciando Function App '$functionAppName'..." -ForegroundColor Green

az functionapp restart `
    --name $functionAppName `
    --resource-group $ResourceGroupName `
    --output none 2>$null

if ($LASTEXITCODE -eq 0) {
    Write-Host "  Function App reiniciada." -ForegroundColor Green
} else {
    Write-Host "  No se pudo reiniciar la Function App (puede que no exista aun). Continuando..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Storage Account desbloqueado" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  El Storage Account '$storageAccountName' ahora tiene acceso" -ForegroundColor Gray
Write-Host "  publico habilitado y la Function App fue reiniciada." -ForegroundColor Gray
Write-Host ""
