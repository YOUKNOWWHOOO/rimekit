using RimeKit.Windows.Core.Utilities;

namespace RimeKit.Windows.Core;

public static class TemplateService
{
    public static string? RepositoryRoot { get; set; }

    private static string TemplatesRoot
    {
        get
        {
            if (RepositoryRoot is null)
                throw new InvalidOperationException("TemplateService.RepositoryRoot 未设置，无法访问模板目录。请确保在应用启动时正确初始化。");
            return Path.Combine(RepositoryRoot, "workspace", "windows", "templates");
        }
    }

    public static string WeaselTemplatePath => Path.Combine(TemplatesRoot, "weasel.yaml");

    public static string SchemaTemplatePath(string schemaId) => Path.Combine(TemplatesRoot, schemaId, $"{schemaId}.schema.yaml");

    public static void EnsureRepositoryRoot(string? startDirectory)
    {
        string discovered = RepositoryContext.DiscoverRepositoryRoot(startDirectory ?? ".");
        if (string.IsNullOrWhiteSpace(discovered))
            throw new InvalidOperationException($"无法发现仓库根目录，startDirectory='{startDirectory}'。请确保在 RimeKit 仓库内运行。");
        RepositoryRoot = discovered;
    }

    public static void CaptureTemplates(string schemaId, string runtimeTargetRoot)
    {
        EnsureRepositoryRootIsSet("CaptureTemplates");
        Directory.CreateDirectory(TemplatesRoot);
        string weaselSource = Path.Combine(runtimeTargetRoot, "weasel.yaml");
        if (File.Exists(weaselSource))
        {
            FileHelper.CopyFileWithBackoff(weaselSource, WeaselTemplatePath, overwrite: true);
        }

        string schemaDir = Path.Combine(TemplatesRoot, schemaId);
        Directory.CreateDirectory(schemaDir);
        string schemaSource = Path.Combine(runtimeTargetRoot, $"{schemaId}.schema.yaml");
        if (File.Exists(schemaSource))
        {
            FileHelper.CopyFileWithBackoff(schemaSource, SchemaTemplatePath(schemaId), overwrite: true);
        }
    }

    public static void DeleteWeaselTemplate()
    {
        string path = WeaselTemplatePath;
        if (File.Exists(path))
            FileHelper.DeleteFileWithBackoff(path);
    }

    public static void DeleteSchemaTemplate(string schemaId)
    {
        string path = SchemaTemplatePath(schemaId);
        if (File.Exists(path))
            FileHelper.DeleteFileWithBackoff(path);
    }

    public static bool TemplatesAreAvailable()
    {
        if (RepositoryRoot is null)
            return false;
        return File.Exists(WeaselTemplatePath) && File.Exists(SchemaTemplatePath("rime_mint"));
    }

    public static void DeleteAllTemplates()
    {
        if (Directory.Exists(TemplatesRoot))
            FileHelper.DeleteDirectoryWithBackoff(TemplatesRoot);
    }

    public static string? TryEnsureTemplateDiagnostic()
    {
        if (RepositoryRoot is null)
            return "RepositoryRoot 未设置";
        bool hasWeasel = File.Exists(WeaselTemplatePath);
        bool hasSchema = File.Exists(SchemaTemplatePath("rime_mint"));
        if (hasWeasel && hasSchema)
            return null;
        if (!hasWeasel && !hasSchema)
            return "模板文件均不存在。请重新安装输入方案以生成模板缓存。";
        if (!hasWeasel)
            return $"weasel.yaml 模板文件不存在：{WeaselTemplatePath}。请重新安装输入方案以生成模板缓存。";
        return $"rime_mint.schema.yaml 模板文件不存在：{SchemaTemplatePath("rime_mint")}。请重新安装输入方案以生成模板缓存。";
    }

    public static ParsedWeaselYaml? GetWeaselDefaults()
    {
        string path = WeaselTemplatePath;
        if (!File.Exists(path))
            return null;
        string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
        return ArtifactService.ParseWeaselTemplateYaml(content);
    }

    public static ParsedSchemaDefaults? GetSchemaDefaults(string schemaId)
    {
        string path = SchemaTemplatePath(schemaId);
        if (!File.Exists(path))
            return null;
        string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
        return ArtifactService.ParseSchemaTemplateYaml(content);
    }

    public static int? GetSchemaInt(string fieldName)
    {
        ParsedSchemaDefaults? defaults = GetSchemaDefaults("rime_mint");
        if (defaults is null)
            return null;
        return fieldName switch
        {
            "PageSize" => defaults.PageSize,
            _ => null,
        };
    }

    public static bool? GetSchemaBool(string fieldName)
    {
        ParsedSchemaDefaults? defaults = GetSchemaDefaults("rime_mint");
        if (defaults is null)
            return null;
        return fieldName switch
        {
            "FullShapeEnabled" => defaults.FullShapeEnabled,
            "AsciiPunctEnabled" => defaults.AsciiPunctEnabled,
            "EmojiSuggestionEnabled" => defaults.EmojiSuggestionEnabled,
            "ToneDisplayEnabled" => defaults.ToneDisplayEnabled,
            "ShowEmojiComments" => defaults.ShowEmojiComments,
            "EnableUserDict" => defaults.EnableUserDict,
            _ => null,
        };
    }

    public static string? GetSchemaString(string fieldName)
    {
        ParsedSchemaDefaults? defaults = GetSchemaDefaults("rime_mint");
        if (defaults is null)
            return null;
        return fieldName switch
        {
            "SimplificationMode" => defaults.SimplificationMode,
            _ => null,
        };
    }

