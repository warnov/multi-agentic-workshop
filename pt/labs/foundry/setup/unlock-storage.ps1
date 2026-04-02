# ============================================================================
# Contoso Retail - Desbloquear Storage Account
# Workshop Multi-Agêntico
# ============================================================================
# Quando uma política de assinatura desabilita o acesso público ao
# Storage Account, a Function App não consegue iniciar (erro 503) porque
# o host do Functions não alcança seu próprio armazenamento de suporte.
#
# Este script identifica o Storage Account do participante a partir do
# sufixo atribuído durante o setup inicial e reabilita o acesso
# público de rede.
#
# Uso:
#   .\unlock-storage.ps1
#   .\unlock-storage.ps1 -ResourceGroupName "rg-contoso-retail"
#   .\unlock-storage.ps1 -Suffix "sytao"
#   .\unlock-storage.ps1 -FunctionAppName "func-contosoretail-sytao"
# ============================================================================

param(
    [Parameter(Mandatory = $false, HelpMessage = "Sufixo de 5 caracteres atribuído ao participante durante o setup inicial. Se não for fornecido, é detectado automaticamente a partir da Function App.")]
    [ValidatePattern('^[a-z0-9]{5}$')]
    [string]$Suffix,

    [Parameter(Mandatory = $false, HelpMessage = "Nome do Resource Group (padrão: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Nome exato da Function App. Se fornecido, é usado para derivar o sufixo.")]
    [string]$FunctionAppName
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Workshop Multi-Agêntico" -ForegroundColor Cyan
Write-Host " Desbloquear Storage Account" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Sufixo:          $(if ([string]::IsNullOrWhiteSpace($Suffix)) { '<auto>' } else { $Suffix })" -ForegroundColor Yellow
Write-Host "  Storage Account: $(if ([string]::IsNullOrWhiteSpace($Suffix)) { '<auto>' } else { "stcontosoretail$Suffix" })" -ForegroundColor Yellow
Write-Host "  Resource Group:  $ResourceGroupName" -ForegroundColor Yellow
Write-Host ""

# --- 1. Verificar Azure CLI ---
Write-Host "[1/4] Verificando Azure CLI..." -ForegroundColor Green
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Host "  Azure CLI v$($azVersion.'azure-cli') detectado." -ForegroundColor Gray
} catch {
    Write-Error "O Azure CLI não está instalado. Instale em https://aka.ms/installazurecli"
    exit 1
}

# --- 2. Verificar sessão ativa ---
Write-Host "[2/4] Verificando sessão do Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  Nenhuma sessão ativa. Iniciando login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Assinatura: $($account.name) ($($account.id))" -ForegroundColor Gray

if ([string]::IsNullOrWhiteSpace($Suffix)) {
    Write-Host "  Detectando sufixo automaticamente..." -ForegroundColor Yellow

    if (-not [string]::IsNullOrWhiteSpace($FunctionAppName)) {
        if ($FunctionAppName -notmatch '^func-contosoretail-([a-z0-9]{5})$') {
            Write-Error "FunctionAppName '$FunctionAppName' não está no formato esperado 'func-contosoretail-<suffix>'."
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
            Write-Error "Nenhuma Function App com prefixo 'func-contosoretail-' encontrada no Resource Group '$ResourceGroupName'. Use -Suffix ou -FunctionAppName."
            exit 1
        }

        $functionApps = @($functionAppsTsv -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

        if ($functionApps.Count -gt 1) {
            Write-Host "  Múltiplas Function Apps candidatas encontradas:" -ForegroundColor Yellow
            $functionApps | ForEach-Object { Write-Host "   - $_" -ForegroundColor Yellow }
            Write-Error "Especifique -Suffix ou -FunctionAppName para evitar ambiguidade."
            exit 1
        }

        $FunctionAppName = $functionApps[0]

        if ($FunctionAppName -notmatch '^func-contosoretail-([a-z0-9]{5})$') {
            Write-Error "Não foi possível derivar o sufixo a partir da Function App '$FunctionAppName'."
            exit 1
        }

        $Suffix = $Matches[1]
    }
}

$storageAccountName = "stcontosoretail$Suffix"
Write-Host "  Sufixo resolvido: $Suffix" -ForegroundColor Gray
Write-Host "  Storage resolvido: $storageAccountName" -ForegroundColor Gray

# --- 3. Verificar e desbloquear Storage Account ---
Write-Host "[3/4] Verificando Storage Account '$storageAccountName'..." -ForegroundColor Green

$storageJson = az storage account show `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --query "{publicNetworkAccess:publicNetworkAccess, provisioningState:provisioningState}" `
    --output json 2>$null

if (-not $storageJson) {
    Write-Error "Storage Account '$storageAccountName' não encontrado no Resource Group '$ResourceGroupName'."
    exit 1
}

$storage = $storageJson | ConvertFrom-Json
Write-Host "  Estado atual: publicNetworkAccess = $($storage.publicNetworkAccess)" -ForegroundColor Gray

if ($storage.publicNetworkAccess -eq "Enabled") {
    Write-Host ""
    Write-Host "  O Storage Account já tem acesso público habilitado. Nenhuma ação necessária." -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host "  Habilitando acesso público de rede..." -ForegroundColor Yellow
az storage account update `
    --name $storageAccountName `
    --resource-group $ResourceGroupName `
    --public-network-access Enabled `
    --output none

Write-Host "  Acesso público habilitado." -ForegroundColor Green

# --- 4. Reiniciar Function App para reconectar ao storage ---
$functionAppName = if (-not [string]::IsNullOrWhiteSpace($FunctionAppName)) { $FunctionAppName } else { "func-contosoretail-$Suffix" }
Write-Host "[4/4] Reiniciando Function App '$functionAppName'..." -ForegroundColor Green

az functionapp restart `
    --name $functionAppName `
    --resource-group $ResourceGroupName `
    --output none 2>$null

if ($LASTEXITCODE -eq 0) {
    Write-Host "  Function App reiniciada." -ForegroundColor Green
} else {
    Write-Host "  Não foi possível reiniciar a Function App (pode ainda não existir). Continuando..." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Storage Account desbloqueado" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  O Storage Account '$storageAccountName' agora tem acesso" -ForegroundColor Gray
Write-Host "  público habilitado e a Function App foi reiniciada." -ForegroundColor Gray
Write-Host ""
