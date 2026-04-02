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
//  Julie - Agente Orquestrador de Campanhas de Marketing
//  (Microsoft Foundry - nova experiência)
//
//  Program.cs SOMENTE se encarrega de:
//  1. Criar/verificar os 3 agentes no Microsoft Foundry
//     (SqlAgent, MarketingAgent, Julie)
//  2. Abrir um chat interativo com Julie
//
//  Toda a orquestração é feita internamente por Julie:
//    SqlAgent (tool) → gera T-SQL
//    SqlExecutor (OpenAPI tool) → executa SQL contra o BD
//    MarketingAgent (tool) → gera mensagens personalizadas
//    Julie → organiza o resultado como JSON de campanha
// =====================================================================

// --- Carregar configuração ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Ausente FoundryProjectEndpoint no appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Ausente ModelDeploymentName no appsettings.json");
var bingConnectionId = config["BingConnectionId"]
    ?? throw new InvalidOperationException("Ausente BingConnectionId no appsettings.json");

// URL base da Function App com o executor de consultas SQL.
// Configurar em appsettings.json quando a função estiver implantada.
var functionAppBaseUrl = config["FunctionAppBaseUrl"];

// --- Carregar estrutura do banco de dados ---
var dbStructurePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "db-structure.txt");
if (!File.Exists(dbStructurePath))
    dbStructurePath = Path.Combine(Directory.GetCurrentDirectory(), "db-structure.txt");
if (!File.Exists(dbStructurePath))
{
    throw new FileNotFoundException(
        "Arquivo db-structure.txt não encontrado. " +
        "Certifique-se de que ele existe na pasta raiz do projeto JulieAgent.");
}
var dbStructure = File.ReadAllText(dbStructurePath);
Console.WriteLine($"[Config] Estrutura do BD carregada ({dbStructure.Length} caracteres)");

// --- (Opcional) Baixar spec OpenAPI da Function App ---
JsonElement? openApiSpecJson = null;

if (!string.IsNullOrEmpty(functionAppBaseUrl) && !functionAppBaseUrl.StartsWith("<"))
{
    Console.WriteLine("[OpenAPI] Baixando especificação da Function App...");
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
            Console.WriteLine($"[OpenAPI] Especificação baixada ({openApiSpec.Length} bytes)");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAPI] Tentativa {attempt}/{maxAttempts} falhou: {ex.Message}");
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }

            Console.WriteLine("[OpenAPI] Julie será criada sem ferramenta OpenAPI.");
        }
    }
}
else
{
    Console.WriteLine("[Config] FunctionAppBaseUrl não configurada.");
    Console.WriteLine("  → Julie será criada sem ferramenta OpenAPI (execução SQL pendente).");
    Console.WriteLine("  → Configure FunctionAppBaseUrl em appsettings.json quando a Function App estiver implantada.");
}

// --- Cliente do projeto Foundry ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// =====================================================================
//  FASE 1: Criar/verificar os 3 agentes no Microsoft Foundry
// =====================================================================

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine(" Julie - Orquestrador de Campanhas");
Console.WriteLine("========================================");
Console.WriteLine();

// --- Helper para criar ou reutilizar um agente (definição tipada) ---
async Task EnsureAgent(string agentName, AgentDefinition agentDefinition)
{
    Console.WriteLine($"[Foundry] Procurando agente '{agentName}'...");
    AgentRecord? existingAgent = null;
    var shouldOverride = false;
    try
    {
        existingAgent = projectClient.Agents.GetAgent(agentName);
        AgentRecord existing = existingAgent;
        Console.WriteLine($"[Foundry] Agente '{agentName}' encontrado (ID: {existing.Name})");
        Console.Write($"[Foundry] Deseja substituir '{agentName}' por uma nova versão? (s/N): ");
        var answer = Console.ReadLine();
        shouldOverride = answer?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true
                      || answer?.Trim().Equals("sim", StringComparison.OrdinalIgnoreCase) == true;

        if (!shouldOverride)
        {
            Console.WriteLine($"[Foundry] Agente '{agentName}' mantido.");
            return;
        }

    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"[Foundry] Agente '{agentName}' não encontrado. Um novo será criado.");
    }

    try
    {
        var result = await projectClient.Agents.CreateAgentVersionAsync(
            agentName,
            new AgentVersionCreationOptions(agentDefinition));

        var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
        var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
        Console.WriteLine($"[Foundry] Agente '{agentName}' criado/atualizado (v{version})");
    }
    catch (ClientResultException ex) when (ex.Status == 400 && existingAgent is not null)
    {
        Console.WriteLine($"[Foundry] Não foi possível criar nova versão de '{agentName}': {ex.Message}");
        Console.WriteLine($"[Foundry] A versão existente de '{agentName}' será reutilizada.");
    }
}


// Criar os 3 agentes
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));

Console.WriteLine();
Console.WriteLine("[Foundry] Todos os agentes estão prontos.");

// =====================================================================
//  FASE 2: Chat interactivo con Julie
// =====================================================================

ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversa criada: {conversation.Id}");

ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: JulieOrchestrator.Name,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat com Julie (digite 'sair' para terminar) ===");
Console.WriteLine("Exemplo: 'Crie uma campanha para clientes que compraram bicicletas'");
Console.WriteLine();

while (true)
{
    Console.Write("Você: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("sair", StringComparison.OrdinalIgnoreCase))
        break;

    Console.Write("Julie: ");
    try
    {
        ResponseResult response = responseClient.CreateResponse(input);

        // --- DEBUG ---
        Console.WriteLine();
        Console.WriteLine($"  [DEBUG] Status: {response.Status}");

        // Serializar response completo a JSON para ver a estrutura
        try
        {
            var jsonOpts = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 10 };
            var responseJson = JsonSerializer.Serialize(response, jsonOpts);
            Console.WriteLine($"  [DEBUG] Response JSON ({responseJson.Length} chars):");
            Console.WriteLine(responseJson.Length > 3000 ? responseJson[..3000] + "\n  ... (truncado)" : responseJson);
        }
        catch (Exception serEx)
        {
            Console.WriteLine($"  [DEBUG] Não foi possível serializar response: {serEx.Message}");
            // Fallback: dump propriedades via reflection
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
            Console.WriteLine("[Sem texto de saída — verificando itens da conversa...]");

            // Listar itens da conversa
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
Console.WriteLine("[Foundry] Os agentes permanecem disponíveis no Microsoft Foundry.");
