using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class AiSettings
{
    public string GamingModel { get; set; } = "qwen3:1.7b";
    public string IdleModel { get; set; } = "qwen3:4b-instruct";
    public string FallbackModel { get; set; } = "gemma3:4b";
    public double Temperature { get; set; } = 0.22;
    public int MaxContextTokens { get; set; } = 4096;
    public int MaxResponseTokens { get; set; } = 700;
    public int RamPressurePercent { get; set; } = 78;
    public int CpuPressurePercent { get; set; } = 85;
    public int GpuPressurePercent { get; set; } = 70;
}

