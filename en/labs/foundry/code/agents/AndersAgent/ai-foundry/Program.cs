using Azure.AI.Projects;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

#pragma warning disable CA2252 // Preview API

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

// =====================================================================
//  PHASE 1: Fetch the OpenAPI specification from the Function App
// =====================================================================

Console.WriteLine("[OpenAPI] Downloading specification from the Function App...");

var httpClient = new HttpClient();
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);

Console.WriteLine($"[OpenAPI] Specification downloaded ({openApiSpec.Length} bytes)");

// =====================================================================
//  PHASE 2: Create agent with OpenAPI tool in Foundry
// =====================================================================

// Foundry project client
var projectClient = new AIProjectClient(
    new Uri(foundryEndpoint),
    new DefaultAzureCredential());

// Get the persistent agents client
var agentsClient = projectClient.GetPersistentAgentsClient();

// Define the OpenAPI tool from the downloaded specification
var openApiTool = new OpenApiToolDefinition(
    new OpenApiFunctionDefinition(
        name: "ContosoRetailAPI",
        spec: BinaryData.FromString(openApiSpec),
        openApiAuthentication: new OpenApiAnonymousAuthDetails())
    {
        Description = "Contoso Retail API for generating purchase order reports"
    });

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

Console.WriteLine("[Foundry] Searching for existing Anders agent...");

PersistentAgent? agent = null;

// Check if an agent with the same name already exists
await foreach (var existingAgent in agentsClient.Administration.GetAgentsAsync())
{
    if (existingAgent.Name == "Anders - Executor Agent")
    {
        agent = existingAgent;
        Console.WriteLine($"[Foundry] Existing agent found: {agent.Name} (ID: {agent.Id})");
        break;
    }
}

if (agent is null)
{
    Console.WriteLine("[Foundry] Creating Anders agent with OpenAPI tool...");

    agent = (await agentsClient.Administration.CreateAgentAsync(
        model: modelDeployment,
        name: "Anders - Executor Agent",
        description: "Contoso Retail executor agent with OpenAPI tool",
        instructions: andersInstructions,
        tools: new List<ToolDefinition> { openApiTool })).Value;

    Console.WriteLine($"[Foundry] Agent created: {agent.Name} (ID: {agent.Id})");
}

// =====================================================================
//  PHASE 3: Interact with the agent (threads & runs)
// =====================================================================

PersistentAgentThread thread = (await agentsClient.Threads.CreateThreadAsync()).Value;
Console.WriteLine($"[Foundry] Thread created: {thread.Id}");
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

    // Send user message to the thread
    await agentsClient.Messages.CreateMessageAsync(
        threadId: thread.Id,
        role: MessageRole.User,
        content: input);

    // Run the agent on the thread
    ThreadRun run = (await agentsClient.Runs.CreateRunAsync(thread, agent)).Value;

    // Wait for the run to finish (polling)
    Console.Write("Anders: ");
    while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        run = (await agentsClient.Runs.GetRunAsync(thread.Id, run.Id)).Value;
    }

    // Process result
    if (run.Status == RunStatus.Completed)
    {
        // Get thread messages (most recent first)
        var messages = agentsClient.Messages.GetMessagesAsync(threadId: thread.Id);

        await foreach (PersistentThreadMessage message in messages)
        {
            // Only show the first agent response (most recent)
            if (message.Role == MessageRole.Agent)
            {
                foreach (MessageContent contentItem in message.ContentItems)
                {
                    if (contentItem is MessageTextContent textContent)
                    {
                        Console.WriteLine(textContent.Text);
                    }
                }
                break;
            }
        }
    }
    else
    {
        Console.WriteLine($"\n[Error] Run ended with status: {run.Status}");
        if (run.LastError != null)
            Console.WriteLine($"[Error] {run.LastError.Code}: {run.LastError.Message}");
    }
    Console.WriteLine();
}

// =====================================================================
//  Cleanup: delete the thread (agent persists for reuse)
// =====================================================================

Console.WriteLine("[Foundry] Cleaning up thread...");
await agentsClient.Threads.DeleteThreadAsync(thread.Id);
Console.WriteLine($"[Foundry] Thread deleted. Agent '{agent.Name}' (ID: {agent.Id}) remains available.");
