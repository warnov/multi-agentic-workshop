using System.Globalization;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Net;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Contoso.Retail.Functions.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Contoso.Retail.Functions;

public class FxContosoRetail
{
    private static readonly HashSet<string> ExpectedSqlExecutorColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "FirstName",
        "LastName",
        "PrimaryEmail",
        "FavoriteCategory"
    };

    private readonly ILogger<FxContosoRetail> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public FxContosoRetail(
        ILogger<FxContosoRetail> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }


    [Function("HolaMundo")]
    public IActionResult HolaMundo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("Função HolaMundo executada.");
        return new OkObjectResult("Olá Mundo!");
    }


[OpenApiOperation(operationId: "sqlExecutor", tags: new[] { "Data" },
    Summary = "Executa uma consulta T-SQL para segmentos de clientes",
    Description = "Recebe T-SQL no body, executa a consulta contra o Fabric Warehouse e retorna uma lista com FirstName, LastName, PrimaryEmail e FavoriteCategory.")]
[OpenApiRequestBody(
    contentType: "application/json",
    bodyType: typeof(SqlExecutorRequest),
    Required = true,
    Description = "Objeto JSON com a propriedade 'tsql' que contém a consulta a executar")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.OK,
    contentType: "application/json",
    bodyType: typeof(List<SqlExecutorCustomerRecord>),
    Description = "Resultados tipados do segmento de clientes")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.BadRequest,
    contentType: "text/plain",
    bodyType: typeof(string),
    Description = "Mensagem de erro por body inválido ou colunas diferentes do contrato esperado")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.InternalServerError,
    contentType: "text/plain",
    bodyType: typeof(string),
    Description = "Erro ao executar a consulta SQL")]
    [Function("SqlExecutor")]
    public async Task<IActionResult> SqlExecutor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("SqlExecutor: processando solicitação.");

        SqlExecutorRequest? request;
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            request = await JsonSerializer.DeserializeAsync<SqlExecutorRequest>(req.Body, jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SqlExecutor: body inválido.");
            return new BadRequestObjectResult("O corpo da solicitação não é um JSON válido.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.TSql))
            return new BadRequestObjectResult("É necessário enviar a propriedade 'tsql' com uma consulta válida.");

        if (!IsReadOnlySql(request.TSql))
            return new BadRequestObjectResult("Apenas consultas somente leitura são permitidas (SELECT/CTE).");

        var rawConnectionString = _configuration["FabricWarehouseConnectionString"];
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            return new BadRequestObjectResult("Configuração 'FabricWarehouseConnectionString' não encontrada.");
        }

        var connectionString = EnsureAdDefaultAuthentication(rawConnectionString);

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(request.TSql, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 60
            };

            await using var reader = await command.ExecuteReaderAsync();

            var returnedColumns = Enumerable
                .Range(0, reader.FieldCount)
                .Select(reader.GetName)
                .ToArray();

            if (!HasExpectedSqlExecutorColumns(returnedColumns))
            {
                return new BadRequestObjectResult(
                    "A consulta deve retornar EXATAMENTE estas colunas: FirstName, LastName, PrimaryEmail, FavoriteCategory.");
            }

            int firstNameOrdinal = reader.GetOrdinal("FirstName");
            int lastNameOrdinal = reader.GetOrdinal("LastName");
            int primaryEmailOrdinal = reader.GetOrdinal("PrimaryEmail");
            int favoriteCategoryOrdinal = reader.GetOrdinal("FavoriteCategory");

            var results = new List<SqlExecutorCustomerRecord>();

            while (await reader.ReadAsync())
            {
                results.Add(new SqlExecutorCustomerRecord
                {
                    FirstName = reader.IsDBNull(firstNameOrdinal) ? string.Empty : reader.GetValue(firstNameOrdinal)?.ToString() ?? string.Empty,
                    LastName = reader.IsDBNull(lastNameOrdinal) ? string.Empty : reader.GetValue(lastNameOrdinal)?.ToString() ?? string.Empty,
                    PrimaryEmail = reader.IsDBNull(primaryEmailOrdinal) ? string.Empty : reader.GetValue(primaryEmailOrdinal)?.ToString() ?? string.Empty,
                    FavoriteCategory = reader.IsDBNull(favoriteCategoryOrdinal) ? string.Empty : reader.GetValue(favoriteCategoryOrdinal)?.ToString() ?? string.Empty
                });
            }

            return new OkObjectResult(results);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SqlExecutor: erro SQL ao executar consulta.");
            return new ObjectResult($"Erro ao executar a consulta SQL: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SqlExecutor: erro inesperado.");
            return new ObjectResult("Erro interno ao executar SqlExecutor.")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }


