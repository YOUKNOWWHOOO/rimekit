using System.Text.RegularExpressions;
using RimeKit.Windows.Core.Utilities;

namespace RimeKit.Windows.Core;

public sealed record SchemeColors
{
    public string? TextColor { get; init; }
    public string? CandidateTextColor { get; init; }
    public string? LabelColor { get; init; }
    public string? CommentTextColor { get; init; }
    public string? BackColor { get; init; }
    public string? CandidateBackColor { get; init; }
    public string? BorderColor { get; init; }
    public string? ShadowColor { get; init; }
    public string? HilitedTextColor { get; init; }
    public string? HilitedBackColor { get; init; }
    public string? HilitedLabelColor { get; init; }
    public string? HilitedCandidateTextColor { get; init; }
    public string? HilitedCandidateBackColor { get; init; }
    public string? HilitedCandidateLabelColor { get; init; }
    public string? HilitedCandidateBorderColor { get; init; }
    public string? HilitedCommentTextColor { get; init; }
    public string? HilitedMarkColor { get; init; }
}

public sealed record WeaselUserSettings
{
    public string? ColorScheme { get; init; }
    public string? ColorSchemeDark { get; init; }
    public string? FontFace { get; init; }
    public int? FontPoint { get; init; }
    public string? LabelFontFace { get; init; }
    public int? LabelFontPoint { get; init; }
    public string? CommentFontFace { get; init; }
    public int? CommentFontPoint { get; init; }
    public bool? ShowNotification { get; init; }
    public int? NotificationTimeMs { get; init; }
    public string? LabelFormat { get; init; }
    public string? MarkText { get; init; }
    public bool? PagingOnScroll { get; init; }
    public int? CandidateAbbreviateLength { get; init; }
    public bool? Fullscreen { get; init; }
    public bool? VerticalText { get; init; }
    public bool? VerticalTextLeftToRight { get; init; }
    public bool? VerticalTextWithWrap { get; init; }
    public bool? VerticalAutoReverse { get; init; }
    public bool? InlinePreedit { get; init; }
    public string? PreeditType { get; init; }
    public bool? GlobalAscii { get; init; }
    public string? HoverType { get; init; }
    public bool? ClickToCapture { get; init; }
    public string? AntialiasMode { get; init; }
    public bool? DisplayTrayIcon { get; init; }
    public bool? EnhancedPosition { get; init; }
    public bool? AsciiTipFollowCursor { get; init; }
    public int? LayoutMinWidth { get; init; }
    public int? LayoutMaxWidth { get; init; }
    public int? LayoutMinHeight { get; init; }
    public int? LayoutMaxHeight { get; init; }
    public int? LayoutMarginX { get; init; }
    public int? LayoutMarginY { get; init; }
    public int? LayoutBorderWidth { get; init; }
    public int? LayoutLineSpacing { get; init; }
    public int? LayoutBaseline { get; init; }
    public string? LayoutAlignType { get; init; }
    public int? LayoutSpacing { get; init; }
    public int? LayoutCandidateSpacing { get; init; }
    public int? LayoutHiliteSpacing { get; init; }
    public int? LayoutHilitePadding { get; init; }
    public int? LayoutHilitePaddingX { get; init; }
    public int? LayoutHilitePaddingY { get; init; }
    public int? LayoutShadowRadius { get; init; }
    public int? LayoutShadowOffsetX { get; init; }
    public int? LayoutShadowOffsetY { get; init; }
    public int? LayoutCornerRadius { get; init; }
    public SchemeColors? DayColors { get; init; }
    public SchemeColors? NightColors { get; init; }
    public string? CustomDayBaseScheme { get; init; }
    public string? CustomNightBaseScheme { get; init; }
}

public sealed record MintUserSettings
{
    public int? PageSize { get; init; }
    public string? Layout { get; init; }
    public bool? ShowEmojiComments { get; init; }
    public string? SimplificationMode { get; init; }
    public bool? FullShapeEnabled { get; init; }
    public bool? AsciiPunctEnabled { get; init; }
    public bool? EmojiSuggestionEnabled { get; init; }
    public bool? ToneDisplayEnabled { get; init; }
    public bool? EnableUserDict { get; init; }
    public bool? FuzzyEnabled { get; init; }
    public IReadOnlyList<string> FuzzyAdditionalRules { get; init; } = [];
    public bool? UeCompatEnabled { get; init; }
    public string? CustomPhraseMode { get; init; }
}

public static class UserSettingsReader
{
    private static readonly Regex PatchValuePattern = new(
        @"^\s*""?([^""]+)""?\s*:\s*(.*)$",
        RegexOptions.Compiled);

