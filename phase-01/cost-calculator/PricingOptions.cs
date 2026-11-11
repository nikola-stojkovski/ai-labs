namespace Phase01.CostCalculator;

public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    public string Currency { get; set; } = "USD";
    public string CheckedOn { get; set; } = "";
    public List<ModelPrice> Models { get; set; } = [];
}

public sealed class ModelPrice
{
    public string Name { get; set; } = "";
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }

    public decimal Cost(long inputTokens, long outputTokens) =>
        (inputTokens * InputPerMillion + outputTokens * OutputPerMillion) / 1_000_000m;
}