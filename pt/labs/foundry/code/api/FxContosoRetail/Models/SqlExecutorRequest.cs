using System.Text.Json.Serialization;

namespace Contoso.Retail.Functions.Models;

public class SqlExecutorRequest
{
    [JsonPropertyName("tsql")]
    public string TSql { get; set; } = string.Empty;
}
