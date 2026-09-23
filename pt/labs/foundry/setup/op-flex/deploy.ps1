# ============================================================================
# Contoso Retail - Script de Implantação (Flex Consumption FC1 / Linux)
# Workshop Multi-Agêntico
# ============================================================================
# Uso:
#   .\deploy.ps1 -TenantName "meu-tenant-temporario"
#   .\deploy.ps1 -TenantName "meu-tenant-temporario" -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" -FabricWarehouseDatabase "<database>"
#   .\deploy.ps1 -TenantName "meu-tenant-temporario" -Location "eastus" -FabricWarehouseSqlEndpoint "<endpoint-sql-fabric>" -FabricWarehouseDatabase "<database>"
# ============================================================================

param(
    [Parameter(Mandatory = $true, HelpMessage = "Nome do tenant temporário atribuído ao participante.")]
    [string]$TenantName,

    [Parameter(Mandatory = $false, HelpMessage = "Região do Azure (padrão: eastus).")]
    [string]$Location = "eastus",

    [Parameter(Mandatory = $false, HelpMessage = "Nome do Resource Group (padrão: rg-contoso-retail).")]
    [string]$ResourceGroupName = "rg-contoso-retail",

    [Parameter(Mandatory = $false, HelpMessage = "Endpoint SQL do Warehouse do Fabric (sem protocolo). Ex: xyz.datawarehouse.fabric.microsoft.com")]
    [string]$FabricWarehouseSqlEndpoint = "",

    [Parameter(Mandatory = $false, HelpMessage = "Nome do banco de dados do Warehouse do Fabric.")]
    [string]$FabricWarehouseDatabase = ""
)

$ErrorActionPreference = "Stop"

# --- Verificar PowerShell 7+ ---
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host ""
    Write-Host "ERRO: Este script requer PowerShell 7 ou superior." -ForegroundColor Red
    Write-Host "  Versão detectada: PowerShell $($PSVersionTable.PSVersion)" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Baixe o PowerShell 7:" -ForegroundColor Yellow
    Write-Host "    Windows : https://aka.ms/powershell-release?tag=stable  (MSI installer)" -ForegroundColor Cyan
    Write-Host "             ou execute:  winget install Microsoft.PowerShell" -ForegroundColor Gray
    Write-Host "    Linux   : https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-linux" -ForegroundColor Cyan
    Write-Host "    macOS   : https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos" -ForegroundColor Cyan
    Write-Host "             ou execute:  brew install powershell/tap/powershell" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Após instalar, abra um terminal 'pwsh' (não 'powershell') e execute o script novamente." -ForegroundColor Yellow
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
        Write-Host "ERRO: A ExecutionPolicy não permite executar scripts." -ForegroundColor Red
        Write-Host "  Policy atual (CurrentUser): $execPolicy" -ForegroundColor Red
        Write-Host "  Policy atual (LocalMachine): $systemPolicy" -ForegroundColor Red
        Write-Host ""
        Write-Host "  Execute este comando no pwsh e tente novamente:" -ForegroundColor Yellow
        Write-Host "    Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser" -ForegroundColor Cyan
        Write-Host ""
        exit 1
    }
}

Write-Host "Pressione Enter para o padrão." -ForegroundColor DarkGray

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
    $configureFabricNow = Read-Host "Deseja configurar agora a conexão SQL do Fabric para o Lab04? (s/N)"
    if ($configureFabricNow -match '^(s|si|sim|y|yes)$') {
        $FabricWarehouseSqlEndpoint = (Read-Host "FabricWarehouseSqlEndpoint (sem protocolo, sem porta)").Trim()
        $FabricWarehouseDatabase = (Read-Host "FabricWarehouseDatabase").Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseDatabase = (Read-Host "Falta FabricWarehouseDatabase. Informe o valor ou deixe vazio para ignorar").Trim()
}

if ([string]::IsNullOrWhiteSpace($FabricWarehouseSqlEndpoint) -and -not [string]::IsNullOrWhiteSpace($FabricWarehouseDatabase)) {
    $FabricWarehouseSqlEndpoint = (Read-Host "Falta FabricWarehouseSqlEndpoint. Informe o valor ou deixe vazio para ignorar").Trim()
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Workshop Multi-Agêntico - Implantação" -ForegroundColor Cyan
Write-Host " Plano: Flex Consumption (FC1 / Linux)" -ForegroundColor Cyan
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
    Write-Warning "Apenas um dos parâmetros do Fabric foi fornecido. Ambos serão ignorados para não configurar uma conexão incompleta."
    $FabricWarehouseSqlEndpoint = ""
    $FabricWarehouseDatabase = ""
    $hasCompleteFabricConfig = $false
}

if (-not $hasCompleteFabricConfig) {
    Write-Warning "A conexão SQL para o Lab04 não será configurada nesta implantação. Você precisará ajustá-la manualmente depois."
}

