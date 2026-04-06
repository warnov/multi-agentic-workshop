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
//  Julie - Marketing Campaign Orchestrator Agent
//  (Microsoft Foundry - new experience)
//
//  Program.cs is ONLY responsible for:
//  1. Creating/verifying the 3 agents in Microsoft Foundry
//     (SqlAgent, MarketingAgent, Julie)
//  2. Opening an interactive chat with Julie
//
//  All orchestration is done by Julie internally:
//    SqlAgent (tool) → generates T-SQL
//    SqlExecutor (OpenAPI tool) → executes SQL against the DB
//    MarketingAgent (tool) → generates personalized messages
//    Julie → organizes the result as campaign JSON
// =====================================================================

// --- Load configuration ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Missing FoundryProjectEndpoint in appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Missing ModelDeploymentName in appsettings.json");
var bingConnectionName = config["BingConnectionName"]
    ?? throw new InvalidOperationException("Missing BingConnectionName in appsettings.json");

// Base URL of the Function App with the SQL query executor.
// Configured in appsettings.json once the function is deployed.
var functionAppBaseUrl = config["FunctionAppBaseUrl"];

// --- Load database structure ---
var dbStructurePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "db-structure.txt");
if (!File.Exists(dbStructurePath))
    dbStructurePath = Path.Combine(Directory.GetCurrentDirectory(), "db-structure.txt");
if (!File.Exists(dbStructurePath))
{
    throw new FileNotFoundException(
        "File db-structure.txt not found. " +
        "Make sure it exists in the root folder of the JulieAgent project.");
}
var dbStructure = File.ReadAllText(dbStructurePath);
Console.WriteLine($"[Config] DB structure loaded ({dbStructure.Length} chars)");

// --- (Optional) Download OpenAPI spec from the Function App ---
JsonElement? openApiSpecJson = null;

if (!string.IsNullOrEmpty(functionAppBaseUrl) && !functionAppBaseUrl.StartsWith("<"))
{
    Console.WriteLine("[OpenAPI] Downloading specification from the Function App...");
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
            Console.WriteLine($"[OpenAPI] Specification downloaded ({openApiSpec.Length} bytes)");
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAPI] Attempt {attempt}/{maxAttempts} failed: {ex.Message}");
            if (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }

            Console.WriteLine("[OpenAPI] Julie will be created without the OpenAPI tool.");
        }
    }
}
else
{
    Console.WriteLine("[Config] FunctionAppBaseUrl not configured.");
    Console.WriteLine("  → Julie will be created without the OpenAPI tool (SQL execution pending).");
    Console.WriteLine("  → Configure FunctionAppBaseUrl in appsettings.json once the Function App is deployed.");
}

// --- Foundry project client ---
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// --- Resolve the full ID of the Bing connection ---
Console.WriteLine($"[Config] Resolving Bing connection '{bingConnectionName}'...");
string bingConnectionId;
try
{
    var bingConnection = await projectClient.Connections.GetConnectionAsync(bingConnectionName);
    bingConnectionId = bingConnection.Value.Id;
    Console.WriteLine($"[Config] Bing connection resolved: {bingConnectionId}");
}
catch (Exception ex)
{
    Console.WriteLine($"[Config] Could not resolve Bing connection: {ex.Message}");
    Console.WriteLine($"[Config] Using name as-is: {bingConnectionName}");
    bingConnectionId = bingConnectionName;
}

// =====================================================================
//  PHASE 1: Create/verify the 3 agents in Microsoft Foundry
// =====================================================================

Console.WriteLine();
Console.WriteLine("========================================");
Console.WriteLine(" Julie - Campaign Orchestrator");
Console.WriteLine("========================================");
Console.WriteLine();

// --- Helper to create or reuse an agent (typed definition) ---
async Task EnsureAgent(string agentName, AgentDefinition agentDefinition)
{
    Console.WriteLine($"[Foundry] Searching for agent '{agentName}'...");
    AgentRecord? existingAgent = null;
    var shouldOverride = false;
    try
    {
        existingAgent = projectClient.Agents.GetAgent(agentName);
        AgentRecord existing = existingAgent;
        Console.WriteLine($"[Foundry] Agent '{agentName}' found (ID: {existing.Name})");
        Console.Write($"[Foundry] Do you want to overwrite '{agentName}' with a new version? (y/N): ");
        var answer = Console.ReadLine();
        shouldOverride = answer?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true
                      || answer?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;

        if (!shouldOverride)
        {
            Console.WriteLine($"[Foundry] Keeping existing '{agentName}'.");
            return;
        }

    }
    catch (ClientResultException ex) when (ex.Status == 404)
    {
        Console.WriteLine($"[Foundry] Agent '{agentName}' not found. A new one will be created.");
    }

    try
    {
        var result = await projectClient.Agents.CreateAgentVersionAsync(
            agentName,
            new AgentVersionCreationOptions(agentDefinition));

        var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
        var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
        Console.WriteLine($"[Foundry] Agent '{agentName}' created/updated (v{version})");
    }
    catch (ClientResultException ex) when (ex.Status == 400 && existingAgent is not null)
    {
        Console.WriteLine($"[Foundry] Could not create new version of '{agentName}': {ex.Message}");
        Console.WriteLine($"[Foundry] Reusing the existing version of '{agentName}'.");
    }
}


// Create the 3 agents
await EnsureAgent(SqlAgent.Name, SqlAgent.GetAgentDefinition(modelDeployment, dbStructure, openApiSpecJson));
await EnsureAgent(MarketingAgent.Name, MarketingAgent.GetAgentDefinition(modelDeployment, bingConnectionId));
await EnsureAgent(JulieOrchestrator.Name, JulieOrchestrator.GetAgentDefinition(modelDeployment, openApiSpecJson));

Console.WriteLine();
Console.WriteLine("[Foundry] All agents are ready.");

// =====================================================================
//  PHASE 2: Interactive chat with Julie
// =====================================================================

ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversation created: {conversation.Id}");

ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: JulieOrchestrator.Name,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat with Julie (type 'exit' to quit) ===");
Console.WriteLine("Example: 'Create a campaign for customers who have purchased bicycles'");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    Console.Write("Julie: ");
    try
    {
        ResponseResult response = responseClient.CreateResponse(input);

        // --- DEBUG ---
        Console.WriteLine();
        Console.WriteLine($"  [DEBUG] Status: {response.Status}");

        // Serialize full response to JSON to inspect structure
        try
        {
            var jsonOpts = new JsonSerializerOptions { WriteIndented = true, MaxDepth = 10 };
            var responseJson = JsonSerializer.Serialize(response, jsonOpts);
            Console.WriteLine($"  [DEBUG] Response JSON ({responseJson.Length} chars):");
            Console.WriteLine(responseJson.Length > 3000 ? responseJson[..3000] + "\n  ... (truncated)" : responseJson);
        }
        catch (Exception serEx)
        {
            Console.WriteLine($"  [DEBUG] Could not serialize response: {serEx.Message}");
            // Fallback: dump properties via reflection
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
            Console.WriteLine("[No output text — checking conversation items...]");

            // List conversation items
            try
            {
                var convItems = projectClient.OpenAI.Conversations.GetProjectConversationItems(conversation.Id);
                int count = 0;
                foreach (var ci in convItems)
                {
                    count++;
                    // Serialize each conversation item
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
                Console.WriteLine($"  [DEBUG] Error reading conversation: {convEx.Message}");
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

Console.WriteLine("[Foundry] Chat ended.");
Console.WriteLine("[Foundry] Agents remain available in Microsoft Foundry.");