    public static WeaselUserSettings ReadWeasel(string targetRoot)
    {
        string path = Path.Combine(targetRoot, "weasel.custom.yaml");
        if (!File.Exists(path))
            return new WeaselUserSettings();

        Dictionary<string, string> raw = ParsePatchYaml(path);
        string? dayScheme = ReadString(raw, "style/color_scheme");
        string? nightScheme = ReadString(raw, "style/color_scheme_dark");
        string? dayBaseRef = dayScheme == "rimekit_custom_day"
            ? ReadString(raw, "preset_color_schemes/rimekit_custom_day/__base_scheme__")
            : null;
        string? nightBaseRef = nightScheme == "rimekit_custom_night"
            ? ReadString(raw, "preset_color_schemes/rimekit_custom_night/__base_scheme__")
            : null;
        return new WeaselUserSettings
        {
            ColorScheme = dayScheme,
            ColorSchemeDark = nightScheme,
            FontFace = ReadString(raw, "style/font_face"),
            FontPoint = ReadInt(raw, "style/font_point"),
            LabelFontFace = ReadString(raw, "style/label_font_face"),
            LabelFontPoint = ReadInt(raw, "style/label_font_point"),
            CommentFontFace = ReadString(raw, "style/comment_font_face"),
            CommentFontPoint = ReadInt(raw, "style/comment_font_point"),
            ShowNotification = ReadBool(raw, "show_notifications"),
            NotificationTimeMs = ReadInt(raw, "show_notifications_time"),
            LabelFormat = ReadString(raw, "style/label_format"),
            MarkText = ReadString(raw, "style/mark_text"),
            PagingOnScroll = ReadBool(raw, "style/paging_on_scroll"),
            CandidateAbbreviateLength = ReadInt(raw, "style/candidate_abbreviate_length"),
            Fullscreen = ReadBool(raw, "style/fullscreen"),
            VerticalText = ReadBool(raw, "style/vertical_text"),
            VerticalTextLeftToRight = ReadBool(raw, "style/vertical_text_left_to_right"),
            VerticalTextWithWrap = ReadBool(raw, "style/vertical_text_with_wrap"),
            VerticalAutoReverse = ReadBool(raw, "style/vertical_auto_reverse"),
            InlinePreedit = ReadBool(raw, "style/inline_preedit"),
            PreeditType = ReadString(raw, "style/preedit_type"),
            GlobalAscii = ReadBool(raw, "global_ascii"),
            HoverType = ReadString(raw, "style/hover_type"),
            ClickToCapture = ReadBool(raw, "style/click_to_capture"),
            AntialiasMode = ReadString(raw, "style/antialias_mode"),
            DisplayTrayIcon = ReadBool(raw, "style/display_tray_icon"),
            EnhancedPosition = ReadBool(raw, "style/enhanced_position"),
            AsciiTipFollowCursor = ReadBool(raw, "style/ascii_tip_follow_cursor"),
            LayoutMinWidth = ReadInt(raw, "style/layout/min_width"),
            LayoutMaxWidth = ReadInt(raw, "style/layout/max_width"),
            LayoutMinHeight = ReadInt(raw, "style/layout/min_height"),
            LayoutMaxHeight = ReadInt(raw, "style/layout/max_height"),
            LayoutMarginX = ReadInt(raw, "style/layout/margin_x"),
            LayoutMarginY = ReadInt(raw, "style/layout/margin_y"),
            LayoutBorderWidth = ReadInt(raw, "style/layout/border_width"),
            LayoutLineSpacing = ReadInt(raw, "style/layout/linespacing"),
            LayoutBaseline = ReadInt(raw, "style/layout/baseline"),
            LayoutAlignType = ReadString(raw, "style/layout/align_type"),
            LayoutSpacing = ReadInt(raw, "style/layout/spacing"),
            LayoutCandidateSpacing = ReadInt(raw, "style/layout/candidate_spacing"),
            LayoutHiliteSpacing = ReadInt(raw, "style/layout/hilite_spacing"),
            LayoutHilitePadding = ReadInt(raw, "style/layout/hilite_padding"),
            LayoutHilitePaddingX = ReadInt(raw, "style/layout/hilite_padding_x"),
            LayoutHilitePaddingY = ReadInt(raw, "style/layout/hilite_padding_y"),
            LayoutShadowRadius = ReadInt(raw, "style/layout/shadow_radius"),
            LayoutShadowOffsetX = ReadInt(raw, "style/layout/shadow_offset_x"),
            LayoutShadowOffsetY = ReadInt(raw, "style/layout/shadow_offset_y"),
            LayoutCornerRadius = ReadInt(raw, "style/layout/corner_radius"),
            DayColors = ReadSchemeColors(raw, dayScheme),
            NightColors = ReadSchemeColors(raw, nightScheme),
            CustomDayBaseScheme = dayBaseRef,
            CustomNightBaseScheme = nightBaseRef,
        };
    }

