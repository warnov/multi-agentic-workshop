using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenAI.Responses;
using JulieAgent;

#pragma warning disable AAIP001 // Azure.AI.Projects.Agents: Toolbox es API de vista previa
#pragma warning disable OPENAI001 // API de vista previa de OpenAI

// =====================================================================
//  Julie - Agente Orquestador de Campañas de Marketing
//  (Microsoft Foundry - nueva experiencia)
//
//  Program.cs SOLO se encarga de:
//  1. Crear/verificar los agentes de prompt en Microsoft Foundry
//     (SqlAgent y MarketingAgent)
//  2. Desplegar Julie como agente hospedado a partir del código fuente
//     del proyecto JulieHosted
//  3. Abrir un chat interactivo con Julie
//
//  Toda la orquestación la hace Julie internamente, ya como código de
//  Agent Framework:
//    SqlAgent (tool) → genera T-SQL
//    SqlExecutor (OpenAPI tool) → ejecuta SQL contra la BD
//    MarketingAgent (tool) → genera mensajes personalizados
//    Julie → organiza el resultado como JSON de campaña
// =====================================================================

// --- Cargar configuración ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Falta FoundryProjectEndpoint en appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Falta ModelDeploymentName en appsettings.json");

// URL base de la Function App con el ejecutor de consultas SQL.
// Se configura en appsettings.json cuando la función esté desplegada.
var functionAppBaseUrl = config["FunctionAppBaseUrl"];

// --- Cargar estructura de la base de datos ---
var dbStructurePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "db-structure.txt");
if (!File.Exists(dbStructurePath))
    dbStructurePath = Path.Combine(Directory.GetCurrentDirectory(), "db-structure.txt");
if (!File.Exists(dbStructurePath))
{
    throw new FileNotFoundException(
        "No se encontró el archivo db-structure.txt. " +
        "Asegúrate de que existe en la carpeta raíz del proyecto JulieAgent.");
}
var dbStructure = File.ReadAllText(dbStructurePath);
Console.WriteLine($"[Config] Estructura de BD cargada ({dbStructure.Length} caracteres)");

// --- (Opcional) Descargar spec OpenAPI de la Function App ---
JsonElement? openApiSpecJson = null;

if (!string.IsNullOrEmpty(functionAppBaseUrl) && !functionAppBaseUrl.StartsWith("<"))
{
    Console.WriteLine("[OpenAPI] Descargando especificación desde la Function App...");
    var openApiUrl = $"{functionAppBaseUrl}/openapi/v3.json";
    var maxAttempts = 3;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            var openApiSpec = await httpClient.GetStringAsync(openApiUrl);
            openApiSpecJson = JsonSerializer.Deserialize<JsonElement>(openApiSpec);
            Console.WriteLine($"[OpenAPI] Especificación descargada ({openApiSpec.Length} bytes)");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAPI] Intento {attempt}/{maxAttempts} falló: {ex.Message}");
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }

            Console.WriteLine("[OpenAPI] Julie se creará sin herramienta OpenAPI.");
        }
    }
}
else
{
    Console.WriteLine("[Config] FunctionAppBaseUrl no configurada.");
    Console.WriteLine("  → Julie se creará sin herramienta OpenAPI (ejecución SQL pendiente).");
    Console.WriteLine("  → Configura FunctionAppBaseUrl en appsettings.json cuando la Function App esté desplegada.");
}

// --- Cliente del proyecto Foundry ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// =====================================================================
//  FASE 1: Crear/verificar los agentes en Microsoft Foundry
// =====================================================================

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine(" Julie - Orquestador de Campañas");
Console.WriteLine("========================================");
Console.WriteLine();

// --- Helper para crear o reutilizar un agente (definición tipada) ---
var agentsClient = projectClient.AgentAdministrationClient;

