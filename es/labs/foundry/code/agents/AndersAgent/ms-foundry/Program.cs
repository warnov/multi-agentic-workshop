using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Text.Json;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // OpenAI preview API

// =====================================================================
//  Anders - Agente Ejecutor (Microsoft Foundry)
//
//  Usa la API nueva de Foundry projects: Azure.AI.Projects 2.x +
//  Azure.AI.Projects.Agents + Azure.AI.Extensions.OpenAI.
//
//  El agente se borra y se recrea en lugar de versionarse, para que se
//  aprovisione bajo el modelo de objetos actual y reciba su propia
//  identidad de Entra, de la que depende el registro en Agent 365.
// =====================================================================

// --- Cargar configuración ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Falta FoundryProjectEndpoint en appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Falta ModelDeploymentName en appsettings.json");
var functionAppBaseUrl = config["FunctionAppBaseUrl"]
    ?? throw new InvalidOperationException("Falta FunctionAppBaseUrl en appsettings.json");
var tenantId = config["TenantId"];
var agentName = "Anders";

// =====================================================================
//  FASE 1: Obtener la especificación OpenAPI de la Function App
// =====================================================================

Console.WriteLine("[OpenAPI] Descargando especificación desde la Function App...");

var httpClient = new HttpClient();
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);

Console.WriteLine($"[OpenAPI] Especificación descargada ({openApiSpec.Length} bytes)");

// =====================================================================
//  FASE 2: Crear agente con herramienta OpenAPI (protocol method)
// =====================================================================

// Instrucciones del agente Anders
var andersInstructions = """
    Eres Anders, el agente ejecutor de Contoso Retail.

    Tu responsabilidad es ejecutar acciones operativas concretas cuando se te soliciten.
    Tu principal capacidad es generar reportes de órdenes de compra de clientes
    usando la API de Contoso Retail disponible como herramienta OpenAPI.

    Cuando recibas datos de órdenes, debes construir el JSON del request body
    con EXACTAMENTE este schema para invocar el endpoint ordersReporter:

    {
      "customerName": "Nombre del Cliente",
      "startDate": "YYYY-MM-DD",
      "endDate": "YYYY-MM-DD",
      "orders": [
        {
          "orderNumber": "código de la orden",
          "orderDate": "YYYY-MM-DD",
          "orderLineNumber": 1,
          "productName": "nombre del producto",
          "brandName": "nombre de la marca",
          "categoryName": "nombre de la categoría",
          "quantity": 1.0,
          "unitPrice": 0.00,
          "lineTotal": 0.00
        }
      ]
    }

    Reglas:
    - TODOS los campos son obligatorios para cada línea de orden.
    - Si una orden tiene múltiples productos, cada producto es un elemento
      separado en el array "orders" con el mismo "orderNumber" y "orderDate"
      pero diferente "orderLineNumber" (secuencial: 1, 2, 3...).
    - Las fechas deben estar en formato ISO: YYYY-MM-DD.
    - "quantity", "unitPrice" y "lineTotal" son numéricos (double).

    Siempre confirma la acción realizada al usuario, incluyendo la URL del reporte.
    Si los datos son insuficientes o inválidos, explica qué falta.
    Si los campos vienen con casing distinto, adáptalos.
    Si falta startDate o endDate, infiere de las fechas de las órdenes.
    Si los totales por línea faltan, calcúlalos.
    Responde en español.
    """;

// Cliente del proyecto Foundry (nueva experiencia)
// Si TenantId está configurado, se usa explícitamente para evitar conflictos
// en máquinas con múltiples tenants de Azure (error 400 "Token tenant does not match").
var credentialOptions = new DefaultAzureCredentialOptions();
if (!string.IsNullOrWhiteSpace(tenantId))
    credentialOptions.TenantId = tenantId;

AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential(credentialOptions));

var agentsClient = projectClient.AgentAdministrationClient;

// Versionar un agente legacy conserva la identidad compartida del
// proyecto, por eso se borra el existente en lugar de versionarlo.
bool shouldCreateAgent = true;

Console.WriteLine($"[Foundry] Buscando agente existente '{agentName}'...");
try
{
    var existingAgent = agentsClient.GetAgent(agentName);
    Console.WriteLine($"[Foundry] Agente encontrado: {existingAgent.Value.Name}");
    Console.Write("[Foundry] ¿Borrarlo y recrearlo desde cero? (s/N): ");
    var answer = Console.ReadLine()?.Trim();
    shouldCreateAgent = string.Equals(answer, "s", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(answer, "si", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(answer, "sí", StringComparison.OrdinalIgnoreCase);

    if (shouldCreateAgent)
    {
        agentsClient.DeleteAgent(agentName);
        Console.WriteLine($"[Foundry] Agente '{agentName}' borrado.");
    }
    else
    {
        Console.WriteLine("[Foundry] Se conserva el agente existente.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] No se encontró un agente existente con nombre '{agentName}'. Se creará uno nuevo.");
}

if (shouldCreateAgent)
{
    Console.WriteLine("[Foundry] Creando agente Anders con herramienta OpenAPI...");

    OpenApiFunctionDefinition openApiFunction = new(
        "ContosoRetailAPI",
        BinaryData.FromString(openApiSpec),
        new OpenAPIAnonymousAuthenticationDetails())
    {
        Description = "API de Contoso Retail para generar reportes de órdenes de compra"
    };

    DeclarativeAgentDefinition agentDefinition = new(model: modelDeployment)
    {
        Instructions = andersInstructions,
        Tools = { new OpenAPITool(openApiFunction) }
    };

    ProjectsAgentVersion created = await agentsClient.CreateAgentVersionAsync(
        agentName: agentName,
        options: new(agentDefinition));

    Console.WriteLine($"[Foundry] Agente creado: {created.Name} (v{created.Version})");
}

// instance_identity es null en agentes legacy y no nulo en agentes con
// identidad propia de Entra, que es lo que sincroniza Agent 365.
var agentRecord = agentsClient.GetAgent(agentName);
using (var agentJson = JsonDocument.Parse(agentRecord.GetRawResponse().Content.ToString()))
{
    var hasIdentity = agentJson.RootElement.TryGetProperty("instance_identity", out var identity)
                      && identity.ValueKind != JsonValueKind.Null;
    Console.WriteLine(hasIdentity
        ? $"[Foundry] instance_identity: {identity}"
        : "[Foundry] instance_identity: null/ausente -> agente legacy, NO se sincronizará con Agent 365.");
}

// =====================================================================
//  FASE 3: Interactuar con el agente (Responses API + Conversations)
// =====================================================================

// Crear conversación para multi-turn
ProjectConversation conversation = projectClient.ProjectOpenAIClient
    .GetProjectConversationsClient()
    .CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversación creada: {conversation.Id}");

// Obtener cliente de Responses vinculado al agente y conversación
ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(
    defaultAgent: agentName,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat con Anders (escribe 'salir' para terminar) ===");
Console.WriteLine();

while (true)
{
    Console.Write("Tú: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("salir", StringComparison.OrdinalIgnoreCase))
        break;

    // Enviar mensaje y obtener respuesta del agente
    Console.Write("Anders: ");
    try
    {
        ResponseResult response = responseClient.CreateResponse(input);
        Console.WriteLine(response.GetOutputText());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[Error] {ex.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine("[Foundry] Chat finalizado.");
Console.WriteLine($"[Foundry] El agente '{agentName}' permanece disponible.");
