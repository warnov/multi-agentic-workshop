using System.Text.Json.Serialization;

namespace Contoso.Retail.Functions.Models;

public class SqlExecutorCustomerRecord
{
    [JsonPropertyName("FirstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("LastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("PrimaryEmail")]
    public string PrimaryEmail { get; set; } = string.Empty;

    [JsonPropertyName("FavoriteCategory")]
    public string FavoriteCategory { get; set; } = string.Empty;
}