// Versionar un agente legacy conserva la identidad compartida del
// proyecto, por eso se borra el existente en lugar de versionarlo.
async Task EnsureAgent(string agentName, ProjectsAgentDefinition agentDefinition)
{
    Console.WriteLine($"[Foundry] Buscando agente '{agentName}'...");
    try
    {
        var existing = agentsClient.GetAgent(agentName);
        Console.WriteLine($"[Foundry] Agente '{agentName}' encontrado");
        Console.Write($"[Foundry] ¿Borrar '{agentName}' y recrearlo desde cero? (s/N): ");
        var answer = Console.ReadLine()?.Trim();
        var shouldRecreate = string.Equals(answer, "s", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(answer, "si", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(answer, "sí", StringComparison.OrdinalIgnoreCase);

        if (!shouldRecreate)
        {
            Console.WriteLine($"[Foundry] Se conserva '{agentName}' existente.");
            return;
        }

        agentsClient.DeleteAgent(agentName);
        Console.WriteLine($"[Foundry] Agente '{agentName}' borrado.");
    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"[Foundry] Agente '{agentName}' no encontrado. Se creará uno nuevo.");
    }

    ProjectsAgentVersion created = await agentsClient.CreateAgentVersionAsync(
        agentName,
        new ProjectsAgentVersionCreationOptions(agentDefinition));

    Console.WriteLine($"[Foundry] Agente '{agentName}' creado (v{created.Version})");

    // instance_identity es null en agentes legacy y no nulo en agentes con
    // identidad propia de Entra, que es lo que sincroniza Agent 365.
    using var agentJson = JsonDocument.Parse(agentsClient.GetAgent(agentName).GetRawResponse().Content.ToString());
    var hasIdentity = agentJson.RootElement.TryGetProperty("instance_identity", out var identity)
                      && identity.ValueKind != JsonValueKind.Null;
    Console.WriteLine(hasIdentity
        ? $"[Foundry] {agentName} instance_identity: {identity}"
        : $"[Foundry] {agentName} instance_identity: null/ausente -> agente legacy, NO se sincronizará con Agent 365.");
}


// --- Asegurar que existe el Toolbox de Web Search que usa MarketingAgent ---
// Crear una versión de Toolbox es una llamada de data-plane (SDK), igual que
// crear un agente: no requiere ningún recurso ARM ni conexión con clave.
const string marketingToolboxName = "marketing-websearch-toolbox";
var toolboxesClient = agentsClient.GetAgentToolboxes();

Console.WriteLine($"[Foundry] Buscando toolbox '{marketingToolboxName}'...");
try
{
    toolboxesClient.Get(marketingToolboxName);
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' ya existe, se reutiliza.");
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' no encontrado. Se creará.");
    await toolboxesClient.CreateVersionAsync(
        marketingToolboxName,
        tools: new List<ToolboxTool> { new WebSearchToolboxTool() },
        description: "Web Search para MarketingAgent (reemplaza a Grounding with Bing Search)");
    Console.WriteLine($"[Foundry] Toolbox '{marketingToolboxName}' creado.");
}

// Endpoint "consumer": siempre sirve la default_version, así que promover una
// versión nueva del toolbox no requiere tocar ni recompilar MarketingAgent.
var marketingToolboxEndpoint = new Uri($"{foundryEndpoint}/toolboxes/{marketingToolboxName}/mcp?api-version=v1");

// Crear los dos sub-agentes de prompt que Julie orquesta
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, marketingToolboxEndpoint));

// =====================================================================
//  Julie: agente hospedado desplegado desde el código fuente
// =====================================================================

const string julieAgentName = "Julie";

// Foundry compila el código subido de forma remota, así que en local no hacen
// falta ni Docker ni un Container Registry.
var hostedSourcePath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "JulieHosted"));

if (!Directory.Exists(hostedSourcePath))
    throw new DirectoryNotFoundException($"No se encontró el código del agente hospedado en {hostedSourcePath}");