    private static SchemeColors? ReadSchemeColors(Dictionary<string, string> raw, string? schemeName)
    {
        if (string.IsNullOrWhiteSpace(schemeName))
            return null;
        string prefix = $"preset_color_schemes/{schemeName}/";
        return new SchemeColors
        {
            TextColor = ReadString(raw, $"{prefix}text_color"),
            CandidateTextColor = ReadString(raw, $"{prefix}candidate_text_color"),
            LabelColor = ReadString(raw, $"{prefix}label_color"),
            CommentTextColor = ReadString(raw, $"{prefix}comment_text_color"),
            BackColor = ReadString(raw, $"{prefix}back_color"),
            CandidateBackColor = ReadString(raw, $"{prefix}candidate_back_color"),
            BorderColor = ReadString(raw, $"{prefix}border_color"),
            ShadowColor = ReadString(raw, $"{prefix}shadow_color"),
            HilitedTextColor = ReadString(raw, $"{prefix}hilited_text_color"),
            HilitedBackColor = ReadString(raw, $"{prefix}hilited_back_color"),
            HilitedLabelColor = ReadString(raw, $"{prefix}hilited_label_color"),
            HilitedCandidateTextColor = ReadString(raw, $"{prefix}hilited_candidate_text_color"),
            HilitedCandidateBackColor = ReadString(raw, $"{prefix}hilited_candidate_back_color"),
            HilitedCandidateLabelColor = ReadString(raw, $"{prefix}hilited_candidate_label_color"),
            HilitedCandidateBorderColor = ReadString(raw, $"{prefix}hilited_candidate_border_color"),
            HilitedCommentTextColor = ReadString(raw, $"{prefix}hilited_comment_text_color"),
            HilitedMarkColor = ReadString(raw, $"{prefix}hilited_mark_color"),
        };
    }

    public static MintUserSettings ReadMint(string targetRoot, string schemaId)
    {
        string path = Path.Combine(targetRoot, $"{schemaId}.custom.yaml");
        if (!File.Exists(path))
            return new MintUserSettings();

        Dictionary<string, string> raw = ParsePatchYaml(path);
        return new MintUserSettings
        {
            PageSize = ReadInt(raw, "menu/page_size"),
            Layout = ReadString(raw, "style/candidate_list_layout"),
            ShowEmojiComments = ReadCommentBool(raw),
            SimplificationMode = ReadSwitchReset(raw, "transcription"),
            FullShapeEnabled = ReadSwitchResetBool(raw, "full_shape"),
            AsciiPunctEnabled = ReadSwitchResetBool(raw, "ascii_punct"),
            EmojiSuggestionEnabled = ReadSwitchResetBool(raw, "emoji_suggestion"),
            ToneDisplayEnabled = ReadSwitchResetBool(raw, "tone_display"),
            EnableUserDict = ReadBool(raw, "translator/enable_user_dict"),
            FuzzyEnabled = HasFuzzyRules(raw),
            FuzzyAdditionalRules = ReadFuzzyRulesFromFile(targetRoot, schemaId),
            UeCompatEnabled = ReadUeCompatFromFile(targetRoot, schemaId),
            CustomPhraseMode = ReadPhraseMode(raw),
        };
    }

