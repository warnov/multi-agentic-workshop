using Azure.AI.Projects;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

#pragma warning disable CA2252 // API em pré-visualização

// --- Carregar configuração ---
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var foundryEndpoint = config["FoundryProjectEndpoint"]
    ?? throw new InvalidOperationException("FoundryProjectEndpoint ausente no appsettings.json");
var modelDeployment = config["ModelDeploymentName"]
    ?? throw new InvalidOperationException("ModelDeploymentName ausente no appsettings.json");
var functionAppBaseUrl = config["FunctionAppBaseUrl"]
    ?? throw new InvalidOperationException("FunctionAppBaseUrl ausente no appsettings.json");

// =====================================================================
//  FASE 1: Obter a especificação OpenAPI da Function App
// =====================================================================

Console.WriteLine("[OpenAPI] Baixando especificação da Function App...");

var httpClient = new HttpClient();
var openApiSpecUrl = $"{functionAppBaseUrl}/openapi/v3.json";
var openApiSpec = await httpClient.GetStringAsync(openApiSpecUrl);

Console.WriteLine($"[OpenAPI] Especificação baixada ({openApiSpec.Length} bytes)");

// =====================================================================
//  FASE 2: Criar agente com ferramenta OpenAPI no Foundry
// =====================================================================

// Cliente do projeto Foundry
var projectClient = new AIProjectClient(
    new Uri(foundryEndpoint),
    new DefaultAzureCredential());

// Obter o cliente de agentes persistentes
var agentsClient = projectClient.GetPersistentAgentsClient();

// Definir a ferramenta OpenAPI a partir da especificação baixada
var openApiTool = new OpenApiToolDefinition(
    new OpenApiFunctionDefinition(
        name: "ContosoRetailAPI",
        spec: BinaryData.FromString(openApiSpec),
        openApiAuthentication: new OpenApiAnonymousAuthDetails())
    {
        Description = "API da Contoso Retail para gerar relatórios de pedidos de compra"
    });

// Instruções do agente Anders
var andersInstructions = """
    Você é Anders, o agente executor da Contoso Retail.

    Sua responsabilidade é executar ações operacionais concretas quando solicitado.
    Sua principal capacidade é gerar relatórios de pedidos de compra de clientes
    usando a API da Contoso Retail disponível como ferramenta OpenAPI.

    Quando receber dados de pedidos, você deve construir o JSON do request body
    com EXATAMENTE este schema para invocar o endpoint ordersReporter:

    {
      "customerName": "Nome do Cliente",
      "startDate": "YYYY-MM-DD",
      "endDate": "YYYY-MM-DD",
      "orders": [
        {
          "orderNumber": "código do pedido",
          "orderDate": "YYYY-MM-DD",
          "orderLineNumber": 1,
          "productName": "nome do produto",
          "brandName": "nome da marca",
          "categoryName": "nome da categoria",
          "quantity": 1.0,
          "unitPrice": 0.00,
          "lineTotal": 0.00
        }
      ]
    }

    Regras:
    - TODOS os campos são obrigatórios para cada linha de pedido.
    - Se um pedido tiver múltiplos produtos, cada produto é um elemento
      separado no array "orders" com o mesmo "orderNumber" e "orderDate"
      mas diferente "orderLineNumber" (sequencial: 1, 2, 3...).
    - As datas devem estar no formato ISO: YYYY-MM-DD.
    - "quantity", "unitPrice" e "lineTotal" são numéricos (double).

    Sempre confirme a ação realizada ao usuário, incluindo a URL do relatório.
    Se os dados forem insuficientes ou inválidos, explique o que falta.
    Responda em português.
    """;

Console.WriteLine("[Foundry] Procurando agente Anders existente...");

PersistentAgent? agent = null;

// Verificar se já existe um agente com o mesmo nome
await foreach (var existingAgent in agentsClient.Administration.GetAgentsAsync())
{
    if (existingAgent.Name == "Anders - Agente Executor")
    {
        agent = existingAgent;
        Console.WriteLine($"[Foundry] Agente existente encontrado: {agent.Name} (ID: {agent.Id})");
        break;
    }
}

if (agent is null)
{
    Console.WriteLine("[Foundry] Criando agente Anders com ferramenta OpenAPI...");

    agent = (await agentsClient.Administration.CreateAgentAsync(
        model: modelDeployment,
        name: "Anders - Agente Executor",
        description: "Agente executor da Contoso Retail com ferramenta OpenAPI",
        instructions: andersInstructions,
        tools: new List<ToolDefinition> { openApiTool })).Value;

    Console.WriteLine($"[Foundry] Agente criado: {agent.Name} (ID: {agent.Id})");
}

// =====================================================================
//  FASE 3: Interagir com o agente (threads & runs)
// =====================================================================

PersistentAgentThread thread = (await agentsClient.Threads.CreateThreadAsync()).Value;
Console.WriteLine($"[Foundry] Thread creado: {thread.Id}");
Console.WriteLine();
Console.WriteLine("=== Chat com Anders (digite 'sair' para terminar) ===");
Console.WriteLine();

while (true)
{
    Console.Write("Você: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) ||
        input.Equals("sair", StringComparison.OrdinalIgnoreCase))
        break;

    // Enviar mensagem do usuário ao thread
    await agentsClient.Messages.CreateMessageAsync(
        threadId: thread.Id,
        role: MessageRole.User,
        content: input);

    // Executar o agente no thread
    ThreadRun run = (await agentsClient.Runs.CreateRunAsync(thread, agent)).Value;

    // Aguardar o run terminar (polling)
    Console.Write("Anders: ");
    while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        run = (await agentsClient.Runs.GetRunAsync(thread.Id, run.Id)).Value;
    }

    // Processar resultado
    if (run.Status == RunStatus.Completed)
    {
        // Obter mensagens do thread (as mais recentes primeiro)
        var messages = agentsClient.Messages.GetMessagesAsync(threadId: thread.Id);

        await foreach (PersistentThreadMessage message in messages)
        {
            // Mostrar apenas a primeira resposta do agente (a mais recente)
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
        Console.WriteLine($"\n[Erro] Run terminou com estado: {run.Status}");
        if (run.LastError != null)
            Console.WriteLine($"[Erro] {run.LastError.Code}: {run.LastError.Message}");
    }
    Console.WriteLine();
}

// =====================================================================
//  Limpeza do thread (o agente persiste para reutilização)
// =====================================================================

Console.WriteLine("[Foundry] Limpando thread...");
await agentsClient.Threads.DeleteThreadAsync(thread.Id);
Console.WriteLine($"[Foundry] Thread excluído. O agente '{agent.Name}' (ID: {agent.Id}) permanece disponível.");
