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
//  Julie - Marketing Campaign Orchestrator (Foundry hosted agent)
//
//  Runs inside the Foundry-managed sandbox and is reached through the
//  Responses protocol. The orchestration that used to live in workflow
//  YAML is now an Agent Framework workflow: an explicit graph.
//
//      query-customers  ->  write-campaign  ->  output
//
//  The order is fixed by the graph, not decided by a model. Each node
//  wraps one prompt agent, which is what lets the first node fall back to
//  demo data when Fabric SQL is not configured, and the second one call
//  MarketingAgent once per customer.
// =====================================================================

var projectEndpoint = Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set.");
var sqlAgentName = Environment.GetEnvironmentVariable("SQL_AGENT_NAME") ?? "SqlAgent";
var marketingAgentName = Environment.GetEnvironmentVariable("MARKETING_AGENT_NAME") ?? "MarketingAgent";

// real = fail when Fabric is unavailable, demo = never query SQL, auto = fall back to demo data.
var dataMode = (Environment.GetEnvironmentVariable("JULIE_DATA_MODE") ?? "auto").Trim().ToLowerInvariant();

AIProjectClient projectClient = new(new Uri(projectEndpoint), new DefaultAzureCredential());

// The graph can only be built once the sub-agents are resolved. Letting that
// throw would kill the container before the Responses endpoint exists, and the
// caller would only see an opaque "424 session_not_ready", so a failure is
// turned into a single-node workflow that reports the real reason instead.
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
        .AddEdge(queryCustomers, writeCampaign, label: "customers")
        .WithOutputFrom(writeCampaign)
        .Build();
}
catch (Exception ex)
{
    StartupFailure failure = new(ex.Message);
    workflow = new WorkflowBuilder(failure).WithOutputFrom(failure).Build();
}

// Printed once so the graph can be read in the agent logs and rendered with Graphviz.
Console.WriteLine($"=== Julie workflow (data mode: {dataMode}) ===");
Console.WriteLine(workflow.ToDotString());
Console.WriteLine("=== end of workflow graph ===");

// Without includeWorkflowOutputsInResponse the workflow runs but the answer comes back empty.
AIAgent julie = workflow.AsAIAgent(
    name: "Julie",
    description: "Marketing campaign orchestrator for Contoso Retail.",
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

// SqlAgent is invoked from inside this node rather than being a node itself.
// A failing agent node aborts the whole run, and that would make the lab
// unusable whenever the Fabric SQL connection is missing.
//
// The start node of a hosted workflow must speak the chat protocol
// (List<ChatMessage> plus TurnToken), which is what ChatProtocolExecutor provides.
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

    // The wording a user types is not the catalog category, so "bicycles" has to map to "Bikes".
    private static readonly (string Keyword, string Category)[] CategoryHints =
    [
        ("bicycle", "Bikes"),
        ("bike", "Bikes"),
        ("cycling", "Bikes"),
        ("clothing", "Clothing"),
        ("apparel", "Clothing"),
        ("jersey", "Clothing"),
        ("accessor", "Accessories"),
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
            return Demo(segment, "JULIE_DATA_MODE=demo: Fabric SQL was not queried. These customers are fictional.");
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
                ? new CustomerQuery([], "fabric", $"{sqlAgent.Name} returned no usable rows for this segment.")
                : Demo(segment, $"{sqlAgent.Name} returned no usable rows, so fictional customers are being used instead.");
        }
        catch (Exception ex) when (dataMode != "real")
        {
            return Demo(segment, $"Fabric SQL is not available ({ex.Message.Split('\n')[0]}). These customers are fictional.");
        }
    }

    // The demo customers stand in for the rows SQL would have returned, so the
    // requested segment still has to be applied to them.
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
            $"{warning} They were filtered to the '{category}' segment.");
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

// One MarketingAgent call per customer: this is what keeps each message tied to
// the category that customer actually buys.
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
                $"Customer: {customer.FullName}. Favorite category: {customer.FavoriteCategory}.",
                cancellationToken: cancellationToken);

            messages.Add(new
            {
                to = customer.Email,
                subject = $"{customer.FirstName}, news about {customer.FavoriteCategory}",
                body = reply.Text
            });
        }

        var campaign = new
        {
            campaignName = query.DataSource == "demo"
                ? "Contoso Retail campaign (DEMO DATA)"
                : "Contoso Retail campaign",
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
            $"WORKFLOW_ERROR: Julie could not build her workflow. {reason}",
            cancellationToken);
}
