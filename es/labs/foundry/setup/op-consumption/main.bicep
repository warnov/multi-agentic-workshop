// ============================================================================
// Contoso Retail - Infraestructura Azure
// Taller Multi-Agéntico
// ============================================================================
// Cada attendee despliega en su propia suscripción.
// El sufijo único (5 chars) se genera a partir del nombre del tenant temporal.
// ============================================================================

targetScope = 'resourceGroup'

// ============================================================================
// Parámetros
// ============================================================================

@description('Nombre del tenant temporal asignado al attendee (ej: "contoso-abc123tenant").')
param tenantName string

@description('Ubicación de los recursos. Por defecto: eastus.')
param location string = 'eastus'

@description('Nombre del modelo GPT a desplegar en AI Services.')
param gptModelName string = 'gpt-4.1'

@description('Versión del modelo GPT.')
param gptModelVersion string = '2025-04-14'

@description('Capacidad del deployment (tokens por minuto en miles).')
param gptDeploymentCapacity int = 30

@description('Endpoint SQL del Warehouse de Fabric (sin protocolo), por ejemplo: xyz.datawarehouse.fabric.microsoft.com')
param fabricWarehouseSqlEndpoint string = ''

@description('Nombre de la base de datos del Warehouse de Fabric.')
param fabricWarehouseDatabase string = ''

@description('Connection string SQL completa de Fabric. Se usa para preservar un valor existente cuando no se envían endpoint/database.')
param fabricWarehouseConnectionString string = ''

// ============================================================================
// Variables - Sufijo y nombres
// ============================================================================

var suffix = substring(uniqueString(tenantName), 0, 5)

var storageAccountName = 'stcontosoretail${suffix}'
var appServicePlanName = 'asp-contosoretail-${suffix}'
var functionAppName = 'func-contosoretail-${suffix}'
var aiFoundryName = 'ais-contosoretail-${suffix}'
var aiProjectName = 'aip-contosoretail-${suffix}'
var bingGroundingName = 'bingsearch-${suffix}'
var bingConnectionName = '${aiFoundryName}-bingsearchconnection'

var tags = {
  project: 'taller-multi-agentic'
  environment: 'workshop'
}

// ============================================================================
// 1. Storage Account (AVM)
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
    allowSharedKeyAccess: true // requerido por Function App para el content file share
  }
}

// File share pre-creado para que la Function App no lo intente crear ella misma
// (evita 403 por timing: el data plane del Storage aún no está listo cuando ARM
// reporta el Storage Account como "Succeeded")
resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource contentShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: toLower(functionAppName)
}

// Blob container para los reportes generados por la Function App
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource reportsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'reports'
}

// ============================================================================
// 2. App Service Plan (Consumption Y1 - nativo)
// ============================================================================

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: false
  }
}

// ============================================================================
// 3. Function App (nativo - evita sub-deployments del AVM)
// ============================================================================

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'
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

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp'
  dependsOn: [contentShare] // esperar a que el file share exista antes de crear la Function App
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      use32BitWorkerProcess: false
      ftpsState: 'Disabled'
      appSettings: concat([
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'AzureWebJobsStorage__accountName', value: storageAccount.name }
        { name: 'StorageAccountName', value: storageAccount.name }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: storageConnectionString }
        { name: 'WEBSITE_CONTENTSHARE', value: toLower(functionAppName) }
        { name: 'WEBSITE_SKIP_CONTENTSHARE_VALIDATION', value: '1' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'BillTemplate', value: 'https://raw.githubusercontent.com/warnov/taller-multi-agentic/refs/heads/main/assets/bill-template.html' }
      ], optionalFabricSettings)
    }
  }
}

// ============================================================================
// 3b. Role Assignments - Function App → Storage Account
// ============================================================================
// La Function App usa Managed Identity para acceder al Storage desde código.
// Se requieren 3 roles:
//   - Storage Blob Data Owner       → triggers, bindings, blob storage
//   - Storage Queue Data Contributor → queue triggers
//   - Storage Account Contributor   → file share

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
// Este recurso unifica AI Services + Foundry Hub en un solo recurso.
// Reemplaza el antiguo patrón Hub (MachineLearningServices/workspaces kind:Hub).
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
// 8. AI Foundry Project (hijo directo del Foundry Resource)
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
// 9. Model Deployment (GPT sobre el Foundry Resource)
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
