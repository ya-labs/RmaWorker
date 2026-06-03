namespace RmaWorker.Configuration;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; init; } = "http://localhost:11434";

    public string Model { get; init; } = "qwen3:4b";

    public double Temperature { get; init; } = 0;
}
