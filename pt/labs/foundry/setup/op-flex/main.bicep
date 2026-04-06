// ============================================================================
// Contoso Retail - Infraestrutura Azure (Flex Consumption)
// Workshop Multi-Agêntico
// ============================================================================
// Cada participante implanta em sua própria assinatura.
// O sufixo único (5 chars) é gerado a partir do nome do tenant temporário.
//
// Plan: Flex Consumption (FC1 / Linux)
// - Identity-based storage completo (sin connection strings para runtime)
// - No requiere file share pre-creado
// - Deployment via blob container
// Basado en: https://github.com/Azure-Samples/azure-functions-flex-consumption-samples
// ============================================================================

targetScope = 'resourceGroup'

// ============================================================================
// Parâmetros
// ============================================================================

@description('Nome do tenant temporário atribuído ao participante (ex: "contoso-abc123tenant").')
param tenantName string

@description('Localização dos recursos. Padrão: eastus.')
param location string = 'eastus'

@description('Nome do modelo GPT a implantar no AI Services.')
param gptModelName string = 'gpt-4.1'

@description('Versão do modelo GPT.')
param gptModelVersion string = '2025-04-14'

@description('Capacidade do deployment (tokens por minuto em milhares).')
param gptDeploymentCapacity int = 30

@description('Endpoint SQL do Warehouse do Fabric (sem protocolo), por exemplo: xyz.datawarehouse.fabric.microsoft.com')
param fabricWarehouseSqlEndpoint string = ''

@description('Nome do banco de dados do Warehouse do Fabric.')
param fabricWarehouseDatabase string = ''

@description('Connection string SQL completa do Fabric. Usada para preservar um valor existente quando endpoint/database não são enviados.')
param fabricWarehouseConnectionString string = ''

// ============================================================================
// Variáveis - Sufixo e nomes
// ============================================================================

var suffix = substring(uniqueString(tenantName), 0, 5)

var storageAccountName = 'stcontosoretail${suffix}'
var appServicePlanName = 'asp-contosoretail-${suffix}'
var functionAppName = 'func-contosoretail-${suffix}'
var aiFoundryName = 'ais-contosoretail-${suffix}'
var aiProjectName = 'aip-contosoretail-${suffix}'
var bingGroundingName = 'bingsearch-${suffix}'
var bingConnectionName = '${aiFoundryName}-bingsearchconnection'

// Container para o pacote de deployment da Function App
var deploymentContainerName = 'app-package-${toLower(functionAppName)}'
var hasFabricWarehouseConfig = !empty(fabricWarehouseSqlEndpoint) && !empty(fabricWarehouseDatabase)
var computedFabricWarehouseConnectionString = 'Server=tcp:${fabricWarehouseSqlEndpoint},1433;Database=${fabricWarehouseDatabase};Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Default;Connection Timeout=30;'
var effectiveFabricWarehouseConnectionString = !empty(fabricWarehouseConnectionString)
  ? fabricWarehouseConnectionString
  : (hasFabricWarehouseConfig ? computedFabricWarehouseConnectionString : '')
var optionalFabricSettings = !empty(effectiveFabricWarehouseConnectionString)
  ? [
      { name: 'FabricWarehouseConnectionString', value: effectiveFabricWarehouseConnectionString }
    ]
  : []

var tags = {
  project: 'taller-multi-agentic'
  environment: 'workshop'
}

// ============================================================================
// 1. Storage Account
// ============================================================================

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false // Flex Consumption soporta identity-based completo
  }
}

// Blob containers: reports (app) + deployment package
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource reportsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'reports'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
}

// Flex Consumption no requiere file share, pero sí table y queue services
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

// ============================================================================
// 2. App Service Plan (Flex Consumption FC1 / Linux)
// ============================================================================

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true // Linux
  }
}

