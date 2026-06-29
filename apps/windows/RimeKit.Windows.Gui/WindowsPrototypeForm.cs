using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using RimeKit.Windows.Core;

namespace RimeKit.Windows.Gui;

/// <summary>
/// Windows 端当前采用的正式界面骨架。
/// </summary>
public class WindowsPrototypeForm : Form
{
    private readonly WindowsWorkflowService _workflowService;
    private readonly string _configModelPath;
    private readonly Label _statusLabel;

    private readonly ListBox _dictionaryListBox;
    private readonly Panel _dictionaryDetailPanel;
    private readonly ListBox _modelListBox;
    private readonly Panel _modelDetailPanel;
    private readonly Panel _carrierInfoHost;
    private readonly Panel _schemeInfoHost;
    private readonly Panel _syncStatusHost;
    private readonly Panel _backupStatusHost;

    private readonly BindingList<UserEntryRow> _userEntries;
    private readonly BindingList<FuzzyRuleRow> _fuzzyRules;

    private bool _carrierStatusDetected;
    private bool _userEntriesDetected;
    private bool _inputSchemeDetected;
    private bool _templatesAreMissing;
    private bool _prevCarrierAvailable;
    private bool _isRechecking;
    private bool _showingTemplateDefaults;
    private string? _dayBaseScheme;
    private string? _nightBaseScheme;
    private string? _detectedDictionaryStatusName;
    private string? _detectedModelStatusName;

    private readonly ComboBox _schemeComboBox;
    private readonly RadioButton _simplifiedRadio;
    private readonly RadioButton _traditionalRadio;
    private readonly RadioButton _halfShapeRadio;
    private readonly RadioButton _fullShapeRadio;
    private readonly CheckBox _asciiPunctCheckBox;
    private readonly CheckBox _emojiCheckBox;
    private readonly CheckBox _toneCheckBox;
    private readonly CheckBox _enableUserDictCheckBox;
    private readonly CheckBox _fuzzyCheckBox;
    private readonly DataGridView _fuzzyRulesGrid;

    private Button? _schemeInstallBtn;
    private Button? _schemeUninstallBtn;
    private Button? _dictInstallBtn;
    private Button? _dictInstallFromFileBtn;
    private Button? _dictUninstallBtn;
    private Button? _modelInstallBtn;
    private Button? _modelInstallFromFileBtn;
    private Button? _modelUninstallBtn;
    private Button? _carrierDetectBtn;
    private Button? _dictDetectLocalBtn;
    private Button? _dictDetectStatusBtn;
    private Button? _modelDetectLocalBtn;
    private Button? _modelDetectStatusBtn;
    private Button? _schemeDetectBtn;

    private readonly ComboBox _dayThemeComboBox;
    private readonly ComboBox _nightThemeComboBox;
    private readonly TextBox _fontTextBox;
    private readonly TextBox _fontSizeText;
    private readonly CheckBox _statusNotificationCheckBox;
    private readonly NumericUpDown _candidateCountNumeric;
    private readonly ComboBox _candidateDirectionComboBox;
    private readonly CheckBox _candidateCommentCheckBox;

    private readonly CheckBox _ueCompatCheckBox;
    private readonly ComboBox _customPhraseComboBox;
    private readonly ComboBox _symbolProfileComboBox;
    private readonly ComboBox _preeditFormatComboBox;
    private readonly TextBox _labelFontTextBox;
    private readonly TextBox _labelFontSizeText;
    private readonly TextBox _commentFontTextBox;
    private readonly TextBox _commentFontSizeText;
    private readonly TextBox _notificationTimeText;
    private readonly TextBox _labelFormatTextBox;
    private readonly TextBox _markTextTextBox;
    private readonly CheckBox _pagingOnScrollCheckBox;
    private readonly TextBox _candidateAbbreviateText;
    private readonly ComboBox _commentStyleComboBox;
    private readonly CheckBox _fullscreenCheckBox;
    private readonly CheckBox _verticalTextCheckBox;
    private readonly CheckBox _verticalTextLeftToRightCheckBox;
    private readonly CheckBox _verticalTextWithWrapCheckBox;
    private readonly CheckBox _verticalAutoReverseCheckBox;
    private readonly CheckBox _inlinePreeditCheckBox;
    private readonly ComboBox _preeditTypeComboBox;
    private readonly ComboBox _globalAsciiComboBox;
    private readonly ComboBox _hoverTypeComboBox;
    private readonly CheckBox _clickToCaptureCheckBox;
    private readonly ComboBox _antialiasModeComboBox;
    private readonly CheckBox _displayTrayIconCheckBox;
    private readonly CheckBox _enhancedPositionCheckBox;
    private readonly CheckBox _asciiTipFollowCursorCheckBox;
    private readonly TextBox _layoutMinWidthText;
    private readonly TextBox _layoutMinHeightText;
    private readonly TextBox _layoutMaxWidthText;
    private readonly TextBox _layoutMaxHeightText;
    private readonly TextBox _layoutMarginXText;
    private readonly TextBox _layoutMarginYText;
    private readonly TextBox _layoutBorderWidthText;
    private readonly TextBox _layoutLineSpacingText;
    private readonly TextBox _layoutBaselineText;
    private readonly TextBox _layoutSpacingText;
    private readonly TextBox _layoutCandidateSpacingText;
    private readonly TextBox _layoutHiliteSpacingText;
    private readonly TextBox _layoutHilitePaddingText;
    private readonly TextBox _layoutHilitePaddingXText;
    private readonly TextBox _layoutHilitePaddingYText;
    private readonly TextBox _layoutShadowRadiusText;
    private readonly TextBox _layoutShadowOffsetXText;
    private readonly TextBox _layoutShadowOffsetYText;
    private readonly TextBox _layoutCornerRadiusText;
    private readonly ComboBox _layoutAlignTypeComboBox;

    private readonly FlowLayoutPanel _textColorField;
    private readonly FlowLayoutPanel _candidateTextColorField;
    private readonly FlowLayoutPanel _labelColorField;
    private readonly FlowLayoutPanel _commentTextColorField;
    private readonly FlowLayoutPanel _backColorField;
    private readonly FlowLayoutPanel _candidateBackColorField;
    private readonly FlowLayoutPanel _borderColorField;
    private readonly FlowLayoutPanel _shadowColorField;
    private readonly FlowLayoutPanel _hilitedTextColorField;
    private readonly FlowLayoutPanel _hilitedBackColorField;
    private readonly FlowLayoutPanel _hilitedLabelColorField;
    private readonly FlowLayoutPanel _hilitedCandidateTextColorField;
    private readonly FlowLayoutPanel _hilitedCandidateBackColorField;
    private readonly FlowLayoutPanel _hilitedCandidateLabelColorField;
    private readonly FlowLayoutPanel _hilitedCandidateBorderColorField;
    private readonly FlowLayoutPanel _hilitedCommentTextColorField;
    private readonly FlowLayoutPanel _hilitedMarkColorField;
    private readonly RadioButton _editDayRadio;
    private readonly RadioButton _editNightRadio;

    protected string StartDirectory { get; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string?>? ExportUserConfigPathProvider { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string?>? ImportUserConfigPathProvider { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? UnsupportedActionObserver { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? WorkflowErrorObserver { get; set; }

    public WindowsPrototypeForm()
        : this(AppContext.BaseDirectory)
    {
    }

    public WindowsPrototypeForm(string startDirectory)
    {
        StartDirectory = startDirectory;
        _workflowService = new WindowsWorkflowService(startDirectory);
        WorkflowErrorObserver = _ =>
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = "操作执行失败。请查看 workspace\\windows\\state\\last_diagnostic.json 了解详情。";
        };
        _configModelPath = ResolveCurrentConfigModelPath(startDirectory);
        FontFamily uiFontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        Font = new Font(uiFontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "RimeKit Windows";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1400, 900);
        Size = new Size(1600, 960);
        AutoScaleMode = AutoScaleMode.Dpi;

        _userEntries =
        [
            new UserEntryRow("流程闭环", "lcbh", "当前已生效"),
            new UserEntryRow("自动验证", "zdyz", "当前已生效"),
            new UserEntryRow("皇室战争", "huangshizhanzheng", "当前已生效"),
        ];
        _fuzzyRules =
        [
            new FuzzyRuleRow("zh", "z"),
            new FuzzyRuleRow("ch", "c"),
            new FuzzyRuleRow("sh", "s"),
        ];

        _dictionaryListBox = CreateSelectorListBox();
        _dictionaryListBox.Name = "_dictionaryListBox";
        _dictionaryListBox.SelectedIndexChanged += (_, _) => RenderDictionaryDetail();
        _dictionaryDetailPanel = new Panel { Name = "_dictionaryDetailPanel", Dock = DockStyle.Fill };

        _modelListBox = CreateSelectorListBox();
        _modelListBox.Name = "_modelListBox";
        _modelListBox.SelectedIndexChanged += (_, _) => RenderModelDetail();
        _modelDetailPanel = new Panel { Name = "_modelDetailPanel", Dock = DockStyle.Fill };
        _carrierInfoHost = new Panel { Name = "_carrierInfoHost", Dock = DockStyle.Top, AutoSize = true };
        _schemeInfoHost = new Panel { Name = "_schemeInfoHost", Dock = DockStyle.Top, AutoSize = true };
        _syncStatusHost = new Panel { Name = "_syncStatusHost", Dock = DockStyle.Top, AutoSize = true };
        _backupStatusHost = new Panel { Name = "_backupStatusHost", Dock = DockStyle.Top, AutoSize = true };

        _schemeComboBox = CreateComboBox([string.Empty]);
        _schemeComboBox.Name = "_schemeComboBox";
        _simplifiedRadio = new RadioButton { Text = "简体", AutoSize = true, Checked = true };
        _traditionalRadio = new RadioButton { Text = "繁体", AutoSize = true };
        _halfShapeRadio = new RadioButton { Text = "半角", AutoSize = true, Checked = true };
        _fullShapeRadio = new RadioButton { Text = "全角", AutoSize = true };
        _asciiPunctCheckBox = new CheckBox { Text = "英文标点", AutoSize = true };
        _emojiCheckBox = new CheckBox { Text = "Emoji 候选", AutoSize = true, Checked = true };
        _toneCheckBox = new CheckBox { Text = "声调显示", AutoSize = true, Checked = true };
        _enableUserDictCheckBox = new CheckBox { Text = "输入学习", AutoSize = true, Checked = true };
        _fuzzyCheckBox = new CheckBox { Text = "启用模糊音", AutoSize = true, Checked = false };
        _fuzzyRulesGrid = CreateFuzzyRuleGrid();
        _fuzzyRulesGrid.DataSource = _fuzzyRules;

        _dayThemeComboBox = CreateComboBox(["蓝水鸭", "碧皓青", "黑水鸭", "碧月青", "自定义"]);
        _nightThemeComboBox = CreateComboBox(["黑水鸭", "碧月青", "蓝水鸭", "碧皓青", "自定义"]);
        _fontTextBox = new TextBox { Width = 420, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, MinimumSize = new Size(0, 120), Anchor = AnchorStyles.Left, Text = string.Empty };
        _fontSizeText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _statusNotificationCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _candidateCountNumeric = CreateNumeric(1, 20, 5);
        _candidateDirectionComboBox = CreateComboBox(["竖排", "横排"]);
        _candidateCommentCheckBox = new CheckBox { Text = "显示", AutoSize = true, Checked = true };

        _ueCompatCheckBox = new CheckBox { Text = "开启", AutoSize = true, Checked = false };
        _customPhraseComboBox = CreateComboBox(["关闭", "简码匹配", "完整短语"]);
        _symbolProfileComboBox = CreateComboBox(["默认符号配置"]);
        _symbolProfileComboBox.Enabled = false;
        _preeditFormatComboBox = CreateComboBox(["保留当前", "原始编码", "翻译编码"]);
        _labelFontTextBox = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _labelFontSizeText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _commentFontTextBox = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _commentFontSizeText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _notificationTimeText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _labelFormatTextBox = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = "%s" };
        _markTextTextBox = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _pagingOnScrollCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _candidateAbbreviateText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _commentStyleComboBox = CreateComboBox(["显示所有注释", "不显示", "中文", "拉丁", "混合"]);
        _fullscreenCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _verticalTextCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _verticalTextLeftToRightCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _verticalTextWithWrapCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _verticalAutoReverseCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _inlinePreeditCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _preeditTypeComboBox = CreateComboBox(["编码区模式", "预览区模式", "预览全部"]);
        _globalAsciiComboBox = CreateComboBox(["每窗口独立", "全局同步"]);
        _hoverTypeComboBox = CreateComboBox(["无效果", "半高亮", "高亮"]);
        _clickToCaptureCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _antialiasModeComboBox = CreateComboBox(["系统默认", "ClearType", "灰度", "无"]);
        _displayTrayIconCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _enhancedPositionCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _asciiTipFollowCursorCheckBox = new CheckBox { Text = "开启", AutoSize = true };
        _layoutMinWidthText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutMinHeightText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutMaxWidthText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutMaxHeightText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutMarginXText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutMarginYText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutBorderWidthText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutLineSpacingText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutBaselineText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutSpacingText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutCandidateSpacingText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutHiliteSpacingText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutHilitePaddingText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutHilitePaddingXText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutHilitePaddingYText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutShadowRadiusText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutShadowOffsetXText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutShadowOffsetYText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutCornerRadiusText = new TextBox { Width = 420, Anchor = AnchorStyles.Left, Text = string.Empty };
        _layoutAlignTypeComboBox = CreateComboBox(["默认", "居上", "居中", "居下"]);

        _textColorField = CreateColorField();
        _candidateTextColorField = CreateColorField();
        _labelColorField = CreateColorField();
        _commentTextColorField = CreateColorField();
        _backColorField = CreateColorField();
        _candidateBackColorField = CreateColorField();
        _borderColorField = CreateColorField();
        _shadowColorField = CreateColorField();
        _hilitedTextColorField = CreateColorField();
        _hilitedBackColorField = CreateColorField();
        _hilitedLabelColorField = CreateColorField();
        _hilitedCandidateTextColorField = CreateColorField();
        _hilitedCandidateBackColorField = CreateColorField();
        _hilitedCandidateLabelColorField = CreateColorField();
        _hilitedCandidateBorderColorField = CreateColorField();
        _hilitedCommentTextColorField = CreateColorField();
        _hilitedMarkColorField = CreateColorField();
        _editDayRadio = new RadioButton { Text = "浅色主题配色", AutoSize = true, Checked = true };
        _editNightRadio = new RadioButton { Text = "深色主题配色", AutoSize = true };
        _editDayRadio.CheckedChanged += (_, _) => { if (_editDayRadio.Checked) LoadColorFields(); };
        _editNightRadio.CheckedChanged += (_, _) => { if (_editNightRadio.Checked) LoadColorFields(); };

        _dayThemeComboBox.SelectedIndexChanged += (_, _) => OnThemeComboChanged(isDay: true);
        _nightThemeComboBox.SelectedIndexChanged += (_, _) => OnThemeComboChanged(isDay: false);

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16, 12, 16, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        TabControl mainTabs = new()
        {
            Dock = DockStyle.Fill,
            Multiline = false,
            ItemSize = new Size(140, 34),
            SizeMode = TabSizeMode.Fixed,
        };
        mainTabs.TabPages.Add(CreateTabPage("承载器", CreateCarrierPage()));
        mainTabs.TabPages.Add(CreateTabPage("输入方案", CreateSchemePage()));
        mainTabs.TabPages.Add(CreateTabPage("词库", CreateDictionaryPage()));
        mainTabs.TabPages.Add(CreateTabPage("语法模型", CreateModelPage()));
        mainTabs.TabPages.Add(CreateTabPage("输入设置", CreateInputSettingsPage()));
        mainTabs.TabPages.Add(CreateTabPage("同步", CreateSyncPage()));
        root.Controls.Add(mainTabs, 0, 0);

        _statusLabel = new Label
        {
            Name = "_statusLabel",
            AutoSize = false,
            Dock = DockStyle.Top,
            Text = string.Empty,
            Margin = new Padding(0),
            Padding = new Padding(4, 1, 4, 1),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            MinimumSize = new Size(0, 0),
        };
        root.Controls.Add(_statusLabel, 0, 1);

        ConfigModel initialModel = GetEditingModel();
        if (!File.Exists(_configModelPath))
        {
            _workflowService.RunSaveConfig(_configModelPath, initialModel, "text");
        }
        LoadConfigIntoControls(initialModel);
        RenderDictionaryDetail();
        RenderModelDetail();
        RefreshSyncAndBackupStatus();
        ApplyFontRecursively(this, Font);
        _settingsControls.AddRange(new Control?[]
        {
            _simplifiedRadio, _traditionalRadio, _halfShapeRadio, _fullShapeRadio,
            _asciiPunctCheckBox, _emojiCheckBox, _toneCheckBox, _enableUserDictCheckBox,
            _ueCompatCheckBox,
            _customPhraseComboBox, _symbolProfileComboBox, _preeditFormatComboBox,
            _fuzzyCheckBox, _fuzzyRulesGrid,
            _dayThemeComboBox, _nightThemeComboBox,
            _fontTextBox, _fontSizeText,
            _labelFontTextBox, _labelFontSizeText,
            _commentFontTextBox, _commentFontSizeText,
            _statusNotificationCheckBox, _notificationTimeText,
            _labelFormatTextBox, _markTextTextBox,
            _pagingOnScrollCheckBox, _candidateAbbreviateText,
            _candidateCountNumeric, _candidateDirectionComboBox,
            _candidateCommentCheckBox, _commentStyleComboBox,
            _fullscreenCheckBox, _verticalTextCheckBox,
            _verticalTextLeftToRightCheckBox, _verticalTextWithWrapCheckBox, _verticalAutoReverseCheckBox,
            _inlinePreeditCheckBox, _preeditTypeComboBox,
            _globalAsciiComboBox, _hoverTypeComboBox, _clickToCaptureCheckBox,
            _antialiasModeComboBox, _displayTrayIconCheckBox, _enhancedPositionCheckBox,
            _asciiTipFollowCursorCheckBox,
            _layoutMinWidthText, _layoutMinHeightText, _layoutMaxWidthText, _layoutMaxHeightText,
            _layoutMarginXText, _layoutMarginYText, _layoutBorderWidthText,
            _layoutLineSpacingText, _layoutBaselineText, _layoutSpacingText,
            _layoutCandidateSpacingText, _layoutHiliteSpacingText, _layoutHilitePaddingText,
            _layoutHilitePaddingXText, _layoutHilitePaddingYText,
            _layoutShadowRadiusText, _layoutShadowOffsetXText, _layoutShadowOffsetYText,
            _layoutCornerRadiusText, _layoutAlignTypeComboBox,
            _textColorField, _candidateTextColorField, _labelColorField, _commentTextColorField,
            _backColorField, _candidateBackColorField, _borderColorField, _shadowColorField,
            _hilitedTextColorField, _hilitedBackColorField, _hilitedLabelColorField,
            _hilitedCandidateTextColorField, _hilitedCandidateBackColorField, _hilitedCandidateLabelColorField,
            _hilitedCandidateBorderColorField, _hilitedCommentTextColorField, _hilitedMarkColorField,

        }.OfType<Control>());
        foreach (Button btn in _fuzzyActionButtons) { if (btn is not null) _settingsControls.Add(btn); }
        if (!TemplateService.TemplatesAreAvailable())
        {
            _templatesAreMissing = true;
        }
        ResolveAnnotationPlaceholders();
        RefreshCarrierDependentUi();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_workflowService.HasPendingWeaselOperation() && !_isRechecking)
        {
            _isRechecking = true;
            _ = RunWorkflowOperationAsync(
                "正在自动回检承载器状态…",
                _ => CreateDetectionProbeResult(_workflowService.BuildWindowsCarrierStateView(_configModelPath)),
                _ =>
                {
                    _carrierStatusDetected = true;
                    RenderCarrierInfo();
                    RefreshCarrierDependentUi();
                }).ContinueWith(
                t =>
                {
                    _isRechecking = false;
                    if (t.IsFaulted && t.Exception is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Gui] OnActivated auto-recheck failed: {t.Exception.InnerException?.Message}");
                    }
                },
                TaskScheduler.Default);
        }
    }

