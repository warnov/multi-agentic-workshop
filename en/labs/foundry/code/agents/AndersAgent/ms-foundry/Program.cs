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
//  Anders - Executor Agent (Microsoft Foundry - new experience)
//
//  This version uses the Azure.AI.Projects + Azure.AI.Projects.OpenAI SDK
//  with the Responses API (new Microsoft Foundry experience).
//
//  The OpenAPI tool is configured via protocol method (BinaryContent)
//  because OpenApiAgentTool types are internal in SDK 1.2.x.
// =====================================================================

// --- Load configuration ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("Missing FoundryProjectEndpoint in appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("Missing ModelDeploymentName in appsettings.json");
var functionAppBaseUrl = config["FunctionAppBaseUrl"]
    ?? throw new InvalidOperationException("Missing FunctionAppBaseUrl in appsettings.json");
var agentName = "Anders";

// =====================================================================
//  PHASE 1: Download the OpenAPI specification from the Function App
// =====================================================================

Console.WriteLine("[OpenAPI] Downloading specification from the Function App...");

var httpClient = new HttpClient();
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);

Console.WriteLine($"[OpenAPI] Specification downloaded ({openApiSpec.Length} bytes)");

// =====================================================================
//  PHASE 2: Create agent with OpenAPI tool (protocol method)
// =====================================================================

// Anders agent instructions
var andersInstructions = """
    You are Anders, the executor agent for Contoso Retail.

    Your responsibility is to execute specific operational actions when requested.
    Your main capability is to generate customer purchase order reports
    using the Contoso Retail API available as an OpenAPI tool.

    When you receive order data, you must build the JSON request body
    with EXACTLY this schema to invoke the ordersReporter endpoint:

    {
      "customerName": "Customer Name",
      "startDate": "YYYY-MM-DD",
      "endDate": "YYYY-MM-DD",
      "orders": [
        {
          "orderNumber": "order code",
          "orderDate": "YYYY-MM-DD",
          "orderLineNumber": 1,
          "productName": "product name",
          "brandName": "brand name",
          "categoryName": "category name",
          "quantity": 1.0,
          "unitPrice": 0.00,
          "lineTotal": 0.00
        }
      ]
    }

    Rules:
    - ALL fields are required for each order line.
    - If an order has multiple products, each product is a separate
      element in the "orders" array with the same "orderNumber" and "orderDate"
      but a different "orderLineNumber" (sequential: 1, 2, 3...).
    - Dates must be in ISO format: YYYY-MM-DD.
    - "quantity", "unitPrice" and "lineTotal" are numeric (double).

    Always confirm the action taken to the user, including the report URL.
    If the data is insufficient or invalid, explain what is missing.
    Respond in English.
    """;

// Foundry project client (new experience)
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

// Check if the agent already exists
bool shouldCreateAgent = true;
AgentRecord? existingAgent = null;

Console.WriteLine($"[Foundry] Searching for existing agent '{agentName}'...");
try
{
    existingAgent = projectClient.Agents.GetAgent(agentName);
    Console.WriteLine($"[Foundry] Agent found: {existingAgent.Name} (ID: {existingAgent.Id})");
    Console.Write("[Foundry] Do you want to overwrite it with a new version? (y/N): ");
    var answer = Console.ReadLine();
    shouldCreateAgent = answer?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true
                     || answer?.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase) == true;

    if (!shouldCreateAgent)
    {
        Console.WriteLine("[Foundry] Keeping existing agent.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] No existing agent found with name '{agentName}'. A new one will be created.");
}

AgentRecord agentRecord;

if (shouldCreateAgent)
{
    // Build the agent definition JSON including the OpenAPI tool
    // (OpenApiAgentTool types are internal, using protocol method with BinaryContent)
    Console.WriteLine("[Foundry] Creating/updating Anders agent with OpenAPI tool...");

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
                        description = "Contoso Retail API for generating purchase order reports",
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

    // Parse response to get agent info
    var responseJson = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
    var version = responseJson.RootElement.TryGetProperty("version", out var vProp) ? vProp.GetString() : "?";
    Console.WriteLine($"[Foundry] Agent created/updated: {agentName} (v{version})");
}

// Get the registered agent
agentRecord = projectClient.Agents.GetAgent(agentName);
Console.WriteLine($"[Foundry] Agent retrieved: {agentRecord.Name} (ID: {agentRecord.Id})");

// =====================================================================
//  PHASE 3: Interact with the agent (Responses API + Conversations)
// =====================================================================

// Create conversation for multi-turn
ProjectConversation conversation = projectClient.OpenAI.Conversations.CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversation created: {conversation.Id}");

// Get Responses client bound to the agent and conversation
ProjectResponsesClient responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(
    defaultAgent: agentName,
    defaultConversationId: conversation.Id);

Console.WriteLine();
Console.WriteLine("=== Chat with Anders (type 'exit' to quit) ===");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    // Send message and get agent response
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

Console.WriteLine("[Foundry] Chat ended.");
Console.WriteLine($"[Foundry] Agent '{agentName}' remains available.");
