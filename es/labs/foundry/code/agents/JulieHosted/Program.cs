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
//  Julie - Orquestadora de Campañas de Marketing (agente hospedado)
//
//  Se ejecuta dentro del sandbox gestionado por Foundry y se invoca a
//  través del protocolo Responses. La orquestación que antes vivía en el
//  YAML del workflow ahora es un workflow de Agent Framework: un grafo
//  explícito.
//
//      query-customers  ->  write-campaign  ->  salida
//
//  El orden lo fija el grafo, no lo decide un modelo. Cada nodo envuelve a
//  un agente de prompt, que es lo que permite al primero caer a datos de
//  demostración cuando Fabric SQL no está configurado, y al segundo llamar
//  a MarketingAgent una vez por cliente.
// =====================================================================

var projectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT no está definida.");
var sqlAgentName = Environment.GetEnvironmentVariable("SQL_AGENT_NAME") ?? "SqlAgent";
var marketingAgentName = Environment.GetEnvironmentVariable("MARKETING_AGENT_NAME") ?? "MarketingAgent";

// real = falla si Fabric no está disponible, demo = nunca consulta SQL, auto = cae a datos de demostración.
var dataMode = (Environment.GetEnvironmentVariable("JULIE_DATA_MODE") ?? "auto").Trim().ToLowerInvariant();

AIProjectClient projectClient = new(new Uri(projectEndpoint), new DefaultAzureCredential());

// El grafo solo se puede construir con los sub-agentes ya resueltos. Dejar que
// eso lance tumbaría el contenedor antes de que exista el endpoint Responses, y
// quien llamara solo vería un opaco "424 session_not_ready"; por eso un fallo se
// convierte en un workflow de un solo nodo que informa del motivo real.
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

// Se imprime una vez para poder leer el grafo en los logs del agente y renderizarlo con Graphviz.
Console.WriteLine($"=== Workflow de Julie (modo de datos: {dataMode}) ===");
Console.WriteLine(workflow.ToDotString());
Console.WriteLine("=== fin del grafo del workflow ===");

// Sin includeWorkflowOutputsInResponse el workflow se ejecuta pero la respuesta llega vacía.
AIAgent julie = workflow.AsAIAgent(
    name: "Julie",
    description: "Orquestadora de campañas de marketing de Contoso Retail.",
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

// SqlAgent se invoca desde dentro de este nodo en lugar de ser un nodo en sí.
// Un nodo agente que falla aborta toda la ejecución, y eso dejaría el
// laboratorio inservible siempre que falte la conexión SQL de Fabric.
//
// El nodo inicial de un workflow hospedado debe hablar el protocolo de chat
// (List<ChatMessage> más TurnToken), que es lo que aporta ChatProtocolExecutor.
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

    // Lo que escribe el usuario no es la categoría del catálogo: "bicicletas" debe resolverse a "Bikes".
    private static readonly (string Keyword, string Category)[] CategoryHints =
    [
        ("bicicleta", "Bikes"),
        ("bicycle", "Bikes"),
        ("bike", "Bikes"),
        ("ciclismo", "Bikes"),
        ("ropa", "Clothing"),
        ("clothing", "Clothing"),
        ("prenda", "Clothing"),
        ("accesorio", "Accessories"),
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
            return Demo(segment, "JULIE_DATA_MODE=demo: no se consultó Fabric SQL. Estos clientes son ficticios.");
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
                ? new CustomerQuery([], "fabric", $"{sqlAgent.Name} no devolvió filas utilizables para este segmento.")
                : Demo(segment, $"{sqlAgent.Name} no devolvió filas utilizables, así que se usan clientes ficticios.");
        }
        catch (Exception ex) when (dataMode != "real")
        {
            return Demo(segment, $"Fabric SQL no está disponible ({ex.Message.Split('\n')[0]}). Estos clientes son ficticios.");
        }
    }

    // Los clientes de demostración sustituyen a las filas que habría devuelto SQL,
    // así que el segmento solicitado también debe aplicarse sobre ellos.
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
            $"{warning} Se filtraron al segmento '{category}'.");
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

// Una llamada a MarketingAgent por cliente: esto es lo que mantiene cada mensaje
// ligado a la categoría que ese cliente compra de verdad.
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
                $"Cliente: {customer.FullName}. Categoría favorita: {customer.FavoriteCategory}.",
                cancellationToken: cancellationToken);

            messages.Add(new
            {
                to = customer.Email,
                subject = $"{customer.FirstName}, novedades en {customer.FavoriteCategory}",
                body = reply.Text
            });
        }

        var campaign = new
        {
            campaignName = query.DataSource == "demo"
                ? "Campaña de Contoso Retail (DATOS DE DEMOSTRACIÓN)"
                : "Campaña de Contoso Retail",
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
            $"WORKFLOW_ERROR: Julie no pudo construir su workflow. {reason}",
            cancellationToken);
}