    private Control CreateCarrierPage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel actions = CreateActionBar();
        _carrierDetectBtn = CreateInlineButton("检测承载器状态", async (_, _) =>
        {
            await RunWorkflowOperationAsync(
                "正在检测承载器状态…",
                _ =>
                {
                    var detail = _workflowService.BuildWindowsCarrierStateView(_configModelPath);
                    _workflowService.RunDoctor(_configModelPath, "text");
                    return CreateDetectionProbeResult(detail);
                },
                _ =>
                {
                    _carrierStatusDetected = true;
                    RenderCarrierInfo();
                    RefreshCarrierDependentUi();
                });
        }, name: "BtnCarrierDetect");
        actions.Controls.Add(_carrierDetectBtn);
        actions.Controls.Add(CreateInlineButton("下载并安装小狼毫", async (_, _) =>
            await RunWorkflowOperationAsync(
                "正在获取小狼毫下载地址…",
                phase =>
                {
                    var r = _workflowService.RunDownloadAndLaunchWeaselInstaller("text", phase);
                    if (r.ExitCode == 0) phase("安装完成，正在确认…");
                    return r;
                },
                _ =>
                {
                    _carrierStatusDetected = true;
                    RenderCarrierInfo();
                    RefreshCarrierDependentUi();
                })));
        actions.Controls.Add(CreateInlineButton("从文件安装", async (_, _) =>
        {
            using OpenFileDialog dlg = new()
            {
                Filter = "小狼毫安装程序 (*.exe)|*.exe",
                Title = "选择小狼毫安装程序",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            await RunWorkflowOperationAsync(
                "正在从文件安装小狼毫…",
                phase =>
                {
                    var r = _workflowService.RunLaunchWeaselInstallerFromFile(dlg.FileName, "text", phase: phase);
                    if (r.ExitCode == 0) phase("安装完成，正在确认…");
                    return r;
                },
                _ =>
                {
                    _carrierStatusDetected = true;
                    RenderCarrierInfo();
                    RefreshCarrierDependentUi();
                });
        }));
        Button weaselUninstallBtn = CreateInlineButton("卸载小狼毫", async (_, _) =>
            await RunWorkflowOperationAsync(
                "正在定位小狼毫卸载工具…",
                phase =>
                {
                    var r = _workflowService.RunLaunchWeaselUninstaller("text", phase: phase);
                    if (r.ExitCode == 0) phase("卸载完成，正在清理…");
                    return r;
                },
                r =>
                {
                    _carrierStatusDetected = true;
                    RenderCarrierInfo();
                    RefreshCarrierDependentUi();
                    if (r.JsonPayload is System.Text.Json.JsonElement payload
                        && payload.TryGetProperty("cleanup_warning", out System.Text.Json.JsonElement warning)
                        && !string.IsNullOrWhiteSpace(warning.GetString()))
                    {
                        _statusLabel.ForeColor = Color.Red;
                        _statusLabel.Text = $"⚠ {warning.GetString()}";
                    }
                }));
        actions.Controls.Add(weaselUninstallBtn);
        RegisterCarrierDependent(weaselUninstallBtn);
        Button uninstallAllBtn = CreateInlineButton("完全清理并卸载Rime", async (_, _) =>
            await RunWorkflowOperationAsync(
                "正在清理已安装资源…",
                phase =>
                {
                var r = _workflowService.RunUninstallAll(_configModelPath, "text", phase: phase);
                if (r.ExitCode == 0) phase("清理完成，正在确认…");
                    return r;
                },
                r =>
                {
                    _carrierStatusDetected = true;
                    RenderCarrierInfo();
                    RefreshCarrierDependentUi();
                    if (r.JsonPayload is System.Text.Json.JsonElement payload
                        && payload.TryGetProperty("errors", out System.Text.Json.JsonElement errors)
                        && errors.ValueKind == System.Text.Json.JsonValueKind.Array
                        && errors.GetArrayLength() > 0)
                    {
                        _statusLabel.ForeColor = Color.Red;
                        _statusLabel.Text = "清理残留文件失败，请手动检查输入法目录。";
                    }
                }));
        actions.Controls.Add(uninstallAllBtn);
        layout.Controls.Add(actions, 0, 0);

        layout.Controls.Add(_carrierInfoHost, 0, 1);
        RenderCarrierInfo();
        return layout;
    }

    private void RenderCarrierInfo()
    {
        _carrierInfoHost.Controls.Clear();
        if (!_carrierStatusDetected)
        {
            return;
        }

        string carrierView = _workflowService.BuildWindowsCarrierStateView(_configModelPath);
        TableLayoutPanel grid = CreateKeyValueGrid(
        [
            ("小狼毫本体", ExtractViewValue(carrierView, "小狼毫本体", "未检测到")),
            ("本地小狼毫版本", ExtractViewValue(carrierView, "当前小狼毫版本", "未检测到")),
            ("系统默认输入法", ExtractViewValue(carrierView, "系统默认输入法覆盖", "未检测到")),
            ("部署工具位置", ExtractViewValue(carrierView, "部署工具位置", "未检测到")),
            ("卸载工具位置", ExtractViewValue(carrierView, "卸载工具位置", "未检测到")),
        ]);
        grid.Padding = new Padding(0, 8, 0, 0);
        TableLayoutPanel linkRow = new() { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.Controls.Add(CreateFieldLabel("官方主页"), 0, 0);
        linkRow.Controls.Add(CreateLinkLabel("https://rime.im/download/", "https://rime.im/download/"), 1, 0);
        _carrierInfoHost.Controls.Add(linkRow);
        _carrierInfoHost.Controls.Add(grid);
    }

    private Control CreateDictionaryPage()
    {
        return CreateSelectorPage(CreateDictionarySelectorPanel(), _dictionaryDetailPanel);
    }

    private Control CreateDictionarySelectorPanel()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel actions = CreateActionBar();
        _dictDetectLocalBtn = CreateInlineButton("检测本地词库", async (_, _) =>
        {
            await RunWorkflowOperationAsync(
                "正在检测本地词库…",
                _ => CreateDetectionProbeResult(_workflowService.BuildInstalledResourceStateView()),
                _ =>
                {
                    LoadDetectedDictionaries();
                    RenderDictionaryDetail();
                });
        });
        actions.Controls.Add(_dictDetectLocalBtn);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(_dictionaryListBox, 0, 1);
        return layout;
    }