# --- 1. Verificar Azure CLI ---
Write-Host "[1/5] Verificando Azure CLI..." -ForegroundColor Green
try {
    $azVersion = az version --output json | ConvertFrom-Json
    Write-Host "  Azure CLI v$($azVersion.'azure-cli') detectado." -ForegroundColor Gray
} catch {
    Write-Error "O Azure CLI não está instalado. Instale em https://aka.ms/installazurecli"
    exit 1
}

# --- 2. Verificar sessão ativa ---
Write-Host "[2/5] Verificando sessão do Azure..." -ForegroundColor Green
$account = az account show --output json 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "  Nenhuma sessão ativa. Iniciando login..." -ForegroundColor Yellow
    az login
    $account = az account show --output json | ConvertFrom-Json
}
Write-Host "  Assinatura: $($account.name) ($($account.id))" -ForegroundColor Gray

# --- 3. Criar Resource Group ---
Write-Host "[3/5] Criando Resource Group '$ResourceGroupName'..." -ForegroundColor Green
az group create --name $ResourceGroupName --location $Location --output none
Write-Host "  Resource Group pronto." -ForegroundColor Gray

# Tentar preservar configuração existente (requer que o RG já exista)
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
        Write-Host "  FabricWarehouseConnectionString existente será preservada na Function App." -ForegroundColor Yellow
    }
}

# --- 4. Implantar Bicep ---
Write-Host "[4/5] Implantando infraestrutura..." -ForegroundColor Green

# Calcular e exibir o sufixo antes de implantar
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
Write-Host "  Isso pode levar ~5 minutos." -ForegroundColor Yellow
Write-Host ""
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$templateFile = Join-Path $scriptDir "main.bicep"
$deploymentName = "main"

# O ARM rejeita uma nova implantação enquanto a conta do Foundry tem outra operação em andamento.
function Wait-ForFoundryAccountIdle {
    param(
        [string]$ResourceGroupName,
        [string]$AccountName,
        [int]$TimeoutSeconds = 300
    )

    if ([string]::IsNullOrWhiteSpace($AccountName)) { return }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $state = az cognitiveservices account show `
            --resource-group $ResourceGroupName `
            --name $AccountName `
            --query 'properties.provisioningState' `
            --output tsv 2>$null

        if ([string]::IsNullOrWhiteSpace($state)) { return }
        if ($state -notin @('Creating', 'Updating', 'Deleting', 'Accepted')) { return }

        Write-Host "  A conta do Foundry '$AccountName' está ocupada ($state). Aguardando 15s..." -ForegroundColor Yellow
        Start-Sleep -Seconds 15
    }

    Write-Warning "Tempo esgotado aguardando '$AccountName' ficar livre. Continuando mesmo assim."
}

$foundryAccountName = if ([string]::IsNullOrWhiteSpace($suffixResult)) { $null } else { "ais-contosoretail-$suffixResult" }
$maxDeployAttempts = 3
$deployAttempt = 0
$depJson = $null

while ($true) {
    $deployAttempt++

    Wait-ForFoundryAccountIdle -ResourceGroupName $ResourceGroupName -AccountName $foundryAccountName

    if ($deployAttempt -gt 1) {
        Write-Host "  Tentando a implantação novamente (tentativa $deployAttempt de $maxDeployAttempts)..." -ForegroundColor Yellow
    }

    # Iniciar implantação em background (--no-wait)
    az deployment group create `
        --resource-group $ResourceGroupName `
        --template-file $templateFile `
        --parameters tenantName=$TenantName location=$Location fabricWarehouseSqlEndpoint=$FabricWarehouseSqlEndpoint fabricWarehouseDatabase=$FabricWarehouseDatabase fabricWarehouseConnectionString="$FabricWarehouseConnectionString" `
        --name $deploymentName `
        --no-wait `
        --output none

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Não foi possível iniciar a implantação. Verifique se não há recursos soft-deleted (az cognitiveservices account list-deleted)."
        exit 1
    }

    # Aguardar o deployment aparecer no ARM (~5 segundos)
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
        Write-Error "O deployment '$deploymentName' não foi registrado no Azure. Verifique erros de validação."
        exit 1
    }

    # Acompanhamento recurso a recurso
    $completedOps = @{}
    $spinChars = @('|', '/', '-', '\\')
    $spinIdx = 0
    $deployFailed = $false

    while ($true) {
        Start-Sleep -Seconds 3

        # Obter operações do deployment
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

            # Exibir apenas transições novas
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

        # Verificar se o deployment terminou
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

    if ($depJson -eq 'Succeeded') { break }

    $deployErrorJson = az deployment group show `
        --resource-group $ResourceGroupName `
        --name $deploymentName `
        --query 'properties.error' `
        --output json 2>$null

    $isTransientConflict = $deployErrorJson -match 'RequestConflict' -or $deployErrorJson -match 'Another operation is in progress'

    if ($isTransientConflict -and $deployAttempt -lt $maxDeployAttempts) {
        Write-Host ""
        Write-Host "  ⚠️  Conflito transitório: a conta do Foundry ainda tinha uma operação em andamento." -ForegroundColor Yellow
        Write-Host "  Aguardando 45 segundos antes de tentar novamente..." -ForegroundColor Yellow
        Start-Sleep -Seconds 45
        continue
    }

    Write-Host ""
    Write-Host $deployErrorJson
    Write-Error "A implantação falhou. Verifique os erros acima."
    exit 1
}

