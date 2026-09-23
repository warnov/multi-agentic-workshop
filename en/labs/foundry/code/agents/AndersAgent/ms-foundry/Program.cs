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
//  Anders - Executor Agent (Microsoft Foundry)
//
//  Uses the Foundry projects (new) API: Azure.AI.Projects 2.x +
//  Azure.AI.Projects.Agents + Azure.AI.Extensions.OpenAI.
//
//  The agent object is deleted and recreated rather than versioned, so it
//  is provisioned under the current object model and receives its own
//  Entra agent identity, which Agent 365 registry sync depends on.
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
    If fields come with different casing, adapt them.
    If startDate or endDate is missing, infer them from the order dates.
    If per-line totals are missing, calculate them.
    Respond in English.
    """;

// Foundry project client
AIProjectClient projectClient = new(
    endpoint: new Uri(foundryEndpoint),
    tokenProvider: new DefaultAzureCredential());

var agentsClient = projectClient.AgentAdministrationClient;

// Adding a version to a legacy agent object keeps the shared project
// identity, so an existing agent is deleted instead of versioned.
bool shouldCreateAgent = true;

Console.WriteLine($"[Foundry] Searching for existing agent '{agentName}'...");
try
{
    var existingAgent = agentsClient.GetAgent(agentName);
    Console.WriteLine($"[Foundry] Agent found: {existingAgent.Value.Name}");
    Console.Write("[Foundry] Delete it and recreate it from scratch? (y/N): ");
    var answer = Console.ReadLine()?.Trim();
    shouldCreateAgent = string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase);

    if (shouldCreateAgent)
    {
        agentsClient.DeleteAgent(agentName);
        Console.WriteLine($"[Foundry] Agent '{agentName}' deleted.");
    }
    else
    {
        Console.WriteLine("[Foundry] Keeping existing agent.");
    }
}
catch (ClientResultException ex) when (ex.Status == 404)
{
    Console.WriteLine($"[Foundry] No existing agent found with name '{agentName}'. A new one will be created.");
}

if (shouldCreateAgent)
{
    Console.WriteLine("[Foundry] Creating Anders agent with OpenAPI tool...");

    OpenApiFunctionDefinition openApiFunction = new(
        "ContosoRetailAPI",
        BinaryData.FromString(openApiSpec),
        new OpenAPIAnonymousAuthenticationDetails())
    {
        Description = "Contoso Retail API for generating purchase order reports"
    };

    DeclarativeAgentDefinition agentDefinition = new(model: modelDeployment)
    {
        Instructions = andersInstructions,
        Tools = { new OpenAPITool(openApiFunction) }
    };

    ProjectsAgentVersion created = await agentsClient.CreateAgentVersionAsync(
        agentName: agentName,
        options: new(agentDefinition));

    Console.WriteLine($"[Foundry] Agent created: {created.Name} (v{created.Version})");
}

// instance_identity is null on legacy agents and non-null on agents that
// carry their own Entra identity, which is what Agent 365 syncs.
var agentRecord = agentsClient.GetAgent(agentName);
using (var agentJson = JsonDocument.Parse(agentRecord.GetRawResponse().Content.ToString()))
{
    var hasIdentity = agentJson.RootElement.TryGetProperty("instance_identity", out var identity)
                      && identity.ValueKind != JsonValueKind.Null;
    Console.WriteLine(hasIdentity
        ? $"[Foundry] instance_identity: {identity}"
        : "[Foundry] instance_identity: null/absent -> legacy agent, it will NOT sync to Agent 365.");
}

// =====================================================================
//  PHASE 3: Interact with the agent (Responses API + Conversations)
// =====================================================================

// Create conversation for multi-turn
ProjectConversation conversation = projectClient.ProjectOpenAIClient
    .GetProjectConversationsClient()
    .CreateProjectConversation();
Console.WriteLine($"[Foundry] Conversation created: {conversation.Id}");

// Get Responses client bound to the agent and conversation
ProjectResponsesClient responseClient = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(
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