[OpenApiOperation(operationId: "ordersReporter", tags: new[] { "Relatórios" },
    Summary = "Gera um relatório HTML de pedidos",
    Description = "Recebe linhas de pedidos de um cliente, gera um relatório HTML com o detalhe, faz upload para o Blob Storage e retorna a URL SAS para visualizar/baixar.")]
[OpenApiRequestBody(
    contentType: "application/json",
    bodyType: typeof(OrdersReportRequest),
    Required = true,
    Description = "Dados do cliente e linhas de pedidos a incluir no relatório")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.OK,
    contentType: "application/json",
    bodyType: typeof(object),
    Description = "Objeto JSON com a propriedade 'reportUrl' que contém a URL SAS do relatório gerado")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.BadRequest,
    contentType: "text/plain",
    bodyType: typeof(string),
    Description = "Mensagem de erro quando o JSON é inválido ou não contém pedidos")]
    [Function("OrdersReporter")]
    public async Task<IActionResult> OrdersReporter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("OrdersReporter: processando solicitação.");

        // --- Desserializar o body ---
        OrdersReportRequest? request;
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            request = await JsonSerializer.DeserializeAsync<OrdersReportRequest>(req.Body, jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Erro ao desserializar o JSON de entrada.");
            return new BadRequestObjectResult("O corpo da solicitação não é um JSON válido.");
        }

        if (request is null || request.Orders.Count == 0)
            return new BadRequestObjectResult("Nenhuma linha de pedido recebida.");

        string customerName = request.CustomerName;
        string dateRangeStart = request.StartDate;
        string dateRangeEnd = request.EndDate;
        var lines = request.Orders;

        // --- Baixar o template HTML ---
        string templateUrl = _configuration["BillTemplate"]
            ?? "https://raw.githubusercontent.com/warnov/taller-multi-agentic/refs/heads/main/assets/bill-template.pt.html";

        string html;
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            html = await httpClient.GetStringAsync(templateUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Erro ao baixar o template de {Url}", templateUrl);
            return new StatusCodeResult(502);
        }

        // --- Injetar metadados do relatório ---
        string today = DateTime.UtcNow.ToString("dd/MM/yyyy");
        string dateRange = $"{dateRangeStart} - {dateRangeEnd}";

        html = html.Replace(
            "<span id=\"customer-name\"></span>",
            $"<span id=\"customer-name\">{customerName}</span>");

        html = html.Replace(
            "<span id=\"report-date\"></span>",
            $"<span id=\"report-date\">{today}</span>");

        html = html.Replace(
            "<span id=\"report-date-range\"></span>",
            $"<span id=\"report-date-range\">{dateRange}</span>");

        // --- Agrupar linhas por pedido ---
        var orders = lines
            .GroupBy(l => new { l.OrderNumber, l.OrderDate })
            .OrderBy(g => g.Key.OrderDate)
            .ThenBy(g => g.Key.OrderNumber);

        // --- Construir blocos HTML de pedidos ---
        var ordersHtml = new StringBuilder();
        double grandTotal = 0;

        foreach (var order in orders)
        {
            double orderTotal = order.Sum(l => l.LineTotal);
            grandTotal += orderTotal;

            ordersHtml.AppendLine("    <div class=\"order-block\">");
            ordersHtml.AppendLine($"        <div class=\"order-header\">");
            ordersHtml.AppendLine($"            Pedido: <span class=\"order-id\">{order.Key.OrderNumber}</span> &nbsp; | &nbsp;");
            ordersHtml.AppendLine($"            Data: <span class=\"order-date\">{order.Key.OrderDate}</span>");
            ordersHtml.AppendLine($"        </div>");
            ordersHtml.AppendLine();
            ordersHtml.AppendLine("        <table>");
            ordersHtml.AppendLine("            <thead>");
            ordersHtml.AppendLine("                <tr>");
            ordersHtml.AppendLine("                    <th># Línea</th>");
            ordersHtml.AppendLine("                    <th>Producto</th>");
            ordersHtml.AppendLine("                    <th>Marca</th>");
            ordersHtml.AppendLine("                    <th>Categoría</th>");
            ordersHtml.AppendLine("                    <th>Cantidad</th>");
            ordersHtml.AppendLine("                    <th>Precio Unit.</th>");
            ordersHtml.AppendLine("                    <th>Total Línea</th>");
            ordersHtml.AppendLine("                </tr>");
            ordersHtml.AppendLine("            </thead>");
            ordersHtml.AppendLine("            <tbody class=\"order-lines\">");

            foreach (var line in order.OrderBy(l => l.OrderLineNumber))
            {
                ordersHtml.AppendLine("                <tr>");
                ordersHtml.AppendLine($"                    <td>{line.OrderLineNumber}</td>");
                ordersHtml.AppendLine($"                    <td>{line.ProductName}</td>");
                ordersHtml.AppendLine($"                    <td>{line.BrandName}</td>");
                ordersHtml.AppendLine($"                    <td>{line.CategoryName}</td>");
                ordersHtml.AppendLine($"                    <td>{line.Quantity:F0}</td>");
                ordersHtml.AppendLine($"                    <td>{FormatCurrency(line.UnitPrice)}</td>");
                ordersHtml.AppendLine($"                    <td>{FormatCurrency(line.LineTotal)}</td>");
                ordersHtml.AppendLine("                </tr>");
            }

            ordersHtml.AppendLine("            </tbody>");
            ordersHtml.AppendLine("        </table>");
            ordersHtml.AppendLine();
            ordersHtml.AppendLine("        <div class=\"order-total\">");
            ordersHtml.AppendLine($"            Total Pedido: <strong class=\"order-total-amount\">{FormatCurrency(orderTotal)}</strong>");
            ordersHtml.AppendLine("        </div>");
            ordersHtml.AppendLine("    </div>");
            ordersHtml.AppendLine();
        }

        // --- Injetar pedidos no contêner ---
        int containerStart = html.IndexOf("<div id=\"orders-container\">");
        int containerEnd = html.IndexOf("</div>", html.IndexOf("FIM TEMPLATE DE PEDIDO"));
        if (containerStart >= 0 && containerEnd >= 0)
        {
            int innerStart = html.IndexOf('>', containerStart) + 1;
            html = string.Concat(
                html.AsSpan(0, innerStart),
                Environment.NewLine,
                ordersHtml.ToString(),
                html.AsSpan(containerEnd));
        }

        // --- Inyectar gran total ---
        html = html.Replace(
            "<strong id=\"report-grand-total\"></strong>",
            $"<strong id=\"report-grand-total\">{FormatCurrency(grandTotal)}</strong>");

        // --- Subir HTML como blob al container "reports" ---
        string storageAccountName = _configuration["StorageAccountName"]
            ?? throw new InvalidOperationException("StorageAccountName não está configurada.");

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string blobName = $"report-{timestamp}.htm";

        var blobServiceUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
        var credential = new DefaultAzureCredential();
        var blobServiceClient = new BlobServiceClient(blobServiceUri, credential);
        var containerClient = blobServiceClient.GetBlobContainerClient("reports");
        var blobClient = containerClient.GetBlobClient(blobName);

        var htmlBytes = Encoding.UTF8.GetBytes(html);
        using var stream = new MemoryStream(htmlBytes);
        await blobClient.UploadAsync(stream, new BlobHttpHeaders
        {
            ContentType = "text/html; charset=utf-8"
        });

        _logger.LogInformation("Relatório enviado como blob: {BlobName}", blobName);

        // --- Gerar SAS somente leitura com duração de 1 hora (User Delegation) ---
        var userDelegationKey = await blobServiceClient.GetUserDelegationKeyAsync(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = "reports",
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var blobUriBuilder = new BlobUriBuilder(blobClient.Uri)
        {
            Sas = sasBuilder.ToSasQueryParameters(userDelegationKey, storageAccountName)
        };

        return new OkObjectResult(new { reportUrl = blobUriBuilder.ToUri().ToString() });
    }

    /// <summary>
    /// Formata um valor monetário como $ 1.234,56 (separador de milhar: ponto, decimal: vírgula).
    /// </summary>
    private static string FormatCurrency(double value)
    {
        var culture = new CultureInfo("es-CO");
        return $"$ {value.ToString("N2", culture)}";
    }

    private static bool HasExpectedSqlExecutorColumns(IReadOnlyCollection<string> returnedColumns)
    {
        if (returnedColumns.Count != ExpectedSqlExecutorColumns.Count)
            return false;

        return returnedColumns.All(column => ExpectedSqlExecutorColumns.Contains(column));
    }

    private static bool IsReadOnlySql(string tsql)
    {
        var trimmed = tsql.TrimStart();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureAdDefaultAuthentication(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.Authentication == SqlAuthenticationMethod.NotSpecified)
        {
            builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
        }

        return builder.ConnectionString;
    }
}
