namespace Phase01.PromptKata;

public sealed class KataOptions
{
    public const string SectionName = "Kata";

    public List<float> Temperatures { get; set; } = [];
    public List<KataRun> Runs { get; set; } = [];
}

public sealed class KataRun
{
    public string Model { get; set; } = "";
    public int Samples { get; set; } = 1;
}