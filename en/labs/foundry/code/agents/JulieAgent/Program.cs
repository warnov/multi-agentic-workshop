using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using System.Text.Json;
using OpenAI.Responses;
using JulieAgent;

#pragma warning disable OPENAI001 // OpenAI preview API

// =====================================================================
//  Julie - Agente Orquestador de Campañas de Marketing
//  (Microsoft Foundry - nueva experiencia)
//
//  Program.cs SOLO se encarga de:
//  1. Crear/verificar los 3 agentes en Microsoft Foundry
//     (SqlAgent, MarketingAgent, Julie)
//  2. Abrir un chat interactivo con Julie
//
//  Toda la orquestación la hace Julie internamente:
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
var bingConnectionId = config["BingConnectionId"]
    ?? throw new InvalidOperationException("Falta BingConnectionId en appsettings.json");

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
    Console.WriteLine("  → Configure FunctionAppBaseUrl en appsettings.json cuando la Function App esté desplegada.");
}

// --- Cliente del proyecto Foundry ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// =====================================================================
//  FASE 1: Crear/verificar los 3 agentes en Microsoft Foundry
// =====================================================================

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine(" Julie - Orquestador de Campañas");
Console.WriteLine("========================================");
Console.WriteLine();

// --- Helper para crear o reutilizar un agente (definición tipada) ---
async Task EnsureAgent(string agentName, AgentDefinition agentDefinition)
{
    Console.WriteLine($"[Foundry] Buscando agente '{agentName}'...");
    AgentRecord? existingAgent = null;
    var shouldOverride = false;
    try
    {
        existingAgent = projectClient.Agents.GetAgent(agentName);
        AgentRecord existing = existingAgent;
        Console.WriteLine($"[Foundry] Agente '{agentName}' encontrado (ID: {existing.Name})");
        Console.Write($"[Foundry] ¿Desea sobreescribir '{agentName}' con una nueva versión? (s/N): ");
        var answer = Console.ReadLine();
        shouldOverride = answer?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true
                      || answer?.Trim().Equals("si", StringComparison.OrdinalIgnoreCase) == true
                      || answer?.Trim().Equals("sí", StringComparison.OrdinalIgnoreCase) == true;

        if (!shouldOverride)
        {
            Console.WriteLine($"[Foundry] Se conserva '{agentName}' existente.");
            return;
        }

    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"[Foundry] Agente '{agentName}' no encontrado. Se creará uno nuevo.");
    }

    try
    {
        var result = await projectClient.Agents.CreateAgentVersionAsync(
            agentName,
            new AgentVersionCreationOptions(agentDefinition));

        var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
        var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
        Console.WriteLine($"[Foundry] Agente '{agentName}' creado/actualizado (v{version})");
    }
    catch (ClientResultException ex) when (ex.Status == 400 && existingAgent is not null)
    {
        Console.WriteLine($"[Foundry] No se pudo crear nueva versión de '{agentName}': {ex.Message}");
        Console.WriteLine($"[Foundry] Se reutilizará la versión existente de '{agentName}'.");
    }
}


// Crear los 3 agentes
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));

Console.WriteLine();
Console.WriteLine("[Foundry] Todos los agentes están listos.");

// =====================================================================
//  FASE 2: Chat interactivo con Julie
// =====================================================================

ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversación creada: {conversation.Id}");

ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: JulieOrchestrator.Name,
    defaultConversationId: conversation.Id);

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

        // --- DEBUG ---
        Console.WriteLine();
        Console.WriteLine($"  [DEBUG] Status: {response.Status}");

        // Serializar response completo a JSON para ver la estructura
        try
        {
            var jsonOpts = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 10 };
            var responseJson = JsonSerializer.Serialize(response, jsonOpts);
            Console.WriteLine($"  [DEBUG] Response JSON ({responseJson.Length} chars):");
            Console.WriteLine(responseJson.Length > 3000 ? responseJson[..3000] + "\n  ... (truncado)" : responseJson);
        }
        catch (Exception serEx)
        {
            Console.WriteLine($"  [DEBUG] No se pudo serializar response: {serEx.Message}");
            // Fallback: dump propiedades via reflection
            foreach (var prop in response.GetType().GetProperties())
            {
                try
                {
                    var val = prop.GetValue(response);
                    var valStr = val?.ToString() ?? "(null)";
                    Console.WriteLine($"  [DEBUG] {prop.Name} ({prop.PropertyType.Name}): {(valStr.Length > 200 ? valStr[..200] + "..." : valStr)}");
                }
                catch { Console.WriteLine($"  [DEBUG] {prop.Name}: <error reading>"); }
            }
        }

        var outputText = response.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            Console.WriteLine();
            Console.WriteLine(outputText);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[Sin texto de salida — revisando conversation items...]");

            // Listar items de la conversación
            try
            {
                var convItems = projectClient.OpenAI.Conversations.GetProjectConversationItems(conversation.Id);
                int count = 0;
                foreach (var ci in convItems)
                {
                    count++;
                    // Serializar cada conversation item
                    try
                    {
                        var ciJson = JsonSerializer.Serialize(ci, new JsonSerializerOptions { WriteIndented = true, MaxDepth = 10 });
                        Console.WriteLine($"  [DEBUG] ConvItem #{count}: {(ciJson.Length > 500 ? ciJson[..500] + "..." : ciJson)}");
                    }
                    catch
                    {
                        Console.WriteLine($"  [DEBUG] ConvItem #{count}: {ci}");
                    }
                }
                Console.WriteLine($"  [DEBUG] Total conversation items: {count}");
            }
            catch (Exception convEx)
            {
                Console.WriteLine($"  [DEBUG] Error leyendo conversation: {convEx.Message}");
            }
        }
        // --- FIN DEBUG ---
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