# Obter outputs do deployment bem-sucedido
$result = az deployment group show `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --output json | ConvertFrom-Json

$outputs = $result.properties.outputs
$functionAppName = $outputs.functionAppName.value

# --- 5. Publicar código da Function App ---
Write-Host "[5/5] Publicando código do FxContosoRetail..." -ForegroundColor Green
$projectDir = Join-Path (Join-Path (Join-Path (Join-Path $scriptDir "..") "..") "code") "api"
$projectDir = Join-Path $projectDir "FxContosoRetail"
$publishDir = Join-Path (Join-Path $projectDir "bin") "publish"

Write-Host "  Compilando projeto..." -ForegroundColor Gray
$publishOutput = dotnet publish $projectDir --configuration Release --output $publishDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "" 
    Write-Host "===== Detalhe do erro de compilação =====" -ForegroundColor Red
    $publishOutput | ForEach-Object { Write-Host $_ }
    Write-Host "==========================================" -ForegroundColor Red
    Write-Error "Erro ao compilar o projeto. Verifique o código."
    exit 1
}

# Criar zip para implantação
$zipPath = Join-Path $env:TEMP "fxcontosoretail-publish.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

# Aguardar o endpoint SCM estar resolvível por DNS (Flex Consumption demora)
$scmHost = "$functionAppName.scm.azurewebsites.net"
Write-Host "  Aguardando endpoint SCM ficar disponível..." -ForegroundColor Gray
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
    Write-Warning "  O DNS de $scmHost não resolveu após 5 minutos. Tentando deploy mesmo assim..."
}

# Deploy com novas tentativas (até 3 tentativas com espera incremental)
$maxRetries = 3
$deploySuccess = $false
for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
    Write-Host "  Implantando em $functionAppName (tentativa $attempt/$maxRetries)..." -ForegroundColor Gray
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
        Write-Host "  ⚠️  Tentativa $attempt falhou. Tentando novamente em $waitSecs segundos..." -ForegroundColor Yellow
        Start-Sleep -Seconds $waitSecs
    }
}

if (-not $deploySuccess) {
    Write-Error "Erro ao publicar o código após $maxRetries tentativas. Você pode tentar manualmente com: az functionapp deployment source config-zip --resource-group $ResourceGroupName --name $functionAppName --src `"$zipPath`""
    exit 1
}

# Limpar arquivos temporários
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "  ✅ Código publicado com sucesso." -ForegroundColor Green

# --- Resumo final ---
$functionAppUrl = $outputs.functionAppUrl.value

$apiUrl = "$functionAppUrl/api/OrdersReporter"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Implantação concluída!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Sufijo único:        $($outputs.suffix.value)" -ForegroundColor White
Write-Host "  Storage Account:     $($outputs.storageAccountName.value)" -ForegroundColor White
Write-Host "  Function App:        $functionAppName" -ForegroundColor White
Write-Host "  Function App Base URL:      $functionAppUrl/api" -ForegroundColor White
Write-Host "  API OrdersReporter:          $apiUrl" -ForegroundColor White
Write-Host "  Foundry Project Endpoint:    $($outputs.foundryProjectEndpoint.value)" -ForegroundColor White
Write-Host "  Subscription ID:             $($outputs.subscriptionId.value)" -ForegroundColor White
Write-Host "  Resource Group:              $($outputs.resourceGroupName.value)" -ForegroundColor White
if ($hasCompleteFabricConfig) {
    Write-Host "  Fabric SQL Connection:       atualizada a partir dos parâmetros" -ForegroundColor White
}
elseif (-not [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Fabric SQL Connection:       preservada da configuração existente" -ForegroundColor White
}
else {
    Write-Host "  Fabric SQL Connection:       não configurada" -ForegroundColor Yellow
}
if (-not $hasCompleteFabricConfig -and [string]::IsNullOrWhiteSpace($FabricWarehouseConnectionString)) {
    Write-Host "  Aviso Lab04:                 A conexão SQL (FabricWarehouseConnectionString) não foi configurada. Configure-a manualmente na Function App." -ForegroundColor Yellow
}
Write-Host ""
