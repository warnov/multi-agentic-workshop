using System.Text.Json.Serialization;

namespace Contoso.Retail.Functions.Models;

public class OrderLine
{
    [JsonPropertyName("OrderNumber")]
    public string OrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("OrderDate")]
    public string OrderDate { get; set; } = string.Empty;

    [JsonPropertyName("OrderLineNumber")]
    public int OrderLineNumber { get; set; }

    [JsonPropertyName("ProductName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("BrandName")]
    public string BrandName { get; set; } = string.Empty;

    [JsonPropertyName("CategoryName")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonPropertyName("Quantity")]
    public double Quantity { get; set; }

    [JsonPropertyName("UnitPrice")]
    public double UnitPrice { get; set; }

    [JsonPropertyName("LineTotal")]
    public double LineTotal { get; set; }
}
