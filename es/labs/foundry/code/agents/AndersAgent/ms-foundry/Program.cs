using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI.Responses;

#pragma warning disable OPENAI001 // OpenAI preview API

// =====================================================================
//  Anders - Agente Ejecutor (Microsoft Foundry - nueva experiencia)
//
//  Esta versión usa el SDK Azure.AI.Projects + Azure.AI.Projects.OpenAI
//  con la API de Responses (nueva experiencia de Microsoft Foundry).
//
//  La herramienta OpenAPI se configura vía protocol method (BinaryContent)
//  ya que los tipos OpenApiAgentTool son internos en el SDK 1.2.x.
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
    Responde en español.
    """;

// Cliente del proyecto Foundry (nueva experiencia)
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// Verificar si el agente ya existe
bool shouldCreateAgent = true;
AgentRecord? existingAgent = null;

Console.WriteLine($"[Foundry] Buscando agente existente '{agentName}'...");
try
{
    existingAgent = projectClient.Agents.GetAgent(agentName);
    Console.WriteLine($"[Foundry] Agente encontrado: {existingAgent.Name} (ID: {existingAgent.Id})");
    Console.Write("[Foundry] ¿Desea sobreescribirlo con una nueva versión? (s/N): ");
    var answer = Console.ReadLine();
    shouldCreateAgent = answer?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true
                     || answer?.Trim().Equals("si", StringComparison.OrdinalIgnoreCase) == true
                     || answer?.Trim().Equals("sí", StringComparison.OrdinalIgnoreCase) == true;

    if (!shouldCreateAgent)
    {
        Console.WriteLine("[Foundry] Se conserva el agente existente.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] No se encontró un agente existente con nombre '{agentName}'. Se creará uno nuevo.");
}

AgentRecord agentRecord;

if (shouldCreateAgent)
{
    // Construir el JSON con la definición del agente incluyendo herramienta OpenAPI
    // (los tipos OpenApiAgentTool son internos, se usa protocol method con BinaryContent)
    Console.WriteLine("[Foundry] Creando/actualizando agente Anders con herramienta OpenAPI...");

    var openApiSpecJson = JsonSerializer.Deserialize<JsonElement>(openApiSpec);

    var agentDefinitionJson = new
    {
        definition = new
        {
            kind = "prompt",
            model = modelDeployment,
            instructions = andersInstructions,
            tools = new object[]
            {
                new
                {
                    type = "openapi",
                    openapi = new
                    {
                        name = "ContosoRetailAPI",
                        description = "API de Contoso Retail para generar reportes de órdenes de compra",
                        spec = openApiSpecJson,
                        auth = new { type = "anonymous" }
                    }
                }
            }
        }
    };

    var jsonContent = JsonSerializer.Serialize(agentDefinitionJson, new JsonSerializerOptions { WriteIndented = false });
    var result = await projectClient.Agents.CreateAgentVersionAsync(
        agentName,
        BinaryContent.Create(BinaryData.FromString(jsonContent)),
        new RequestOptions());

    // Parsear respuesta para obtener info del agente
    var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
    var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
    Console.WriteLine($"[Foundry] Agente creado/actualizado: {agentName} (v{version})");
}

// Obtener el agente registrado
agentRecord = projectClient.Agents.GetAgent(agentName);
Console.WriteLine($"[Foundry] Agente obtenido: {agentRecord.Name} (ID: {agentRecord.Id})");

// =====================================================================
//  FASE 3: Interactuar con el agente (Responses API + Conversations)
// =====================================================================

// Crear conversación para multi-turn
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversación creada: {conversation.Id}");

// Obtener cliente de Responses vinculado al agente y conversación
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
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
