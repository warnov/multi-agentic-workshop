using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

#pragma warning disable AAIP001

// =====================================================================
//  Julie - Orquestradora de Campanhas de Marketing (agente hospedado)
//
//  Executa dentro do sandbox gerenciado pelo Foundry e é acessada através
//  do protocolo Responses. A orquestração que antes vivia no YAML do
//  workflow agora é um workflow do Agent Framework: um grafo explícito.
//
//      query-customers  ->  write-campaign  ->  saída
//
//  A ordem é fixada pelo grafo, não decidida por um modelo. Cada nó
//  envolve um agente de prompt, o que permite ao primeiro cair para dados
//  de demonstração quando o Fabric SQL não está configurado, e ao segundo
//  chamar o MarketingAgent uma vez por cliente.
// =====================================================================

var projectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT não está definida.");
var sqlAgentName = Environment.GetEnvironmentVariable("SQL_AGENT_NAME") ?? "SqlAgent";
var marketingAgentName = Environment.GetEnvironmentVariable("MARKETING_AGENT_NAME") ?? "MarketingAgent";

// real = falha se o Fabric não estiver disponível, demo = nunca consulta SQL, auto = cai para dados de demonstração.
var dataMode = (Environment.GetEnvironmentVariable("JULIE_DATA_MODE") ?? "auto").Trim().ToLowerInvariant();

AIProjectClient projectClient = new(new Uri(projectEndpoint), new DefaultAzureCredential());

// O grafo só pode ser construído com os subagentes já resolvidos. Deixar isso
// lançar derrubaria o contêiner antes de o endpoint Responses existir, e quem
// chamasse veria apenas um opaco "424 session_not_ready"; por isso uma falha é
// convertida em um workflow de um único nó que informa o motivo real.
Workflow workflow;
try
{
    AIAgent sqlAgent = projectClient.AsAIAgent(
        await projectClient.AgentAdministrationClient.GetAgentAsync(sqlAgentName));
    AIAgent marketingAgent = projectClient.AsAIAgent(
        await projectClient.AgentAdministrationClient.GetAgentAsync(marketingAgentName));

    QueryCustomers queryCustomers = new(sqlAgent, dataMode);
    WriteCampaign writeCampaign = new(marketingAgent);

    workflow = new WorkflowBuilder(queryCustomers)
        .AddEdge(queryCustomers, writeCampaign, label: "clientes")
        .WithOutputFrom(writeCampaign)
        .Build();
}
catch (Exception ex)
{
    StartupFailure failure = new(ex.Message);
    workflow = new WorkflowBuilder(failure).WithOutputFrom(failure).Build();
}

// Impresso uma vez para que o grafo possa ser lido nos logs do agente e renderizado com o Graphviz.
Console.WriteLine($"=== Workflow da Julie (modo de dados: {dataMode}) ===");
Console.WriteLine(workflow.ToDotString());
Console.WriteLine("=== fim do grafo do workflow ===");

// Sem includeWorkflowOutputsInResponse o workflow executa, mas a resposta volta vazia.
AIAgent julie = workflow.AsAIAgent(
    name: "Julie",
    description: "Orquestradora de campanhas de marketing da Contoso Retail.",
    includeExceptionDetails: true,
    includeWorkflowOutputsInResponse: true);

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(julie);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();

internal sealed record Customer(string FirstName, string LastName, string Email, string FavoriteCategory)
{
    public string FullName => $"{FirstName} {LastName}".Trim();
}

internal sealed record CustomerQuery(List<Customer> Customers, string DataSource, string? Warning);

