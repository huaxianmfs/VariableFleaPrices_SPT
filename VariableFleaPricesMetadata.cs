using SPTarkov.Server.Core.Models.Spt.Mod;

public record VariableFleaPricesMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "VariableFleaPrices";
    public string Name { get; init; } = "[Variable.Flea.Prices]";
    public string Author { get; init; } = "YourName";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("3.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~5.0.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public bool? IsBundleMod { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; }
}