    public static void WriteWeaselCrossLayer(string targetRoot, WeaselUserSettings settings, MintUserSettings mint)
    {
        string path = Path.Combine(targetRoot, "weasel.custom.yaml");
        List<string> lines = ["patch:"];
        AppendIf(lines, "style/color_scheme", settings.ColorScheme);
        AppendIf(lines, "style/color_scheme_dark", settings.ColorSchemeDark);
        AppendIf(lines, "style/font_face", settings.FontFace);
        AppendIntIf(lines, "style/font_point", settings.FontPoint);
        AppendIf(lines, "style/label_font_face", settings.LabelFontFace);
        AppendIntIf(lines, "style/label_font_point", settings.LabelFontPoint);
        AppendIf(lines, "style/comment_font_face", settings.CommentFontFace);
        AppendIntIf(lines, "style/comment_font_point", settings.CommentFontPoint);
        AppendBoolIf(lines, "show_notifications", settings.ShowNotification);
        AppendIntIf(lines, "show_notifications_time", settings.NotificationTimeMs);
        AppendIf(lines, "style/label_format", settings.LabelFormat);
        AppendIf(lines, "style/mark_text", settings.MarkText);
        AppendBoolIf(lines, "style/paging_on_scroll", settings.PagingOnScroll);
        AppendIntIf(lines, "style/candidate_abbreviate_length", settings.CandidateAbbreviateLength);
        AppendBoolIf(lines, "style/fullscreen", settings.Fullscreen);
        AppendBoolIf(lines, "style/vertical_text", settings.VerticalText);
        AppendBoolIf(lines, "style/vertical_text_left_to_right", settings.VerticalTextLeftToRight);
        AppendBoolIf(lines, "style/vertical_text_with_wrap", settings.VerticalTextWithWrap);
        AppendBoolIf(lines, "style/vertical_auto_reverse", settings.VerticalAutoReverse);
        AppendBoolIf(lines, "style/inline_preedit", settings.InlinePreedit);
        AppendIf(lines, "style/preedit_type", settings.PreeditType);
        if (settings.GlobalAscii.HasValue)
            lines.Add($"  \"global_ascii\": {(settings.GlobalAscii.Value ? "true" : "false")}");
        AppendIf(lines, "style/hover_type", settings.HoverType);
        AppendBoolIf(lines, "style/click_to_capture", settings.ClickToCapture);
        AppendIf(lines, "style/antialias_mode", settings.AntialiasMode);
        AppendBoolIf(lines, "style/display_tray_icon", settings.DisplayTrayIcon);
        AppendBoolIf(lines, "style/enhanced_position", settings.EnhancedPosition);
        AppendBoolIf(lines, "style/ascii_tip_follow_cursor", settings.AsciiTipFollowCursor);
        AppendIntIf(lines, "style/layout/min_width", settings.LayoutMinWidth);
        AppendIntIf(lines, "style/layout/max_width", settings.LayoutMaxWidth);
        AppendIntIf(lines, "style/layout/min_height", settings.LayoutMinHeight);
        AppendIntIf(lines, "style/layout/max_height", settings.LayoutMaxHeight);
        AppendIntIf(lines, "style/layout/margin_x", settings.LayoutMarginX);
        AppendIntIf(lines, "style/layout/margin_y", settings.LayoutMarginY);
        AppendIntIf(lines, "style/layout/border_width", settings.LayoutBorderWidth);
        AppendIntIf(lines, "style/layout/linespacing", settings.LayoutLineSpacing);
        AppendIntIf(lines, "style/layout/baseline", settings.LayoutBaseline);
        AppendIf(lines, "style/layout/align_type", settings.LayoutAlignType);
        AppendIntIf(lines, "style/layout/spacing", settings.LayoutSpacing);
        AppendIntIf(lines, "style/layout/candidate_spacing", settings.LayoutCandidateSpacing);
        AppendIntIf(lines, "style/layout/hilite_spacing", settings.LayoutHiliteSpacing);
        AppendIntIf(lines, "style/layout/hilite_padding", settings.LayoutHilitePadding);
        AppendIntIf(lines, "style/layout/hilite_padding_x", settings.LayoutHilitePaddingX);
        AppendIntIf(lines, "style/layout/hilite_padding_y", settings.LayoutHilitePaddingY);
        AppendIntIf(lines, "style/layout/shadow_radius", settings.LayoutShadowRadius);
        AppendIntIf(lines, "style/layout/shadow_offset_x", settings.LayoutShadowOffsetX);
        AppendIntIf(lines, "style/layout/shadow_offset_y", settings.LayoutShadowOffsetY);
        AppendIntIf(lines, "style/layout/corner_radius", settings.LayoutCornerRadius);

        if (!string.IsNullOrWhiteSpace(mint.Layout))
        {
            if (string.Equals(mint.Layout, "linear", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"  \"style/candidate_list_layout\": \"linear\"");
                lines.Add($"  \"style/horizontal\": true");
            }
            else if (string.Equals(mint.Layout, "stacked", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"  \"style/candidate_list_layout\": \"stacked\"");
                lines.Add($"  \"style/horizontal\": false");
            }
        }

        WriteSchemeColorOverrides(lines, settings.ColorScheme, settings.DayColors);
        WriteSchemeColorOverrides(lines, settings.ColorSchemeDark, settings.NightColors);
        if (!string.IsNullOrWhiteSpace(settings.CustomDayBaseScheme) && settings.ColorScheme == "rimekit_custom_day")
            lines.Add($"  \"preset_color_schemes/rimekit_custom_day/__base_scheme__\": \"{settings.CustomDayBaseScheme}\"");
        if (!string.IsNullOrWhiteSpace(settings.CustomNightBaseScheme) && settings.ColorSchemeDark == "rimekit_custom_night")
            lines.Add($"  \"preset_color_schemes/rimekit_custom_night/__base_scheme__\": \"{settings.CustomNightBaseScheme}\"");

        Directory.CreateDirectory(targetRoot);
        FileHelper.WriteTextWithVerification(path, string.Join("\r\n", lines) + "\r\n", System.Text.Encoding.UTF8);
    }

