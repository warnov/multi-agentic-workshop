using System.Text.Json.Serialization;

namespace Contoso.Retail.Functions.Models;

public class OrdersReportRequest
{
    [JsonPropertyName("CustomerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("StartDate")]
    public string StartDate { get; set; } = string.Empty;

    [JsonPropertyName("EndDate")]
    public string EndDate { get; set; } = string.Empty;

    [JsonPropertyName("Orders")]
    public List<OrderLine> Orders { get; set; } = [];
}
