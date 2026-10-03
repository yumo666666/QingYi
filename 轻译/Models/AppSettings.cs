namespace QingYi.Models;

public sealed class AppSettings
{
    public string ApiUrl { get; set; } = "https://api.siliconflow.cn/v1";
    public string TextModel { get; set; } = "Qwen/Qwen3.5-9B";
    public string ImageModel { get; set; } = "Qwen/Qwen3.5-9B";
    public bool EnableThinking { get; set; }
    public string ProtectedApiKey { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public string TargetLanguage { get; set; } = "简体中文";
    public bool ShowOriginalByDefault { get; set; }
    public double ResultWidth { get; set; } = 460;
    public double ResultHeight { get; set; } = 320;
    public double ToolbarScale { get; set; } = 1;
    public double MainWidth { get; set; } = 590;
    public double MainHeight { get; set; } = 820;
    public double? MainLeft { get; set; }
    public double? MainTop { get; set; }
    public double? ResultLeft { get; set; }
    public double? ResultTop { get; set; }
}
