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


    [Function("HelloWorld")]
    public IActionResult HelloWorld(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequest req)
    {
        _logger.LogInformation("HelloWorld function executed.");
        return new OkObjectResult("Hello World!");
    }


[OpenApiOperation(operationId: "sqlExecutor", tags: new[] { "Data" },
    Summary = "Executes a T-SQL query for customer segments",
    Description = "Receives T-SQL in the body, runs the query against Fabric Warehouse and returns a list with FirstName, LastName, PrimaryEmail and FavoriteCategory.")]
[OpenApiRequestBody(
    contentType: "application/json",
    bodyType: typeof(SqlExecutorRequest),
    Required = true,
    Description = "JSON object with a 'tsql' property containing the query to execute")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.OK,
    contentType: "application/json",
    bodyType: typeof(List<SqlExecutorCustomerRecord>),
    Description = "Typed results for the customer segment")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.BadRequest,
    contentType: "text/plain",
    bodyType: typeof(string),
    Description = "Error message for invalid body or columns differing from the expected contract")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.InternalServerError,
    contentType: "text/plain",
    bodyType: typeof(string),
    Description = "Error executing the SQL query")]
    [Function("SqlExecutor")]
    public async Task<IActionResult> SqlExecutor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("SqlExecutor: processing request.");

        SqlExecutorRequest? request;
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            request = await JsonSerializer.DeserializeAsync<SqlExecutorRequest>(req.Body, jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "SqlExecutor: invalid body.");
            return new BadRequestObjectResult("The request body is not valid JSON.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.TSql))
            return new BadRequestObjectResult("You must provide the 'tsql' property with a valid query.");

        if (!IsReadOnlySql(request.TSql))
            return new BadRequestObjectResult("Only read-only queries are allowed (SELECT/CTE).");

        var rawConnectionString = _configuration["FabricWarehouseConnectionString"];
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            return new BadRequestObjectResult("Configuration 'FabricWarehouseConnectionString' is missing.");
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
                    "The query must return EXACTLY these columns: FirstName, LastName, PrimaryEmail, FavoriteCategory.");
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
            _logger.LogError(ex, "SqlExecutor: SQL error executing query.");
            return new ObjectResult($"Error executing SQL query: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SqlExecutor: unexpected error.");
            return new ObjectResult("Internal error executing SqlExecutor.")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }


[OpenApiOperation(operationId: "ordersReporter", tags: new[] { "Reports" },
    Summary = "Generates an HTML orders report",
    Description = "Receives order lines for a customer, generates an HTML report with the details, uploads it to Blob Storage and returns the SAS URL to view or download it.")]
[OpenApiRequestBody(
    contentType: "application/json",
    bodyType: typeof(OrdersReportRequest),
    Required = true,
    Description = "Customer data and order lines to include in the report")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.OK,
    contentType: "application/json",
    bodyType: typeof(object),
    Description = "JSON object with a 'reportUrl' property containing the SAS URL of the generated report")]
[OpenApiResponseWithBody(
    statusCode: HttpStatusCode.BadRequest,
    contentType: "text/plain",
    bodyType: typeof(string),
    Description = "Error message when the JSON is invalid or contains no orders")]
    [Function("OrdersReporter")]
    public async Task<IActionResult> OrdersReporter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
    {
        _logger.LogInformation("OrdersReporter: processing request.");

        // --- Deserialize request body ---
        OrdersReportRequest? request;
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            request = await JsonSerializer.DeserializeAsync<OrdersReportRequest>(req.Body, jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error deserializing input JSON.");
            return new BadRequestObjectResult("The request body is not valid JSON.");
        }

        if (request is null || request.Orders.Count == 0)
            return new BadRequestObjectResult("No order lines were received.");

        string customerName = request.CustomerName;
        string dateRangeStart = request.StartDate;
        string dateRangeEnd = request.EndDate;
        var lines = request.Orders;

        // --- Download the HTML template ---
        string templateUrl = _configuration["BillTemplate"]
            ?? "https://raw.githubusercontent.com/warnov/multi-agentic-workshop/refs/heads/master/assets/bill-template.en.html";

        string html;
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            html = await httpClient.GetStringAsync(templateUrl);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error downloading template from {Url}", templateUrl);
            return new StatusCodeResult(502);
        }

        // --- Inject report metadata ---
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

        // --- Group lines by order ---
        var orders = lines
            .GroupBy(l => new { l.OrderNumber, l.OrderDate })
            .OrderBy(g => g.Key.OrderDate)
            .ThenBy(g => g.Key.OrderNumber);

        // --- Build HTML order blocks ---
        var ordersHtml = new StringBuilder();
        double grandTotal = 0;

        foreach (var order in orders)
        {
            double orderTotal = order.Sum(l => l.LineTotal);
            grandTotal += orderTotal;

            ordersHtml.AppendLine("    <div class=\"order-block\">");
            ordersHtml.AppendLine($"        <div class=\"order-header\">");
            ordersHtml.AppendLine($"            Order: <span class=\"order-id\">{order.Key.OrderNumber}</span> &nbsp; | &nbsp;");
            ordersHtml.AppendLine($"            Date: <span class=\"order-date\">{order.Key.OrderDate}</span>");
            ordersHtml.AppendLine($"        </div>");
            ordersHtml.AppendLine();
            ordersHtml.AppendLine("        <table>");
            ordersHtml.AppendLine("            <thead>");
            ordersHtml.AppendLine("                <tr>");
            ordersHtml.AppendLine("                    <th>Line #</th>");
            ordersHtml.AppendLine("                    <th>Product</th>");
            ordersHtml.AppendLine("                    <th>Brand</th>");
            ordersHtml.AppendLine("                    <th>Category</th>");
            ordersHtml.AppendLine("                    <th>Quantity</th>");
            ordersHtml.AppendLine("                    <th>Unit Price</th>");
            ordersHtml.AppendLine("                    <th>Line Total</th>");
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
            ordersHtml.AppendLine($"            Order Total: <strong class=\"order-total-amount\">{FormatCurrency(orderTotal)}</strong>");
            ordersHtml.AppendLine("        </div>");
            ordersHtml.AppendLine("    </div>");
            ordersHtml.AppendLine();
        }

        // --- Inject orders into the container ---
        int containerStart = html.IndexOf("<div id=\"orders-container\">");
        int containerEnd = html.IndexOf("</div>", html.IndexOf("ORDER TEMPLATE END"));
        if (containerStart >= 0 && containerEnd >= 0)
        {
            int innerStart = html.IndexOf('>', containerStart) + 1;
            html = string.Concat(
                html.AsSpan(0, innerStart),
                Environment.NewLine,
                ordersHtml.ToString(),
                html.AsSpan(containerEnd));
        }

        // --- Inject grand total ---
        html = html.Replace(
            "<strong id=\"report-grand-total\"></strong>",
            $"<strong id=\"report-grand-total\">{FormatCurrency(grandTotal)}</strong>");

        // --- Upload HTML as blob to "reports" container ---
        string storageAccountName = _configuration["StorageAccountName"]
            ?? throw new InvalidOperationException("StorageAccountName is not configured.");

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

        _logger.LogInformation("Report uploaded as blob: {BlobName}", blobName);

        // --- Generate read-only SAS valid for 1 hour (User Delegation) ---
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

    private static string FormatCurrency(double value)
    {
        var culture = new CultureInfo("en-US");
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