    private void RenderDictionaryDetail()
    {
        _dictionaryDetailPanel.Controls.Clear();

        string? selected = _dictionaryListBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected))
        {
            _dictionaryDetailPanel.Controls.Add(CreatePlaceholderLabel("请先在左侧选择一个词库。"));
            return;
        }

        if (string.Equals(selected, "用户词条", StringComparison.Ordinal))
        {
            _dictionaryDetailPanel.Controls.Add(CreateUserEntriesPanel());
            return;
        }

        _dictionaryDetailPanel.Controls.Add(CreateDictionaryActionPanel(selected, string.Equals(_detectedDictionaryStatusName, selected, StringComparison.Ordinal)));
    }

    private Control CreateDictionaryActionPanel(string dictionaryName, bool showStatus)
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = showStatus ? 2 : 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (showStatus)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        FlowLayoutPanel actions = CreateActionBar();
        string? dictionaryId = ResolveDictionaryId(dictionaryName);
        _dictDetectStatusBtn = CreateInlineButton("检测词库状态", async (_, _) =>
        {
            await RunWorkflowOperationAsync(
                $"正在检测 {dictionaryName} 状态…",
                _ =>
                {
                    var detail = _workflowService.BuildInstalledResourceStateView();
                    _workflowService.RunDoctor(_configModelPath, "text");
                    return CreateDetectionProbeResult(detail);
                },
                _ =>
                {
                    _detectedDictionaryStatusName = dictionaryName;
                    RenderDictionaryDetail();
                });
        });
        actions.Controls.Add(_dictDetectStatusBtn);
        ConfigModel model = GetEditingModel();
        bool installed = dictionaryId switch
        {
            "rime_mint" => _workflowService.GetInstalledSchemaIds().Contains("rime_mint") && AreRimeMintRuntimeFilesPresent(model),
            _ => !string.IsNullOrWhiteSpace(dictionaryId) && _workflowService.GetInstalledDictionaryIds().Contains(dictionaryId),
        };

        _dictInstallBtn = CreateInlineButton("下载并部署词库", async (_, _) =>
            await InstallOrUpdateFormalResourceAsync(dictionaryName, $"正在下载并部署 {dictionaryName}…"));
        actions.Controls.Add(_dictInstallBtn);
        Button dictInstallFromFileBtn = CreateInlineButton("从文件安装", async (_, _) =>
        {
            using OpenFileDialog dlg = new()
            {
                Filter = "词库文件 (*.dict.yaml;*.zip;*.scel)|*.dict.yaml;*.zip;*.scel",
                Title = $"选择 {dictionaryName} 安装文件",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            await InstallOrUpdateFormalResourceAsync(dictionaryName, $"正在从文件安装 {dictionaryName}…", dlg.FileName);
        });
        _dictInstallFromFileBtn = dictInstallFromFileBtn;
        actions.Controls.Add(dictInstallFromFileBtn);
        _dictUninstallBtn = CreateInlineButton("卸载词库", async (_, _) =>
            await UninstallFormalResourceAsync(dictionaryName));
        actions.Controls.Add(_dictUninstallBtn);
        bool carrierAvailable = IsCarrierAvailable();
        bool schemeAvailable = IsRimeMintSchemeAvailable();
        _dictDetectStatusBtn.Enabled = carrierAvailable;
        _dictInstallBtn.Enabled = carrierAvailable;
        if (_dictInstallFromFileBtn is not null && !_dictInstallFromFileBtn.IsDisposed) _dictInstallFromFileBtn.Enabled = carrierAvailable;
        _dictUninstallBtn.Enabled = carrierAvailable && schemeAvailable && installed;
        layout.Controls.Add(actions, 0, 0);
        if (showStatus)
        {
            bool enabled = dictionaryId switch
            {
                "rime_mint" => model.ProfileSettings.EnabledSchemaIds.Contains("rime_mint", StringComparer.OrdinalIgnoreCase) &&
                               string.Equals(model.ProfileSettings.WindowsDefaultSchemaId, "rime_mint", StringComparison.OrdinalIgnoreCase),
                _ => !string.IsNullOrWhiteSpace(dictionaryId) &&
                     model.DictionarySettings.EnabledDictionaryIds.Contains(dictionaryId, StringComparer.OrdinalIgnoreCase),
            };
            layout.Controls.Add(CreateKeyValueGrid(
            [
                ("当前词库", dictionaryName),
                ("词库状态", BuildDictionaryStateLabel(model, dictionaryId, installed, enabled)),
            ]), 0, 1);
            string? dictHomepage = ResolveDictionaryHomepage(dictionaryName);
            if (dictHomepage is not null)
            {
                TableLayoutPanel linkRow = new() { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
                linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
                linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                linkRow.Controls.Add(CreateFieldLabel("官方主页"), 0, 0);
                linkRow.Controls.Add(CreateLinkLabel(dictHomepage, dictHomepage), 1, 0);
                layout.RowCount++;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(linkRow, 0, layout.RowCount - 1);
            }
        }
        return layout;
    }

    private Control CreateUserEntriesPanel()
    {
        if (!IsCarrierAvailable())
        {
            TableLayoutPanel unavailable = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };
            unavailable.Controls.Add(CreatePlaceholderLabel("承载器未安装，无法查看或编辑用户词条。"));
            return unavailable;
        }

        bool schemeAvailable = IsRimeMintSchemeAvailable();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel detectActions = CreateActionBar();
        detectActions.Controls.Add(CreateInlineButton("检测目前用户词条", async (_, _) =>
        {
            await RunWorkflowOperationAsync(
                "正在检测目前用户词条…",
                _ => CreateDetectionProbeResult(_workflowService.BuildUserDataStateView(_configModelPath)),
                _ =>
                {
                    _userEntriesDetected = true;
                    LoadDetectedUserEntries();
                    RenderDictionaryDetail();
                });
        }));
        layout.Controls.Add(detectActions, 0, 0);

        if (_userEntriesDetected)
        {
            FlowLayoutPanel header = CreateActionBar();
            header.Controls.Add(CreateSectionLabel("用户词条"));
            DataGridView mergedGrid = CreateEntryGrid(readOnly: false);
            mergedGrid.DataSource = _userEntries;
            header.Controls.Add(CreateInlineButton("新增词条", (_, _) => _userEntries.Add(new UserEntryRow("新词条", "xinci", "待保存"))));
            header.Controls.Add(CreateInlineButton("删除词条", (_, _) =>
            {
                if (mergedGrid.CurrentRow?.DataBoundItem is UserEntryRow row)
                {
                    _userEntries.Remove(row);
                }
            }));
            Button importBtn = CreateInlineButton("导入词条", (_, _) =>
            {
                using OpenFileDialog dlg = new() { Filter = "CSV 文件 (*.csv)|*.csv", Title = "导入用户词条" };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (string line in File.ReadAllLines(dlg.FileName, System.Text.Encoding.UTF8))
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        string[] parts = trimmed.Split(',', 2);
                        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                            _userEntries.Add(new UserEntryRow(parts[0].Trim(), parts[1].Trim(), "已导入"));
                    }
                }
            });
            header.Controls.Add(importBtn);
            header.Controls.Add(new Label { Text = "无需表头，每行一条。格式：词语,编码。例如：你好,nh", AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText, Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9F) });
            Button applyBtn = CreateInlineButton("应用用户词条", async (_, _) => await ApplyUserEntriesAsync());
            header.Controls.Add(applyBtn);
            applyBtn.Enabled = schemeAvailable;
            layout.Controls.Add(header, 0, 1);
            layout.Controls.Add(mergedGrid, 0, 2);
        }
        else
        {
            layout.Controls.Add(CreatePlainLabel("请先检测目前用户词条，再查看和编辑当前词条。"), 0, 1);
        }
        return layout;
    }

    private Control CreateModelPage()
    {
        return CreateSelectorPage(CreateModelSelectorPanel(), _modelDetailPanel);
    }

    private Control CreateModelSelectorPanel()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel actions = CreateActionBar();
        _modelDetectLocalBtn = CreateInlineButton("检测本地语法模型", async (_, _) =>
        {
            await RunWorkflowOperationAsync(
                "正在检测本地语法模型…",
                _ => CreateDetectionProbeResult(_workflowService.BuildModelInstallStateView()),
                _ =>
                {
                    LoadDetectedModels();
                    RenderModelDetail();
                });
        });
        actions.Controls.Add(_modelDetectLocalBtn);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(_modelListBox, 0, 1);
        return layout;
    }

    private void RenderModelDetail()
    {
        _modelDetailPanel.Controls.Clear();

        string? selected = _modelListBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected))
        {
            _modelDetailPanel.Controls.Add(CreatePlaceholderLabel("请先在左侧选择一个语法模型。"));
            return;
        }

        _modelDetailPanel.Controls.Add(CreateModelActionPanel(selected, string.Equals(_detectedModelStatusName, selected, StringComparison.Ordinal)));
    }

    private Control CreateModelActionPanel(string modelName, bool showStatus)
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = showStatus ? 2 : 1,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (showStatus)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        FlowLayoutPanel actions = CreateActionBar();
        string? _modelId = ResolveModelId(modelName);
        _modelDetectStatusBtn = CreateInlineButton("检测语法模型状态", async (_, _) =>
        {
            await RunWorkflowOperationAsync(
                $"正在检测 {modelName} 状态…",
                _ =>
                {
                    var detail = _workflowService.BuildModelInstallStateView();
                    _workflowService.RunDoctor(_configModelPath, "text");
                    return CreateDetectionProbeResult(detail);
                },
                _ =>
                {
                    _detectedModelStatusName = modelName;
                    RenderModelDetail();
                });
        });
        actions.Controls.Add(_modelDetectStatusBtn);
        ConfigModel _mdl = GetEditingModel();
        bool modelInstalled = !string.IsNullOrWhiteSpace(_modelId) && _workflowService.GetInstalledModelIds().Contains(_modelId);

        _modelInstallBtn = CreateInlineButton("下载并部署语法模型", async (_, _) =>
            await InstallOrUpdateFormalResourceAsync(modelName, $"正在下载并部署 {modelName}…"));
        actions.Controls.Add(_modelInstallBtn);
        Button modelInstallFromFileBtn = CreateInlineButton("从文件安装", async (_, _) =>
        {
            using OpenFileDialog dlg = new()
            {
                Filter = "语法模型 (*.gram)|*.gram",
                Title = "选择语法模型文件",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            await InstallOrUpdateFormalResourceAsync(modelName, $"正在从文件安装 {modelName}…", dlg.FileName);
        });
        _modelInstallFromFileBtn = modelInstallFromFileBtn;
        actions.Controls.Add(modelInstallFromFileBtn);
        _modelUninstallBtn = CreateInlineButton("卸载语法模型", async (_, _) =>
            await UninstallFormalResourceAsync(modelName));
        actions.Controls.Add(_modelUninstallBtn);
        bool carrierAvailable = IsCarrierAvailable();
        bool schemeAvailable = IsRimeMintSchemeAvailable();
        _modelDetectStatusBtn.Enabled = carrierAvailable;
        _modelInstallBtn.Enabled = carrierAvailable;
        if (_modelInstallFromFileBtn is not null && !_modelInstallFromFileBtn.IsDisposed) _modelInstallFromFileBtn.Enabled = carrierAvailable;
        _modelUninstallBtn.Enabled = carrierAvailable && schemeAvailable && modelInstalled;
        layout.Controls.Add(actions, 0, 0);
        if (showStatus)
        {
            string? modelId = _modelId;
            ConfigModel model = _mdl;
            bool installed = modelInstalled;
            bool enabled = !string.IsNullOrWhiteSpace(modelId) &&
                           model.ModelSettings.EnabledModelIds.Contains(modelId, StringComparer.OrdinalIgnoreCase);
            layout.Controls.Add(CreateKeyValueGrid(
            [
                ("当前语法模型", modelName),
                ("模型状态", BuildModelStateLabel(model, modelId, installed, enabled)),
            ]), 0, 1);
            string modelHomepage = "https://github.com/amzxyz/RIME-LMDG/releases";
            TableLayoutPanel linkRow = new() { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
            linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            linkRow.Controls.Add(CreateFieldLabel("官方主页"), 0, 0);
            linkRow.Controls.Add(CreateLinkLabel(modelHomepage, modelHomepage), 1, 0);
            layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(linkRow, 0, layout.RowCount - 1);
        }
        return layout;
    }

    private static TabPage CreateScrollableSubTabPage(string title, Control content)
    {
        TabPage page = new()
        {
            Text = title,
            Padding = new Padding(8),
            AutoScroll = false,
        };
        Panel scrollHost = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };
        scrollHost.Controls.Add(content);
        page.Controls.Add(scrollHost);
        return page;
    }

    private Control CreateInputSettingsPage()
    {
        TabControl tabs = new()
        {
            Name = "_inputSettingsTabControl",
            Dock = DockStyle.Fill,
            Multiline = false,
            ItemSize = new Size(110, 30),
            SizeMode = TabSizeMode.Fixed,
        };
        tabs.TabPages.Add(CreateScrollableSubTabPage("显示", CreateDisplayPage()));
        tabs.TabPages.Add(CreateScrollableSubTabPage("输入", CreateInputPage()));
        tabs.TabPages.Add(CreateScrollableSubTabPage("窗口", CreateWindowPage()));
        tabs.TabPages.Add(CreateScrollableSubTabPage("布局", CreateLayoutPage()));
        tabs.TabPages.Add(CreateScrollableSubTabPage("配色", CreateColorPage()));
        return tabs;
    }

    private Control CreateSchemePage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel actions = CreateActionBar();
        _schemeDetectBtn = CreateInlineButton("检测输入方案状态", async (_, _) =>
        {
            await RunWorkflowOperationAsync(
                "正在检测输入方案状态…",
                _ =>
                {
                    var detail = _workflowService.BuildInputSchemeStateView(_configModelPath);
                    _workflowService.RunDoctor(_configModelPath, "text");
                    return CreateDetectionProbeResult(detail);
                },
                _ =>
                {
                    _inputSchemeDetected = true;
                    RenderSchemeInfo();
                });
        });
        actions.Controls.Add(_schemeDetectBtn);
        _schemeInstallBtn = CreateInlineButton("下载并安装输入方案", async (_, _) =>
            await InstallOrUpdateFormalResourceAsync(GetSelectedSchemeDisplayName(), "正在下载并安装输入方案…"));
        actions.Controls.Add(_schemeInstallBtn);
        Button schemeInstallFromFileBtn = CreateInlineButton("从文件安装", async (_, _) =>
        {
            using OpenFileDialog dlg = new()
            {
                Filter = "方案压缩包 (*.zip)|*.zip",
                Title = "选择输入方案安装文件",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            await InstallOrUpdateFormalResourceAsync(GetSelectedSchemeDisplayName(), "正在从文件安装输入方案…", dlg.FileName);
        });
        actions.Controls.Add(schemeInstallFromFileBtn);
        _schemeUninstallBtn = CreateInlineButton("卸载输入方案", async (_, _) =>
            await UninstallFormalResourceAsync(GetSelectedSchemeDisplayName()));
        actions.Controls.Add(_schemeUninstallBtn);
        RegisterCarrierDependent(_schemeInstallBtn);
        RegisterCarrierDependent(_schemeUninstallBtn);
        RegisterCarrierDependent(schemeInstallFromFileBtn);
        _schemeComboBox.SelectedIndexChanged += (_, _) => RefreshSchemeButtonCapability();
        layout.Controls.Add(actions, 0, 0);

        layout.Controls.Add(
            CreateFieldGrid(
            [
                ("选择输入方案", _schemeComboBox),
            ]),
            0,
            1);
        layout.Controls.Add(_schemeInfoHost, 0, 2);
        RenderSchemeInfo();
        return layout;
    }

    private void RefreshSchemeButtonCapability()
    {
        bool carrierAvailable = IsCarrierAvailable();
        bool isFormal = _workflowService.IsFormalManagedSchema(ResolveSelectedSchemeId() ?? string.Empty);
        bool schemeInstalled = IsSelectedSchemeInstalled();
        if (_schemeInstallBtn is not null)
            _schemeInstallBtn.Enabled = carrierAvailable && isFormal;
        if (_schemeUninstallBtn is not null)
            _schemeUninstallBtn.Enabled = carrierAvailable && isFormal && schemeInstalled;
    }

    private bool IsSelectedSchemeInstalled()
    {
        string? schemaId = ResolveSelectedSchemeId();
        if (string.IsNullOrWhiteSpace(schemaId))
        {
            return false;
        }

        if (_workflowService.IsFormalManagedSchema(schemaId))
        {
            return _workflowService.GetInstalledSchemaIds().Contains(schemaId);
        }

        return true;
    }

    private Control CreateInputPage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        (string, string, Control)[] schemaDefs =
        [
            ("简体 / 繁体", "选择输入文字为简体还是繁体。默认为{SimplificationMode}。", CreateVerticalRadioGroup(_simplifiedRadio, _traditionalRadio)),
            ("半角 / 全角", "选择字符为半角还是全角形态。全角时 ASCII 字符转为全角对应字符（如 123 变为 １２３）。默认为{FullShape}。", CreateVerticalRadioGroup(_halfShapeRadio, _fullShapeRadio)),
            ("英文标点", "开启后在中文模式下输出英文标点（. , ! ?），关闭后输出中文标点（。，！？）。全角模式下全角优先于英文标点，全角开启后标点仍强制转为全角形式。默认为{AsciiPunct}。", _asciiPunctCheckBox),
            ("Emoji 候选", "开启后在候选列表中显示 emoji 符号（如输入 kaixin 时出现 😄）。关闭后仅显示文字候选。默认为{EmojiSuggestion}。", _emojiCheckBox),
            ("声调显示", "开启后在拼音输入区域显示出带声调的拼音（如 nǐ hǎo）。仅影响打字区拼音显示，不影响候选窗里候选字的声调注释。默认为{ToneDisplay}。", _toneCheckBox),
            ("输入学习", "控制输入法是否根据您的输入习惯自动学习并调整候选词排序。开启后，您常用的词语会逐渐排在前面；关闭后，候选词顺序保持不变，此时输入法不会记录您的选词偏好。默认为{EnableUserDict}。", _enableUserDictCheckBox),
        ];

        (string, string, Control)[] rimekitDefs =
        [
            ("üe 兼容", "控制 nüe/lüe 的输入方式。开启后输入 nue 得到 nüe、输入 lue 得到 lüe，与搜狗、微软拼音等主流输入法一致。关闭后须使用 v 键输入 ü（如 nve 得到 nüe）。默认为关闭。", _ueCompatCheckBox),
            ("自定义短语", "控制薄荷方案的简拼翻译器。可选：简码匹配（需输全编码，如 wm → 我们）、完整短语（可输部分编码）、关闭。默认为关闭。注意：选择关闭以外的选项后，需要手动添加简拼词条，RimeKit 当前无此功能。", _customPhraseComboBox),
            ("符号配置", "选择中文标点和特殊符号的配置方案。符号配置文件由薄荷方案提供，包含全角半角符号映射和特殊符号快捷输入（如 /jt 得到箭头、/sx 得到数学符号）。仅支持内置默认配置，不支持自定义。", _symbolProfileComboBox),
            ("预编辑格式", "控制拼音输入时预编辑文本的显示格式。可选保留当前、原始编码、翻译编码三种模式。原始编码和翻译编码为功能预留，仅保留当前可生效。默认为保留当前（保留输入方案内置的预编辑格式）。", _preeditFormatComboBox),
            ("模糊音", "开启后启用模糊音匹配规则，下方表格中的规则才会生效。例如 zh→z 规则使输入 zisi 也能匹配到「只是」。默认为关闭。", _fuzzyCheckBox),
        ];

        layout.Controls.Add(CreateSettingsGroupBox("输入方案选项", schemaDefs, columns: 3), 0, 0);
        layout.Controls.Add(CreateSettingsGroupBox("rimekit选项", rimekitDefs, columns: 3), 0, 1);

        TableLayoutPanel fuzzyPanel = new()
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 3,
            MinimumSize = new Size(0, 300),
            Height = 350,
        };
        fuzzyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fuzzyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fuzzyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fuzzyPanel.Controls.Add(CreateSectionLabel("模糊音规则"), 0, 0);
        _fuzzyRulesGrid.Dock = DockStyle.Fill;
        _fuzzyRulesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        fuzzyPanel.Controls.Add(_fuzzyRulesGrid, 0, 1);
        FlowLayoutPanel fuzzyActions = CreateActionBar();
        fuzzyActions.Margin = new Padding(0, 4, 0, 0);
        Button presetBtn = CreateInlineButton("默认模糊音规则", (_, _) =>
        {
            _fuzzyRules.Clear();
            _fuzzyRules.Add(new FuzzyRuleRow("zh", "z"));
            _fuzzyRules.Add(new FuzzyRuleRow("ch", "c"));
            _fuzzyRules.Add(new FuzzyRuleRow("sh", "s"));
        });
        Button addBtn = CreateInlineButton("添加规则", (_, _) => _fuzzyRules.Add(new FuzzyRuleRow("n", "l")));
        Button delBtn = CreateInlineButton("删除规则", (_, _) =>
        {
            if (_fuzzyRulesGrid.CurrentRow?.DataBoundItem is FuzzyRuleRow row)
            {
                _fuzzyRules.Remove(row);
            }
        });
        Button importRulesBtn = CreateInlineButton("导入规则", (_, _) =>
        {
            using OpenFileDialog dlg = new() { Filter = "CSV 文件 (*.csv)|*.csv", Title = "导入模糊音规则" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                foreach (string line in File.ReadAllLines(dlg.FileName, System.Text.Encoding.UTF8))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    string[] parts = trimmed.Split(',', 2);
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                        _fuzzyRules.Add(new FuzzyRuleRow(parts[0].Trim(), parts[1].Trim()));
                }
            }
        });
        _fuzzyActionButtons = [presetBtn, addBtn, delBtn, importRulesBtn];
        fuzzyActions.Controls.Add(presetBtn);
        fuzzyActions.Controls.Add(addBtn);
        fuzzyActions.Controls.Add(delBtn);
        fuzzyActions.Controls.Add(importRulesBtn);
        fuzzyActions.Controls.Add(new Label { Text = "无需表头，每行一条。格式：原拼音,模糊拼音。例如：zh,z", AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText, Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9F) });
        fuzzyPanel.Controls.Add(fuzzyActions, 0, 2);
        layout.Controls.Add(fuzzyPanel, 0, 2);

        layout.Controls.Add(CreateBottomSettingsActionBar("输入设置"), 0, 3);
        return layout;
    }

    private Control CreateDisplayPage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TableLayoutPanel innerGrid = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
        };
        innerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        innerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        innerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        innerGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        (string, string, Control)[] carrierDefs =
        [
            ("候选词字体", "控制候选窗中候选词文字的字体。Weasel 支持多字体分段渲染：字体名后可带 :起始码位:结束码位 指定该字体渲染哪些字符。可输入任意已安装的字体名覆盖默认设置（只需写字体名，无需写完整字体栈）。默认为{FontFace}。\n以上默认值的具体含义：\n  数字 0-9 → Segoe UI Emoji\n  符号 #* → Segoe UI Emoji\n  Emoji 修饰符 → Segoe UI Emoji\n  中文/英文正文 → Microsoft YaHei\n  备用 → SF Pro\n  Emoji 回退 → Segoe UI Emoji、Noto Color Emoji", _fontTextBox),
            ("候选词字号", "候选窗中候选词文字的字号大小，单位 pt。输入整数，留空表示使用模板默认值。默认为{FontPoint}。", _fontSizeText),
            ("标签字体", "候选窗中序号（如 1. 2. 3.）的字体名称。与候选词字体独立设置，可输入任意已安装的字体名。默认为{LabelFontFace}。", _labelFontTextBox),
            ("标签字号", "候选窗中序号（如 1. 2. 3.）的字号大小，单位 pt。输入整数，留空表示使用模板默认值。与候选词字号独立设置。默认为{LabelFontPoint}。", _labelFontSizeText),
            ("注释字体", "候选窗中注释文字（如 emoji 候选旁的 [开心]）的字体名称。与候选词字体独立设置。默认为{CommentFontFace}。", _commentFontTextBox),
            ("注释字号", "候选窗中注释文字的字号大小，单位 pt。输入整数，留空表示使用模板默认值。可与候选词字号独立设置。默认为{CommentFontPoint}。", _commentFontSizeText),
            ("状态变化通知", "开启后中英文模式切换时在屏幕上显示短暂的通知气泡，提示当前输入状态。关闭后不显示。默认为{ShowNotification}。", _statusNotificationCheckBox),
            ("通知显示时长(ms)", "状态变化通知气泡在屏幕上的显示时间，单位毫秒。输入整数，留空表示使用模板默认值。仅当「状态变化通知」开启时生效。默认为{NotificationTimeMs}。", _notificationTimeText),
            ("标签格式", "候选窗中候选编号（序号）的显示格式。采用 %s 占位符格式，%s 会被替换为候选序号数字。例如 %s 显示为 1 2 3，%s. 显示为 1. 2. 3.，%s) 显示为 1) 2) 3)。默认为{LabelFormat}。", _labelFormatTextBox),
            ("标记文本", "候选窗中当前高亮选中项前显示的标记字符。可输入任意字符（如 >、▸、●）作为高亮候选的视觉标识。留空则使用 Windows 11 输入法风格标记。注意：修改标记字符后，还需手动在「配色」子页中设置「标记颜色」才能看到标记效果，建议设为与「高亮候选文字颜色」一致的值。默认为{MarkText}。由于输入方案的实现方法，默认值不会在候选窗中自动显示，需要用户手动应用设置。", _markTextTextBox),
            ("滚轮翻页", "开启后可用鼠标滚轮在候选列表中翻页切换候选页。默认为{PagingOnScroll}。", _pagingOnScrollCheckBox),
            ("候选缩写长度", "候选旁注的拼音文字超过此长度后自动截断并显示省略号（…），单位是字母数。输入整数，留空表示使用模板默认值。例如设为 20 表示拼音注释超过 20 个字母即截断。0 表示不截断。默认为{CandidateAbbreviateLength}。", _candidateAbbreviateText),
        ];

        (string, string, Control)[] schemaDefs =
        [
            ("候选数", "每页显示的候选项数量。默认为{PageSize}。", _candidateCountNumeric),
            ("候选方向", "选择候选词的排列方向。横排＝候选词从左到右一排排展开；竖排＝候选词从上到下一行行堆叠。注意：如果「窗口」子页的「竖排」开关处于开启状态，候选词的排列方式将强制为竖排，此设置不生效。当「竖排」关闭时，此设置独立生效。默认为{Layout}。", _candidateDirectionComboBox),
            ("Emoji 注释", "开启后在 emoji 候选右侧显示文字注释（如 [开心] [大笑]）。关闭后不显示。注意：关闭后还需手动在「配色」子页中将「注释文字颜色」和「高亮注释文字颜色」设为透明（`0x00000000`）才能完全隐藏注释；如需恢复显示，将这两项颜色输入框清空即可。默认为{ShowEmojiComments}。", _candidateCommentCheckBox),
        ];

        (string, string, Control)[] rimekitDefs =
        [
            ("注释内容", "控制候选窗中注释文字的整体显示。此设置为功能预留，当前选择后不会改变输入行为。可选：显示所有注释、不显示、中文、拉丁、混合。默认为显示所有注释。Emoji 注释的单独开关由「显示」子页的「Emoji 注释」设置控制。", _commentStyleComboBox),
        ];

        GroupBox carrierBox = CreateSettingsGroupBox("承载器选项", carrierDefs);
        GroupBox schemaBox = CreateSettingsGroupBox("输入方案选项", schemaDefs, columns: 1);
        innerGrid.Controls.Add(carrierBox, 0, 0);
        innerGrid.SetColumnSpan(carrierBox, 2);
        innerGrid.Controls.Add(schemaBox, 2, 0);
        layout.Controls.Add(innerGrid, 0, 0);

        layout.Controls.Add(CreateSettingsGroupBox("rimekit选项", rimekitDefs, columns: 1), 0, 1);

        layout.Controls.Add(CreateBottomSettingsActionBar("显示设置"), 0, 2);
        return layout;
    }

    private Control CreateSyncPage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel actions = CreateActionBar();
        actions.Margin = new Padding(0, 20, 0, 0);
        Button exportBtn = CreateInlineButton("导出用户配置", async (_, _) => await ExportPrototypeTomlAsync());
        Button importBtn = CreateInlineButton("导入用户配置", async (_, _) => await ImportPrototypeTomlAsync());
        exportBtn.Enabled = false;
        importBtn.Enabled = false;
        actions.Controls.Add(exportBtn);
        actions.Controls.Add(importBtn);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(_backupStatusHost, 0, 2);
        return layout;
    }

    private Control CreateWindowPage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        (string, string, Control)[] defs =
        [
            ("全屏", "开启后候选窗在目标窗口最大化或全屏时自动铺满窗口宽度。默认为{Fullscreen}。", _fullscreenCheckBox),
            ("竖排", "开启后，候选窗中的每一个文字都会旋转为竖直方向显示（像对联那样从上往下读）。此时无论「候选方向」设为什么，候选词都会以竖排方式排列。关闭后，候选词的排列方式完全由「显示」子页的「候选方向」设置决定。默认为{VerticalText}。", _verticalTextCheckBox),
            ("竖排左→右", "竖排模式下候选列从左到右排列。关闭时从右到左排列。仅当「竖排」开启时生效。默认为{VerticalTextLeftToRight}。", _verticalTextLeftToRightCheckBox),
            ("竖排换行", "竖排模式下候选文字超出候选窗高度时自动换行。仅当「竖排」开启时生效。默认为{VerticalTextWithWrap}。", _verticalTextWithWrapCheckBox),
            ("竖排自动反转", "竖排模式下当候选窗的列数较多、即将超出屏幕边缘时，自动反转候选列的排列方向以保证全部候选项可见。例如当候选列从左到右排列、右边没有足够空间时，自动改为从右到左排列（向左展开），而不是让候选窗溢出屏幕。仅当「竖排」开启时生效。默认为{VerticalAutoReverse}。", _verticalAutoReverseCheckBox),
            ("内嵌预编辑", "开启后拼音直接显示在文本光标处（即打字的位置），关闭后拼音显示在候选窗中。默认为{InlinePreedit}。", _inlinePreeditCheckBox),
            ("预编辑类型", "控制预编辑文本的显示模式。可选编码区模式（拼音显示在光标附近）、预览区模式（拼音显示在候选窗中）、预览全部（完整预览模式）三种。默认为{PreeditType}。", _preeditTypeComboBox),
            ("全局英文", "控制所有应用的默认输入模式。可选每窗口独立（某窗口切换中英文后仅影响当前窗口）、全局同步（某窗口切换中英文后所有窗口同时变更）。默认为{GlobalAscii}。", _globalAsciiComboBox),
            ("悬停类型", "鼠标悬停在候选窗上时的交互行为。可选无效果（不触发任何悬停反应）、半高亮、高亮三种。默认为{HoverType}。", _hoverTypeComboBox),
            ("点击捕获", "开启后点击候选窗时优先由候选窗处理鼠标事件，防止点击穿透到下层窗口。默认为{ClickToCapture}。", _clickToCaptureCheckBox),
            ("抗锯齿", "候选文字的渲染模式。可选系统默认（由系统决定，通常为 ClearType）、ClearType（子像素渲染，适合液晶屏）、灰度（灰度抗锯齿）、无抗锯齿（不进行处理）四种。默认为{AntialiasMode}。", _antialiasModeComboBox),
            ("显示托盘图标", "在系统托盘中显示小狼毫输入法图标，可右键进行快捷操作。默认为{DisplayTrayIcon}。", _displayTrayIconCheckBox),
            ("增强定位", "使用增强算法自动为候选窗选择更合适的光标跟随位置。默认为{EnhancedPosition}。", _enhancedPositionCheckBox),
            ("英文跟随光标", "英文输入模式下的提示文字跟随文本光标位置显示。默认为{AsciiTipFollowCursor}。", _asciiTipFollowCursorCheckBox),
        ];

        layout.Controls.Add(CreateSettingsGroupBox("承载器选项", defs, columns: 3), 0, 0);
        layout.Controls.Add(CreateBottomSettingsActionBar("窗口设置"), 0, 1);
        return layout;
    }

    private Control CreateLayoutPage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        (string, string, Control)[] defs =
        [
            ("最小宽度", "候选窗的最小宽度，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutMinWidth}。", _layoutMinWidthText),
            ("最小高度", "候选窗的最小高度，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutMinHeight}。", _layoutMinHeightText),
            ("最大宽度", "候选窗的最大宽度，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutMaxWidth}。", _layoutMaxWidthText),
            ("最大高度", "候选窗的最大高度，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutMaxHeight}。", _layoutMaxHeightText),
            ("水平边距", "候选窗与屏幕左右边缘的距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutMarginX}。", _layoutMarginXText),
            ("垂直边距", "候选窗与屏幕上下边缘的距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutMarginY}。", _layoutMarginYText),
            ("边框宽度", "候选窗边框线条的粗细，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutBorderWidth}。", _layoutBorderWidthText),
            ("行距", "候选窗正文文字区域与上方边框之间的距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutLinespacing}。", _layoutLineSpacingText),
            ("基线", "候选窗正文文字区域与左侧边框之间的距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutBaseline}。", _layoutBaselineText),
            ("候选项间距", "相邻两行候选项之间的垂直距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutSpacing}。", _layoutSpacingText),
            ("候选间距", "同一行候选项中两个候选文字之间的水平距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutCandidateSpacing}。", _layoutCandidateSpacingText),
            ("高亮间距", "高亮选中项与四周相邻候选项的距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutHiliteSpacing}。", _layoutHiliteSpacingText),
            ("高亮填充", "高亮选中项四周边框与内部文字的距离，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutHilitePadding}。", _layoutHilitePaddingText),
            ("高亮横向填充", "高亮选中项左右边框与内部文字的距离，单位像素。输入整数，留空表示使用模板默认值。由于输入方案的实现方法，默认值不会自动生效，需要用户手动应用设置。默认为{LayoutHilitePaddingX}。", _layoutHilitePaddingXText),
            ("高亮纵向填充", "高亮选中项上下边框与内部文字的距离，单位像素。输入整数，留空表示使用模板默认值。由于输入方案的实现方法，默认值不会自动生效，需要用户手动应用设置。默认为{LayoutHilitePaddingY}。", _layoutHilitePaddingYText),
            ("阴影半径", "候选窗背后阴影的模糊扩散半径，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutShadowRadius}。", _layoutShadowRadiusText),
            ("阴影横向偏移", "候选窗阴影在水平方向上的偏移量，正值为右、负值为左，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutShadowOffsetX}。", _layoutShadowOffsetXText),
            ("阴影纵向偏移", "候选窗阴影在垂直方向上的偏移量，正值为下、负值为上，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutShadowOffsetY}。", _layoutShadowOffsetYText),
            ("圆角半径", "候选窗四角圆弧的半径，单位像素。输入整数，留空表示使用模板默认值。默认为{LayoutCornerRadius}。", _layoutCornerRadiusText),
            ("对齐方式", "候选窗在屏幕上相对于输入光标的位置。可选居上（候选窗出现在光标下方）、居中（候选窗出现在屏幕中央）、居下（候选窗出现在光标上方）三种。默认为{LayoutAlignType}。", _layoutAlignTypeComboBox),
        ];

        layout.Controls.Add(CreateSettingsGroupBox("承载器选项", defs, columns: 3), 0, 0);
        layout.Controls.Add(CreateBottomSettingsActionBar("布局设置"), 0, 1);
        return layout;
    }

    private static FlowLayoutPanel CreateColorField()
    {
        FlowLayoutPanel container = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        TextBox hexBox = new()
        {
            Width = 300,
            Anchor = AnchorStyles.Left,
            Text = string.Empty,
        };
        Panel preview = new()
        {
            Size = new Size(24, 24),
            Margin = new Padding(4, 0, 0, 0),
        };
        preview.Paint += (_, e) =>
        {
            using Pen borderPen = new(SystemColors.ControlDark);
            e.Graphics.DrawRectangle(borderPen, 0, 0, 23, 23);
            if (string.IsNullOrWhiteSpace(hexBox.Text))
                return;
            Color? c = FromAbgrHex(hexBox.Text);
            if (c.HasValue)
            {
                using SolidBrush brush = new(c.Value);
                e.Graphics.FillRectangle(brush, 1, 1, 22, 22);
            }
            else
            {
                using Pen errPen = new(Color.Red, 2);
                e.Graphics.DrawLine(errPen, 2, 2, 21, 21);
                e.Graphics.DrawLine(errPen, 21, 2, 2, 21);
            }
        };
        Button pickerBtn = new()
        {
            Text = "\u2026",
            Size = new Size(28, 24),
            Margin = new Padding(2, 0, 0, 0),
        };
        pickerBtn.Click += (_, _) =>
        {
            Color current = Color.Black;
            if (!string.IsNullOrWhiteSpace(hexBox.Text))
            {
                Color? parsed = FromAbgrHex(hexBox.Text);
                if (parsed.HasValue)
                    current = parsed.Value;
            }
            using ColorDialog dlg = new() { Color = current, FullOpen = true };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                hexBox.Text = ToAbgrHex(dlg.Color);
                preview.Invalidate();
            }
        };
        hexBox.TextChanged += (_, _) => preview.Invalidate();
        container.Controls.Add(hexBox);
        container.Controls.Add(preview);
        container.Controls.Add(pickerBtn);
        return container;
    }

    private static Color? FromAbgrHex(string hex)
    {
        string clean = hex.Replace("0x", "").Replace("0X", "").Trim();
        if (clean.Length == 6)
            clean = "FF" + clean;
        if (clean.Length != 8 || !int.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out int abgr))
            return null;
        uint u = (uint)abgr;
        int argb = unchecked((int)(
            (u & 0xFF000000) |
            ((u & 0x000000FF) << 16) |
            (u & 0x0000FF00) |
            ((u >> 16) & 0x000000FF)));
        return Color.FromArgb(argb);
    }

    private static string ToAbgrHex(Color c)
        => $"0x{c.A:X2}{c.B:X2}{c.G:X2}{c.R:X2}";

    private static TextBox GetColorTextBox(FlowLayoutPanel field)
        => (TextBox)field.Controls[0];

    private Control CreateColorPage()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel schemeRow = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        schemeRow.Controls.Add(new Label { Text = "浅色主题", AutoSize = true, Margin = new Padding(0, 4, 4, 0) });
        schemeRow.Controls.Add(_dayThemeComboBox);
        schemeRow.Controls.Add(new Label { Text = "深色主题", AutoSize = true, Margin = new Padding(12, 4, 4, 0) });
        schemeRow.Controls.Add(_nightThemeComboBox);
        schemeRow.Controls.Add(_editDayRadio);
        schemeRow.Controls.Add(_editNightRadio);
        layout.Controls.Add(schemeRow, 0, 0);

        (string, string, Control)[] defs =
        [
            ("文字颜色", "控制候选窗中普通文字的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{TextColor}。", _textColorField),
            ("候选文字颜色", "控制候选列表中非高亮候选文字的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{CandidateTextColor}。", _candidateTextColorField),
            ("标签颜色", "控制候选编号（如 1. 2. 3.）的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{LabelColor}。", _labelColorField),
            ("注释文字颜色", "控制注释文字的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。如需在关闭「显示」子页的「Emoji 注释」后完全隐藏注释，请将此颜色设为透明（`0x00000000`）；如需恢复显示，清空输入框即可。默认为{CommentTextColor}。", _commentTextColorField),
            ("背景颜色", "控制候选窗背景的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{BackColor}。", _backColorField),
            ("候选背景颜色", "控制候选窗中候选区域的背景颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{CandidateBackColor}。", _candidateBackColorField),
            ("边框颜色", "控制候选窗边框的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{BorderColor}。", _borderColorField),
            ("阴影颜色", "控制候选窗阴影的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{ShadowColor}。", _shadowColorField),
            ("高亮文字颜色", "控制编码区（输入拼音处）的文字颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{HilitedTextColor}。", _hilitedTextColorField),
            ("高亮背景颜色", "控制编码区（输入拼音处）的背景颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{HilitedBackColor}。", _hilitedBackColorField),
            ("高亮标签颜色", "控制高亮候选的编号颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{HilitedLabelColor}。", _hilitedLabelColorField),
            ("高亮候选文字颜色", "控制高亮选中候选项的文字颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{HilitedCandidateTextColor}。", _hilitedCandidateTextColorField),
            ("高亮候选背景颜色", "控制高亮选中候选项的背景颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{HilitedCandidateBackColor}。", _hilitedCandidateBackColorField),
            ("高亮候选标签颜色", "控制高亮选中候选项的编号颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{HilitedCandidateLabelColor}。", _hilitedCandidateLabelColorField),
            ("高亮候选边框颜色", "控制高亮选中候选项的边框颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。默认为{HilitedCandidateBorderColor}。", _hilitedCandidateBorderColorField),
            ("高亮注释文字颜色", "控制高亮选中候选项旁注释文字的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。如需在关闭「显示」子页的「Emoji 注释」后完全隐藏注释，请将此颜色设为透明（`0x00000000`）；如需恢复显示，清空输入框即可。默认为{HilitedCommentTextColor}。", _hilitedCommentTextColorField),
            ("标记颜色", "控制候选窗前标记字符的颜色。使用十六进制 ABGR 格式（如 `0xFFBBGGRR`）。仅当「显示」子页的「标记文本」非空时生效。建议设为与「高亮候选文字颜色」一致的值，即可看到标记效果。默认为{HilitedMarkColor}。", _hilitedMarkColorField),
        ];

        layout.Controls.Add(CreateSettingsGroupBox("承载器选项", defs, columns: 3), 0, 1);
        layout.Controls.Add(CreateBottomSettingsActionBar("配色设置"), 0, 2);
        return layout;
    }

    private void RenderSchemeInfo()
    {
        RefreshSchemeComboBox();
        _schemeInfoHost.Controls.Clear();
        if (!_inputSchemeDetected)
        {
            return;
        }

        string selectedDisplayName = (_schemeComboBox.SelectedItem as WindowsSchemeOption)?.DisplayName ?? "薄荷拼音-全拼输入";
        string? selectedSchemaId = (_schemeComboBox.SelectedItem as WindowsSchemeOption)?.SchemaId;
        ConfigModel model = GetEditingModel();
        bool carrierAvailable = IsCarrierAvailable();
        string targetRoot = Environment.ExpandEnvironmentVariables(model.SyncSettings.WindowsTargetRoot);
        string schemeStatus = !carrierAvailable
            ? "承载器未安装"
            : BuildSchemeStateLabel(model, selectedSchemaId);
        string schemeHomepage = "https://github.com/Mintimate/oh-my-rime";
        TableLayoutPanel linkRow = new() { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.Controls.Add(CreateFieldLabel("官方主页"), 0, 0);
        linkRow.Controls.Add(CreateLinkLabel(schemeHomepage, schemeHomepage), 1, 0);
        _schemeInfoHost.Controls.Add(linkRow);
        _schemeInfoHost.Controls.Add(CreateKeyValueGrid(
        [
            ("当前输入方案", selectedDisplayName),
            ("方案状态", schemeStatus),
            ("输入法目录", carrierAvailable ? targetRoot : "目录不存在"),
        ]));
    }

    private void RefreshSchemeComboBox()
    {
        WindowsSchemeOption[] options = GetWindowsSchemeOptions();
        string? previousSchemaId = (_schemeComboBox.SelectedItem as WindowsSchemeOption)?.SchemaId;
        _schemeComboBox.Items.Clear();
        foreach (WindowsSchemeOption option in options)
        {
            _schemeComboBox.Items.Add(option);
        }

        if (_schemeComboBox.Items.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(previousSchemaId))
            {
                for (int i = 0; i < _schemeComboBox.Items.Count; i++)
                {
                    if (_schemeComboBox.Items[i] is WindowsSchemeOption opt
                        && string.Equals(opt.SchemaId, previousSchemaId, StringComparison.OrdinalIgnoreCase))
                    {
                        _schemeComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (_schemeComboBox.SelectedIndex < 0)
            {
                _schemeComboBox.SelectedIndex = 0;
            }
        }
    }

    private void LoadDetectedUserEntries()
    {
        _userEntries.Clear();
        if (!IsCarrierAvailable() || !IsRimeMintSchemeAvailable())
        {
            return;
        }

        ConfigModel model = GetEditingModel();
        string targetRoot = Environment.ExpandEnvironmentVariables(model.SyncSettings.WindowsTargetRoot);
        string customSimplePath = Path.Combine(targetRoot, "dicts", "custom_simple.dict.yaml");
        if (File.Exists(customSimplePath))
        {
            string yamlContent = File.ReadAllText(customSimplePath);
            foreach (string line in yamlContent.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('-') || trimmed.StartsWith("...") || trimmed.StartsWith("---"))
                    continue;
                int firstTab = trimmed.IndexOf('\t');
                if (firstTab < 0)
                    continue;
                string text = trimmed[..firstTab].Trim();
                string remainder = trimmed[(firstTab + 1)..];
                int secondTab = remainder.IndexOf('\t');
                string code = secondTab >= 0 ? remainder[..secondTab].Trim() : remainder.Trim();
                if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(code))
                {
                    _userEntries.Add(new UserEntryRow(text, code, "已生效"));
                }
            }
        }
        else
        {
            foreach (CustomEntry entry in model.DictionarySettings.CustomEntries)
            {
                _userEntries.Add(new UserEntryRow(entry.Text, entry.Code, "已保存"));
            }
        }
    }

    private void LoadDetectedDictionaries(string? selectedName = null, string? detectedStatusName = null, bool resetUserEntries = true)
    {
        string? preferredSelection = selectedName ?? _dictionaryListBox.SelectedItem?.ToString();
        _dictionaryListBox.Items.Clear();
        object[] dictionaryDisplayNames = _workflowService.GetFormalDictionaryDescriptors()
            .Select(ResolveDictionaryDisplayName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Cast<object>()
            .ToArray();
        if (dictionaryDisplayNames.Length > 0)
        {
            _dictionaryListBox.Items.AddRange(dictionaryDisplayNames);
        }
        if (!string.IsNullOrWhiteSpace(preferredSelection) && _dictionaryListBox.Items.Contains(preferredSelection))
        {
            _dictionaryListBox.SelectedItem = preferredSelection;
        }
        else
        {
            _dictionaryListBox.SelectedIndex = 0;
        }

        _detectedDictionaryStatusName = detectedStatusName;
        if (resetUserEntries)
        {
            _userEntriesDetected = false;
        }
    }

    private void LoadDetectedModels(string? selectedName = null, string? detectedStatusName = null)
    {
        string? preferredSelection = selectedName ?? _modelListBox.SelectedItem?.ToString();
        _modelListBox.Items.Clear();
        foreach (FormalResourceDescriptor model in _workflowService.GetFormalModelDescriptors())
        {
            _modelListBox.Items.Add(model.DisplayName.Replace("（简体）", string.Empty, StringComparison.Ordinal));
        }
        if (_modelListBox.Items.Count == 0)
        {
            _modelListBox.Items.Add("万象官方语法模型");
        }
        if (!string.IsNullOrWhiteSpace(preferredSelection) && _modelListBox.Items.Contains(preferredSelection))
        {
            _modelListBox.SelectedItem = preferredSelection;
        }
        else
        {
            _modelListBox.SelectedIndex = 0;
        }

        _detectedModelStatusName = detectedStatusName;
    }

    private static string? ResolveDictionaryHomepage(string dictionaryName)
    {
        return dictionaryName switch
        {
            "moetype" => "https://github.com/suiginko/moetype",
            "搜狗网络流行新词" => "https://pinyin.sogou.com/dict/detail/index/4",
            "zhwiki" => "https://github.com/felixonmars/fcitx5-pinyin-zhwiki",
            _ => null,
        };
    }

    private static string ResolveDictionaryDisplayName(FormalResourceDescriptor descriptor)
    {
        return descriptor.ResourceId switch
        {
            "moetype" => "moetype",
            "sogou_network_popular_words" => "搜狗网络流行新词",
            "zhwiki" => "zhwiki",
            "custom_simple" => "用户词条",
            _ => descriptor.DisplayName,
        };
    }

    private async Task UninstallFormalResourceAsync(string displayName)
    {
        if (!IsCarrierAvailable())
        {
            ShowUnsupportedAction("承载器未安装，无法执行卸载操作。请先安装小狼毫。");
            return;
        }

        string? resourceId = ResolveResourceId(displayName);
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            ShowUnsupportedAction($"当前没有找到 {displayName} 的正式资源标识。");
            return;
        }

        await RunWorkflowOperationAsync(
            $"正在卸载 {displayName}…",
            phase =>
            {
                var r = _workflowService.RunUninstallFormalResource(resourceId, _configModelPath, "text", forceStopWeasel: true, phase: phase);
                if (r.ExitCode == 0) phase("正在清理并验证…");
                return r;
            },
            _ =>
            {
                LoadConfigIntoControls(GetEditingModel());
                if (ResolveDictionaryId(displayName) is not null && _dictionaryListBox.Items.Count > 0)
                {
                    LoadDetectedDictionaries(displayName, displayName, resetUserEntries: false);
                    RenderDictionaryDetail();
                }
                if (ResolveModelId(displayName) is not null && _modelListBox.Items.Count > 0)
                {
                    LoadDetectedModels(displayName, displayName);
                    RenderModelDetail();
                }
                if (ResolveSchemeResourceId(displayName) is not null)
                {
                    _inputSchemeDetected = true;
                    RenderSchemeInfo();
                }

                RefreshViewsAfterConfigMutation(forceSchemeRefresh: false);
            });
    }

    private async Task ExportPrototypeTomlAsync()
    {
        string? automationExportPath = Environment.GetEnvironmentVariable("RIMEKIT_AUTOMATION_EXPORT_USER_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(automationExportPath))
        {
            await RunWorkflowOperationAsync(
                "正在导出用户配置…",
                phase => _workflowService.RunExportUserConfigToml(automationExportPath, _configModelPath, "text", phase: phase));
            return;
        }

        if (ExportUserConfigPathProvider is not null)
        {
            string? targetPath = ExportUserConfigPathProvider();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            await RunWorkflowOperationAsync(
                "正在导出用户配置…",
                phase => _workflowService.RunExportUserConfigToml(targetPath, _configModelPath, "text", phase: phase));
            return;
        }

        using SaveFileDialog dialog = new()
        {
            Filter = "TOML 文件 (*.toml)|*.toml",
            Title = "导出用户配置",
            FileName = "user-config.toml",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunWorkflowOperationAsync(
            "正在导出用户配置…",
            phase => _workflowService.RunExportUserConfigToml(dialog.FileName, _configModelPath, "text", phase: phase));
    }

    private async Task ImportPrototypeTomlAsync()
    {
        string? automationImportPath = Environment.GetEnvironmentVariable("RIMEKIT_AUTOMATION_IMPORT_USER_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(automationImportPath))
        {
            await RunWorkflowOperationAsync(
                "正在导入用户配置…",
                phase => _workflowService.RunImportUserConfigToml(automationImportPath, _configModelPath, "text", forceStopWeasel: true, phase: phase),
                _ => RefreshViewsAfterConfigMutation());
            return;
        }

        if (ImportUserConfigPathProvider is not null)
        {
            string? sourcePath = ImportUserConfigPathProvider();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            await RunWorkflowOperationAsync(
                "正在导入用户配置…",
                phase => _workflowService.RunImportUserConfigToml(sourcePath, _configModelPath, "text", forceStopWeasel: true, phase: phase),
                _ => RefreshViewsAfterConfigMutation());
            return;
        }

        using OpenFileDialog dialog = new()
        {
            Filter = "TOML 文件 (*.toml)|*.toml",
            Title = "导入用户配置",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunWorkflowOperationAsync(
            "正在导入用户配置…",
            phase => _workflowService.RunImportUserConfigToml(dialog.FileName, _configModelPath, "text", forceStopWeasel: true, phase: phase),
            _ => RefreshViewsAfterConfigMutation());
    }

    private void RefreshViewsAfterConfigMutation(bool forceSchemeRefresh = false)
    {
        LoadConfigIntoControls(GetEditingModel());
        if (_carrierStatusDetected)
        {
            RenderCarrierInfo();
        }
        if (_inputSchemeDetected || forceSchemeRefresh)
        {
            _inputSchemeDetected = true;
            RenderSchemeInfo();
        }
        if (_dictionaryListBox.Items.Count > 0)
        {
            RenderDictionaryDetail();
        }
        if (_modelListBox.Items.Count > 0)
        {
            RenderModelDetail();
        }

        RefreshSyncAndBackupStatus();
        RefreshCarrierDependentUi();
    }

    private void RefreshSyncAndBackupStatus()
    {
        RenderSyncStatus();
        RenderBackupStatus();
    }

    private async Task SaveCurrentSettingsAsync(bool apply, string busyMessage)
    {
        if (apply && !IsCarrierAvailable())
        {
            ShowUnsupportedAction("承载器未安装，无法部署设置到输入法目录。请先安装承载器，再应用设置。");
            return;
        }

        ConfigModel model = BuildConfigModelFromControls();
        await RunWorkflowOperationAsync(
            busyMessage,
            phase => SaveAndApplyWithOptionalInputRecovery(
                () =>
                {
                    phase("正在保存配置…");
                    return _workflowService.RunSaveConfig(_configModelPath, model, "text");
                },
                apply
                    ? () => _workflowService.RunApply(_configModelPath, "text", forceStopWeasel: true, phase: phase)
                    : null),
            _ => RefreshViewsAfterConfigMutation(forceSchemeRefresh: true));
    }

    private async Task ApplyUserEntriesAsync()
    {
        if (!IsCarrierAvailable())
        {
            ShowUnsupportedAction("承载器未安装，无法应用用户词条。请先安装小狼毫。");
            return;
        }

        ConfigModel model = BuildConfigModelFromControls();
        await RunWorkflowOperationAsync(
            "正在应用用户词条…",
            phase => SaveAndApplyWithOptionalInputRecovery(
                () =>
                {
                    phase("正在保存词条…");
                    return _workflowService.RunSaveConfig(_configModelPath, model, "text");
                },
                () => _workflowService.RunApply(_configModelPath, "text", forceStopWeasel: true, phase: phase)),
            _ =>
            {
                _userEntriesDetected = true;
                RefreshViewsAfterConfigMutation(forceSchemeRefresh: false);
            });
    }

    private async Task InstallOrUpdateFormalResourceAsync(string displayName, string busyMessage, string? filePath = null)
    {
        if (!IsCarrierAvailable())
        {
            ShowUnsupportedAction("承载器未安装，无法执行下载或安装操作。请先安装小狼毫。");
            return;
        }

        string? resourceId = ResolveResourceId(displayName);
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            ShowUnsupportedAction($"当前没有找到 {displayName} 的正式资源标识。");
            return;
        }

        await RunWorkflowOperationAsync(
            busyMessage,
            phase =>
            {
                var r = filePath is not null
                    ? _workflowService.RunInstallFormalResourceFromFile(resourceId, filePath, _configModelPath, "text", forceStopWeasel: true, phase: phase)
                    : _workflowService.RunInstallFormalResource(resourceId, _configModelPath, "text", forceStopWeasel: true, phase: phase);
                if (r.ExitCode == 0) phase("部署完成，正在验证…");
                return r;
            },
            result =>
            {
                if (ResolveDictionaryId(displayName) is not null)
                    _detectedDictionaryStatusName = displayName;
                if (ResolveModelId(displayName) is not null)
                    _detectedModelStatusName = displayName;
                if (_dictionaryListBox.Items.Count > 0)
                {
                    LoadDetectedDictionaries(
                        selectedName: ResolveDictionaryId(displayName) is not null ? displayName : _dictionaryListBox.SelectedItem?.ToString(),
                        detectedStatusName: _detectedDictionaryStatusName,
                        resetUserEntries: false);
                }
                if (_modelListBox.Items.Count > 0)
                {
                    LoadDetectedModels(
                        selectedName: ResolveModelId(displayName) is not null ? displayName : _modelListBox.SelectedItem?.ToString(),
                        detectedStatusName: _detectedModelStatusName);
                }
                if (ResolveSchemeResourceId(displayName) is not null)
                {
                    _inputSchemeDetected = true;
                    RenderSchemeInfo();
                }
                RenderDictionaryDetail();
                RenderModelDetail();
                RefreshSyncAndBackupStatus();
                if (result.JsonPayload is not null)
                {
                    try
                    {
                        string payload = System.Text.Json.JsonSerializer.Serialize(result.JsonPayload);
                        var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payload);
                        if (json.TryGetProperty("capture_errors", out var errors) && errors.GetArrayLength() > 0)
                        {
                            _statusLabel.ForeColor = Color.Red;
                            _statusLabel.Text = "⚠ 模板文件捕获失败。请重新安装输入方案。";
                        }
                    }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException) { System.Diagnostics.Debug.WriteLine($"[GUI] capture_errors check failed: {ex.Message}"); }
                }
                RefreshCarrierDependentUi();
            });
    }

    private async Task RunWorkflowOperationAsync(
        string busyMessage,
        Func<Action<string>, CommandExecutionResult> operation,
        Action<CommandExecutionResult>? onSuccess = null)
    {
        _statusLabel.ForeColor = SystemColors.ControlText;
        _statusLabel.Text = busyMessage;

        CommandExecutionResult result = await Task.Run(() => operation(phase => BeginInvoke(() => { _statusLabel.ForeColor = SystemColors.ControlText; _statusLabel.Text = phase; })));

        if (result.ExitCode == 0)
        {
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.Text = "操作完成，正在刷新…";
            onSuccess?.Invoke(result);
        }

        if (_statusLabel.ForeColor != Color.Red || result.ExitCode != 0)
        {
            _statusLabel.ForeColor = result.ExitCode == 0 ? SystemColors.ControlText : Color.Red;
            _statusLabel.Text = result.ExitCode == 0 ? "已完成" : "执行失败";
        }

        if (result.ExitCode != 0)
        {
            if (WorkflowErrorObserver is not null)
            {
                WorkflowErrorObserver(result.TextOutput);
            }
            else
            {
                MessageBox.Show(this, result.TextOutput, "执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        await Task.Delay(220);
        if (result.ExitCode == 0 && _statusLabel.ForeColor != Color.Red)
        {
            _statusLabel.Text = string.Empty;
        }
    }

    private CommandExecutionResult SaveAndApplyWithOptionalInputRecovery(
        Func<CommandExecutionResult> saveOperation,
        Func<CommandExecutionResult>? applyOperation)
    {
        CommandExecutionResult saveResult = saveOperation();
        if (saveResult.ExitCode != 0 || applyOperation is null)
        {
            return saveResult;
        }

        CommandExecutionResult applyResult = applyOperation();
        return applyResult;
    }

    private void LoadConfigIntoControls(ConfigModel model, bool useTemplateDefaults = false)
    {
        bool carrierOk = IsCarrierAvailable();
        bool schemeOk = IsRimeMintSchemeAvailable();
        RefreshSchemeComboBox();
        string targetSchemaId = model.ProfileSettings.WindowsDefaultSchemaId;
        for (int i = 0; i < _schemeComboBox.Items.Count; i++)
        {
            if (_schemeComboBox.Items[i] is WindowsSchemeOption opt
                && string.Equals(opt.SchemaId, targetSchemaId, StringComparison.OrdinalIgnoreCase))
            {
                _schemeComboBox.SelectedIndex = i;
                break;
            }
        }

        string targetRoot = ResolveTargetRootFromModel(model);
        _showingTemplateDefaults = useTemplateDefaults;
        WeaselUserSettings weasel = useTemplateDefaults ? new WeaselUserSettings() : UserSettingsReader.ReadWeasel(targetRoot);
        MintUserSettings mint = useTemplateDefaults ? new MintUserSettings() : UserSettingsReader.ReadMint(targetRoot, "rime_mint");

        string? simplificationMode = mint.SimplificationMode ?? (carrierOk && schemeOk ? TemplateService.GetSchemaString("SimplificationMode") : null);
        _simplifiedRadio.Checked = !string.Equals(simplificationMode, "traditional", StringComparison.OrdinalIgnoreCase);
        _traditionalRadio.Checked = string.Equals(simplificationMode, "traditional", StringComparison.OrdinalIgnoreCase);
        _halfShapeRadio.Checked = !BoolFromUserOrDefault(mint.FullShapeEnabled, "FullShapeEnabled", carrierOk, schemeOk);
        _fullShapeRadio.Checked = BoolFromUserOrDefault(mint.FullShapeEnabled, "FullShapeEnabled", carrierOk, schemeOk);
        _asciiPunctCheckBox.Checked = BoolFromUserOrDefault(mint.AsciiPunctEnabled, "AsciiPunctEnabled", carrierOk, schemeOk);
        _emojiCheckBox.Checked = mint.EmojiSuggestionEnabled ?? BoolFromUserOrDefault(null, "EmojiSuggestionEnabled", carrierOk, schemeOk);
        _toneCheckBox.Checked = BoolFromUserOrDefault(mint.ToneDisplayEnabled, "ToneDisplayEnabled", carrierOk, schemeOk);
        _enableUserDictCheckBox.Checked = BoolFromUserOrDefault(mint.EnableUserDict, "EnableUserDict", carrierOk, schemeOk);
        _fuzzyCheckBox.Checked = mint.FuzzyEnabled == true;

        _fuzzyRules.Clear();
        foreach (string rule in mint.FuzzyAdditionalRules)
        {
            if (TryParseFuzzyRule(rule, out FuzzyRuleRow? row))
                _fuzzyRules.Add(row);
        }

        string? colorScheme = weasel.ColorScheme ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("ColorScheme") : null);
        _dayThemeComboBox.SelectedItem = ResolveThemeLabel(colorScheme);
        _dayBaseScheme = colorScheme == "rimekit_custom_day"
            ? (weasel.CustomDayBaseScheme ?? TemplateService.GetStyleString("ColorScheme"))
            : (colorScheme is { Length: > 0 } ? colorScheme : TemplateService.GetStyleString("ColorScheme"));
        string? colorSchemeDark = weasel.ColorSchemeDark ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("ColorSchemeDark") : null);
        _nightThemeComboBox.SelectedItem = ResolveThemeLabel(colorSchemeDark);
        _nightBaseScheme = colorSchemeDark == "rimekit_custom_night"
            ? (weasel.CustomNightBaseScheme ?? TemplateService.GetStyleString("ColorSchemeDark"))
            : (colorSchemeDark is { Length: > 0 } ? colorSchemeDark : TemplateService.GetStyleString("ColorSchemeDark"));
        string? fontFace = weasel.FontFace ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("FontFace") : null);
        _fontTextBox.Text = fontFace ?? "";
        if (fontFace is null && carrierOk && schemeOk)
            System.Diagnostics.Debug.WriteLine("[r47] FontFace 模板字段不可用, 使用空白");
        _fontSizeText.Text = ResolveIntFieldText(weasel.FontPoint, "FontPoint", carrierOk, schemeOk);
        _statusNotificationCheckBox.Checked = BoolFromUserOrDefault(weasel.ShowNotification, "ShowNotification", carrierOk, schemeOk);
        _candidateCountNumeric.Value = NumericFromUserOrDefault(mint.PageSize, "PageSize", _candidateCountNumeric, carrierOk, schemeOk);
        _candidateCommentCheckBox.Checked = mint.ShowEmojiComments ?? BoolFromUserOrDefault(null, "ShowEmojiComments", carrierOk, schemeOk);

        string? candidateLayout = mint.Layout ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("CandidateListLayout") : null);
        SelectComboByValue(_candidateDirectionComboBox, string.Equals(candidateLayout, "linear", StringComparison.Ordinal) ? "横排" : string.Equals(candidateLayout, "stacked", StringComparison.Ordinal) ? "竖排" : "");

        _ueCompatCheckBox.Checked = mint.UeCompatEnabled == true;
        SelectComboByValue(_customPhraseComboBox, mint.CustomPhraseMode switch { "disabled" => "关闭", "full_phrase" => "完整短语", "simple_code_only" => "简码匹配", _ => "关闭" });
        SelectComboByValue(_commentStyleComboBox, model.PersonalizationSettings.CommentStyleVariant switch { "none" => "不显示", "chinese" => "中文", "latin" => "拉丁", "mixed" => "混合", _ => "显示所有注释" });

        SelectComboByValue(_symbolProfileComboBox, "默认符号配置");
        SelectComboByValue(_preeditFormatComboBox, model.PersonalizationSettings.PreeditFormatMode switch { "raw_code" => "原始编码", "translated_code" => "翻译编码", _ => "保留当前" });

        string? labelFontFace = weasel.LabelFontFace ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("LabelFontFace") : null);
        _labelFontTextBox.Text = labelFontFace ?? "";
        if (labelFontFace is null && carrierOk && schemeOk)
            System.Diagnostics.Debug.WriteLine("[r47] LabelFontFace 模板字段不可用, 使用空白");
        string? commentFontFace = weasel.CommentFontFace ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("CommentFontFace") : null);
        _commentFontTextBox.Text = commentFontFace ?? "";
        if (commentFontFace is null && carrierOk && schemeOk)
            System.Diagnostics.Debug.WriteLine("[r47] CommentFontFace 模板字段不可用, 使用空白");
        _labelFontSizeText.Text = ResolveIntFieldText(weasel.LabelFontPoint, "LabelFontPoint", carrierOk, schemeOk);
        _commentFontSizeText.Text = ResolveIntFieldText(weasel.CommentFontPoint, "CommentFontPoint", carrierOk, schemeOk);
        _notificationTimeText.Text = ResolveIntFieldText(weasel.NotificationTimeMs, "NotificationTimeMs", carrierOk, schemeOk);
        string? labelFormat = weasel.LabelFormat ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("LabelFormat") : null);
        _labelFormatTextBox.Text = labelFormat ?? "";
        if (labelFormat is null && carrierOk && schemeOk)
            System.Diagnostics.Debug.WriteLine("[r47] LabelFormat 模板字段不可用, 使用空白");
        string? markText = weasel.MarkText ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("MarkText") : null);
        _markTextTextBox.Text = markText ?? "";
        if (markText is null && carrierOk && schemeOk)
            System.Diagnostics.Debug.WriteLine("[r47] MarkText 模板字段不可用, 使用空白");
        _pagingOnScrollCheckBox.Checked = BoolFromUserOrDefault(weasel.PagingOnScroll, "PagingOnScroll", carrierOk, schemeOk);
        _candidateAbbreviateText.Text = ResolveIntFieldText(weasel.CandidateAbbreviateLength, "CandidateAbbreviateLength", carrierOk, schemeOk);
        _fullscreenCheckBox.Checked = BoolFromUserOrDefault(weasel.Fullscreen, "Fullscreen", carrierOk, schemeOk);
        _verticalTextCheckBox.Checked = BoolFromUserOrDefault(weasel.VerticalText, "VerticalText", carrierOk, schemeOk);
        _verticalTextLeftToRightCheckBox.Checked = BoolFromUserOrDefault(weasel.VerticalTextLeftToRight, "VerticalTextLeftToRight", carrierOk, schemeOk);
        _verticalTextWithWrapCheckBox.Checked = BoolFromUserOrDefault(weasel.VerticalTextWithWrap, "VerticalTextWithWrap", carrierOk, schemeOk);
        _verticalAutoReverseCheckBox.Checked = BoolFromUserOrDefault(weasel.VerticalAutoReverse, "VerticalAutoReverse", carrierOk, schemeOk);
        _inlinePreeditCheckBox.Checked = BoolFromUserOrDefault(weasel.InlinePreedit, "InlinePreedit", carrierOk, schemeOk);
        string? preeditType = weasel.PreeditType ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("PreeditType") : null);
        SelectComboByValue(_preeditTypeComboBox, preeditType switch { "composition" => "编码区模式", "preview" => "预览区模式", "preview_all" => "预览全部", _ => "编码区模式" });
        string globalAscii = weasel.GlobalAscii.HasValue ? (weasel.GlobalAscii.Value ? "全局同步" : "每窗口独立") : (carrierOk && schemeOk ? (TemplateService.GetStyleBool("GlobalAscii") switch { true => "全局同步", false => "每窗口独立", _ => "" }) : "");
        SelectComboByValue(_globalAsciiComboBox, globalAscii);
        string? hoverType = weasel.HoverType ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("HoverType") : null);
        SelectComboByValue(_hoverTypeComboBox, hoverType switch { "semi_hilite" => "半高亮", "hilite" => "高亮", "" or "none" => "无效果", _ => "" });
        _clickToCaptureCheckBox.Checked = BoolFromUserOrDefault(weasel.ClickToCapture, "ClickToCapture", carrierOk, schemeOk);
        _displayTrayIconCheckBox.Checked = BoolFromUserOrDefault(weasel.DisplayTrayIcon, "DisplayTrayIcon", carrierOk, schemeOk);
        _enhancedPositionCheckBox.Checked = BoolFromUserOrDefault(weasel.EnhancedPosition, "EnhancedPosition", carrierOk, schemeOk);
        _asciiTipFollowCursorCheckBox.Checked = BoolFromUserOrDefault(weasel.AsciiTipFollowCursor, "AsciiTipFollowCursor", carrierOk, schemeOk);
        string? antialiasMode = weasel.AntialiasMode ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("AntialiasMode") : null);
        SelectComboByValue(_antialiasModeComboBox, antialiasMode switch { "cleartype" => "ClearType", "grayscale" => "灰度", "aliased" => "无", "" or "default" => "系统默认", _ => "" });
        _layoutMinWidthText.Text = ResolveIntFieldText(weasel.LayoutMinWidth, "LayoutMinWidth", carrierOk, schemeOk);
        _layoutMinHeightText.Text = ResolveIntFieldText(weasel.LayoutMinHeight, "LayoutMinHeight", carrierOk, schemeOk);
        _layoutMaxWidthText.Text = ResolveIntFieldText(weasel.LayoutMaxWidth, "LayoutMaxWidth", carrierOk, schemeOk);
        _layoutMaxHeightText.Text = ResolveIntFieldText(weasel.LayoutMaxHeight, "LayoutMaxHeight", carrierOk, schemeOk);
        _layoutMarginXText.Text = ResolveIntFieldText(weasel.LayoutMarginX, "LayoutMarginX", carrierOk, schemeOk);
        _layoutMarginYText.Text = ResolveIntFieldText(weasel.LayoutMarginY, "LayoutMarginY", carrierOk, schemeOk);
        _layoutBorderWidthText.Text = ResolveIntFieldText(weasel.LayoutBorderWidth, "LayoutBorderWidth", carrierOk, schemeOk);
        _layoutLineSpacingText.Text = ResolveIntFieldText(weasel.LayoutLineSpacing, "LayoutLineSpacing", carrierOk, schemeOk);
        _layoutBaselineText.Text = ResolveIntFieldText(weasel.LayoutBaseline, "LayoutBaseline", carrierOk, schemeOk);
        _layoutSpacingText.Text = ResolveIntFieldText(weasel.LayoutSpacing, "LayoutSpacing", carrierOk, schemeOk);
        _layoutCandidateSpacingText.Text = ResolveIntFieldText(weasel.LayoutCandidateSpacing, "LayoutCandidateSpacing", carrierOk, schemeOk);
        _layoutHiliteSpacingText.Text = ResolveIntFieldText(weasel.LayoutHiliteSpacing, "LayoutHiliteSpacing", carrierOk, schemeOk);
        _layoutHilitePaddingText.Text = ResolveIntFieldText(weasel.LayoutHilitePadding, "LayoutHilitePadding", carrierOk, schemeOk);
        _layoutHilitePaddingXText.Text = ResolveIntFieldText(weasel.LayoutHilitePaddingX, "LayoutHilitePaddingX", carrierOk, schemeOk);
        _layoutHilitePaddingYText.Text = ResolveIntFieldText(weasel.LayoutHilitePaddingY, "LayoutHilitePaddingY", carrierOk, schemeOk);
        _layoutShadowRadiusText.Text = ResolveIntFieldText(weasel.LayoutShadowRadius, "LayoutShadowRadius", carrierOk, schemeOk);
        _layoutShadowOffsetXText.Text = ResolveIntFieldText(weasel.LayoutShadowOffsetX, "LayoutShadowOffsetX", carrierOk, schemeOk);
        _layoutShadowOffsetYText.Text = ResolveIntFieldText(weasel.LayoutShadowOffsetY, "LayoutShadowOffsetY", carrierOk, schemeOk);
        _layoutCornerRadiusText.Text = ResolveIntFieldText(weasel.LayoutCornerRadius, "LayoutCornerRadius", carrierOk, schemeOk);
        string? layoutAlignType = weasel.LayoutAlignType ?? (carrierOk && schemeOk ? TemplateService.GetStyleString("LayoutAlignType") : null);
        SelectComboByValue(_layoutAlignTypeComboBox, layoutAlignType switch { "top" => "居上", "center" => "居中", "bottom" => "居下", _ => "" });

        LoadColorField(_textColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "text_color");
        LoadColorField(_candidateTextColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "candidate_text_color");
        LoadColorField(_labelColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "label_color");
        LoadColorField(_commentTextColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "comment_text_color");
        LoadColorField(_backColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "back_color");
        LoadColorField(_candidateBackColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "candidate_back_color");
        LoadColorField(_borderColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "border_color");
        LoadColorField(_shadowColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "shadow_color");
        LoadColorField(_hilitedTextColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_text_color");
        LoadColorField(_hilitedBackColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_back_color");
        LoadColorField(_hilitedLabelColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_label_color");
        LoadColorField(_hilitedCandidateTextColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_candidate_text_color");
        LoadColorField(_hilitedCandidateBackColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_candidate_back_color");
        LoadColorField(_hilitedCandidateLabelColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_candidate_label_color");
        LoadColorField(_hilitedCandidateBorderColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_candidate_border_color");
        LoadColorField(_hilitedCommentTextColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_comment_text_color");
        LoadColorField(_hilitedMarkColorField, colorScheme, colorSchemeDark, weasel.DayColors, weasel.NightColors, "hilited_mark_color");

        _userEntries.Clear();
        foreach (CustomEntry entry in model.DictionarySettings.CustomEntries)
        {
            _userEntries.Add(new UserEntryRow(entry.Text, entry.Code, "已保存"));
        }

        if (carrierOk && schemeOk && !VerifyTemplateIntegrity())
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = "模板不完整，部分设置项默认值不可用。请重新安装输入方案以生成模板缓存。";
        }
    }

    private static bool BoolFromUserOrDefault(bool? userValue, string templateField, bool carrierOk, bool schemeOk)
    {
        if (userValue.HasValue)
            return userValue.Value;
        if (carrierOk && schemeOk)
        {
            bool? t = TemplateService.GetSchemaBool(templateField) ?? TemplateService.GetStyleBool(templateField);
            if (t.HasValue)
                return t.Value;
        }
        return false;
    }

    private static int NumericFromUserOrDefault(int? userValue, string templateField, NumericUpDown numeric, bool carrierOk, bool schemeOk)
    {
        if (userValue.HasValue)
            return Math.Clamp(userValue.Value, (int)numeric.Minimum, (int)numeric.Maximum);
        if (carrierOk && schemeOk)
        {
            int? t = TemplateService.GetSchemaInt(templateField) ?? TemplateService.GetStyleInt(templateField);
            if (t.HasValue)
                return Math.Clamp(t.Value, (int)numeric.Minimum, (int)numeric.Maximum);
        }
        return (int)numeric.Minimum;
    }

    private static bool VerifyTemplateIntegrity()
    {
        ParsedWeaselYaml? w = TemplateService.GetWeaselDefaults();
        ParsedSchemaDefaults? s = TemplateService.GetSchemaDefaults("rime_mint");
        return w is not null
            && w.FontPoint is not null
            && s is not null
            && s.FullShapeEnabled is not null
            && s.AsciiPunctEnabled is not null;
    }

    private void LoadColorFields()
    {
        string targetRoot = ResolveTargetRootFromModel(GetEditingModel());
        WeaselUserSettings weasel = _showingTemplateDefaults ? new WeaselUserSettings() : UserSettingsReader.ReadWeasel(targetRoot);
        string? dayScheme = weasel.ColorScheme ?? TemplateService.GetStyleString("ColorScheme");
        string? nightScheme = weasel.ColorSchemeDark ?? TemplateService.GetStyleString("ColorSchemeDark");
        LoadColorField(_textColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "text_color");
        LoadColorField(_candidateTextColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "candidate_text_color");
        LoadColorField(_labelColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "label_color");
        LoadColorField(_commentTextColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "comment_text_color");
        LoadColorField(_backColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "back_color");
        LoadColorField(_candidateBackColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "candidate_back_color");
        LoadColorField(_borderColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "border_color");
        LoadColorField(_shadowColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "shadow_color");
        LoadColorField(_hilitedTextColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_text_color");
        LoadColorField(_hilitedBackColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_back_color");
        LoadColorField(_hilitedLabelColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_label_color");
        LoadColorField(_hilitedCandidateTextColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_candidate_text_color");
        LoadColorField(_hilitedCandidateBackColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_candidate_back_color");
        LoadColorField(_hilitedCandidateLabelColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_candidate_label_color");
        LoadColorField(_hilitedCandidateBorderColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_candidate_border_color");
        LoadColorField(_hilitedCommentTextColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_comment_text_color");
        LoadColorField(_hilitedMarkColorField, dayScheme, nightScheme, weasel.DayColors, weasel.NightColors, "hilited_mark_color");
    }

    private void LoadColorField(FlowLayoutPanel field, string? dayScheme, string? nightScheme, SchemeColors? dayColors, SchemeColors? nightColors, string colorFieldName)
    {
        string? hex;
        if (_editDayRadio.Checked)
        {
            string? scheme = GetActiveSchemeName(isDay: true, dayScheme, nightScheme);
            hex = GetColorValue(dayColors, colorFieldName) ?? (scheme is { Length: > 0 } ? TemplateService.GetColorSchemeColor(scheme, colorFieldName) : null);
        }
        else
        {
            string? scheme = GetActiveSchemeName(isDay: false, dayScheme, nightScheme);
            hex = GetColorValue(nightColors, colorFieldName) ?? (scheme is { Length: > 0 } ? TemplateService.GetColorSchemeColor(scheme, colorFieldName) : null);
        }
        GetColorTextBox(field).Text = hex ?? "";
    }

    private static string? GetColorValue(SchemeColors? colors, string fieldName)
    {
        if (colors is null)
            return null;
        return fieldName switch
        {
            "text_color" => colors.TextColor,
            "candidate_text_color" => colors.CandidateTextColor,
            "label_color" => colors.LabelColor,
            "comment_text_color" => colors.CommentTextColor,
            "back_color" => colors.BackColor,
            "candidate_back_color" => colors.CandidateBackColor,
            "border_color" => colors.BorderColor,
            "shadow_color" => colors.ShadowColor,
            "hilited_text_color" => colors.HilitedTextColor,
            "hilited_back_color" => colors.HilitedBackColor,
            "hilited_label_color" => colors.HilitedLabelColor,
            "hilited_candidate_text_color" => colors.HilitedCandidateTextColor,
            "hilited_candidate_back_color" => colors.HilitedCandidateBackColor,
            "hilited_candidate_label_color" => colors.HilitedCandidateLabelColor,
            "hilited_candidate_border_color" => colors.HilitedCandidateBorderColor,
            "hilited_comment_text_color" => colors.HilitedCommentTextColor,
            "hilited_mark_color" => colors.HilitedMarkColor,
            _ => null,
        };
    }

    private bool IsCustomThemeSelected(bool isDay)
    {
        string? item = (isDay ? _dayThemeComboBox : _nightThemeComboBox).SelectedItem?.ToString();
        return string.Equals(item, "自定义", StringComparison.Ordinal);
    }

    private void OnThemeComboChanged(bool isDay)
    {
        string? item = (isDay ? _dayThemeComboBox : _nightThemeComboBox).SelectedItem?.ToString();
        if (!string.Equals(item, "自定义", StringComparison.Ordinal))
        {
            if (isDay) _dayBaseScheme = ResolveThemeKey(item);
            else _nightBaseScheme = ResolveThemeKey(item);
        }
        if (IsCustomThemeSelected(isDay))
        {
            _showingTemplateDefaults = false;
        }
        LoadColorFields();
        RefreshCarrierDependentUi();
    }

    private string? GetActiveSchemeName(bool isDay, string? dayScheme, string? nightScheme)
    {
        if (isDay)
            return _dayBaseScheme ?? dayScheme ?? TemplateService.GetStyleString("ColorScheme");
        return _nightBaseScheme ?? nightScheme ?? TemplateService.GetStyleString("ColorSchemeDark");
    }

    private static string ResolveTargetRootFromModel(ConfigModel model)
    {
        return Environment.ExpandEnvironmentVariables(model.SyncSettings.WindowsTargetRoot);
    }

    private ConfigModel BuildConfigModelFromControls()
    {
        ConfigModel current = GetEditingModel();
        string targetRoot = ResolveTargetRootFromModel(current);

        WeaselUserSettings weaselSettings = new()
        {
            ColorScheme = IsCustomThemeSelected(isDay: true)
                ? "rimekit_custom_day"
                : _dayBaseScheme ?? ResolveThemeKey(_dayThemeComboBox.SelectedItem?.ToString()),
            ColorSchemeDark = IsCustomThemeSelected(isDay: false)
                ? "rimekit_custom_night"
                : _nightBaseScheme ?? ResolveThemeKey(_nightThemeComboBox.SelectedItem?.ToString()),
            CustomDayBaseScheme = IsCustomThemeSelected(isDay: true) ? _dayBaseScheme : null,
            CustomNightBaseScheme = IsCustomThemeSelected(isDay: false) ? _nightBaseScheme : null,
            FontFace = ResolveFontFace(),
            FontPoint = ResolveIntField(_fontSizeText, "FontPoint"),
            ShowNotification = _statusNotificationCheckBox.Checked,
            LabelFontFace = ResolveTextBox(_labelFontTextBox),
            LabelFontPoint = ResolveIntField(_labelFontSizeText, "LabelFontPoint"),
            CommentFontFace = ResolveTextBox(_commentFontTextBox),
            CommentFontPoint = ResolveIntField(_commentFontSizeText, "CommentFontPoint"),
            NotificationTimeMs = ResolveIntField(_notificationTimeText, "NotificationTimeMs"),
            LabelFormat = ResolveTextBox(_labelFormatTextBox),
            MarkText = ResolveTextBox(_markTextTextBox),
            PagingOnScroll = _pagingOnScrollCheckBox.Checked,
            CandidateAbbreviateLength = ResolveIntField(_candidateAbbreviateText, "CandidateAbbreviateLength"),
            Fullscreen = _fullscreenCheckBox.Checked,
            VerticalText = _verticalTextCheckBox.Checked,
            VerticalTextLeftToRight = _verticalTextLeftToRightCheckBox.Checked,
            VerticalTextWithWrap = _verticalTextWithWrapCheckBox.Checked,
            VerticalAutoReverse = _verticalAutoReverseCheckBox.Checked,
            InlinePreedit = _inlinePreeditCheckBox.Checked,
            PreeditType = ResolvePreeditType(),
            GlobalAscii = ResolveGlobalAscii(),
            HoverType = ResolveHoverType(),
            ClickToCapture = _clickToCaptureCheckBox.Checked,
            AntialiasMode = ResolveAntialiasMode(),
            DisplayTrayIcon = _displayTrayIconCheckBox.Checked,
            EnhancedPosition = _enhancedPositionCheckBox.Checked,
            AsciiTipFollowCursor = _asciiTipFollowCursorCheckBox.Checked,
            LayoutMinWidth = ResolveIntField(_layoutMinWidthText, "LayoutMinWidth"),
            LayoutMaxWidth = ResolveIntField(_layoutMaxWidthText, "LayoutMaxWidth"),
            LayoutMinHeight = ResolveIntField(_layoutMinHeightText, "LayoutMinHeight"),
            LayoutMaxHeight = ResolveIntField(_layoutMaxHeightText, "LayoutMaxHeight"),
            LayoutMarginX = ResolveIntField(_layoutMarginXText, "LayoutMarginX"),
            LayoutMarginY = ResolveIntField(_layoutMarginYText, "LayoutMarginY"),
            LayoutBorderWidth = ResolveIntField(_layoutBorderWidthText, "LayoutBorderWidth"),
            LayoutLineSpacing = ResolveIntField(_layoutLineSpacingText, "LayoutLineSpacing"),
            LayoutBaseline = ResolveIntField(_layoutBaselineText, "LayoutBaseline"),
            LayoutSpacing = ResolveIntField(_layoutSpacingText, "LayoutSpacing"),
            LayoutCandidateSpacing = ResolveIntField(_layoutCandidateSpacingText, "LayoutCandidateSpacing"),
            LayoutHiliteSpacing = ResolveIntField(_layoutHiliteSpacingText, "LayoutHiliteSpacing"),
            LayoutHilitePadding = ResolveIntField(_layoutHilitePaddingText, "LayoutHilitePadding"),
            LayoutHilitePaddingX = ResolveIntField(_layoutHilitePaddingXText, "LayoutHilitePaddingX"),
            LayoutHilitePaddingY = ResolveIntField(_layoutHilitePaddingYText, "LayoutHilitePaddingY"),
            LayoutShadowRadius = ResolveIntField(_layoutShadowRadiusText, "LayoutShadowRadius"),
            LayoutShadowOffsetX = ResolveIntField(_layoutShadowOffsetXText, "LayoutShadowOffsetX"),
            LayoutShadowOffsetY = ResolveIntField(_layoutShadowOffsetYText, "LayoutShadowOffsetY"),
            LayoutCornerRadius = ResolveIntField(_layoutCornerRadiusText, "LayoutCornerRadius"),
            LayoutAlignType = ResolveLayoutAlignType(),
            DayColors = IsCustomThemeSelected(isDay: true)
                ? (_editDayRadio.Checked ? BuildSchemeColors(isDay: true) : LoadSchemeColorsFromCurrent(targetRoot, isDay: true))
                : null,
            NightColors = IsCustomThemeSelected(isDay: false)
                ? (_editNightRadio.Checked ? BuildSchemeColors(isDay: false) : LoadSchemeColorsFromCurrent(targetRoot, isDay: false))
                : null,
        };

        string candidateLayout = string.Equals(_candidateDirectionComboBox.SelectedItem?.ToString(), "横排", StringComparison.Ordinal) ? "linear"
            : string.Equals(_candidateDirectionComboBox.SelectedItem?.ToString(), "竖排", StringComparison.Ordinal) ? "stacked" : "";

        MintUserSettings mintSettings = new()
        {
            PageSize = (int)_candidateCountNumeric.Value > 0 ? (int)_candidateCountNumeric.Value : null,
            Layout = candidateLayout.Length > 0 ? candidateLayout : null,
            ShowEmojiComments = _candidateCommentCheckBox.Checked,
            SimplificationMode = _traditionalRadio.Checked ? "traditional" : "simplified",
            FullShapeEnabled = _fullShapeRadio.Checked,
            AsciiPunctEnabled = _asciiPunctCheckBox.Checked,
            EmojiSuggestionEnabled = _emojiCheckBox.Checked,
            ToneDisplayEnabled = _toneCheckBox.Checked,
            EnableUserDict = _enableUserDictCheckBox.Checked,
            FuzzyEnabled = _fuzzyCheckBox.Checked ? true : null,
            FuzzyAdditionalRules = _fuzzyCheckBox.Checked
                ? _fuzzyRules.Select(row => $"derive/{row.From}/{row.To}").ToArray()
                : [],
            UeCompatEnabled = _ueCompatCheckBox.Checked ? true : null,
            CustomPhraseMode = ResolveCustomPhraseMode(),
        };

        UserSettingsReader.WriteWeaselCrossLayer(targetRoot, weaselSettings, mintSettings);
        UserSettingsReader.WriteMint(targetRoot, "rime_mint", mintSettings);

        if (current.ModelSettings.EnabledModelIds.Count > 0)
        {
            UserSettingsReader.WriteGrammarDefaults(targetRoot, "rime_mint");
        }

        return new ConfigModel
        {
            ConfigVersion = current.ConfigVersion,
            ProfileSettings = current.ProfileSettings,
            FuzzyPinyinSettings = new FuzzyPinyinSettings
            {
                PresetId = _fuzzyCheckBox.Checked ? "cn_common" : string.Empty,
                TargetSchemaIds = _fuzzyCheckBox.Checked && current.FuzzyPinyinSettings.TargetSchemaIds.Count == 0
                    ? current.ProfileSettings.EnabledSchemaIds
                    : current.FuzzyPinyinSettings.TargetSchemaIds,
            },
            PersonalizationSettings = new PersonalizationSettings
            {
                SymbolProfileId = current.PersonalizationSettings.SymbolProfileId,
                PreeditFormatMode = ResolvePreeditFormatMode(),
                CommentStyleVariant = ResolveCommentStyleVariant(),
            },
            DictionarySettings = new DictionarySettings
            {
                EnabledDictionaryIds = BuildDictionaryEnabledIds(current.DictionarySettings.EnabledDictionaryIds, _userEntries),
                DictionaryOrder = BuildDictionaryOrder(current.DictionarySettings.DictionaryOrder, _userEntries),
                CustomEntries = _userEntries.Select(row => new CustomEntry { Text = row.Text, Code = row.Code, Weight = 1 }).ToArray(),
            },
            ModelSettings = new ModelSettings
            {
                EnabledModelIds = current.ModelSettings.EnabledModelIds,
                ActiveModelId = current.ModelSettings.ActiveModelId,
                ModelRoot = current.ModelSettings.ModelRoot,
                ModelVersions = current.ModelSettings.ModelVersions,
            },
            SyncSettings = current.SyncSettings,
            AndroidSettings = current.AndroidSettings,
            WindowsSettings = new WindowsSettings
            {
                DpiScaleMode = current.WindowsSettings.DpiScaleMode,
            },
        };
    }

    private void ResetCurrentSettings()
    {
        ConfigModel defaultModel = ConfigModel.CreateDefault();
        LoadConfigIntoControls(defaultModel, useTemplateDefaults: true);
        RefreshCarrierDependentUi();
    }

    private void ShowUnsupportedAction(string message)
    {
        if (UnsupportedActionObserver is not null)
        {
            UnsupportedActionObserver(message);
            return;
        }

        MessageBox.Show(this, message, "当前未接入", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private ConfigModel GetEditingModel()
    {
        return File.Exists(_configModelPath)
            ? _workflowService.GetConfigModelForEditing(_configModelPath)
            : _workflowService.GetConfigModelForEditing(null);
    }

    private bool IsCarrierAvailable()
    {
        return _workflowService.IsWeaselAvailable();
    }

    private void RefreshCarrierDependentUi()
    {
        bool carrierAvailable = IsCarrierAvailable();
        bool schemeAvailable = IsRimeMintSchemeAvailable();
        foreach (Control control in _carrierDependentControls)
        {
            if (control is not null && !control.IsDisposed)
            {
                control.Enabled = carrierAvailable;
            }
        }

        bool settingsEnabled = carrierAvailable && schemeAvailable;

        if (!settingsEnabled)
        {
            if (!_templatesAreMissing && !TemplateService.TemplatesAreAvailable())
            {
                _templatesAreMissing = true;
                ResolveAnnotationPlaceholders();
            }
        }
        else if (_templatesAreMissing)
        {
            if (TemplateService.TemplatesAreAvailable())
            {
                _templatesAreMissing = false;
                LoadConfigIntoControls(GetEditingModel());
                ResolveAnnotationPlaceholders();
            }
            else
            {
                ResolveAnnotationPlaceholders();
            }
        }

        foreach (Button btn in _settingsApplyBtns)
        {
            if (btn is not null && !btn.IsDisposed)
            {
                btn.Enabled = carrierAvailable && schemeAvailable && !_templatesAreMissing;
            }
        }

        if (_dictInstallBtn is not null && !_dictInstallBtn.IsDisposed) _dictInstallBtn.Enabled = carrierAvailable;
        if (_dictInstallFromFileBtn is not null && !_dictInstallFromFileBtn.IsDisposed) _dictInstallFromFileBtn.Enabled = carrierAvailable;
        if (_dictUninstallBtn is not null && !_dictUninstallBtn.IsDisposed) _dictUninstallBtn.Enabled = carrierAvailable && schemeAvailable && IsSelectedDictionaryInstalled();
        if (_modelInstallBtn is not null && !_modelInstallBtn.IsDisposed) _modelInstallBtn.Enabled = carrierAvailable;
        if (_modelInstallFromFileBtn is not null && !_modelInstallFromFileBtn.IsDisposed) _modelInstallFromFileBtn.Enabled = carrierAvailable;
        if (_modelUninstallBtn is not null && !_modelUninstallBtn.IsDisposed) _modelUninstallBtn.Enabled = carrierAvailable && schemeAvailable && IsSelectedModelInstalled();
        if (_dictDetectStatusBtn is not null && !_dictDetectStatusBtn.IsDisposed) _dictDetectStatusBtn.Enabled = carrierAvailable;
        if (_modelDetectStatusBtn is not null && !_modelDetectStatusBtn.IsDisposed) _modelDetectStatusBtn.Enabled = carrierAvailable;
        if (_dictDetectLocalBtn is not null && !_dictDetectLocalBtn.IsDisposed) _dictDetectLocalBtn.Enabled = carrierAvailable;
        if (_modelDetectLocalBtn is not null && !_modelDetectLocalBtn.IsDisposed) _modelDetectLocalBtn.Enabled = carrierAvailable;
        if (_schemeDetectBtn is not null && !_schemeDetectBtn.IsDisposed) _schemeDetectBtn.Enabled = carrierAvailable;
        foreach (Button btn in _settingsDetectBtns)
        {
            if (btn is not null && !btn.IsDisposed)
            {
                btn.Enabled = carrierAvailable;
            }
        }

        foreach (Control control in _settingsControls)
        {
            if (control is not null && !control.IsDisposed)
            {
                control.Enabled = settingsEnabled;
            }
        }

        bool dayCustom = IsCustomThemeSelected(isDay: true);
        bool nightCustom = IsCustomThemeSelected(isDay: false);
        bool colorFieldsEditable = (_editDayRadio.Checked ? dayCustom : nightCustom) && settingsEnabled;
        foreach (FlowLayoutPanel? field in new FlowLayoutPanel?[]
        {
            _textColorField, _candidateTextColorField, _labelColorField, _commentTextColorField,
            _backColorField, _candidateBackColorField, _borderColorField, _shadowColorField,
            _hilitedTextColorField, _hilitedBackColorField, _hilitedLabelColorField,
            _hilitedCandidateTextColorField, _hilitedCandidateBackColorField, _hilitedCandidateLabelColorField,
            _hilitedCandidateBorderColorField, _hilitedCommentTextColorField, _hilitedMarkColorField,
        })
        {
            if (field is not null && !field.IsDisposed)
                field.Enabled = colorFieldsEditable;
        }

        if (_fuzzyRulesGrid is not null && !_fuzzyRulesGrid.IsDisposed)
        {
            _fuzzyRulesGrid.ReadOnly = !settingsEnabled;
        }

        RefreshSchemeButtonCapability();

        if (!carrierAvailable && _prevCarrierAvailable)
        {
            _carrierStatusDetected = false;
            _inputSchemeDetected = false;
            _detectedDictionaryStatusName = null;
            _detectedModelStatusName = null;
            RenderCarrierInfo();
            RenderSchemeInfo();
            RenderDictionaryDetail();
            RenderModelDetail();
        }

        _prevCarrierAvailable = carrierAvailable;

        if (_templatesAreMissing && settingsEnabled)
        {
            string? diag = TemplateService.TryEnsureTemplateDiagnostic();
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = diag ?? "⚠ 无法加载设置默认值。模板文件缺失，请重新安装输入方案以生成模板缓存。";
        }
        else if (!_templatesAreMissing && settingsEnabled)
        {
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.Text = string.Empty;
        }
        else
        {
            string reason = !carrierAvailable
                ? "⚠ 承载器未安装。请先安装小狼毫承载器，再安装输入方案。"
                : "⚠ 输入方案未部署，无法加载设置默认值。请先安装薄荷方案。";
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = reason;
        }
    }

    private bool IsSelectedDictionaryInstalled()
    {
        string? selected = _dictionaryListBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return false;
        }

        string? dictionaryId = ResolveDictionaryId(selected);
        if (string.IsNullOrWhiteSpace(dictionaryId))
        {
            return false;
        }

        if (string.Equals(dictionaryId, "rime_mint", StringComparison.OrdinalIgnoreCase))
        {
            ConfigModel model = GetEditingModel();
            return _workflowService.GetInstalledSchemaIds().Contains("rime_mint") && AreRimeMintRuntimeFilesPresent(model);
        }

        return _workflowService.GetInstalledDictionaryIds().Contains(dictionaryId);
    }

    private bool IsSelectedModelInstalled()
    {
        string? selected = _modelListBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return false;
        }

        string? modelId = ResolveModelId(selected);
        return !string.IsNullOrWhiteSpace(modelId) && _workflowService.GetInstalledModelIds().Contains(modelId);
    }

    private bool IsRimeMintSchemeAvailable()
    {
        ConfigModel model = GetEditingModel();
        return _workflowService.GetInstalledSchemaIds().Contains("rime_mint")
               && AreRimeMintRuntimeFilesPresent(model);
    }

    private static IReadOnlyList<string> BuildDictionaryEnabledIds(IReadOnlyList<string> current, BindingList<UserEntryRow> userEntries)
    {
        List<string> ids = [.. current];
        if (userEntries.Count > 0 && !ids.Contains("custom_simple", StringComparer.OrdinalIgnoreCase))
        {
            ids.Add("custom_simple");
        }

        return ids;
    }

    private static IReadOnlyList<string> BuildDictionaryOrder(IReadOnlyList<string> current, BindingList<UserEntryRow> userEntries)
    {
        List<string> order = [.. current];
        if (userEntries.Count > 0 && !order.Contains("custom_simple", StringComparer.OrdinalIgnoreCase))
        {
            order.Add("custom_simple");
        }

        return order;
    }

    private bool IsDictionaryUsable(string dictionaryId)
    {
        if (string.Equals(dictionaryId, "custom_simple", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _workflowService.GetInstalledDictionaryIds().Contains(dictionaryId);
    }

    private List<Control> _carrierDependentControls = [];
    private readonly List<Button> _settingsApplyBtns = [];
    private readonly List<Label> _annotationLabels = [];
    private readonly Dictionary<Label, string> _annotationOriginals = [];
    private readonly List<Button> _settingsDetectBtns = [];
    private readonly List<Control> _settingsControls = [];
    private Button[] _fuzzyActionButtons = [];

    private void RegisterCarrierDependent(Control control)
    {
        _carrierDependentControls.Add(control);
    }

    private static readonly Dictionary<string, FuzzyRuleRow> NormalizedFuzzyRuleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["derive/zh/z"] = new FuzzyRuleRow("zh", "z"),
        ["derive/ch/c"] = new FuzzyRuleRow("ch", "c"),
        ["derive/sh/s"] = new FuzzyRuleRow("sh", "s"),
        ["derive/^zh([a-z]+)$/z$1/"] = new FuzzyRuleRow("zh", "z"),
        ["derive/^ch([a-z]+)$/c$1/"] = new FuzzyRuleRow("ch", "c"),
        ["derive/^sh([a-z]+)$/s$1/"] = new FuzzyRuleRow("sh", "s"),
    };

    private static bool TryParseFuzzyRule(string rule, out FuzzyRuleRow row)
    {
        if (NormalizedFuzzyRuleMap.TryGetValue(rule, out FuzzyRuleRow? normalized))
        {
            row = normalized;
            return true;
        }

        row = new FuzzyRuleRow(string.Empty, string.Empty);
        string[] parts = rule.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        row = new FuzzyRuleRow(parts[1], parts[2]);
        return true;
    }

    private static IReadOnlyList<string> ExpandFuzzyRulesForDisplay(FuzzyPinyinSettings settings)
    {
        List<string> rules = [];
        if (string.Equals(settings.PresetId, "cn_common", StringComparison.OrdinalIgnoreCase))
        {
            rules.AddRange(GetProductFuzzyPresetRules(settings.PresetId));
        }
        return rules;
    }

    private static string[] GetProductFuzzyPresetRules(string? presetId)
    {
        return string.Equals(presetId, "cn_common", StringComparison.OrdinalIgnoreCase)
            ? ["derive/zh/z", "derive/ch/c", "derive/sh/s"]
            : [];
    }

    private static string ResolveThemeLabel(string? themeKey)
    {
        return themeKey switch
        {
            "mint_light_blue" => "蓝水鸭",
            "mint_light_green" => "碧皓青",
            "mint_dark_blue" => "黑水鸭",
            "mint_dark_green" => "碧月青",
            "rimekit_custom_day" => "自定义",
            "rimekit_custom_night" => "自定义",
            _ => string.Empty,
        };    }

    private static string ResolveThemeKey(string? themeLabel)
    {
        return themeLabel switch
        {
            "蓝水鸭" => "mint_light_blue",
            "碧皓青" => "mint_light_green",
            "黑水鸭" => "mint_dark_blue",
            "碧月青" => "mint_dark_green",
            _ => string.Empty,
        };    }

    private string ResolveFontFace()
    {
        string? text = _fontTextBox.Text?.Trim();
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    private static void SelectComboByValue(ComboBox combo, string? value)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static string ResolveTextBox(TextBox box)
    {
        string? text = box.Text?.Trim();
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    private static int StyleIntOrZero(NumericUpDown numeric, string fieldName)
    {
        int val = (int)numeric.Value;
        int? template = TemplateService.GetStyleInt(fieldName);
        if (template.HasValue && val == template.Value)
            return 0;
        return val;
    }

    private static int SchemaIntOrDefault(NumericUpDown numeric, string fieldName)
    {
        int val = (int)numeric.Value;
        int? template = TemplateService.GetSchemaInt(fieldName);
        if (template.HasValue && val == template.Value)
            return 0;
        return val;
    }

    private static int? StyleIntOrNull(NumericUpDown numeric, string fieldName)
    {
        int val = (int)numeric.Value;
        int? template = TemplateService.GetStyleInt(fieldName);
        if (template.HasValue && val == template.Value)
            return null;
        return val == 0 ? null : val;
    }

    private static bool? BoolOrNullIfDefault(bool controlValue, string schemaField, string? weaselField = null)
    {
        bool? template = TemplateService.GetSchemaBool(schemaField)
            ?? (weaselField is not null ? TemplateService.GetStyleBool(weaselField) : null);
        if (template.HasValue && controlValue == template.Value)
            return null;
        return controlValue;
    }

    private static string StringOrEmptyIfDefault(string controlValue, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(controlValue))
            return string.Empty;
        string? template = TemplateService.GetStyleString(fieldName)
            ?? TemplateService.GetSchemaString(fieldName);
        if (template is not null && string.Equals(controlValue, template, StringComparison.Ordinal))
            return string.Empty;
        return controlValue;
    }

    private string ResolvePreeditType()
    {
        string? selected = _preeditTypeComboBox.SelectedItem?.ToString();
        return selected switch { "编码区模式" => "composition", "预览区模式" => "preview", "预览全部" => "preview_all", _ => string.Empty };
    }

    private string ResolvePreeditFormatMode()
    {
        string? selected = _preeditFormatComboBox.SelectedItem?.ToString();
        return selected switch { "原始编码" => "raw_code", "翻译编码" => "translated_code", _ => "upstream_default" };
    }

    private string ResolveCustomPhraseMode()
    {
        string? selected = _customPhraseComboBox.SelectedItem?.ToString();
        return selected switch { "关闭" => "disabled", "完整短语" => "full_phrase", "简码匹配" => "simple_code_only", _ => "disabled" };
    }

    private string ResolveCommentStyleVariant()
    {
        string? selected = _commentStyleComboBox.SelectedItem?.ToString();
        return selected switch { "不显示" => "none", "中文" => "chinese", "拉丁" => "latin", "混合" => "mixed", _ => "default" };
    }

    private bool? ResolveGlobalAscii()
    {
        string? selected = _globalAsciiComboBox.SelectedItem?.ToString();
        return selected switch { "全局同步" => true, "每窗口独立" => false, _ => null };
    }

    private string ResolveHoverType()
    {
        string? selected = _hoverTypeComboBox.SelectedItem?.ToString();
        return selected switch { "半高亮" => "semi_hilite", "高亮" => "hilite", _ => string.Empty };
    }

    private string ResolveAntialiasMode()
    {
        string? selected = _antialiasModeComboBox.SelectedItem?.ToString();
        return selected switch { "ClearType" => "cleartype", "灰度" => "grayscale", "无" => "aliased", _ => string.Empty };
    }

    private string ResolveLayoutAlignType()
    {
        string? selected = _layoutAlignTypeComboBox.SelectedItem?.ToString();
        return selected switch { "居上" => "top", "居中" => "center", "居下" => "bottom", _ => string.Empty };
    }

    private SchemeColors BuildSchemeColors(bool isDay)
    {
        string? schemeName = isDay ? _dayBaseScheme : _nightBaseScheme
            ?? ResolveThemeKey((isDay ? _dayThemeComboBox : _nightThemeComboBox).SelectedItem?.ToString());
        return new SchemeColors
        {
            TextColor = ResolveCustomColorValue(_textColorField, schemeName, "text_color"),
            CandidateTextColor = ResolveCustomColorValue(_candidateTextColorField, schemeName, "candidate_text_color"),
            LabelColor = ResolveCustomColorValue(_labelColorField, schemeName, "label_color"),
            CommentTextColor = ResolveCustomColorValue(_commentTextColorField, schemeName, "comment_text_color"),
            BackColor = ResolveCustomColorValue(_backColorField, schemeName, "back_color"),
            CandidateBackColor = ResolveCustomColorValue(_candidateBackColorField, schemeName, "candidate_back_color"),
            BorderColor = ResolveCustomColorValue(_borderColorField, schemeName, "border_color"),
            ShadowColor = ResolveCustomColorValue(_shadowColorField, schemeName, "shadow_color"),
            HilitedTextColor = ResolveCustomColorValue(_hilitedTextColorField, schemeName, "hilited_text_color"),
            HilitedBackColor = ResolveCustomColorValue(_hilitedBackColorField, schemeName, "hilited_back_color"),
            HilitedLabelColor = ResolveCustomColorValue(_hilitedLabelColorField, schemeName, "hilited_label_color"),
            HilitedCandidateTextColor = ResolveCustomColorValue(_hilitedCandidateTextColorField, schemeName, "hilited_candidate_text_color"),
            HilitedCandidateBackColor = ResolveCustomColorValue(_hilitedCandidateBackColorField, schemeName, "hilited_candidate_back_color"),
            HilitedCandidateLabelColor = ResolveCustomColorValue(_hilitedCandidateLabelColorField, schemeName, "hilited_candidate_label_color"),
            HilitedCandidateBorderColor = ResolveCustomColorValue(_hilitedCandidateBorderColorField, schemeName, "hilited_candidate_border_color"),
            HilitedCommentTextColor = ResolveCustomColorValue(_hilitedCommentTextColorField, schemeName, "hilited_comment_text_color"),
            HilitedMarkColor = ResolveCustomColorValue(_hilitedMarkColorField, schemeName, "hilited_mark_color"),
        };
    }

    private static SchemeColors? LoadSchemeColorsFromCurrent(string targetRoot, bool isDay)
    {
        WeaselUserSettings current = UserSettingsReader.ReadWeasel(targetRoot);
        return isDay ? current.DayColors : current.NightColors;
    }

    private static string ResolveIntFieldText(int? userValue, string templateField, bool carrierOk, bool schemeOk)
    {
        if (userValue.HasValue)
            return userValue.Value.ToString();
        if (carrierOk && schemeOk)
        {
            int? t = TemplateService.GetSchemaInt(templateField) ?? TemplateService.GetStyleInt(templateField) ?? TemplateService.GetLayoutInt(templateField);
            if (t.HasValue)
                return t.Value.ToString();
        }
        return string.Empty;
    }

    private static int? ResolveIntField(TextBox box, string templateField)
    {
        string? text = box.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int n))
            return null;
        int? t = TemplateService.GetStyleInt(templateField) ?? TemplateService.GetLayoutInt(templateField) ?? TemplateService.GetSchemaInt(templateField);
        return t.HasValue && n == t.Value ? null : n;
    }

    private static string? ResolveCustomColorValue(FlowLayoutPanel field, string? schemeName, string colorFieldName)
    {
        string? hex = GetColorTextBox(field).Text?.Trim();
        string? templateDefault = schemeName is { Length: > 0 } ? TemplateService.GetColorSchemeColor(schemeName, colorFieldName) : null;
        if (string.IsNullOrWhiteSpace(hex))
            return templateDefault;
        return hex;
    }

    private static string ResolveComboValue(ComboBox combo, string defaultValue)
    {
        string? selected = combo.SelectedItem?.ToString();
        return string.Equals(selected, defaultValue, StringComparison.Ordinal) ? string.Empty : selected ?? string.Empty;
    }

    private static string? ResolveSchemeResourceId(string displayName)
    {
        return displayName switch
        {
            "薄荷拼音-全拼输入" => "rime_mint",
            "朙月拼音" => "luna_pinyin",
            "朙月拼音-语句流" => "luna_pinyin_fluency",
            "朙月拼音-台湾正体" => "luna_pinyin_tw",
            "朙月拼音-全拼" => "luna_quanpin",
            "自然码双拼" => "double_pinyin",
            "小鹤双拼" => "double_pinyin_flypy",
            "微软双拼" => "double_pinyin_mspy",
            "搜狗双拼" => "double_pinyin_sogou",
            "紫光双拼" => "double_pinyin_ziguang",
            "智能ABC双拼" => "double_pinyin_abc",
            "注音" => "bopomofo",
            "仓颉五代" => "cangjie5",
            "五笔" => "wubi86",
            "粤拼" => "jyutping",
            "地球拼音" => "terra_pinyin",
            "Emoji" => "emoji",
            "五笔画" => "stroke",
            "行列30" => "array30",
            "宫保拼音" => "combo_pinyin",
            "快速仓颉" => "scj6",
            "Easy English" => "easy_en",
            "速成" => "stenotype",
            "袖珍简化字拼音" => "pinyin_simp",
            "吴语" => "wugniu",
            "中古全拼" => "zyenpheng",
            _ => null,
        };
    }

    private string GetSelectedSchemeDisplayName()
    {
        return (_schemeComboBox.SelectedItem as WindowsSchemeOption)?.DisplayName ?? "薄荷拼音-全拼输入";
    }

    private static string? ResolveDictionaryId(string displayName)
    {
        return displayName switch
        {
            "moetype" => "moetype",
            "搜狗网络流行新词" => "sogou_network_popular_words",
            "zhwiki" => "zhwiki",
            _ => null,
        };
    }

    private string ResolveSchemeDisplayName(string? schemaId)
    {
        if (string.IsNullOrWhiteSpace(schemaId))
            return "薄荷拼音-全拼输入";
        return GetWindowsSchemeOptions()
            .FirstOrDefault(item => string.Equals(item.SchemaId, schemaId, StringComparison.OrdinalIgnoreCase))
            ?.DisplayName
            ?? "薄荷拼音-全拼输入";
    }

    private string? ResolveSelectedSchemeId()
    {
        return (_schemeComboBox.SelectedItem as WindowsSchemeOption)?.SchemaId;
    }

    private static bool IsWindowsFormalSchemeLocked(string schemeId)
    {
        return false;
    }

    private WindowsSchemeOption[] GetWindowsSchemeOptions()
    {
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        List<WindowsSchemeOption> options = [];

        foreach (FormalResourceDescriptor descriptor in _workflowService.GetFormalSchemaDescriptors())
        {
            string schemaId = descriptor.ResourceId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(schemaId) || !seenIds.Add(schemaId))
            {
                continue;
            }

            options.Add(new WindowsSchemeOption(schemaId, descriptor.DisplayName ?? MapSchemaIdToDisplayName(schemaId)));
        }

        foreach (string installedId in _workflowService.GetInstalledSchemaIds())
        {
            if (string.IsNullOrWhiteSpace(installedId) || !seenIds.Add(installedId))
            {
                continue;
            }

            options.Add(new WindowsSchemeOption(installedId, MapSchemaIdToDisplayName(installedId)));
        }

        string carrierRoot = Environment.ExpandEnvironmentVariables(GetEditingModel().SyncSettings.WindowsTargetRoot);
        foreach (WindowsSchemeOption option in DiscoverRuntimeSchemaOptions(carrierRoot))
        {
            if (string.IsNullOrWhiteSpace(option.SchemaId) || !seenIds.Add(option.SchemaId))
            {
                continue;
            }

            options.Add(option);
        }

        if (options.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[GUI] 未发现任何输入方案, 使用产品默认 rime_mint");
            options.Add(new WindowsSchemeOption("rime_mint", "薄荷拼音-全拼输入"));
        }

        return [.. options];
    }

    private static List<WindowsSchemeOption> DiscoverRuntimeSchemaOptions(string carrierRoot)
    {
        List<WindowsSchemeOption> options = [];
        if (!Directory.Exists(carrierRoot))
        {
            return options;
        }

        foreach (string file in Directory.GetFiles(carrierRoot, "*.schema.yaml", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "default.schema.yaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string schemaId = Path.GetFileNameWithoutExtension(fileName).Replace(".schema", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(schemaId))
            {
                continue;
            }

            if (string.Equals(schemaId, "t9", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string displayName = MapSchemaIdToDisplayName(schemaId);
            try
            {
                string content = File.ReadAllText(file);
                string? yamlSchemaId = null;
                string? yamlName = null;
                bool inSchema = false;
                foreach (string rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
                {
                    string line = rawLine.TrimStart();
                    if (line.StartsWith("schema:", StringComparison.Ordinal) && !line.StartsWith("schema_id:", StringComparison.Ordinal) && !line.StartsWith("schema/", StringComparison.Ordinal))
                    {
                        inSchema = true;
                        continue;
                    }

                    if (inSchema)
                    {
                        if (line.StartsWith("schema_id:", StringComparison.OrdinalIgnoreCase) && yamlSchemaId is null)
                        {
                            yamlSchemaId = line["schema_id:".Length..].Trim().Trim('"');
                        }
                        else if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase) && yamlName is null)
                        {
                            yamlName = line["name:".Length..].Trim().Trim('"');
                        }

                        if (!line.StartsWith(' ') && !line.StartsWith('\t') &&
                            !line.StartsWith("schema_id:", StringComparison.OrdinalIgnoreCase) &&
                            !line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }

                    if (yamlSchemaId is not null && yamlName is not null)
                    {
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(yamlSchemaId))
                {
                    schemaId = yamlSchemaId;
                }

                if (!string.IsNullOrWhiteSpace(yamlName))
                {
                    displayName = yamlName;
                }

                if (string.Equals(schemaId, "t9", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                System.Diagnostics.Debug.WriteLine($"[GUI] ParseSchemaYaml({file}) failed: {ex.Message}");
            }

            options.Add(new WindowsSchemeOption(schemaId, displayName));
        }

        return options;
    }

    private static string MapSchemaIdToDisplayName(string schemaId)
    {
        return schemaId.ToLowerInvariant() switch
        {
            "rime_mint" => "薄荷拼音-全拼输入",
            "luna_pinyin" or "luna_pinyin_simp" => "朙月拼音",
            "luna_pinyin_fluency" => "朙月拼音-语句流",
            "luna_pinyin_tw" => "朙月拼音-台湾正体",
            "luna_quanpin" => "朙月拼音-全拼",
            "double_pinyin" => "自然码双拼",
            "double_pinyin_flypy" => "小鹤双拼",
            "double_pinyin_mspy" => "微软双拼",
            "double_pinyin_sogou" => "搜狗双拼",
            "double_pinyin_ziguang" => "紫光双拼",
            "double_pinyin_abc" => "智能ABC双拼",
            "bopomofo" or "bopomofo_tw" => "注音",
            "cangjie5" => "仓颉五代",
            "wubi86" or "wubi_pinyin" => "五笔",
            "jyutping" => "粤拼",
            "terra_pinyin" => "地球拼音",
            "emoji" => "Emoji",
            "stroke" => "五笔画",
            "array30" => "行列30",
            "combo_pinyin" => "宫保拼音",
            "scj6" => "快速仓颉",
            "easy_en" => "Easy English",
            "stenotype" => "速成",
            "pinyin_simp" => "袖珍简化字拼音",
            "wugniu" => "吴语",
            "zyenpheng" => "中古全拼",
            _ when schemaId.StartsWith("rime_mint", StringComparison.OrdinalIgnoreCase) =>
                schemaId.Length > "rime_mint".Length ? $"薄荷拼音-变体({schemaId.Substring("rime_mint".Length).TrimStart('_')})" : "薄荷拼音-全拼输入",
            _ => schemaId,
        };
    }

    private static string? ResolveModelId(string displayName)
    {
        return displayName switch
        {
            "万象官方语法模型" => "wanxiang_lts_zh_hans",
            _ => null,
        };
    }

    private static string? ResolveResourceId(string displayName)
    {
        return ResolveSchemeResourceId(displayName) ?? ResolveDictionaryId(displayName) ?? ResolveModelId(displayName);
    }

    private static string ExtractViewValue(string view, string label, string fallback)
    {
        foreach (string rawLine in view.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith(label + ":", StringComparison.Ordinal))
            {
                continue;
            }

            string value = line[(label.Length + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        return fallback;
    }

    private static bool AreRimeMintRuntimeFilesPresent(ConfigModel model)
    {
        string targetRoot = Environment.ExpandEnvironmentVariables(model.SyncSettings.WindowsTargetRoot);
        return File.Exists(Path.Combine(targetRoot, "default.custom.yaml")) &&
               File.Exists(Path.Combine(targetRoot, "rime_mint.custom.yaml")) &&
               File.Exists(Path.Combine(targetRoot, "rime_mint.dict.yaml"));
    }

    private string BuildSchemeStateLabel(ConfigModel model, string? schemaId)
    {
        if (!IsCarrierAvailable())
        {
            return "承载器未安装";
        }

        bool schemaInstalledInWorkspace = _workflowService.GetInstalledSchemaIds()
            .Contains(schemaId ?? "rime_mint", StringComparer.OrdinalIgnoreCase);

        if (!schemaInstalledInWorkspace)
        {
            return "未安装";
        }

        return IsLatestRecheckSuccessful() ? "已生效" : "已保存但当前无法自动确认";
    }

    private string BuildDictionaryStateLabel(ConfigModel model, string? dictionaryId, bool installed, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(dictionaryId))
        {
            return "当前无法自动确认";
        }

        if (!IsCarrierAvailable())
        {
            return "承载器未安装";
        }

        if (!installed)
        {
            return "未安装";
        }

        return IsLatestRecheckSuccessful() ? "已生效" : "已保存但当前无法自动确认";
    }

    private string BuildModelStateLabel(ConfigModel model, string? modelId, bool installed, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return "当前无法自动确认";
        }

        if (!IsCarrierAvailable())
        {
            return "承载器未安装";
        }

        if (!installed)
        {
            return "未安装";
        }

        return IsLatestRecheckSuccessful() ? "已生效" : "已保存但当前无法自动确认";
    }

    private bool IsLatestRecheckSuccessful()
    {
        string recheckPath = Path.Combine(ResolveStateRoot(StartDirectory), "last_recheck_summary.json");
        if (!File.Exists(recheckPath))
        {
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(recheckPath));
        string status = document.RootElement.TryGetProperty("status", out JsonElement statusElement)
            ? statusElement.GetString() ?? string.Empty
            : string.Empty;
        return string.Equals(status, WorkflowStatuses.Completed, StringComparison.OrdinalIgnoreCase);
    }

    private void RenderSyncStatus()
    {
        _syncStatusHost.Controls.Clear();
        _syncStatusHost.Controls.Add(
            CreateSummarySection(
                "最近同步状态",
                BuildSyncStatusRows()));
    }

    private void RenderBackupStatus()
    {
        _backupStatusHost.Controls.Clear();
        _backupStatusHost.Controls.Add(
            CreateSummarySection(
                "最近备份与恢复状态",
                BuildBackupStatusRows()));
    }

    private (string Label, string? Value)[] BuildSyncStatusRows()
    {
        string statePath = Path.Combine(ResolveStateRoot(StartDirectory), "latest_sync_status.json");
        if (!File.Exists(statePath))
        {
            return
            [
                ("最近一次同步动作", "还没有记录"),
                ("同步结果", "还没有记录"),
            ];
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(statePath));
        JsonElement root = document.RootElement;
        return
        [
            ("最近一次同步动作", ReadJsonString(root, "action", "还没有记录")),
            ("同步结果", MapWorkflowStatus(ReadJsonString(root, "status", "unknown"))),
            ("同步时间", FormatRecordedAt(ReadJsonString(root, "recorded_at", string.Empty))),
            ("同步来源", ReadJsonString(root, "source_path", "还没有记录")),
            ("同步目标", ReadJsonString(root, "target_path", "还没有记录")),
            ("本次同步的配置文件", JoinJsonArray(root, "config_files")),
            ("本次同步的词库", JoinJsonArray(root, "dictionary_ids", MapDictionaryId)),
            ("本次同步的模型", JoinJsonArray(root, "model_ids", MapModelId)),
            ("本次同步的用户数据", JoinJsonArray(root, "user_data_ids", MapUserDataId)),
            ("相关文件", JoinJsonArray(root, "related_files")),
        ];
    }

    private (string Label, string? Value)[] BuildBackupStatusRows()
    {
        string statePath = Path.Combine(ResolveStateRoot(StartDirectory), "latest_backup_status.json");
        if (!File.Exists(statePath))
        {
            return
            [
                ("最近一次备份或恢复动作", "还没有记录"),
                ("备份结果", "还没有记录"),
            ];
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(statePath));
        JsonElement root = document.RootElement;
        string includesUserData = root.TryGetProperty("includes_user_data", out JsonElement includesElement) &&
                                  includesElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? (includesElement.GetBoolean() ? "包含" : "未包含")
            : "当前无法自动确认";
        return
        [
            ("最近一次备份或恢复动作", ReadJsonString(root, "action", "还没有记录")),
            ("备份结果", MapWorkflowStatus(ReadJsonString(root, "status", "unknown"))),
            ("备份时间", FormatRecordedAt(ReadJsonString(root, "recorded_at", string.Empty))),
            ("覆盖文件", JoinJsonArray(root, "target_files")),
            ("资源状态", JoinJsonArray(root, "resource_state", MapResourceId)),
            ("用户数据", includesUserData),
            ("相关文件", JoinJsonArray(root, "related_files")),
        ];
    }

    private static string ReadJsonString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? fallback
            : fallback;
    }

    private static string JoinJsonArray(JsonElement root, string propertyName, Func<string, string>? map = null)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) || element.ValueKind != JsonValueKind.Array)
        {
            return "还没有记录";
        }

        string[] values = element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => map is null ? item : map(item))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return values.Length == 0 ? "还没有记录" : string.Join("、", values);
    }

    private static string FormatRecordedAt(string raw)
    {
        return DateTimeOffset.TryParse(raw, out DateTimeOffset value)
            ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : (string.IsNullOrWhiteSpace(raw) ? "还没有记录" : raw);
    }

    private static string MapWorkflowStatus(string rawStatus)
    {
        return rawStatus switch
        {
            WorkflowStatuses.Completed => "已经完成",
            WorkflowStatuses.Failed => "执行失败",
            WorkflowStatuses.Blocked => "当前被阻塞",
            _ => string.IsNullOrWhiteSpace(rawStatus) ? "还没有记录" : rawStatus,
        };
    }

    private static string MapDictionaryId(string resourceId)
    {
        return resourceId switch
        {
            "moetype" => "moetype",
            "sogou_network_popular_words" => "搜狗网络流行新词",
            "zhwiki" => "zhwiki",
            "custom_simple" => "用户词条",
            _ => resourceId,
        };
    }

    private static string MapModelId(string resourceId)
    {
        return resourceId switch
        {
            "wanxiang_lts_zh_hans" => "万象官方语法模型",
            _ => resourceId,
        };
    }

    private static string MapUserDataId(string resourceId)
    {
        return resourceId switch
        {
            "custom_entries" => "自定义词条",
            "user_dict_exports" => "用户词典",
            _ => resourceId,
        };
    }

    private static string MapResourceId(string resourceId)
    {
        return resourceId switch
        {
            "rime_mint" => "薄荷拼音-全拼输入",
            "moetype" => "moetype",
            "sogou_network_popular_words" => "搜狗网络流行新词",
            "zhwiki" => "zhwiki",
            "custom_simple" => "用户词条",
            "wanxiang_lts_zh_hans" => "万象官方语法模型",
            _ => resourceId,
        };
    }

    private static CommandExecutionResult CreateDetectionProbeResult(string detail)
    {
        return new CommandExecutionResult
        {
            ExitCode = 0,
            TextOutput = detail,
        };
    }

    private static string ResolveStateRoot(string startDirectory)
    {
        string root = TemplateService.RepositoryRoot
            ?? throw new InvalidOperationException("TemplateService.RepositoryRoot 未初始化。");
        return Path.Combine(root, "workspace", "windows", "state");
    }

    private static string ResolveCurrentConfigModelPath(string startDirectory)
    {
        string? overridePath = Environment.GetEnvironmentVariable("RIMEKIT_CURRENT_CONFIG_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridePath));
        }

        return Path.Combine(ResolveStateRoot(startDirectory), "current_config_model.json");
    }

    private Control CreateBottomSettingsActionBar(string areaName)
    {
        FlowLayoutPanel actions = CreateActionBar();
        actions.Margin = new Padding(0, 8, 0, 0);
        Button settingsDetectBtn = CreateInlineButton(
            "检测目前设置",
            async (_, _) => await RunWorkflowOperationAsync(
                $"正在检测 {areaName}…",
                _ =>
                {
                    var detail = _workflowService.BuildSettingsDetectionView(_configModelPath);
                    _workflowService.RunDoctor(_configModelPath, "text");
                    return CreateDetectionProbeResult(detail);
                },
                _ => LoadConfigIntoControls(GetEditingModel())));
        actions.Controls.Add(settingsDetectBtn);
        _settingsDetectBtns.Add(settingsDetectBtn);
        Button resetBtn = CreateInlineButton("重置设置", (_, _) => ResetCurrentSettings());
        actions.Controls.Add(resetBtn);
        RegisterCarrierDependent(resetBtn);
        Button settingsApplyBtn = CreateInlineButton("应用设置", async (_, _) => await SaveCurrentSettingsAsync(apply: true, $"正在应用 {areaName}…"));
        _settingsApplyBtns.Add(settingsApplyBtn);
        bool carrierAvailable = IsCarrierAvailable();
        bool schemeAvailable = IsRimeMintSchemeAvailable();
        settingsApplyBtn.Enabled = carrierAvailable && schemeAvailable;
        actions.Controls.Add(settingsApplyBtn);
        return actions;
    }

    private static TableLayoutPanel CreatePageLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private static Control CreateSelectorPage(Control selectorPanel, Control detailPanel)
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectorPanel.Dock = DockStyle.Fill;
        detailPanel.Dock = DockStyle.Fill;
        layout.Controls.Add(selectorPanel, 0, 0);
        layout.Controls.Add(detailPanel, 1, 0);
        return layout;
    }

    private static ListBox CreateSelectorListBox()
    {
        return new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
        };
    }

    private static FlowLayoutPanel CreateActionBar()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 10),
        };
    }

    private static Button CreateInlineButton(string text, EventHandler onClick, string? name = null)
    {
        Button button = new()
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 8),
            Padding = new Padding(10, 6, 10, 6),
            Text = text,
            UseVisualStyleBackColor = true,
        };
        button.Name = name ?? ("Btn_" + text);
        button.Click += (s, e) =>
        {
            try
            {
                onClick(s, e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GUI] Button '{button.Name}' handler: {ex.Message}");
            }
        };
        return button;
    }

    private static TableLayoutPanel CreateKeyValueGrid((string Label, string? Value)[] rows)
    {
        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = rows.Length,
            AutoSize = true,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int index = 0; index < rows.Length; index++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(CreateFieldLabel(rows[index].Label), 0, index);
            grid.Controls.Add(CreateValueLabel(rows[index].Value), 1, index);
        }

        return grid;
    }

    private static TableLayoutPanel CreateFieldGrid((string Label, Control Control)[] rows)
    {
        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = rows.Length,
            AutoSize = true,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int index = 0; index < rows.Length; index++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rows[index].Control.Margin = new Padding(0);
            grid.Controls.Add(CreateFieldLabel(rows[index].Label), 0, index);
            grid.Controls.Add(rows[index].Control, 1, index);
        }

        return grid;
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 6, 16, 6),
        };
    }

    private static Label CreateValueLabel(string? text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            AutoEllipsis = false,
            MaximumSize = new Size(0, 0),
            Margin = new Padding(0, 6, 16, 6),
        };
    }

    private static LinkLabel CreateLinkLabel(string text, string url)
    {
        LinkLabel link = new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 6, 16, 6),
        };
        link.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                System.Diagnostics.Debug.WriteLine($"[LinkLabel] Failed to open {url}: {ex.Message}");
            }
        };
        return link;
    }

    private static Label CreatePlainLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
            ForeColor = SystemColors.GrayText,
        };
    }

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 6, 0, 6),
        };
    }

    private static Label CreatePlaceholderLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
        };
    }

    private static ComboBox CreateComboBox(IEnumerable<string> items)
    {
        ComboBox comboBox = new()
        {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList,
        MinimumSize = new Size(420, 30),
        };
        comboBox.Items.AddRange(items.Cast<object>().ToArray());
        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }

        return comboBox;
    }

    private static NumericUpDown CreateNumeric(int minimum, int maximum, int value)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Width = 420,
        };
    }

    private static FlowLayoutPanel CreateRadioPanel(params RadioButton[] buttons)
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
            MinimumSize = new Size(240, 28),
        };
        foreach (RadioButton button in buttons)
        {
            button.Margin = new Padding(0, 2, 24, 2);
            panel.Controls.Add(button);
        }

        return panel;
    }

    private static FlowLayoutPanel CreateSingleLineOptionHost(Control control)
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            MinimumSize = new Size(180, 28),
        };
        control.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(control);
        return panel;
    }

    private static FlowLayoutPanel CreateInlineHintPanel(Control control, string hint)
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        control.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(control);
        panel.Controls.Add(CreatePlainLabel(hint));
        return panel;
    }

    private static DataGridView CreateEntryGrid(bool readOnly)
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ReadOnly = readOnly,
        };
        grid.Columns.Add(CreateTextColumn("Text", "词条", 50));
        grid.Columns.Add(CreateTextColumn("Code", "编码", 25));
        grid.Columns.Add(CreateTextColumn("State", "状态", 25));
        return grid;
    }

    private static DataGridView CreateFuzzyRuleGrid()
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        grid.Columns.Add(CreateTextColumn("From", "原本输入", 50));
        grid.Columns.Add(CreateTextColumn("To", "也接受输入", 50));
        return grid;
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(string propertyName, string headerText, int fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
    }

    private static Control CreateSummarySection(string title, (string Label, string? Value)[] rows)
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(CreateSectionLabel(title), 0, 0);
        TableLayoutPanel grid = CreateKeyValueGrid(rows);
        grid.Padding = new Padding(0, 4, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        return layout;
    }

    private static TabPage CreateTabPage(string title, Control content)
    {
        TabPage page = new()
        {
            Text = title,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static void ApplyFontRecursively(Control root, Font baseFont)
    {
        foreach (Control control in root.Controls)
        {
            control.Font = baseFont;
            if (control.HasChildren)
            {
                ApplyFontRecursively(control, baseFont);
            }
        }
    }

    private void ResolveAnnotationPlaceholders()
    {
        foreach (Label descLabel in _annotationLabels)
        {
            if (!_annotationOriginals.TryGetValue(descLabel, out string? text))
                continue;
            text = ReplacePlaceholder(text, "{FontPoint}", () => StylePointLabel(TemplateService.GetStyleInt("FontPoint")));
            text = ReplacePlaceholder(text, "{LabelFontPoint}", () => StylePointLabel(TemplateService.GetStyleInt("LabelFontPoint")));
            text = ReplacePlaceholder(text, "{CommentFontPoint}", () => StylePointLabel(TemplateService.GetStyleInt("CommentFontPoint")));
            text = ReplacePlaceholder(text, "{PageSize}", () => { int? v = TemplateService.GetSchemaInt("PageSize"); return v is null ? "⚠ 模板缺失" : $"{v}个候选"; });
            text = ReplacePlaceholder(text, "{NotificationTimeMs}", () => { int? v = TemplateService.GetStyleInt("NotificationTimeMs"); return v is null ? "⚠ 模板缺失" : $"{v}毫秒"; });
            text = ReplacePlaceholder(text, "{CandidateAbbreviateLength}", () => { int? v = TemplateService.GetStyleInt("CandidateAbbreviateLength"); return v is null ? "⚠ 模板缺失" : $"{v}个字母"; });
            text = ReplacePlaceholder(text, "{ColorScheme}", () => TemplateService.GetStyleString("ColorScheme") switch { "mint_light_blue" => "蓝水鸭", "mint_light_green" => "碧皓青", "mint_dark_blue" => "黑水鸭", "mint_dark_green" => "碧月青", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{ColorSchemeDark}", () => TemplateService.GetStyleString("ColorSchemeDark") switch { "mint_light_blue" => "蓝水鸭", "mint_light_green" => "碧皓青", "mint_dark_blue" => "黑水鸭", "mint_dark_green" => "碧月青", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{LabelFormat}", () => TemplateService.GetStyleString("LabelFormat") ?? "⚠ 模板缺失");
            text = ReplacePlaceholder(text, "{MarkText}", () => TemplateService.GetStyleString("MarkText") ?? "⚠ 模板缺失");
            text = ReplacePlaceholder(text, "{LayoutMinWidth}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutMinWidth")));
            text = ReplacePlaceholder(text, "{LayoutMinHeight}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutMinHeight")));
            text = ReplacePlaceholder(text, "{LayoutMaxWidth}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutMaxWidth")));
            text = ReplacePlaceholder(text, "{LayoutMaxHeight}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutMaxHeight")));
            text = ReplacePlaceholder(text, "{LayoutMarginX}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutMarginX")));
            text = ReplacePlaceholder(text, "{LayoutMarginY}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutMarginY")));
            text = ReplacePlaceholder(text, "{LayoutBorderWidth}", () => { int? w = TemplateService.GetLayoutInt("LayoutBorderWidth"); return w is null ? "⚠ 模板缺失" : w == 0 ? "无边框" : $"{w}px"; });
            text = ReplacePlaceholder(text, "{LayoutBaseline}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutBaseline")));
            text = ReplacePlaceholder(text, "{LayoutLinespacing}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutLineSpacing")));
            text = ReplacePlaceholder(text, "{LayoutSpacing}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutSpacing")));
            text = ReplacePlaceholder(text, "{LayoutCandidateSpacing}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutCandidateSpacing")));
            text = ReplacePlaceholder(text, "{LayoutHiliteSpacing}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutHiliteSpacing")));
            text = ReplacePlaceholder(text, "{LayoutHilitePadding}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutHilitePadding")));
            text = ReplacePlaceholder(text, "{LayoutHilitePaddingX}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutHilitePaddingX")));
            text = ReplacePlaceholder(text, "{LayoutHilitePaddingY}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutHilitePaddingY")));
            text = ReplacePlaceholder(text, "{LayoutShadowRadius}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutShadowRadius")));
            text = ReplacePlaceholder(text, "{LayoutShadowOffsetX}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutShadowOffsetX")));
            text = ReplacePlaceholder(text, "{LayoutShadowOffsetY}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutShadowOffsetY")));
            text = ReplacePlaceholder(text, "{LayoutCornerRadius}", () => LayoutPxLabel(TemplateService.GetLayoutInt("LayoutCornerRadius")));
            text = ReplacePlaceholder(text, "{LayoutAlignType}", () => TemplateService.GetStyleString("LayoutAlignType") switch { "top" => "居上（候选窗出现在光标下方）", "center" => "居中（候选窗出现在光标中间）", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{FontFace}", () => TemplateService.GetStyleString("FontFace") ?? "⚠ 模板缺失");
            text = ReplacePlaceholder(text, "{LabelFontFace}", () => TemplateService.GetStyleString("LabelFontFace") ?? "⚠ 模板缺失");
            text = ReplacePlaceholder(text, "{CommentFontFace}", () => TemplateService.GetStyleString("CommentFontFace") ?? "⚠ 模板缺失");
            text = ReplacePlaceholder(text, "{ShowNotification}", () => BoolLabel(TemplateService.GetStyleBool("ShowNotification")));
            text = ReplacePlaceholder(text, "{PagingOnScroll}", () => BoolLabel(TemplateService.GetStyleBool("PagingOnScroll")));
            text = ReplacePlaceholder(text, "{EnhancedPosition}", () => BoolLabel(TemplateService.GetStyleBool("EnhancedPosition")));
            text = ReplacePlaceholder(text, "{Fullscreen}", () => BoolLabel(TemplateService.GetStyleBool("Fullscreen")));
            text = ReplacePlaceholder(text, "{VerticalText}", () => BoolLabel(TemplateService.GetStyleBool("VerticalText")));
            text = ReplacePlaceholder(text, "{VerticalTextLeftToRight}", () => BoolLabel(TemplateService.GetStyleBool("VerticalTextLeftToRight")));
            text = ReplacePlaceholder(text, "{VerticalTextWithWrap}", () => BoolLabel(TemplateService.GetStyleBool("VerticalTextWithWrap")));
            text = ReplacePlaceholder(text, "{VerticalAutoReverse}", () => BoolLabel(TemplateService.GetStyleBool("VerticalAutoReverse")));
            text = ReplacePlaceholder(text, "{InlinePreedit}", () => BoolLabel(TemplateService.GetStyleBool("InlinePreedit")));
            text = ReplacePlaceholder(text, "{GlobalAscii}", () => TemplateService.GetStyleBool("GlobalAscii") switch { false => "每窗口独立", true => "全局同步", null => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{AsciiTipFollowCursor}", () => BoolLabel(TemplateService.GetStyleBool("AsciiTipFollowCursor")));
            text = ReplacePlaceholder(text, "{DisplayTrayIcon}", () => BoolLabel(TemplateService.GetStyleBool("DisplayTrayIcon")));
            text = ReplacePlaceholder(text, "{ClickToCapture}", () => BoolLabel(TemplateService.GetStyleBool("ClickToCapture")));
            text = ReplacePlaceholder(text, "{PreeditType}", () => TemplateService.GetStyleString("PreeditType") switch { "composition" => "编码区模式（拼音显示在光标附近）", "preview" => "预览区模式（拼音显示在候选窗中）", "preview_all" => "预览全部（完整预览模式）", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{HoverType}", () => TemplateService.GetStyleString("HoverType") switch { "" or "none" => "无效果（不触发任何悬停反应）", "semi_hilite" => "半高亮", "hilite" => "高亮", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{AntialiasMode}", () => TemplateService.GetStyleString("AntialiasMode") switch { "" or "default" => "系统默认（由系统决定，通常为 ClearType）", "cleartype" => "ClearType（子像素渲染，适合液晶屏）", "grayscale" => "灰度（灰度抗锯齿）", "aliased" => "无抗锯齿（不进行处理）", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{SimplificationMode}", () => TemplateService.GetSchemaString("SimplificationMode") switch { "" or "simplified" => "简体中文", "traditional" => "繁体中文", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{EmojiSuggestion}", () => BoolLabel(TemplateService.GetSchemaBool("EmojiSuggestionEnabled")));
            text = ReplacePlaceholder(text, "{ToneDisplay}", () => BoolLabel(TemplateService.GetSchemaBool("ToneDisplayEnabled")));
            text = ReplacePlaceholder(text, "{EnableUserDict}", () => BoolLabel(TemplateService.GetSchemaBool("EnableUserDict")));
            text = ReplacePlaceholder(text, "{FullShape}", () => TemplateService.GetSchemaBool("FullShapeEnabled") switch { true => "全角", false => "半角", null => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{AsciiPunct}", () => BoolLabel(TemplateService.GetSchemaBool("AsciiPunctEnabled")));
            text = ReplacePlaceholder(text, "{ShowEmojiComments}", () => BoolLabel(TemplateService.GetSchemaBool("EmojiSuggestionEnabled")));
            text = ReplacePlaceholder(text, "{Layout}", () => TemplateService.GetStyleString("CandidateListLayout") switch { "linear" => "横排", "" or "stacked" => "竖排", null => "⚠ 模板缺失", _ => "⚠ 模板缺失" });
            text = ReplacePlaceholder(text, "{TextColor}", () => ResolveColorPlaceholder("text_color"));
            text = ReplacePlaceholder(text, "{CandidateTextColor}", () => ResolveColorPlaceholder("candidate_text_color"));
            text = ReplacePlaceholder(text, "{LabelColor}", () => ResolveColorPlaceholder("label_color"));
            text = ReplacePlaceholder(text, "{CommentTextColor}", () => ResolveColorPlaceholder("comment_text_color"));
            text = ReplacePlaceholder(text, "{BackColor}", () => ResolveColorPlaceholder("back_color"));
            text = ReplacePlaceholder(text, "{CandidateBackColor}", () => ResolveColorPlaceholder("candidate_back_color"));
            text = ReplacePlaceholder(text, "{BorderColor}", () => ResolveColorPlaceholder("border_color"));
            text = ReplacePlaceholder(text, "{ShadowColor}", () => ResolveColorPlaceholder("shadow_color"));
            text = ReplacePlaceholder(text, "{HilitedTextColor}", () => ResolveColorPlaceholder("hilited_text_color"));
            text = ReplacePlaceholder(text, "{HilitedBackColor}", () => ResolveColorPlaceholder("hilited_back_color"));
            text = ReplacePlaceholder(text, "{HilitedLabelColor}", () => ResolveColorPlaceholder("hilited_label_color"));
            text = ReplacePlaceholder(text, "{HilitedCandidateTextColor}", () => ResolveColorPlaceholder("hilited_candidate_text_color"));
            text = ReplacePlaceholder(text, "{HilitedCandidateBackColor}", () => ResolveColorPlaceholder("hilited_candidate_back_color"));
            text = ReplacePlaceholder(text, "{HilitedCandidateLabelColor}", () => ResolveColorPlaceholder("hilited_candidate_label_color"));
            text = ReplacePlaceholder(text, "{HilitedCandidateBorderColor}", () => ResolveColorPlaceholder("hilited_candidate_border_color"));
            text = ReplacePlaceholder(text, "{HilitedCommentTextColor}", () => ResolveColorPlaceholder("hilited_comment_text_color"));
            text = ReplacePlaceholder(text, "{HilitedMarkColor}", () => ResolveColorPlaceholder("hilited_mark_color"));
            descLabel.Text = text;
        }
    }

    private static string ReplacePlaceholder(string text, string placeholder, Func<string> valueProvider)
    {
        if (!text.Contains(placeholder))
            return text;
        string value;
        try { value = valueProvider(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { System.Diagnostics.Debug.WriteLine($"[GUI] ReplacePlaceholder({placeholder}) failed: {ex.Message}"); value = "⚠ 模板解析失败"; }
        return text.Replace(placeholder, value);
    }

    private static string StylePointLabel(int? pt) => pt is null ? "⚠ 模板缺失" : $"{pt}pt";

    private static string LayoutPxLabel(int? px) => px is null ? "⚠ 模板缺失" : $"{px}px";

    private static string BoolLabel(bool? v) => v switch { true => "开启", false => "关闭", null => "⚠ 模板缺失" };

    private string ResolveColorPlaceholder(string colorFieldName)
    {
        string? schemeName = _editDayRadio.Checked ? _dayBaseScheme : _nightBaseScheme
            ?? ResolveThemeKey((_editDayRadio.Checked ? _dayThemeComboBox : _nightThemeComboBox).SelectedItem?.ToString());
        string? hex = schemeName is { Length: > 0 } ? TemplateService.GetColorSchemeColor(schemeName, colorFieldName) : null;
        return hex ?? "⚠ 模板缺失";
    }

    private sealed class UserEntryRow
    {
        public UserEntryRow(string text, string code, string state)
        {
            Text = text;
            Code = code;
            State = state;
        }

        public string Text { get; set; }

        public string Code { get; set; }

        public string State { get; set; }
    }

    private sealed class FuzzyRuleRow
    {
        public FuzzyRuleRow(string from, string to)
        {
            From = from;
            To = to;
        }

        public string From { get; set; }

        public string To { get; set; }
    }

    private sealed class WindowsSchemeOption
    {
        public WindowsSchemeOption(string schemaId, string displayName)
        {
            SchemaId = schemaId;
            DisplayName = displayName;
        }

        public string SchemaId { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }

    private static int? LayoutTemplateOrDefault(string fieldName)
    {
        return TemplateService.GetLayoutInt(fieldName);
    }

    private int NumericFromModelOrDefault(int modelValue, string templateField, NumericUpDown numeric, bool? carrierHint = null, bool? schemeHint = null)
    {
        if (modelValue > 0)
            return modelValue;
        bool carrierOk = carrierHint ?? IsCarrierAvailable();
        bool schemeOk = schemeHint ?? IsRimeMintSchemeAvailable();
        if (carrierOk && schemeOk)
        {
            int? template = TemplateService.GetStyleInt(templateField) ?? TemplateService.GetSchemaInt(templateField);
            if (template.HasValue)
                return template.Value;
        }
        return (int)numeric.Minimum;
    }

    private int? NumericNullableFromModelOrDefault(int modelValue, string templateField, NumericUpDown numeric)
    {
        if (modelValue > 0)
            return modelValue;
        if (IsCarrierAvailable() && IsRimeMintSchemeAvailable())
        {
            int? template = TemplateService.GetStyleInt(templateField);
            if (template.HasValue)
                return template.Value;
        }
        return null;
    }

    private bool BoolFromModelOrDefault(bool? modelValue, string templateField, string? weaselField = null, bool? carrierHint = null, bool? schemeHint = null)
    {
        if (modelValue.HasValue)
            return modelValue.Value;
        bool carrierOk = carrierHint ?? IsCarrierAvailable();
        bool schemeOk = schemeHint ?? IsRimeMintSchemeAvailable();
        if (carrierOk && schemeOk)
        {
            bool? template = TemplateService.GetSchemaBool(templateField) ?? (weaselField is not null ? TemplateService.GetStyleBool(weaselField) : null);
            if (template.HasValue)
                return template.Value;
        }
        return false;
    }

    private int LayoutFromModelOrDefault(int modelValue, string fieldName, NumericUpDown numeric, bool? carrierHint = null, bool? schemeHint = null)
    {
        if (modelValue > 0)
            return modelValue;
        bool carrierOk = carrierHint ?? IsCarrierAvailable();
        bool schemeOk = schemeHint ?? IsRimeMintSchemeAvailable();
        if (carrierOk && schemeOk)
        {
            int? template = TemplateService.GetLayoutInt(fieldName);
            if (template.HasValue)
                return template.Value;
        }
        return (int)numeric.Minimum;
    }

    private int? LayoutNullableFromModelOrDefault(int? modelValue, string fieldName, NumericUpDown numeric, bool? carrierHint = null, bool? schemeHint = null)
    {
        if (modelValue.HasValue)
            return modelValue;
        if ((carrierHint ?? IsCarrierAvailable()) && (schemeHint ?? IsRimeMintSchemeAvailable()))
        {
            int? template = TemplateService.GetLayoutInt(fieldName);
            if (template.HasValue)
                return template.Value;
        }
        return null;
    }

    private Control CreateFieldWithDescription(string label, Control control, string description)
    {
        TableLayoutPanel panel = new()
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(control.MinimumSize.Height > 0
            ? new RowStyle(SizeType.Absolute, control.MinimumSize.Height)
            : new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 2, 0, 2) }, 0, 0);
        control.MaximumSize = new Size(420, 0);
        control.Margin = new Padding(0, 0, 0, 2);
        panel.Controls.Add(control, 0, 1);
        Font descFont = new(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        Label descLabel = new()
        {
            Text = description,
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 0, 0, 2),
            ForeColor = SystemColors.GrayText,
            Font = descFont,
        };
        _annotationLabels.Add(descLabel);
        _annotationOriginals[descLabel] = description;
        panel.Controls.Add(descLabel, 0, 2);
        return panel;
    }

    private TableLayoutPanel CreateMultiColumnFieldGrid((string Label, string Description, Control Control)[] defs, int columns)
    {
        int rowCount = (defs.Length + columns - 1) / columns;
        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = columns,
            RowCount = rowCount,
        };
        for (int c = 0; c < columns; c++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
        for (int i = 0; i < rowCount; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < defs.Length; i++)
        {
            Control fieldPanel = CreateFieldWithDescription(defs[i].Label, defs[i].Control, defs[i].Description);
            grid.Controls.Add(fieldPanel, i % columns, i / columns);
        }

        return grid;
    }

    private GroupBox CreateSettingsGroupBox(string title, (string Label, string Description, Control Control)[] defs, int columns = 2)
    {
        GroupBox gb = new()
        {
            Text = title,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9F, FontStyle.Bold, GraphicsUnit.Point),
            Padding = new Padding(8, 18, 8, 8),
        };
        gb.Controls.Add(CreateMultiColumnFieldGrid(defs, columns));
        return gb;
    }

    private static FlowLayoutPanel CreateVerticalRadioGroup(params RadioButton[] buttons)
    {
        FlowLayoutPanel panel = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            MinimumSize = new Size(180, 64),
        };
        foreach (RadioButton button in buttons)
        {
            button.Margin = new Padding(0, 2, 24, 2);
            panel.Controls.Add(button);
        }

        return panel;
    }

}