    public static int? GetLayoutInt(string fieldName)
    {
        ParsedWeaselYaml? defaults = GetWeaselDefaults();
        if (defaults is null)
            return null;
        return fieldName switch
        {
            "LayoutMinWidth" => defaults.LayoutMinWidth,
            "LayoutMinHeight" => defaults.LayoutMinHeight,
            "LayoutMaxWidth" => defaults.LayoutMaxWidth,
            "LayoutMaxHeight" => defaults.LayoutMaxHeight,
            "LayoutMarginX" => defaults.LayoutMarginX,
            "LayoutMarginY" => defaults.LayoutMarginY,
            "LayoutBorderWidth" => defaults.LayoutBorderWidth,
            "LayoutLineSpacing" => defaults.LayoutLineSpacing,
            "LayoutBaseline" => defaults.LayoutBaseline,
            "LayoutSpacing" => defaults.LayoutSpacing,
            "LayoutCandidateSpacing" => defaults.LayoutCandidateSpacing,
            "LayoutHiliteSpacing" => defaults.LayoutHiliteSpacing,
            "LayoutHilitePadding" => defaults.LayoutHilitePadding,
            "LayoutHilitePaddingX" => defaults.LayoutHilitePaddingX,
            "LayoutHilitePaddingY" => defaults.LayoutHilitePaddingY,
            "LayoutShadowRadius" => defaults.LayoutShadowRadius,
            "LayoutShadowOffsetX" => defaults.LayoutShadowOffsetX,
            "LayoutShadowOffsetY" => defaults.LayoutShadowOffsetY,
            "LayoutCornerRadius" => defaults.LayoutCornerRadius,
            _ => null,
        };
    }

    public static int? GetStyleInt(string fieldName)
    {
        ParsedWeaselYaml? defaults = GetWeaselDefaults();
        if (defaults is null)
            return null;
        return fieldName switch
        {
            "FontPoint" => defaults.FontPoint,
            "LabelFontPoint" => defaults.LabelFontPoint,
            "CommentFontPoint" => defaults.CommentFontPoint,
            "NotificationTimeMs" => defaults.NotificationTimeMs,
            "CandidateAbbreviateLength" => defaults.CandidateAbbreviateLength,
            _ => null,
        };
    }

    public static bool? GetStyleBool(string fieldName)
    {
        ParsedWeaselYaml? defaults = GetWeaselDefaults();
        if (defaults is null)
            return null;
        return fieldName switch
        {
            "ShowNotification" => defaults.ShowNotification,
            "ShowEmojiComments" => defaults.ShowEmojiComments,
            "GlobalAscii" => defaults.GlobalAscii,
            "InlinePreedit" => defaults.InlinePreedit,
            "Fullscreen" => defaults.Fullscreen,
            "VerticalText" => defaults.VerticalText,
            "VerticalTextLeftToRight" => defaults.VerticalTextLeftToRight,
            "VerticalTextWithWrap" => defaults.VerticalTextWithWrap,
            "VerticalAutoReverse" => defaults.VerticalAutoReverse,
            "AsciiTipFollowCursor" => defaults.AsciiTipFollowCursor,
            "EnhancedPosition" => defaults.EnhancedPosition,
            "DisplayTrayIcon" => defaults.DisplayTrayIcon,
            "PagingOnScroll" => defaults.PagingOnScroll,
            "ClickToCapture" => defaults.ClickToCapture,
            _ => null,
        };
    }

    public static string? GetStyleString(string fieldName)
    {
        ParsedWeaselYaml? defaults = GetWeaselDefaults();
        if (defaults is null)
            return null;
        return fieldName switch
        {
            "ColorScheme" => defaults.ColorScheme,
            "ColorSchemeDark" => defaults.ColorSchemeDark,
            "FontFace" => defaults.FontFace,
            "LabelFontFace" => defaults.LabelFontFace,
            "CommentFontFace" => defaults.CommentFontFace,
            "PreeditType" => defaults.PreeditType,
            "LabelFormat" => defaults.LabelFormat,
            "MarkText" => defaults.MarkText,
            "AntialiasMode" => defaults.AntialiasMode,
            "HoverType" => defaults.HoverType,
            "LayoutAlignType" => defaults.LayoutAlignType,
            "CandidateListLayout" => defaults.CandidateLayout,
            _ => null,
        };
    }

    public static string? GetColorSchemeColor(string schemeName, string colorField)
    {
        EnsureRepositoryRootIsSet(nameof(GetColorSchemeColor));
        string path = WeaselTemplatePath;
        if (!File.Exists(path))
            return null;
        string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
        string[] rawLines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        string[] flatLines = ArtifactService.FlattenYamlLines(rawLines);
        string targetKey = $"preset_color_schemes/{schemeName}/{colorField}:";
        foreach (string line in flatLines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(targetKey, StringComparison.Ordinal))
            {
                string value = trimmed[targetKey.Length..].Trim();
                if (value.StartsWith('"') && value.EndsWith('"'))
                    value = value[1..^1];
                return value;
            }
        }
        return null;
    }

    private static void EnsureRepositoryRootIsSet(string callerName)
    {
        if (RepositoryRoot is null)
            throw new InvalidOperationException($"TemplateService.RepositoryRoot 未设置，{callerName} 调用被拒绝。请确保在应用启动时正确初始化 RepositoryRoot。");
    }
}

public sealed class ParsedSchemaDefaults
{
    public int? PageSize { get; init; }
    public string? SimplificationMode { get; init; }
    public bool? FullShapeEnabled { get; init; }
    public bool? AsciiPunctEnabled { get; init; }
    public bool? EmojiSuggestionEnabled { get; init; }
    public bool? ToneDisplayEnabled { get; init; }
    public string? CandidateLayout { get; init; }
    public bool? ShowEmojiComments { get; init; }
    public bool? EnableUserDict { get; init; }
}