// ============================================================================
// 3. Function App (Flex Consumption / Linux)
// ============================================================================

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storageAccount.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
    siteConfig: {
      appSettings: concat([
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__blobServiceUri', value: 'https://${storageAccountName}.blob.${environment().suffixes.storage}' }
        { name: 'AzureWebJobsStorage__queueServiceUri', value: 'https://${storageAccountName}.queue.${environment().suffixes.storage}' }
        { name: 'AzureWebJobsStorage__tableServiceUri', value: 'https://${storageAccountName}.table.${environment().suffixes.storage}' }
        { name: 'StorageAccountName', value: storageAccountName }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'BillTemplate', value: 'https://raw.githubusercontent.com/warnov/multi-agentic-workshop/refs/heads/master/assets/bill-template.pt.html' }
      ], optionalFabricSettings)
    }
  }
}

// ============================================================================
// 3b. Role Assignments - Function App → Storage Account
// ============================================================================
// A Function App usa Managed Identity para TUDO: runtime + código.
// São necessários 3 roles:
//   - Storage Blob Data Owner       → triggers, bindings, blob storage, deployment
//   - Storage Queue Data Contributor → queue triggers
//   - Storage Account Contributor   → gestão geral

module functionStorageRbac 'storage-rbac.bicep' = {
  name: 'functionStorageRbacDeployment'
  params: {
    storageAccountName: storageAccount.name
    principalId: functionApp.identity.principalId
  }
}

// ============================================================================
// 7. AI Foundry Resource (CognitiveServices/accounts con allowProjectManagement)
// ============================================================================
// Este recurso unifica AI Services + Foundry Hub em um único recurso.
// Substitui o antigo padrão Hub (MachineLearningServices/workspaces kind:Hub).
// Ref: https://learn.microsoft.com/azure/ai-foundry/how-to/create-resource-template

resource aiFoundry 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: aiFoundryName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'S0'
  }
  kind: 'AIServices'
  properties: {
    allowProjectManagement: true
    customSubDomainName: aiFoundryName
    disableLocalAuth: false
    publicNetworkAccess: 'Enabled'
  }
}

// ============================================================================
// 8. AI Foundry Project (filho direto do Foundry Resource)
// ============================================================================

resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  name: aiProjectName
  parent: aiFoundry
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {}
}

// ============================================================================
// 9. Model Deployment (GPT sobre o Foundry Resource)
// ============================================================================

resource gptDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: aiFoundry
  name: gptModelName
  sku: {
    name: 'Standard'
    capacity: gptDeploymentCapacity
  }
  properties: {
    model: {
      name: gptModelName
      format: 'OpenAI'
      version: gptModelVersion
    }
  }
}

// ============================================================================
// 10. Grounding with Bing Search + Connection para Foundry
// ============================================================================

#disable-next-line BCP081
resource bingGrounding 'Microsoft.Bing/accounts@2020-06-10' = {
  name: bingGroundingName
  location: 'global'
  sku: {
    name: 'G1'
  }
  kind: 'Bing.Grounding'
}

#disable-next-line BCP081
resource bingConnection 'Microsoft.CognitiveServices/accounts/connections@2025-04-01-preview' = {
  parent: aiFoundry
  name: bingConnectionName
  properties: {
    category: 'ApiKey'
    target: 'https://api.bing.microsoft.com/'
    authType: 'ApiKey'
    credentials: {
      key: bingGrounding.listKeys().key1
    }
    isSharedToAll: true
    metadata: {
      ApiType: 'Azure'
      Location: bingGrounding.location
      ResourceId: bingGrounding.id
    }
  }
}

// ============================================================================
// Outputs
// ============================================================================

output suffix string = suffix
output storageAccountName string = storageAccountName
output functionAppName string = functionAppName
output functionAppUrl string = 'https://${functionAppName}.azurewebsites.net'
output aiFoundryName string = aiFoundryName
output aiFoundryEndpoint string = aiFoundry.properties.endpoint
output aiProjectName string = aiProjectName
output foundryProjectEndpoint string = aiProject.properties.endpoints['AI Foundry API']
output bingGroundingName string = bingGrounding.name
output bingConnectionName string = bingConnection.name
output bingConnectionId string = bingConnection.id
