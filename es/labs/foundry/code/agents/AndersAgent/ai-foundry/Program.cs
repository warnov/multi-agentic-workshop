using Azure.AI.Projects;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

#pragma warning disable CA2252 // API en preview

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

// =====================================================================
//  FASE 1: Obtener la especificación OpenAPI de la Function App
// =====================================================================

Console.WriteLine("[OpenAPI] Descargando especificación desde la Function App...");

var httpClient = new HttpClient();
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);

Console.WriteLine($"[OpenAPI] Especificación descargada ({openApiSpec.Length} bytes)");

// =====================================================================
//  FASE 2: Crear agente con herramienta OpenAPI en Foundry
// =====================================================================

// Cliente del proyecto Foundry
var projectClient = new AIProjectClient(
    new Uri(foundryEndpoint),
    new DefaultAzureCredential());

// Obtener el cliente de agentes persistentes
var agentsClient = projectClient.GetPersistentAgentsClient();

// Definir la herramienta OpenAPI a partir de la especificación descargada
var openApiTool = new OpenApiToolDefinition(
    new OpenApiFunctionDefinition(
        name: "ContosoRetailAPI",
        spec: BinaryData.FromString(openApiSpec),
        openApiAuthentication: new OpenApiAnonymousAuthDetails())
    {
        Description = "API de Contoso Retail para generar reportes de órdenes de compra"
    });

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

Console.WriteLine("[Foundry] Buscando agente Anders existente...");

PersistentAgent? agent = null;

// Buscar si ya existe un agente con el mismo nombre
await foreach (var existingAgent in agentsClient.Administration.GetAgentsAsync())
{
    if (existingAgent.Name == "Anders - Agente Ejecutor")
    {
        agent = existingAgent;
        Console.WriteLine($"[Foundry] Agente existente encontrado: {agent.Name} (ID: {agent.Id})");
        break;
    }
}

if (agent is null)
{
    Console.WriteLine("[Foundry] Creando agente Anders con herramienta OpenAPI...");

    agent = (await agentsClient.Administration.CreateAgentAsync(
        model: modelDeployment,
        name: "Anders - Agente Ejecutor",
        description: "Agente ejecutor de Contoso Retail con herramienta OpenAPI",
        instructions: andersInstructions,
        tools: new List<ToolDefinition> { openApiTool })).Value;

    Console.WriteLine($"[Foundry] Agente creado: {agent.Name} (ID: {agent.Id})");
}

// =====================================================================
//  FASE 3: Interactuar con el agente (threads & runs)
// =====================================================================

PersistentAgentThread thread = (await agentsClient.Threads.CreateThreadAsync()).Value;
Console.WriteLine($"[Foundry] Thread creado: {thread.Id}");
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

    // Enviar mensaje del usuario al thread
    await agentsClient.Messages.CreateMessageAsync(
        threadId: thread.Id,
        role: MessageRole.User,
        content: input);

    // Ejecutar el agente sobre el thread
    ThreadRun run = (await agentsClient.Runs.CreateRunAsync(thread, agent)).Value;

    // Esperar a que el run termine (polling)
    Console.Write("Anders: ");
    while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        run = (await agentsClient.Runs.GetRunAsync(thread.Id, run.Id)).Value;
    }

    // Procesar resultado
    if (run.Status == RunStatus.Completed)
    {
        // Obtener mensajes del thread (los más recientes primero)
        var messages = agentsClient.Messages.GetMessagesAsync(threadId: thread.Id);

        await foreach (PersistentThreadMessage message in messages)
        {
            // Solo mostrar la primera respuesta del agente (la más reciente)
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
        Console.WriteLine($"\n[Error] Run terminó con estado: {run.Status}");
        if (run.LastError != null)
            Console.WriteLine($"[Error] {run.LastError.Code}: {run.LastError.Message}");
    }
    Console.WriteLine();
}

// =====================================================================
//  Limpieza del thread (el agente persiste para reutilizarse)
// =====================================================================

Console.WriteLine("[Foundry] Limpiando thread...");
await agentsClient.Threads.DeleteThreadAsync(thread.Id);
Console.WriteLine($"[Foundry] Thread eliminado. El agente '{agent.Name}' (ID: {agent.Id}) permanece disponible.");