// O SqlAgent é invocado de dentro deste nó em vez de ser um nó em si.
// Um nó agente que falha aborta toda a execução, e isso deixaria o
// laboratório inutilizável sempre que faltasse a conexão SQL do Fabric.
//
// O nó inicial de um workflow hospedado precisa falar o protocolo de chat
// (List<ChatMessage> mais TurnToken), que é o que o ChatProtocolExecutor fornece.
internal sealed class QueryCustomers(AIAgent sqlAgent, string dataMode)
    : ChatProtocolExecutor("query-customers", new ChatProtocolExecutorOptions
    {
        StringMessageChatRole = ChatRole.User,
        AutoSendTurnToken = false
    })
{
    private static readonly List<Customer> DemoCustomers =
    [
        new("Ana", "Torres", "ana.torres@example.invalid", "Bikes"),
        new("Luis", "Garcia", "luis.garcia@example.invalid", "Clothing"),
        new("Mia", "Chen", "mia.chen@example.invalid", "Accessories"),
        new("Sofia", "Ramos", "sofia.ramos@example.invalid", "Components")
    ];

    // O que o usuário digita não é a categoria do catálogo: "bicicletas" precisa virar "Bikes".
    private static readonly (string Keyword, string Category)[] CategoryHints =
    [
        ("bicicleta", "Bikes"),
        ("bicycle", "Bikes"),
        ("bike", "Bikes"),
        ("ciclismo", "Bikes"),
        ("roupa", "Clothing"),
        ("vestu", "Clothing"),
        ("clothing", "Clothing"),
        ("acess", "Accessories"),
        ("accessor", "Accessories"),
        ("componente", "Components"),
        ("component", "Components")
    ];

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        => base.ConfigureProtocol(protocolBuilder).SendsMessage<CustomerQuery>();

    protected override async ValueTask TakeTurnAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        bool? emitEvents,
        CancellationToken cancellationToken = default)
    {
        var segment = string.Join("\n", messages.Select(m => m.Text)).Trim();
        await context.SendMessageAsync(
            await ResolveSegmentAsync(segment, cancellationToken),
            cancellationToken: cancellationToken);
    }

    private async ValueTask<CustomerQuery> ResolveSegmentAsync(string segment, CancellationToken cancellationToken)
    {
        if (dataMode == "demo")
        {
            return Demo(segment, "JULIE_DATA_MODE=demo: o Fabric SQL não foi consultado. Estes clientes são fictícios.");
        }

        try
        {
            var reply = await sqlAgent.RunAsync(segment, cancellationToken: cancellationToken);
            var customers = ExtractCustomers(reply.Text);

            if (customers.Count > 0)
            {
                return new CustomerQuery(customers, "fabric", null);
            }

            return dataMode == "real"
                ? new CustomerQuery([], "fabric", $"O {sqlAgent.Name} não devolveu linhas utilizáveis para este segmento.")
                : Demo(segment, $"O {sqlAgent.Name} não devolveu linhas utilizáveis, então clientes fictícios estão sendo usados.");
        }
        catch (Exception ex) when (dataMode != "real")
        {
            return Demo(segment, $"O Fabric SQL não está disponível ({ex.Message.Split('\n')[0]}). Estes clientes são fictícios.");
        }
    }

    // Os clientes de demonstração substituem as linhas que o SQL teria devolvido,
    // então o segmento solicitado também precisa ser aplicado a eles.
    private static CustomerQuery Demo(string segment, string warning)
    {
        var category = DetectCategory(segment);

        if (category is null)
        {
            return new CustomerQuery([.. DemoCustomers], "demo", warning);
        }

        List<Customer> matches =
        [
            .. DemoCustomers.Where(customer =>
                string.Equals(customer.FavoriteCategory, category, StringComparison.OrdinalIgnoreCase))
        ];

        return new CustomerQuery(
            matches,
            "demo",
            $"{warning} Eles foram filtrados para o segmento '{category}'.");
    }

    private static string? DetectCategory(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        foreach (var (keyword, category) in CategoryHints)
        {
            if (segment.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }
        }

        return null;
    }

    private static List<Customer> ExtractCustomers(string text)
    {
        List<Customer> customers = [];

        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return customers;
        }

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return customers;
            }

            foreach (var row in document.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                customers.Add(new Customer(
                    Field(row, "FirstName"),
                    Field(row, "LastName"),
                    Field(row, "PrimaryEmail"),
                    Field(row, "FavoriteCategory")));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return customers;

        static string Field(JsonElement row, string name)
        {
            foreach (var property in row.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ToString();
                }
            }

            return string.Empty;
        }
    }
}

// Uma chamada ao MarketingAgent por cliente: é isso que mantém cada mensagem
// ligada à categoria que aquele cliente realmente compra.
[YieldsOutput(typeof(string))]
internal sealed class WriteCampaign(AIAgent marketingAgent) : Executor<CustomerQuery>("write-campaign")
{
    public override async ValueTask HandleAsync(
        CustomerQuery query,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        List<object> messages = [];

        foreach (var customer in query.Customers)
        {
            if (string.IsNullOrWhiteSpace(customer.Email))
            {
                continue;
            }

            var reply = await marketingAgent.RunAsync(
                $"Cliente: {customer.FullName}. Categoria favorita: {customer.FavoriteCategory}.",
                cancellationToken: cancellationToken);

            messages.Add(new
            {
                to = customer.Email,
                subject = $"{customer.FirstName}, novidades em {customer.FavoriteCategory}",
                body = reply.Text
            });
        }

        var campaign = new
        {
            campaignName = query.DataSource == "demo"
                ? "Campanha da Contoso Retail (DADOS DE DEMONSTRAÇÃO)"
                : "Campanha da Contoso Retail",
            dataSource = query.DataSource,
            isDemoData = query.DataSource == "demo",
            warning = query.Warning,
            messageCount = messages.Count,
            messages
        };

        await context.YieldOutputAsync(
            JsonSerializer.Serialize(campaign, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }
}

internal sealed class StartupFailure(string reason)
    : ChatProtocolExecutor("startup-error", new ChatProtocolExecutorOptions
    {
        StringMessageChatRole = ChatRole.User,
        AutoSendTurnToken = false
    })
{
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        => base.ConfigureProtocol(protocolBuilder).YieldsOutput<string>();

    protected override ValueTask TakeTurnAsync(
        List<ChatMessage> messages,
        IWorkflowContext context,
        bool? emitEvents,
        CancellationToken cancellationToken = default)
        => context.YieldOutputAsync(
            $"WORKFLOW_ERROR: Julie não conseguiu construir o seu workflow. {reason}",
            cancellationToken);
}