// El kind de un agente es inmutable: una Julie heredada del modelo de workflow
// debe borrarse antes de poder recrearla como agente hospedado.
try
{
    var existingJulie = agentsClient.GetAgent(julieAgentName);
    using var julieJson = JsonDocument.Parse(existingJulie.GetRawResponse().Content.ToString());
    var existingKind = julieJson.RootElement
        .GetProperty("versions").GetProperty("latest")
        .GetProperty("definition").GetProperty("kind").GetString();

    if (existingKind == "hosted")
    {
        Console.WriteLine($"[Foundry] El agente '{julieAgentName}' ya es hospedado. Se añadirá una nueva versión.");
    }
    else
    {
        Console.WriteLine($"[Foundry] El agente '{julieAgentName}' existe con kind '{existingKind}', que no se puede cambiar sobre la marcha.");
        Console.Write($"[Foundry] ¿Borrar '{julieAgentName}' y volver a desplegarlo como agente hospedado? (s/N): ");
        var julieAnswer = Console.ReadLine()?.Trim();
        var recreateJulie = string.Equals(julieAnswer, "s", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(julieAnswer, "si", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(julieAnswer, "sí", StringComparison.OrdinalIgnoreCase);

        if (!recreateJulie)
            throw new InvalidOperationException(
                $"Hay que borrar '{julieAgentName}' antes de poder desplegarlo como agente hospedado.");

        agentsClient.DeleteAgent(julieAgentName);
        Console.WriteLine($"[Foundry] Agente '{julieAgentName}' borrado.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] Agente '{julieAgentName}' no encontrado. Se creará uno nuevo.");
}

Console.WriteLine($"[Foundry] Subiendo el código del agente hospedado Julie desde {hostedSourcePath}...");

// El SDK sube la carpeta tal cual y la compilación remota falla con los
// artefactos de compilación locales, así que se borran bin/ y obj/ antes.
foreach (var stale in new[] { "bin", "obj" })
{
    var staleDir = Path.Combine(hostedSourcePath, stale);
    if (Directory.Exists(staleDir))
    {
        Directory.Delete(staleDir, recursive: true);
        Console.WriteLine($"[Foundry] Eliminada la salida de compilación local '{stale}' antes de subir.");
    }
}

HostedAgentDefinition julieDefinition = new(cpu: "0.5", memory: "1Gi")
{
    Versions = { new ProtocolVersionRecord(ProjectsAgentProtocol.Responses, "2.0.0") },
    CodeConfiguration = new(
        runtime: "dotnet_10",
        entryPoint: ["dotnet", "julie-hosted.dll"],
        dependencyResolution: CodeDependencyResolution.RemoteBuild)
};
julieDefinition.EnvironmentVariables.Add("FOUNDRY_PROJECT_ENDPOINT", foundryEndpoint);
julieDefinition.EnvironmentVariables.Add("AZURE_AI_MODEL_DEPLOYMENT_NAME", modelDeployment);
// auto permite a Julie caer a clientes de demostración claramente marcados cuando falta Fabric SQL.
julieDefinition.EnvironmentVariables.Add("JULIE_DATA_MODE", config["JulieDataMode"] ?? "auto");

ProjectsAgentVersion julieVersion = await agentsClient.CreateAgentVersionFromCodeAsync(
    agentName: julieAgentName,
    filePath: hostedSourcePath,
    metadata: new AgentVersionFromCodeMetadata(julieDefinition));

Console.WriteLine($"[Foundry] Versión {julieVersion.Version} de Julie creada. Esperando el aprovisionamiento...");

for (var attempt = 1; attempt <= 60; attempt++)
{
    await Task.Delay(TimeSpan.FromSeconds(10));
    julieVersion = await agentsClient.GetAgentVersionAsync(julieAgentName, julieVersion.Version);
    Console.WriteLine($"[Foundry] Estado del aprovisionamiento: {julieVersion.Status} ({attempt}/60)");

    if (julieVersion.Status == AgentVersionStatus.Active) break;
    if (julieVersion.Status == AgentVersionStatus.Failed)
        throw new InvalidOperationException("Falló el aprovisionamiento del agente hospedado Julie.");
}

if (julieVersion.Status != AgentVersionStatus.Active)
    throw new TimeoutException("Se agotó el tiempo esperando a que Julie estuviera activa.");

await agentsClient.PatchAgentAsync(julieAgentName, new PatchAgentOptions
{
    AgentEndpoint = new AgentEndpointConfiguration
    {
        VersionSelector = new([new FixedRatioVersionSelectionRule(julieVersion.Version, 100)]),
        ProtocolConfiguration = new() { Responses = new ResponsesProtocolConfiguration() }
    }
});
Console.WriteLine($"[Foundry] Endpoint de Julie enrutado a la versión {julieVersion.Version}");

// =====================================================================
//  Dar acceso al proyecto a la identidad del agente hospedado
//
//  Un agente hospedado se ejecuta con su propia identidad de Entra, que se
//  recrea junto con el objeto agente, así que el rol se vuelve a asignar en
//  cada ejecución en lugar de ser un paso manual puntual.
//  Hace falta 'Foundry User': 'Foundry Agent Consumer' solo concede
//  endpoints/interact y Julie recibe 403 en agents/read al buscar SqlAgent y
//  MarketingAgent. La asignación debe apuntar al ámbito del proyecto; con el
//  ámbito de la cuenta no fue suficiente.
// =====================================================================

const string foundryUserRoleId = "53ca6127-db72-4b80-b1b0-d745d6d5456d";

string? juliePrincipalId = null;
using (var julieIdentityJson = JsonDocument.Parse(agentsClient.GetAgent(julieAgentName).GetRawResponse().Content.ToString()))
{
    if (julieIdentityJson.RootElement.TryGetProperty("instance_identity", out var julieIdentity)
        && julieIdentity.ValueKind == JsonValueKind.Object
        && julieIdentity.TryGetProperty("principal_id", out var juliePrincipal))
    {
        juliePrincipalId = juliePrincipal.GetString();
    }
}

// El ámbito ARM del proyecto (para el RBAC de Julie) ya no se puede derivar
// oportunistamente de la conexión Bing (eliminada). Se arma explícitamente a
// partir de la suscripción/grupo de recursos configurados y del nombre de
// cuenta/proyecto, que ya vienen embebidos en FoundryProjectEndpoint.
var subscriptionId = config["SubscriptionId"];
var resourceGroupName = config["ResourceGroupName"];

string? projectScope = null;
if (!string.IsNullOrWhiteSpace(subscriptionId) && !string.IsNullOrWhiteSpace(resourceGroupName)
    && !subscriptionId.StartsWith('<') && !resourceGroupName.StartsWith('<'))
{
    var foundryUri = new Uri(foundryEndpoint);
    var accountName = foundryUri.Host.Split('.')[0];
    var projectName = foundryUri.AbsolutePath.TrimEnd('/').Split('/')[^1];
    projectScope = $"/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}"
                 + $"/providers/Microsoft.CognitiveServices/accounts/{accountName}/projects/{projectName}";
}

if (juliePrincipalId is null || projectScope is null)
{
    Console.WriteLine("[RBAC] No se pudo resolver la identidad de Julie o el ámbito del proyecto.");
    Console.WriteLine("[RBAC] Asigna manualmente el rol 'Foundry User' a Julie antes de chatear.");
}
else
{
    await AssignFoundryUserAsync(projectScope, juliePrincipalId);
}

async Task AssignFoundryUserAsync(string scope, string principalId)
{
    Console.WriteLine($"[RBAC] Concediendo 'Foundry User' a la identidad de Julie {principalId}...");
    var manualCommand = $"az role assignment create --role \"Foundry User\" "
                      + $"--assignee-object-id {principalId} --assignee-principal-type ServicePrincipal "
                      + $"--scope \"{scope}\"";
    try
    {
        var subscriptionId = scope.Split('/')[2];
        var armToken = await new DefaultAzureCredential().GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]), default);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", armToken.Token);

        var payload = JsonSerializer.Serialize(new
        {
            properties = new
            {
                roleDefinitionId = $"/subscriptions/{subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{foundryUserRoleId}",
                principalId,
                principalType = "ServicePrincipal"
            }
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var armResponse = await http.PutAsync(
            $"https://management.azure.com{scope}/providers/Microsoft.Authorization/roleAssignments/{Guid.NewGuid()}?api-version=2022-04-01",
            content);

        if (armResponse.IsSuccessStatusCode)
            Console.WriteLine("[RBAC] Rol asignado. Puede tardar aproximadamente un minuto en hacerse efectivo.");
        else if (armResponse.StatusCode == HttpStatusCode.Conflict)
            Console.WriteLine("[RBAC] Julie ya tenía el rol.");
        else
        {
            Console.WriteLine($"[RBAC] Falló la asignación ({(int)armResponse.StatusCode}): {await armResponse.Content.ReadAsStringAsync()}");
            Console.WriteLine($"[RBAC] Ejecútalo manualmente: {manualCommand}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[RBAC] Falló la asignación: {ex.Message}");
        Console.WriteLine($"[RBAC] Ejecútalo manualmente: {manualCommand}");
    }
}

Console.WriteLine();
Console.WriteLine("[Foundry] Todos los agentes están listos.");

// =====================================================================
//  FASE 2: Chat interactivo con Julie
// =====================================================================

ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForAgentEndpoint(julieAgentName);

Console.WriteLine();
Console.WriteLine("=== Chat con Julie (escribe 'salir' para terminar) ===");
Console.WriteLine("Ejemplo: 'Crea una campaña para clientes que hayan comprado bicicletas'");
Console.WriteLine();

while (true)
{
    Console.Write("Tú: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("salir", StringComparison.OrdinalIgnoreCase))
        break;

    Console.Write("Julie: ");
    try
    {
        ResponseResult response = responseClient.CreateResponse(input);

        Console.WriteLine();
        Console.WriteLine($"  [Estado] {response.Status}");

        var outputText = response.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            Console.WriteLine();
            Console.WriteLine(outputText);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[El agente no devolvió texto de salida]");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[Error] {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"  [Inner] {ex.InnerException.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine("[Foundry] Chat finalizado.");
Console.WriteLine("[Foundry] Los agentes permanecen disponibles en Microsoft Foundry.");