    public static void WriteMint(string targetRoot, string schemaId, MintUserSettings settings)
    {
        string path = Path.Combine(targetRoot, $"{schemaId}.custom.yaml");
        List<string> lines = ["patch:"];
        AppendIntIf(lines, "menu/page_size", settings.PageSize);
        AppendIf(lines, "style/candidate_list_layout", settings.Layout);
        if (settings.ShowEmojiComments.HasValue)
            lines.Add($"  \"translator/always_show_comments\": {(settings.ShowEmojiComments.Value ? "true" : "false")}");
        lines.Add("  \"switches/@0/reset\": 0");
        if (settings.EmojiSuggestionEnabled.HasValue)
            lines.Add($"  \"switches/@1/reset\": {(settings.EmojiSuggestionEnabled.Value ? "1" : "0")}");
        if (settings.FullShapeEnabled.HasValue)
            lines.Add($"  \"switches/@2/reset\": {(settings.FullShapeEnabled.Value ? "1" : "0")}");
        if (settings.ToneDisplayEnabled.HasValue)
            lines.Add($"  \"switches/@3/reset\": {(settings.ToneDisplayEnabled.Value ? "1" : "0")}");
        if (!string.IsNullOrWhiteSpace(settings.SimplificationMode))
        {
            string reset = string.Equals(settings.SimplificationMode, "traditional", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
            lines.Add($"  \"switches/@4/reset\": {reset}");
        }
        if (settings.AsciiPunctEnabled.HasValue)
            lines.Add($"  \"switches/@5/reset\": {(settings.AsciiPunctEnabled.Value ? "1" : "0")}");
        AppendBoolIf(lines, "translator/enable_user_dict", settings.EnableUserDict);
        if (!string.IsNullOrWhiteSpace(settings.CustomPhraseMode)
            && !string.Equals(settings.CustomPhraseMode, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            bool fullPhrase = string.Equals(settings.CustomPhraseMode, "full_phrase", StringComparison.OrdinalIgnoreCase);
            lines.Add("  \"engine/translators/+\":");
            lines.Add("    - table_translator@mint_simple");
            lines.Add("  \"mint_simple/dictionary\": \"\"");
            lines.Add("  \"mint_simple/user_dict\": \"dicts/rime_mint.simple\"");
            lines.Add("  \"mint_simple/db_class\": \"stabledb\"");
            lines.Add($"  \"mint_simple/enable_completion\": {(fullPhrase ? "true" : "false")}");
            lines.Add("  \"mint_simple/enable_sentence\": false");
            lines.Add("  \"mint_simple/initial_quality\": 0.5");
            lines.Add("  \"mint_simple/comment_format\":");
            lines.Add("    - \"xform/^.+$//\"");
        }
        bool fuzzyWritesHeader = settings.FuzzyEnabled == true && settings.FuzzyAdditionalRules.Count > 0;
        if (fuzzyWritesHeader)
        {
            lines.Add("  \"speller/algebra/+\":");
            foreach (string rule in settings.FuzzyAdditionalRules)
            {
                string expanded = ExpandFuzzyRule(rule);
                lines.Add($"    - \"{expanded}\"");
            }
        }
        if (settings.UeCompatEnabled == true)
        {
            if (!fuzzyWritesHeader)
                lines.Add("  \"speller/algebra/+\":");
            lines.Add("    - \"derive/^([nl])ve$/$1ue/\"");
        }
        Directory.CreateDirectory(targetRoot);
        FileHelper.WriteTextWithVerification(path, string.Join("\r\n", lines) + "\r\n", System.Text.Encoding.UTF8);
    }

    public static void WriteDefaultsFromTemplates(string targetRoot, string schemaId)
    {
        WeaselUserSettings weasel = BuildWeaselFromTemplate();
        MintUserSettings mint = BuildMintFromTemplate();
        WriteWeaselCrossLayer(targetRoot, weasel, mint);
        WriteMint(targetRoot, schemaId, mint);
    }

    private static WeaselUserSettings BuildWeaselFromTemplate()
    {
        ParsedWeaselYaml? tmpl = TemplateService.GetWeaselDefaults();
        return new WeaselUserSettings
        {
            ColorScheme = tmpl?.ColorScheme is { Length: > 0 } c ? c : null,
            ColorSchemeDark = tmpl?.ColorSchemeDark is { Length: > 0 } d ? d : null,
            FontFace = tmpl?.FontFace is { Length: > 0 } f ? f : null,
            FontPoint = tmpl?.FontPoint > 0 ? tmpl.FontPoint : null,
            LabelFontFace = tmpl?.LabelFontFace is { Length: > 0 } lf ? lf : null,
            LabelFontPoint = tmpl?.LabelFontPoint > 0 ? tmpl.LabelFontPoint : null,
            CommentFontFace = tmpl?.CommentFontFace is { Length: > 0 } cf ? cf : null,
            CommentFontPoint = tmpl?.CommentFontPoint > 0 ? tmpl.CommentFontPoint : null,
            ShowNotification = tmpl?.ShowNotification,
            NotificationTimeMs = tmpl?.NotificationTimeMs > 0 ? tmpl.NotificationTimeMs : null,
            LabelFormat = tmpl?.LabelFormat is { Length: > 0 } lb ? lb : null,
            MarkText = tmpl?.MarkText is { Length: > 0 } mt ? mt : null,
            PagingOnScroll = tmpl?.PagingOnScroll,
            CandidateAbbreviateLength = tmpl?.CandidateAbbreviateLength > 0 ? tmpl.CandidateAbbreviateLength : null,
            Fullscreen = tmpl?.Fullscreen,
            VerticalText = tmpl?.VerticalText,
            VerticalTextLeftToRight = tmpl?.VerticalTextLeftToRight,
            VerticalTextWithWrap = tmpl?.VerticalTextWithWrap,
            VerticalAutoReverse = tmpl?.VerticalAutoReverse,
            InlinePreedit = tmpl?.InlinePreedit,
            PreeditType = tmpl?.PreeditType is { Length: > 0 } pt ? pt : null,
            GlobalAscii = tmpl?.GlobalAscii,
            HoverType = tmpl?.HoverType is { Length: > 0 } ht ? ht : null,
            ClickToCapture = tmpl?.ClickToCapture,
            AntialiasMode = tmpl?.AntialiasMode is { Length: > 0 } am ? am : null,
            DisplayTrayIcon = tmpl?.DisplayTrayIcon,
            EnhancedPosition = tmpl?.EnhancedPosition,
            AsciiTipFollowCursor = tmpl?.AsciiTipFollowCursor,
            LayoutMinWidth = tmpl?.LayoutMinWidth,
            LayoutMaxWidth = tmpl?.LayoutMaxWidth,
            LayoutMinHeight = tmpl?.LayoutMinHeight,
            LayoutMaxHeight = tmpl?.LayoutMaxHeight,
            LayoutMarginX = tmpl?.LayoutMarginX,
            LayoutMarginY = tmpl?.LayoutMarginY,
            LayoutBorderWidth = tmpl?.LayoutBorderWidth,
            LayoutLineSpacing = tmpl?.LayoutLineSpacing,
            LayoutBaseline = tmpl?.LayoutBaseline,
            LayoutAlignType = tmpl?.LayoutAlignType is { Length: > 0 } la ? la : null,
            LayoutSpacing = tmpl?.LayoutSpacing,
            LayoutCandidateSpacing = tmpl?.LayoutCandidateSpacing,
            LayoutHiliteSpacing = tmpl?.LayoutHiliteSpacing,
            LayoutHilitePadding = tmpl?.LayoutHilitePadding,
            LayoutHilitePaddingX = tmpl?.LayoutHilitePaddingX,
            LayoutHilitePaddingY = tmpl?.LayoutHilitePaddingY,
            LayoutShadowRadius = tmpl?.LayoutShadowRadius,
            LayoutShadowOffsetX = tmpl?.LayoutShadowOffsetX,
            LayoutShadowOffsetY = tmpl?.LayoutShadowOffsetY,
            LayoutCornerRadius = tmpl?.LayoutCornerRadius,
        };
    }

    private static MintUserSettings BuildMintFromTemplate()
    {
        ParsedSchemaDefaults? s = TemplateService.GetSchemaDefaults("rime_mint");
        return new MintUserSettings
        {
            PageSize = s?.PageSize > 0 ? s.PageSize : null,
            Layout = s?.CandidateLayout is { Length: > 0 } cl ? cl : null,
            ShowEmojiComments = s?.ShowEmojiComments,
            SimplificationMode = s?.SimplificationMode is { Length: > 0 } sm ? sm : null,
            EmojiSuggestionEnabled = s?.EmojiSuggestionEnabled,
            FullShapeEnabled = s?.FullShapeEnabled,
            ToneDisplayEnabled = s?.ToneDisplayEnabled,
            AsciiPunctEnabled = s?.AsciiPunctEnabled,
            EnableUserDict = s?.EnableUserDict,
            FuzzyEnabled = null,
            FuzzyAdditionalRules = [],
            UeCompatEnabled = null,
            CustomPhraseMode = null,
        };
    }

    public static void WriteGrammarDefaults(string targetRoot, string schemaId)
    {
        string path = Path.Combine(targetRoot, $"{schemaId}.custom.yaml");
        string existing = File.Exists(path)
            ? File.ReadAllText(path, System.Text.Encoding.UTF8)
            : "patch:\r\n";
        if (!existing.Contains("\"grammar/language\":"))
        {
            string insertion = "  \"grammar/language\": \"wanxiang-lts-zh-hans\"\r\n"
                + "  \"grammar/collocation_max_length\": 8\r\n"
                + "  \"grammar/collocation_min_length\": 2\r\n"
                + "  \"grammar/collocation_penalty\": -16\r\n"
                + "  \"grammar/non_collocation_penalty\": -8\r\n"
                + "  \"grammar/weak_collocation_penalty\": -100\r\n"
                + "  \"grammar/rear_penalty\": -20\r\n"
                + "  \"translator/contextual_suggestions\": true\r\n"
                + "  \"translator/max_homophones\": 7\r\n"
                + "  \"translator/max_homographs\": 7\r\n";
            existing = existing.Replace("patch:\r\n", "patch:\r\n" + insertion);
        }
        Directory.CreateDirectory(targetRoot);
        FileHelper.WriteTextWithVerification(path, existing, System.Text.Encoding.UTF8);
    }

    public static void RemoveGrammarDefaults(string targetRoot, string schemaId)
    {
        string path = Path.Combine(targetRoot, $"{schemaId}.custom.yaml");
        if (!File.Exists(path))
        {
            return;
        }

        string[] lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        List<string> result = new(lines.Length);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("\"grammar/", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("\"translator/contextual_suggestions\"", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("\"translator/max_homophones\"", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("\"translator/max_homographs\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(line);
        }

        FileHelper.WriteTextWithVerification(path, string.Join("\r\n", result) + "\r\n", System.Text.Encoding.UTF8);
    }

    private static Dictionary<string, string> ParsePatchYaml(string path)
    {
        string[] content = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        Dictionary<string, string> raw = new(StringComparer.OrdinalIgnoreCase);
        bool inPatch = false;
        foreach (string line in content)
        {
            string trimmed = line.Trim();
            if (trimmed == "patch:" || trimmed.StartsWith("patch:"))
            {
                inPatch = true;
                continue;
            }
            if (!inPatch)
                continue;
            Match match = PatchValuePattern.Match(trimmed);
            if (!match.Success)
                continue;
            string key = match.Groups[1].Value.Trim();
            string value = match.Groups[2].Value.Trim().Trim('"');
            raw[key] = value;
        }
        return raw;
    }

    private static string? ReadString(Dictionary<string, string> raw, string key)
    {
        return raw.TryGetValue(key, out string? v) && v.Length > 0 ? v : null;
    }

    private static int? ReadInt(Dictionary<string, string> raw, string key)
    {
        return raw.TryGetValue(key, out string? v) && int.TryParse(v, out int n) ? n : null;
    }

    private static bool? ReadBool(Dictionary<string, string> raw, string key)
    {
        return raw.TryGetValue(key, out string? v) ? v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" ? true
            : v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0" ? false : null : null;
    }

    private static bool? ReadCommentBool(Dictionary<string, string> raw)
    {
        return ReadBool(raw, "translator/always_show_comments");
    }

    private static string? ReadPhraseMode(Dictionary<string, string> raw)
    {
        if (raw.TryGetValue("mint_simple/enable_completion", out string? v))
        {
            if (v == "true")
                return "full_phrase";
            if (v == "false")
                return "simple_code_only";
            return null;
        }
        return null;
    }

    private static string? ReadSwitchReset(Dictionary<string, string> raw, string switchName)
    {
        foreach (string key in raw.Keys)
        {
            if (!key.StartsWith("switches/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (raw.TryGetValue(key, out string? val) && key.Contains(switchName, StringComparison.OrdinalIgnoreCase) && key.EndsWith("/reset", StringComparison.OrdinalIgnoreCase))
            {
                if (val == "1")
                    return "traditional";
                if (val == "0")
                    return "simplified";
                return null;
            }
        }
        return null;
    }

    private static readonly Dictionary<string, int> SwitchIndices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ascii_mode"] = 0,
        ["emoji_suggestion"] = 1,
        ["full_shape"] = 2,
        ["tone_display"] = 3,
        ["transcription"] = 4,
        ["ascii_punct"] = 5,
    };

    private static bool? ReadSwitchResetBool(Dictionary<string, string> raw, string switchName)
    {
        foreach (string key in raw.Keys)
        {
            if (!key.StartsWith("switches/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (raw.TryGetValue(key, out string? val) && key.Contains(switchName, StringComparison.OrdinalIgnoreCase) && key.EndsWith("/reset", StringComparison.OrdinalIgnoreCase))
            {
                if (val == "1")
                    return true;
                if (val == "0")
                    return false;
                return null;
            }
        }
        if (SwitchIndices.TryGetValue(switchName, out int idx))
        {
            string indexedKey = $"switches/@{idx}/reset";
            if (raw.TryGetValue(indexedKey, out string? ival))
            {
                if (ival == "1")
                    return true;
                if (ival == "0")
                    return false;
                return null;
            }
        }
        return null;
    }

    private static bool? HasFuzzyRules(Dictionary<string, string> raw)
    {
        return raw.Values.Any(v =>
            v.StartsWith("derive/", StringComparison.Ordinal)
            && !(v.Contains("nl", StringComparison.Ordinal)
                 && v.Contains("ve", StringComparison.Ordinal)
                 && v.Contains("ue", StringComparison.Ordinal))
        ) ? true : null;
    }

    private static IReadOnlyList<string> ReadFuzzyRulesFromFile(string targetRoot, string schemaId)
    {
        string path = Path.Combine(targetRoot, $"{schemaId}.custom.yaml");
        if (!File.Exists(path))
            return [];
        string[] fileLines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        List<string> rules = [];
        bool inAlgebra = false;
        foreach (string line in fileLines)
        {
            string trimmed = line.Trim();
            if (trimmed == "\"speller/algebra/\":" || trimmed == "\"speller/algebra/+\":" || trimmed == "speller/algebra/+:")
            {
                inAlgebra = true;
                continue;
            }
            if (inAlgebra && trimmed.StartsWith("- "))
            {
                string rule = trimmed[2..].Trim().Trim('"');
                if (rule.StartsWith("derive/")
                    && !(rule.Contains("nl") && rule.Contains("ve") && rule.Contains("ue")))
                    rules.Add(rule);
            }
            else if (inAlgebra && (trimmed.Contains(':') || !trimmed.StartsWith("- ")))
            {
                inAlgebra = false;
            }
        }
        return rules;
    }

    private static string ExpandFuzzyRule(string rule)
    {
        if (string.Equals(rule, "derive/zh/z", StringComparison.OrdinalIgnoreCase))
            return "derive/^zh([a-z]+)$/z$1/";
        if (string.Equals(rule, "derive/ch/c", StringComparison.OrdinalIgnoreCase))
            return "derive/^ch([a-z]+)$/c$1/";
        if (string.Equals(rule, "derive/sh/s", StringComparison.OrdinalIgnoreCase))
            return "derive/^sh([a-z]+)$/s$1/";
        return rule;
    }

    private static bool? HasUeCompatRule(Dictionary<string, string> raw)
    {
        return raw.Values.Any(v => v.Contains("nl") && v.Contains("ve") && v.Contains("ue")) ? true : null;
    }

    private static bool? ReadUeCompatFromFile(string targetRoot, string schemaId)
    {
        string path = Path.Combine(targetRoot, $"{schemaId}.custom.yaml");
        if (!File.Exists(path))
            return null;
        string[] fileLines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        bool inAlgebra = false;
        foreach (string line in fileLines)
        {
            string trimmed = line.Trim();
            if (trimmed == "\"speller/algebra/\":" || trimmed == "\"speller/algebra/+\":" || trimmed == "speller/algebra/+:")
            {
                inAlgebra = true;
                continue;
            }
            if (inAlgebra && trimmed.StartsWith("- "))
            {
                string rule = trimmed[2..].Trim().Trim('"');
                if (rule.Contains("nl") && rule.Contains("ve") && rule.Contains("ue"))
                    return true;
            }
            else if (inAlgebra && (trimmed.Contains(':') || !trimmed.StartsWith("- ")))
            {
                inAlgebra = false;
            }
        }
        return null;
    }

    private static void WriteSchemeColorOverrides(List<string> lines, string? schemeName, SchemeColors? colors)
    {
        if (string.IsNullOrWhiteSpace(schemeName) || colors is null)
            return;
        string prefix = $"preset_color_schemes/{schemeName}/";
        AppendColorIf(lines, $"{prefix}text_color", colors.TextColor);
        AppendColorIf(lines, $"{prefix}candidate_text_color", colors.CandidateTextColor);
        AppendColorIf(lines, $"{prefix}label_color", colors.LabelColor);
        AppendColorIf(lines, $"{prefix}comment_text_color", colors.CommentTextColor);
        AppendColorIf(lines, $"{prefix}back_color", colors.BackColor);
        AppendColorIf(lines, $"{prefix}candidate_back_color", colors.CandidateBackColor);
        AppendColorIf(lines, $"{prefix}border_color", colors.BorderColor);
        AppendColorIf(lines, $"{prefix}shadow_color", colors.ShadowColor);
        AppendColorIf(lines, $"{prefix}hilited_text_color", colors.HilitedTextColor);
        AppendColorIf(lines, $"{prefix}hilited_back_color", colors.HilitedBackColor);
        AppendColorIf(lines, $"{prefix}hilited_label_color", colors.HilitedLabelColor);
        AppendColorIf(lines, $"{prefix}hilited_candidate_text_color", colors.HilitedCandidateTextColor);
        AppendColorIf(lines, $"{prefix}hilited_candidate_back_color", colors.HilitedCandidateBackColor);
        AppendColorIf(lines, $"{prefix}hilited_candidate_label_color", colors.HilitedCandidateLabelColor);
        AppendColorIf(lines, $"{prefix}hilited_candidate_border_color", colors.HilitedCandidateBorderColor);
        AppendColorIf(lines, $"{prefix}hilited_comment_text_color", colors.HilitedCommentTextColor);
        AppendColorIf(lines, $"{prefix}hilited_mark_color", colors.HilitedMarkColor);
    }

    private static void AppendColorIf(List<string> lines, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        lines.Add($"  \"{key}\": {value}");
    }

    private static void AppendIf(List<string> lines, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        lines.Add($"  \"{key}\": \"{value}\"");
    }

    private static void AppendIntIf(List<string> lines, string key, int? value)
    {
        if (!value.HasValue)
            return;
        lines.Add($"  \"{key}\": {value}");
    }

    private static void AppendBoolIf(List<string> lines, string key, bool? value)
    {
        if (!value.HasValue)
            return;
        lines.Add($"  \"{key}\": {(value.Value ? "true" : "false")}");
    }
}
