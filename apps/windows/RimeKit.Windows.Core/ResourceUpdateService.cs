using System.Text.Encodings.Web;
using System.Text.Json;
using System.IO.Compression;
using System.Text;
using RimeKit.Windows.Core.Utilities;

namespace RimeKit.Windows.Core;

internal sealed class ResourceUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly RepositoryContext _repositoryContext;

    public ResourceUpdateService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    public IReadOnlyList<FormalResourceDescriptor> GetFormalResourceDescriptors(string resourceKind)
    {
        return GetWindowsDisplayableResources()
            .Where(item => string.Equals(item.ResourceKind, resourceKind, StringComparison.OrdinalIgnoreCase))
            .Select(item => new FormalResourceDescriptor
            {
                ResourceId = item.ResourceId,
                DisplayName = item.DisplayName,
                ResourceKind = item.ResourceKind,
            })
            .ToArray();
    }

    public IReadOnlyList<FormalResourceDescriptor> GetGuiDictionaryDescriptors()
    {
        return LoadFormalResources()
            .Where(item => string.Equals(item.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase))
            .Select(item => new FormalResourceDescriptor
            {
                ResourceId = item.ResourceId,
                DisplayName = item.DisplayName,
                ResourceKind = item.ResourceKind,
            })
            .ToArray();
    }

    public IReadOnlySet<string> GetInstalledResourceIds(string resourceKind)
    {
        HashSet<string> allowedIds = GetWindowsDisplayableResources()
            .Where(item => string.Equals(item.ResourceKind, resourceKind, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ResourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return LoadInstalledResourceStates()
            .Values
            .Where(item => string.Equals(item.ResourceKind, resourceKind, StringComparison.OrdinalIgnoreCase))
            .Where(item => allowedIds.Contains(item.ResourceId))
            .Select(item => item.ResourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> ValidateRequiredInstalledResourcesForWindows(ConfigModel model)
    {
        Dictionary<string, FormalResourceDefinition> resources = LoadFormalResources()
            .ToDictionary(item => item.ResourceId, item => item, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, InstalledResourceState> installedStates = LoadInstalledResourceStates();
        List<string> findings = [];

        HashSet<string> requiredSchemaIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string schemaId in model.ProfileSettings.EnabledSchemaIds)
        {
            if (!string.IsNullOrWhiteSpace(schemaId))
            {
                requiredSchemaIds.Add(schemaId);
            }
        }
        if (!string.IsNullOrWhiteSpace(model.ProfileSettings.WindowsDefaultSchemaId))
        {
            requiredSchemaIds.Add(model.ProfileSettings.WindowsDefaultSchemaId);
        }

        foreach (string schemaId in requiredSchemaIds)
        {
            if (!resources.TryGetValue(schemaId, out FormalResourceDefinition? resource) ||
                !string.Equals(resource.ResourceKind, "schema", StringComparison.OrdinalIgnoreCase) ||
                !resource.Platforms.Contains("windows", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!installedStates.TryGetValue(schemaId, out InstalledResourceState? state))
            {
                continue;
            }

            string? invalidReason = DescribeInvalidInstalledResourceState(resource, state);
            if (!string.IsNullOrWhiteSpace(invalidReason))
            {
                findings.Add($"正式输入方案 {schemaId} 安装状态与实际资源目录不一致：{invalidReason}");
            }
        }

        foreach (string dictionaryId in model.DictionarySettings.EnabledDictionaryIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!resources.TryGetValue(dictionaryId, out FormalResourceDefinition? resource) ||
                !string.Equals(resource.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!installedStates.TryGetValue(dictionaryId, out InstalledResourceState? state))
            {
                continue;
            }

            string? invalidReason = DescribeInvalidInstalledResourceState(resource, state);
            if (!string.IsNullOrWhiteSpace(invalidReason))
            {
                findings.Add($"正式词库 {dictionaryId} 安装状态与实际资源目录不一致：{invalidReason}");
            }
        }

        HashSet<string> requiredModelIds = new(model.ModelSettings.EnabledModelIds, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(model.ModelSettings.ActiveModelId))
        {
            requiredModelIds.Add(model.ModelSettings.ActiveModelId);
        }

        foreach (string modelId in requiredModelIds)
        {
            if (!resources.TryGetValue(modelId, out FormalResourceDefinition? resource) ||
                !string.Equals(resource.ResourceKind, "model", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!installedStates.TryGetValue(modelId, out InstalledResourceState? state))
            {
                continue;
            }

            string? invalidReason = DescribeInvalidInstalledResourceState(resource, state);
            if (!string.IsNullOrWhiteSpace(invalidReason))
            {
                findings.Add($"正式模型 {modelId} 安装状态与实际资源目录不一致：{invalidReason}");
            }
        }

        return findings;
    }

    private sealed class FormalResourceDefinition
    {
        public string ResourceId { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string ResourceKind { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string SourceClass { get; init; } = string.Empty;

        public IReadOnlyList<string> Platforms { get; init; } = [];

        public string SourceType { get; init; } = string.Empty;

        public string DownloadUrl { get; init; } = string.Empty;

        public string SourceRef { get; init; } = string.Empty;

        public string BootstrapSourceUrl { get; init; } = string.Empty;

        public IReadOnlyList<string> ExpectedFiles { get; init; } = [];

        public bool SupportsContextualSuggestions { get; init; }
    }

    private static string MakeInstallPathRelative(string absolutePath, RepositoryContext context)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return string.Empty;

        string repoRoot = Path.GetFullPath(context.RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar);
        if (resolved.StartsWith(repoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return resolved.Substring(repoRoot.Length + 1);

        return absolutePath;
    }

    private static string ResolveInstallPath(string storedPath, RepositoryContext context)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return string.Empty;

        string repoRoot = Path.GetFullPath(context.RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar);
        int wsIndex = storedPath.IndexOf(
            $"workspace{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        if (wsIndex >= 0)
        {
            string relative = storedPath.Substring(wsIndex);
            return Path.GetFullPath(Path.Combine(repoRoot, relative));
        }

        if (!Path.IsPathRooted(storedPath))
            return Path.GetFullPath(Path.Combine(repoRoot, storedPath));

        return storedPath;
    }

    private sealed class InstalledResourceState
    {
        public string ResourceId { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string ResourceKind { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string SourceClass { get; init; } = string.Empty;

        public string InstallPath { get; init; } = string.Empty;

        public string InstalledVersion { get; init; } = string.Empty;

        public string InstalledAt { get; init; } = string.Empty;

        public string Note { get; init; } = string.Empty;
    }

    public string CheckForUpdates()
    {
        string payload = BuildCheckForUpdatesPayload();
        RepositoryContext.WriteUtf8(Path.Combine(_repositoryContext.StateRoot, "last_resource_update_report.json"), payload);
        return payload;
    }

    public string BuildCheckForUpdatesPayload()
    {
        using JsonDocument document = JsonDocument.Parse(RepositoryContext.ReadUtf8(_repositoryContext.SharedSpecPath("resource_manifest.json")));
        Dictionary<string, InstalledResourceState> installedStates = LoadInstalledResourceStates();
        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        List<object> items = [];
        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        foreach (JsonElement schema in document.RootElement.GetProperty("schemas").EnumerateArray())
        {
            items.Add(CheckRemoteResource(
                client,
                schema.GetProperty("schema_id").GetString() ?? string.Empty,
                schema.GetProperty("display_name").GetString() ?? string.Empty,
                ResolveResourceSourceOverride(
                    new FormalResourceDefinition
                    {
                        ResourceId = schema.GetProperty("schema_id").GetString() ?? string.Empty,
                        Source = schema.GetProperty("source_url").GetString() ?? string.Empty,
                    },
                    controls.FormalResourceVersionStrategy,
                    controls.FormalResourcePinnedRef),
                schema.GetProperty("source_class").GetString() ?? string.Empty,
                installedStates.GetValueOrDefault(schema.GetProperty("schema_id").GetString() ?? string.Empty)));
        }

        foreach (JsonElement dictionary in document.RootElement.GetProperty("dictionaries").EnumerateArray())
        {
            items.Add(CheckRemoteResource(
                client,
                dictionary.GetProperty("dictionary_id").GetString() ?? string.Empty,
                dictionary.GetProperty("display_name").GetString() ?? string.Empty,
                ResolveResourceSourceOverride(
                    new FormalResourceDefinition
                    {
                        ResourceId = dictionary.GetProperty("dictionary_id").GetString() ?? string.Empty,
                        Source = dictionary.GetProperty("source").GetString() ?? string.Empty,
                    },
                    controls.FormalResourceVersionStrategy,
                    controls.FormalResourcePinnedRef),
                dictionary.GetProperty("source_class").GetString() ?? string.Empty,
                installedStates.GetValueOrDefault(dictionary.GetProperty("dictionary_id").GetString() ?? string.Empty)));
        }

        foreach (JsonElement model in document.RootElement.GetProperty("models").EnumerateArray())
        {
            items.Add(CheckRemoteResource(
                client,
                model.GetProperty("model_id").GetString() ?? string.Empty,
                model.GetProperty("display_name").GetString() ?? string.Empty,
                ResolveResourceSourceOverride(
                    new FormalResourceDefinition
                    {
                        ResourceId = model.GetProperty("model_id").GetString() ?? string.Empty,
                        Source = model.TryGetProperty("source_url", out JsonElement updateSourceElement)
                            ? updateSourceElement.GetString() ?? string.Empty
                            : string.Empty,
                        SourceType = model.GetProperty("install_kind").GetString() ?? string.Empty,
                        ExpectedFiles = model.TryGetProperty("expected_files", out JsonElement checkExpectedFiles) && checkExpectedFiles.ValueKind == JsonValueKind.Array
                            ? checkExpectedFiles.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
                            : [],
                    },
                    controls.FormalResourceVersionStrategy,
                    controls.FormalResourcePinnedRef),
                model.GetProperty("source_class").GetString() ?? string.Empty,
                installedStates.GetValueOrDefault(model.GetProperty("model_id").GetString() ?? string.Empty)));
        }

        WindowsEnvironmentState environment = WindowsEnvironmentService.Detect(ConfigModel.CreateDefault());
        items.Add(CheckRemoteResource(
            client,
            "windows_weasel",
            "Weasel",
            environment.WeaselUpdateSource ?? "https://rime.im/download/",
            "product_fixed_decision",
            installedState: null,
            currentVersion: environment.WeaselVersion));

        return JsonSerializer.Serialize(new
        {
            checked_at = DateTimeOffset.UtcNow,
            items,
        }, JsonOptions);
    }

    private static void EnsureCleanDirectory(string directory)
    {
        FileHelper.DeleteDirectoryWithBackoff(
            directory,
            maxRetries: 10,
            baseDelayMs: 300,
            maxDelayMs: 4000);
    }

    public string InstallOrUpdateResource(string resourceId)
    {
        FormalResourceDefinition resource = LoadFormalResources()
            .FirstOrDefault(item => string.Equals(item.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未找到正式资源：{resourceId}");

        if (string.Equals(resource.SourceType, "generated", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{resource.DisplayName}' 是本地生成的资源，无需单独下载安装。自定义词条在应用设置时会自动生成。");
        }

        string resourceRoot = Path.Combine(_repositoryContext.ResourcesRoot, resource.ResourceId);
        string currentRoot = Path.Combine(resourceRoot, "current");
        string packageRoot = Path.Combine(resourceRoot, "package");
        Directory.CreateDirectory(resourceRoot);
        EnsureCleanDirectory(currentRoot);
        EnsureCleanDirectory(packageRoot);
        Directory.CreateDirectory(currentRoot);
        Directory.CreateDirectory(packageRoot);

        WindowsRuntimeControls controls = _repositoryContext.LoadWindowsRuntimeControls();
        string resolvedSource = ResolveResourceSourceOverride(
            resource,
            controls.FormalResourceVersionStrategy,
            controls.FormalResourcePinnedRef);
        string downloadSource = ResolveInstallSource(
            resource,
            resolvedSource,
            NormalizeVersionStrategy(controls.FormalResourceVersionStrategy),
            controls.FormalResourcePinnedRef);
        string installedVersion;
        string note;

        if (string.Equals(resource.ResourceKind, "model", StringComparison.OrdinalIgnoreCase))
        {
            (installedVersion, note) = InstallModelResource(resource, downloadSource, currentRoot, packageRoot);
        }
        else if (string.Equals(resource.SourceType, "remote_catalog", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(resource.ResourceId, "sogou_network_popular_words", StringComparison.OrdinalIgnoreCase))
        {
            string scelPath = Path.Combine(packageRoot, $"{resource.ResourceId}.scel");
            (installedVersion, note) = DownloadBinaryResource(downloadSource, scelPath);
            if (!File.Exists(scelPath) || new FileInfo(scelPath).Length == 0)
            {
                throw new InvalidOperationException("搜狗词库下载失败或文件为空，无法继续安装。");
            }
            string generatedYaml = ConvertSogouScelToRimeYaml(scelPath, resource.ResourceId, resource.DisplayName);
            ValidateDictionaryYamlEncoding(generatedYaml, resource.ResourceId);
            string generatedDictionaryPath = Path.Combine(currentRoot, $"{resource.ResourceId}.dict.yaml");
            RepositoryContext.WriteUtf8(
                generatedDictionaryPath,
                generatedYaml);
            note = string.Join("；", new[] { note, $"已生成 {Path.GetFileName(generatedDictionaryPath)}" }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }
        else if (IsArchiveResource(downloadSource))
        {
            string archivePath = Path.Combine(packageRoot, $"{resource.ResourceId}.zip");
            (installedVersion, note) = DownloadBinaryResource(downloadSource, archivePath);
            ExtractArchiveIntoDirectory(archivePath, currentRoot);
            note = string.Join("；", new[] { note, BuildExtractionNote(currentRoot) }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }
        else if (IsBinaryResource(downloadSource))
        {
            string targetPath = Path.Combine(currentRoot, Path.GetFileName(new Uri(downloadSource, UriKind.Absolute).LocalPath));
            (installedVersion, note) = DownloadBinaryResource(downloadSource, targetPath);
        }
        else
        {
            string extension = GuessTextExtension(downloadSource);
            string targetPath = Path.Combine(currentRoot, $"{resource.ResourceId}{extension}");
            (installedVersion, note) = DownloadTextLikeResource(downloadSource, targetPath);
        }

        if (string.Equals(resource.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase))
        {
            note = EnsureDictionaryAlias(resource, currentRoot, note);
        }

        InstalledResourceState state = new()
        {
            ResourceId = resource.ResourceId,
            DisplayName = resource.DisplayName,
            ResourceKind = resource.ResourceKind,
            Source = resolvedSource,
            SourceClass = resource.SourceClass,
            InstallPath = MakeInstallPathRelative(currentRoot, _repositoryContext),
            InstalledVersion = installedVersion,
            InstalledAt = DateTimeOffset.UtcNow.ToString("O"),
            Note = note,
        };

        SaveInstalledResourceState(state);

        string payload = JsonSerializer.Serialize(new
        {
            resource_id = state.ResourceId,
            display_name = state.DisplayName,
            resource_kind = state.ResourceKind,
            source = state.Source,
            install_path = state.InstallPath,
            installed_version = state.InstalledVersion,
            installed_at = state.InstalledAt,
            note = state.Note,
        }, JsonOptions);

        RepositoryContext.WriteUtf8(Path.Combine(_repositoryContext.StateRoot, "last_resource_install_report.json"), payload);
        return payload;
    }

    public string InstallResourceFromLocalFile(string resourceId, string localFilePath)
    {
        FormalResourceDefinition resource = LoadFormalResources()
            .FirstOrDefault(item => string.Equals(item.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未找到正式资源：{resourceId}");

        if (string.Equals(resource.SourceType, "generated", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{resource.DisplayName}' 是本地生成的资源，无需单独下载安装。自定义词条在应用设置时会自动生成。");
        }

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException($"指定的安装文件不存在：{localFilePath}");
        }

        string resourceRoot = Path.Combine(_repositoryContext.ResourcesRoot, resource.ResourceId);
        string currentRoot = Path.Combine(resourceRoot, "current");
        string packageRoot = Path.Combine(resourceRoot, "package");
        Directory.CreateDirectory(resourceRoot);
        EnsureCleanDirectory(currentRoot);
        EnsureCleanDirectory(packageRoot);
        Directory.CreateDirectory(currentRoot);
        Directory.CreateDirectory(packageRoot);

        string installedVersion = "local";
        string note = $"已从本地文件安装：{Path.GetFileName(localFilePath)}";

        if (string.Equals(resource.ResourceKind, "model", StringComparison.OrdinalIgnoreCase))
        {
            string targetName = resource.ExpectedFiles.FirstOrDefault()
                ?? Path.GetFileName(localFilePath);
            FileHelper.CopyFileWithBackoff(localFilePath, Path.Combine(currentRoot, targetName), overwrite: true);
        }
        else if (string.Equals(resource.SourceType, "remote_catalog", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(resource.ResourceId, "sogou_network_popular_words", StringComparison.OrdinalIgnoreCase))
        {
            string scelPath = Path.Combine(packageRoot, $"{resource.ResourceId}.scel");
            FileHelper.CopyFileWithBackoff(localFilePath, scelPath, overwrite: true);
            if (!File.Exists(scelPath) || new FileInfo(scelPath).Length == 0)
            {
                throw new InvalidOperationException("搜狗词库文件为空，无法继续安装。");
            }
            string generatedYaml = ConvertSogouScelToRimeYaml(scelPath, resource.ResourceId, resource.DisplayName);
            ValidateDictionaryYamlEncoding(generatedYaml, resource.ResourceId);
            string generatedDictionaryPath = Path.Combine(currentRoot, $"{resource.ResourceId}.dict.yaml");
            RepositoryContext.WriteUtf8(generatedDictionaryPath, generatedYaml);
            note = string.Join("；", new[] { note, $"已生成 {Path.GetFileName(generatedDictionaryPath)}" }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }
        else if (localFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractArchiveIntoDirectory(localFilePath, currentRoot);
            note = string.Join("；", new[] { note, BuildExtractionNote(currentRoot) }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }
        else
        {
            string targetName = Path.GetFileName(localFilePath);
            FileHelper.CopyFileWithBackoff(localFilePath, Path.Combine(currentRoot, targetName), overwrite: true);
        }

        if (string.Equals(resource.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase))
        {
            note = EnsureDictionaryAlias(resource, currentRoot, note);
        }

        InstalledResourceState state = new()
        {
            ResourceId = resource.ResourceId,
            DisplayName = resource.DisplayName,
            ResourceKind = resource.ResourceKind,
            Source = localFilePath,
            SourceClass = resource.SourceClass,
            InstallPath = MakeInstallPathRelative(currentRoot, _repositoryContext),
            InstalledVersion = installedVersion,
            InstalledAt = DateTimeOffset.UtcNow.ToString("O"),
            Note = note,
        };

        SaveInstalledResourceState(state);

        string payload = JsonSerializer.Serialize(new
        {
            resource_id = state.ResourceId,
            display_name = state.DisplayName,
            resource_kind = state.ResourceKind,
            source = state.Source,
            install_path = state.InstallPath,
            installed_version = state.InstalledVersion,
            installed_at = state.InstalledAt,
            note = state.Note,
        }, JsonOptions);

        RepositoryContext.WriteUtf8(Path.Combine(_repositoryContext.StateRoot, "last_resource_install_report.json"), payload);
        return payload;
    }

    public string UninstallResource(string resourceId)
    {
        FormalResourceDefinition resource = LoadFormalResources()
            .FirstOrDefault(item => string.Equals(item.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"未找到正式资源：{resourceId}");

        Dictionary<string, InstalledResourceState> states = LoadInstalledResourceStates();
        if (!states.TryGetValue(resource.ResourceId, out InstalledResourceState? state))
        {
            throw new InvalidOperationException($"正式资源尚未安装：{resourceId}");
        }

        states.Remove(resource.ResourceId);
        SaveInstalledResourceStates(states.Values);

        string resourceRoot = Path.Combine(_repositoryContext.ResourcesRoot, resource.ResourceId);
        if (Directory.Exists(resourceRoot))
        {
            FileHelper.DeleteDirectoryWithBackoff(resourceRoot, maxRetries: 10, baseDelayMs: 300, maxDelayMs: 4000);
        }
        else if (!string.IsNullOrWhiteSpace(state.InstallPath) && Directory.Exists(state.InstallPath))
        {
            FileHelper.DeleteDirectoryWithBackoff(state.InstallPath, maxRetries: 10, baseDelayMs: 300, maxDelayMs: 4000);
        }

        string payload = JsonSerializer.Serialize(new
        {
            resource_id = resource.ResourceId,
            display_name = state.DisplayName,
            resource_kind = state.ResourceKind,
            removed_install_path = state.InstallPath,
            removed_at = DateTimeOffset.UtcNow,
        }, JsonOptions);

        RepositoryContext.WriteUtf8(Path.Combine(_repositoryContext.StateRoot, "last_resource_uninstall_report.json"), payload);
        return payload;
    }

    public string BuildInstalledResourceStateView()
    {
        Dictionary<string, InstalledResourceState> states = LoadInstalledResourceStates();
        string runtimeRoot = ResolveModelRuntimeRoot();
        List<string> lines =
        [
            "词库与方案安装状态",
            "====================",
        ];

        foreach (FormalResourceDefinition resource in GetWindowsDisplayableResources())
        {
            if (states.TryGetValue(resource.ResourceId, out InstalledResourceState? state))
            {
                string[] expectedRuntimeFiles = GetExpectedRuntimeFiles(resource.ResourceKind, state.InstallPath);
                string[] missingRuntimeFiles = expectedRuntimeFiles
                    .Where(file => !File.Exists(Path.Combine(runtimeRoot, file)))
                    .ToArray();
                lines.Add(resource.DisplayName);
                lines.Add($"类型: {LocalizeResourceKind(resource.ResourceKind)}");
                lines.Add("来源:");
                lines.Add(WrapDisplayText(state.Source));
                lines.Add("安装位置:");
                lines.Add(WrapDisplayText(NormalizeDisplayPath(state.InstallPath)));
                lines.Add("已安装版本:");
                lines.Add(WrapDisplayText(state.InstalledVersion));
                lines.Add($"最近安装时间: {state.InstalledAt}");
                lines.Add($"当前输入法目录: {NormalizeDisplayPath(runtimeRoot)}");
                if (missingRuntimeFiles.Length == 0)
                {
                    lines.Add("状态: 已安装，当前输入法目录已就位");
                }
                else
                {
                    lines.Add("状态: 已安装，但当前输入法目录缺少文件");
                    lines.Add($"缺少文件: {string.Join("、", missingRuntimeFiles)}");
                }
                lines.Add($"备注: {NormalizeInstallNote(resource.ResourceId, state.Note)}");
            }
            else
            {
                lines.Add(resource.DisplayName);
                lines.Add($"类型: {LocalizeResourceKind(resource.ResourceKind)}");
                lines.Add("状态: 尚未安装或更新");
            }

            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string BuildModelInstallStateView()
    {
        IReadOnlyList<FormalResourceDefinition> models = LoadFormalResources()
            .Where(item => string.Equals(item.ResourceKind, "model", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Dictionary<string, InstalledResourceState> states = LoadInstalledResourceStates();
        string runtimeRoot = ResolveModelRuntimeRoot();
        if (models.Count == 0)
        {
            return string.Join(
                Environment.NewLine,
                [
                    "模型资源安装状态",
                    "====================",
                    "当前没有登记的正式模型资源。",
                    "安装/更新入口能力已预留；后续 shared/spec/resource_manifest.json 一旦登记模型资源，GUI 将在此区生成对应入口。",
                ]);
        }

        List<string> lines =
        [
            "模型资源安装状态",
            "====================",
        ];
        foreach (FormalResourceDefinition model in models)
        {
            if (states.TryGetValue(model.ResourceId, out InstalledResourceState? state))
            {
                string[] expectedFiles = model.ExpectedFiles
                    .Select(file => file.Replace('/', Path.DirectorySeparatorChar))
                    .ToArray();
                string[] missingRuntimeFiles = expectedFiles
                    .Where(file => !File.Exists(Path.Combine(runtimeRoot, file)))
                    .ToArray();
                lines.Add(model.DisplayName);
                lines.Add("模型目录:");
                lines.Add(WrapDisplayText(NormalizeDisplayPath(state.InstallPath)));
                lines.Add("已安装版本:");
                lines.Add(WrapDisplayText(state.InstalledVersion));
                lines.Add($"最近安装时间: {state.InstalledAt}");
                lines.Add($"当前输入法目录: {NormalizeDisplayPath(runtimeRoot)}");
                if (missingRuntimeFiles.Length == 0)
                {
                    lines.Add("状态: 已安装，当前输入法目录已就位");
                }
                else
                {
                    lines.Add("状态: 已安装，但当前输入法目录缺少模型文件");
                    lines.Add($"缺少文件: {string.Join("、", missingRuntimeFiles)}");
                }
            }
            else
            {
                lines.Add(model.DisplayName);
                lines.Add("状态: 尚未安装或更新");
            }

            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveModelRuntimeRoot()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable("RIMEKIT_MODEL_RUNTIME_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return RepositoryContext.ExpandPath(overrideRoot);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");
    }

    private static string[] GetExpectedRuntimeFiles(string resourceKind, string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return [];
        }

        return Directory.GetFiles(installPath, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(installPath, file).Replace('/', Path.DirectorySeparatorChar))
            .Where(relativePath => ShouldIncludeRuntimeFile(resourceKind, relativePath))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldIncludeRuntimeFile(string resourceKind, string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (string.Equals(resourceKind, "dictionary", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(resourceKind, "schema", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.EndsWith(".schema.yaml", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "default.yaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "weasel.yaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "symbols.yaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "terra_symbols.yaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "rime.lua", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(resourceKind, "model", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.EndsWith(".gram", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "grammar.yaml", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private IReadOnlyList<FormalResourceDefinition> GetWindowsDisplayableResources()
    {
        return LoadFormalResources()
            .Where(resource =>
                resource.ResourceKind switch
                {
                    "schema" => resource.Platforms.Contains("windows", StringComparer.OrdinalIgnoreCase),
                    "dictionary" => !string.Equals(resource.ResourceId, "custom_simple", StringComparison.OrdinalIgnoreCase),
                    "model" => true,
                    _ => false,
                })
            .ToArray();
    }

    private static string? DescribeInvalidInstalledResourceState(FormalResourceDefinition resource, InstalledResourceState state)
    {
        if (string.IsNullOrWhiteSpace(state.InstallPath))
        {
            return "未记录安装目录";
        }

        string installPath = state.InstallPath;
        if (!Directory.Exists(installPath))
        {
            return $"安装目录不存在：{installPath}";
        }

        return resource.ResourceKind switch
        {
            "schema" => DescribeMissingSchemaRuntimeFile(resource.ResourceId, installPath),
            "dictionary" => DescribeMissingDictionaryRuntimeFile(resource.ResourceId, installPath),
            "model" => DescribeMissingModelRuntimeFile(resource.ResourceId, installPath),
            _ => null,
        };
    }

    private static string? DescribeMissingSchemaRuntimeFile(string resourceId, string installPath)
    {
        string schemaPath = Path.Combine(installPath, $"{resourceId}.schema.yaml");
        if (!File.Exists(schemaPath))
        {
            return $"缺少 {Path.GetFileName(schemaPath)}";
        }

        string defaultYamlPath = Path.Combine(installPath, "default.yaml");
        if (!File.Exists(defaultYamlPath))
        {
            return "缺少 default.yaml";
        }

        return null;
    }

    private static string? DescribeMissingDictionaryRuntimeFile(string resourceId, string installPath)
    {
        string aliasPath = Path.Combine(installPath, $"{resourceId}.dict.yaml");
        if (File.Exists(aliasPath))
        {
            return null;
        }

        bool hasAnyDictionary = Directory.GetFiles(installPath, "*.dict.yaml", SearchOption.AllDirectories).Length > 0;
        return hasAnyDictionary ? null : $"缺少 {resourceId}.dict.yaml 或其它正式词典文件";
    }

    private static string? DescribeMissingModelRuntimeFile(string resourceId, string installPath)
    {
        return resourceId switch
        {
            "wanxiang_lts_zh_hans" => File.Exists(Path.Combine(installPath, "wanxiang-lts-zh-hans.gram"))
                ? null
                : "缺少 wanxiang-lts-zh-hans.gram",
            _ => Directory.GetFiles(installPath, "*.gram", SearchOption.TopDirectoryOnly).Length > 0
                ? null
                : "缺少正式模型文件",
        };
    }

    private string NormalizeDisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('%', StringComparison.Ordinal))
        {
            return path;
        }

        string marker = $"{Path.DirectorySeparatorChar}Documents{Path.DirectorySeparatorChar}rimekit{Path.DirectorySeparatorChar}";
        int markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return path;
        }

        string suffix = path[(markerIndex + marker.Length)..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(_repositoryContext.RepositoryRoot, suffix);
    }

    private static string LocalizeResourceKind(string resourceKind)
    {
        return resourceKind switch
        {
            "schema" => "方案",
            "dictionary" => "词库",
            "model" => "模型",
            _ => resourceKind,
        };
    }

    private static string NormalizeInstallNote(string resourceId, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return "无";
        }

        if (string.Equals(resourceId, "sogou_network_popular_words", StringComparison.OrdinalIgnoreCase) &&
            note.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return "上次下载拿到的是网页，不是可导入词库文件。请重新执行下载检查或安装。";
        }

        return note;
    }

    private static string WrapDisplayText(string text, int maxLineLength = 68)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLineLength)
        {
            return text;
        }

        List<string> lines = [];
        int index = 0;
        while (index < text.Length)
        {
            int remaining = text.Length - index;
            int length = Math.Min(maxLineLength, remaining);
            int splitIndex = text.LastIndexOfAny(['\\', '/', '-', '_', ' '], index + length - 1, length);
            if (splitIndex <= index)
            {
                splitIndex = index + length;
            }
            else
            {
                splitIndex++;
            }

            lines.Add(text[index..splitIndex].Trim());
            index = splitIndex;
        }

        return string.Join(Environment.NewLine, lines.Where(line => line.Length > 0));
    }

    private static object CheckRemoteResource(
        HttpClient client,
        string id,
        string displayName,
        string source,
        string sourceClass,
        InstalledResourceState? installedState = null,
        string? currentVersion = null)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new
            {
                id,
                display_name = displayName,
                source,
                source_class = sourceClass,
                current_version = currentVersion ?? "unknown",
                installed_version = installedState?.InstalledVersion ?? "not_installed",
                installed_at = installedState?.InstalledAt ?? "never",
                reachable = false,
                status_code = 0,
                last_modified = "n/a",
                content_length = -1L,
                note = "当前来源不是可直接做 HTTP 检查的远端地址。",
            };
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Head, uri);
            using HttpResponseMessage response = client.Send(request);
            return new
            {
                id,
                display_name = displayName,
                source,
                source_class = sourceClass,
                current_version = currentVersion ?? "unknown",
                installed_version = installedState?.InstalledVersion ?? "not_installed",
                installed_at = installedState?.InstalledAt ?? "never",
                reachable = response.IsSuccessStatusCode,
                status_code = (int)response.StatusCode,
                last_modified = response.Content.Headers.LastModified?.ToString() ?? "n/a",
                content_length = response.Content.Headers.ContentLength ?? -1L,
                note = response.IsSuccessStatusCode ? "HEAD 检查完成。" : "远端返回非成功状态。",
            };
        }
        catch (HttpRequestException exception)
        {
            return new
            {
                id,
                display_name = displayName,
                source,
                source_class = sourceClass,
                current_version = currentVersion ?? "unknown",
                installed_version = installedState?.InstalledVersion ?? "not_installed",
                installed_at = installedState?.InstalledAt ?? "never",
                reachable = false,
                status_code = 0,
                last_modified = "n/a",
                content_length = -1L,
                note = $"检查失败：{exception.Message}",
            };
        }
        catch (TaskCanceledException)
        {
            return new
            {
                id,
                display_name = displayName,
                source,
                source_class = sourceClass,
                current_version = currentVersion ?? "unknown",
                installed_version = installedState?.InstalledVersion ?? "not_installed",
                installed_at = installedState?.InstalledAt ?? "never",
                reachable = false,
                status_code = 0,
                last_modified = "n/a",
                content_length = -1L,
                note = "请求超时。",
            };
        }
    }

    private IReadOnlyList<FormalResourceDefinition> LoadFormalResources()
    {
        using JsonDocument document = JsonDocument.Parse(RepositoryContext.ReadUtf8(_repositoryContext.SharedSpecPath("resource_manifest.json")));
        List<FormalResourceDefinition> resources = [];

        foreach (JsonElement schema in document.RootElement.GetProperty("schemas").EnumerateArray())
        {
            resources.Add(new FormalResourceDefinition
            {
                ResourceId = schema.GetProperty("schema_id").GetString() ?? string.Empty,
                DisplayName = schema.GetProperty("display_name").GetString() ?? string.Empty,
                ResourceKind = "schema",
                Source = schema.GetProperty("source_url").GetString() ?? string.Empty,
                SourceClass = schema.GetProperty("source_class").GetString() ?? string.Empty,
                SourceType = "git_repository",
                Platforms = schema.TryGetProperty("platforms", out JsonElement schemaPlatforms) && schemaPlatforms.ValueKind == JsonValueKind.Array
                    ? schemaPlatforms.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
                    : [],
            });
        }

        foreach (JsonElement dictionary in document.RootElement.GetProperty("dictionaries").EnumerateArray())
        {
            resources.Add(new FormalResourceDefinition
            {
                ResourceId = dictionary.GetProperty("dictionary_id").GetString() ?? string.Empty,
                DisplayName = dictionary.GetProperty("display_name").GetString() ?? string.Empty,
                ResourceKind = "dictionary",
                Source = dictionary.GetProperty("source").GetString() ?? string.Empty,
                SourceClass = dictionary.GetProperty("source_class").GetString() ?? string.Empty,
                SourceType = dictionary.TryGetProperty("source_type", out JsonElement dictionarySourceType) ? dictionarySourceType.GetString() ?? string.Empty : string.Empty,
                DownloadUrl = dictionary.TryGetProperty("download_url", out JsonElement downloadUrlElement) ? downloadUrlElement.GetString() ?? string.Empty : string.Empty,
                ExpectedFiles = dictionary.TryGetProperty("expected_files", out JsonElement dictExpectedFilesElement) && dictExpectedFilesElement.ValueKind == JsonValueKind.Array
                    ? dictExpectedFilesElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
                    : [],
                Platforms = ["windows", "android"],
            });
        }

        foreach (JsonElement model in document.RootElement.GetProperty("models").EnumerateArray())
        {
            resources.Add(new FormalResourceDefinition
            {
                ResourceId = model.GetProperty("model_id").GetString() ?? string.Empty,
                DisplayName = model.GetProperty("display_name").GetString() ?? string.Empty,
                ResourceKind = "model",
                Source = model.TryGetProperty("source_url", out JsonElement modelSourceElement)
                    ? modelSourceElement.GetString() ?? string.Empty
                    : model.GetProperty("install_kind").GetString() ?? string.Empty,
                SourceClass = model.GetProperty("source_class").GetString() ?? string.Empty,
                SourceType = model.GetProperty("install_kind").GetString() ?? string.Empty,
                SourceRef = model.TryGetProperty("source_ref", out JsonElement sourceRefElement) ? sourceRefElement.GetString() ?? string.Empty : string.Empty,
                BootstrapSourceUrl = model.TryGetProperty("bootstrap_source_url", out JsonElement bootstrapSourceElement) ? bootstrapSourceElement.GetString() ?? string.Empty : string.Empty,
                ExpectedFiles = model.TryGetProperty("expected_files", out JsonElement expectedFilesElement) && expectedFilesElement.ValueKind == JsonValueKind.Array
                    ? expectedFilesElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
                    : [],
                SupportsContextualSuggestions = model.TryGetProperty("supports_contextual_suggestions", out JsonElement supportsSuggestionsElement) && supportsSuggestionsElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? supportsSuggestionsElement.GetBoolean()
                    : false,
                Platforms = ["windows", "android"],
            });
        }

        return resources;
    }

    private Dictionary<string, InstalledResourceState> LoadInstalledResourceStates()
    {
        string path = _repositoryContext.InstalledResourcesStatePath;
        if (!File.Exists(path))
        {
            return new Dictionary<string, InstalledResourceState>(StringComparer.OrdinalIgnoreCase);
        }

        IReadOnlyList<InstalledResourceState>? states;
        try
        {
            states = JsonSerializer.Deserialize<IReadOnlyList<InstalledResourceState>>(
                RepositoryContext.ReadUtf8(path),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ResourceUpdate] installed_resources 文件损坏, 重置: {ex.Message}");
            return new Dictionary<string, InstalledResourceState>(StringComparer.OrdinalIgnoreCase);
        }
        return (states ?? [])
            .Select(item => new InstalledResourceState
            {
                ResourceId = item.ResourceId,
                DisplayName = item.DisplayName,
                ResourceKind = item.ResourceKind,
                Source = item.Source,
                SourceClass = item.SourceClass,
                InstallPath = ResolveInstallPath(item.InstallPath, _repositoryContext),
                InstalledVersion = item.InstalledVersion,
                InstalledAt = item.InstalledAt,
                Note = item.Note,
            })
            .ToDictionary(item => item.ResourceId, item => item, StringComparer.OrdinalIgnoreCase);
    }

    private void SaveInstalledResourceState(InstalledResourceState state)
    {
        Dictionary<string, InstalledResourceState> states = LoadInstalledResourceStates();
        states[state.ResourceId] = state;
        SaveInstalledResourceStates(states.Values);
    }

    private void SaveInstalledResourceStates(IEnumerable<InstalledResourceState> states)
    {
        RepositoryContext.WriteUtf8(
            _repositoryContext.InstalledResourcesStatePath,
            JsonSerializer.Serialize(states.OrderBy(item => item.ResourceId), JsonOptions));
    }

    private static bool TryBuildGitHubArchiveUrl(string source, string versionStrategy, string pinnedRef, out string? archiveUrl)
    {
        archiveUrl = null;
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        if (string.Equals(versionStrategy, "pinned", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(pinnedRef))
        {
            archiveUrl = $"https://codeload.github.com/{segments[0]}/{segments[1]}/zip/refs/{pinnedRef}";
            return true;
        }

        string[] branchCandidates = ["main", "master"];

        using HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RimeKit.Windows/1.0");
        foreach (string branch in branchCandidates)
        {
            string candidate = $"https://codeload.github.com/{segments[0]}/{segments[1]}/zip/refs/heads/{branch}";
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Head, candidate);
                using HttpResponseMessage response = client.Send(request);
                if (response.IsSuccessStatusCode)
                {
                    archiveUrl = candidate;
                    return true;
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ResourceUpdate] GitHub release checks failed: {ex.Message}");
            }
        }

        archiveUrl = $"https://codeload.github.com/{segments[0]}/{segments[1]}/zip/refs/heads/main";
        return true;
    }

    private static string ResolveResourceSourceOverride(FormalResourceDefinition resource, string versionStrategy, string pinnedRef)
    {
        string overrideName = "RIMEKIT_RESOURCE_OVERRIDE_" + resource.ResourceId.ToUpperInvariant().Replace('-', '_');
        string? explicitOverride = Environment.GetEnvironmentVariable(overrideName);
        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            return explicitOverride;
        }

        if (string.Equals(resource.SourceType, "direct_binary", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(resource.SourceType, "direct_download", StringComparison.OrdinalIgnoreCase))
        {
            return resource.Source;
        }

        if (TryBuildGitHubArchiveUrl(resource.Source, NormalizeVersionStrategy(versionStrategy), pinnedRef, out string? archiveUrl))
        {
            return archiveUrl!;
        }

        return resource.Source;
    }

    private static string ResolveInstallSource(
        FormalResourceDefinition resource,
        string source,
        string versionStrategy,
        string pinnedRef)
    {
        if (!string.Equals(source, resource.Source, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        if (!string.IsNullOrWhiteSpace(resource.DownloadUrl))
        {
            return resource.DownloadUrl;
        }

        if (resource.ExpectedFiles.Count > 0)
        {
            if (TryResolveModelGitHubReleaseAssetUrl(resource, out string? gitHubReleaseUrl))
            {
                return gitHubReleaseUrl!;
            }

            if (Uri.TryCreate(resource.Source, UriKind.Absolute, out Uri? srcUri)
                && string.Equals(srcUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"无法解析「{resource.DisplayName}」的最新版本下载地址，" +
                    "GitHub Releases API 访问失败，请检查网络连接后重试。");
            }
        }

        if (string.Equals(resource.ResourceKind, "model", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveModelArchiveSource(resource, versionStrategy, pinnedRef);
        }

        return source;
    }

    private static string ResolveModelArchiveSource(
        FormalResourceDefinition resource,
        string versionStrategy,
        string pinnedRef)
    {
        if (string.Equals(resource.SourceType, "direct_binary", StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveModelGitHubReleaseAssetUrl(resource, out string? resolvedUrl))
            {
                return resolvedUrl!;
            }

            System.Diagnostics.Debug.WriteLine($"[ResourceUpdate] GitHub release asset resolution failed for model {resource.ResourceId}, falling back to direct URL.");
            return resource.Source;
        }

        if (TryBuildGitHubArchiveUrl(resource.Source, versionStrategy, pinnedRef, out string? archiveUrl))
        {
            return archiveUrl!;
        }

        if (Uri.TryCreate(resource.Source, UriKind.Absolute, out Uri? uri) &&
            string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                string effectiveRef = string.Equals(versionStrategy, "pinned", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pinnedRef)
                    ? pinnedRef
                    : string.IsNullOrWhiteSpace(resource.SourceRef) ? "master" : resource.SourceRef;
                string refPath = string.Equals(versionStrategy, "pinned", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pinnedRef)
                    ? effectiveRef
                    : $"heads/{effectiveRef}";
                return $"https://codeload.github.com/{segments[0]}/{segments[1]}/zip/refs/{refPath}";
            }
        }

        return resource.Source;
    }

    private static bool TryResolveModelGitHubReleaseAssetUrl(
        FormalResourceDefinition resource,
        out string? resolvedUrl)
    {
        resolvedUrl = null;
        string apiSource = !string.IsNullOrWhiteSpace(resource.BootstrapSourceUrl)
            ? resource.BootstrapSourceUrl
            : resource.Source;
        if (!Uri.TryCreate(apiSource, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        string apiUrl = $"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases";

        try
        {
            string json = ResumableDownloader.DownloadToString(apiUrl);
            using JsonDocument document = JsonDocument.Parse(json);

            foreach (JsonElement release in document.RootElement.EnumerateArray())
            {
                if (release.GetProperty("prerelease").GetBoolean())
                {
                    continue;
                }

                foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
                {
                    string? assetName = asset.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(assetName))
                    {
                        continue;
                    }

                    if (resource.ExpectedFiles.Any(
                            expected => string.Equals(assetName, expected, StringComparison.OrdinalIgnoreCase) ||
                                        assetName.EndsWith(expected, StringComparison.OrdinalIgnoreCase) ||
                                        assetName.StartsWith(expected, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (string.Equals(resource.ResourceKind, "dictionary", StringComparison.OrdinalIgnoreCase)
                            && !assetName.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase))
                            continue;

                        resolvedUrl = asset.GetProperty("browser_download_url").GetString();
                        if (!string.IsNullOrWhiteSpace(resolvedUrl))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[ResourceUpdate] Model release asset resolution failed: {ex.Message}");
        }

        return false;
    }

    private static (string InstalledVersion, string Note) InstallModelResource(
        FormalResourceDefinition resource,
        string downloadSource,
        string currentRoot,
        string packageRoot)
    {
        string installedVersion;
        string note;
        if (string.Equals(resource.SourceType, "direct_binary", StringComparison.OrdinalIgnoreCase))
        {
            string? targetFileName = resource.ExpectedFiles.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                targetFileName = Path.GetFileName(new Uri(downloadSource, UriKind.Absolute).LocalPath);
            }

            string targetPath = Path.Combine(currentRoot, targetFileName);
            (installedVersion, note) = DownloadBinaryResource(downloadSource, targetPath);
        }
        else
        {
            string archivePath = Path.Combine(packageRoot, $"{resource.ResourceId}.zip");
            (installedVersion, note) = DownloadBinaryResource(downloadSource, archivePath);
            ExtractArchiveIntoDirectory(archivePath, currentRoot);

            if (!string.IsNullOrWhiteSpace(resource.BootstrapSourceUrl))
            {
                string bootstrapTargetPath = Path.Combine(currentRoot, Path.GetFileName(new Uri(resource.BootstrapSourceUrl, UriKind.Absolute).LocalPath));
                DownloadTextLikeResource(resource.BootstrapSourceUrl, bootstrapTargetPath);
            }
        }

        string[] missingFiles = resource.ExpectedFiles
            .Where(expectedFile => !File.Exists(Path.Combine(currentRoot, expectedFile.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            throw new InvalidOperationException($"模型资源缺少必要文件：{string.Join(", ", missingFiles)}");
        }

        string expectedSummary = resource.ExpectedFiles.Count == 0
            ? BuildExtractionNote(currentRoot)
            : $"已就位 {resource.ExpectedFiles.Count} 个正式模型文件";
        return (installedVersion, string.Join("；", new[] { note, expectedSummary }.Where(item => !string.IsNullOrWhiteSpace(item))));
    }

    private static string EnsureDictionaryAlias(FormalResourceDefinition resource, string currentRoot, string note)
    {
        string aliasPath = Path.Combine(currentRoot, $"{resource.ResourceId}.dict.yaml");
        if (File.Exists(aliasPath))
        {
            return note;
        }

        string[] dictionaryFiles = Directory.GetFiles(currentRoot, "*.dict.yaml", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, aliasPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (dictionaryFiles.Length == 0)
        {
            return note;
        }

        string chosenFile = dictionaryFiles.Length == 1
            ? dictionaryFiles[0]
            : dictionaryFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(resource.ResourceId, StringComparison.OrdinalIgnoreCase)) ?? dictionaryFiles[0];

            FileHelper.CopyFileWithBackoff(chosenFile, aliasPath, overwrite: true);
        return string.Join(
            "；",
            new[]
            {
                note,
                $"已生成 {Path.GetFileName(aliasPath)} -> {Path.GetFileName(chosenFile)}",
            }.Where(item => !string.IsNullOrWhiteSpace(item)));
    }

    private static string ConvertSogouScelToRimeYaml(string scelPath, string dictionaryId, string displayName)
    {
        byte[] bytes = File.ReadAllBytes(scelPath);
        (IReadOnlyDictionary<ushort, string> pinyinMap, int startChinese) = ParseScelPinyinMap(bytes);
        IReadOnlyList<ScelEntry> entries = ParseScelEntries(bytes, pinyinMap, startChinese);
        if (entries.Count == 0 && startChinese != 0x2628 && bytes.Length > 0x2628)
        {
            entries = ParseScelEntries(bytes, pinyinMap, 0x2628);
        }
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("SCEL 词库中没有可转换的词条。");
        }

        StringBuilder builder = new();
        builder.AppendLine("---");
        builder.AppendLine($"name: {dictionaryId}");
        builder.AppendLine($"version: \"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}\"");
        builder.AppendLine("sort: by_weight");
        builder.AppendLine("use_preset_vocabulary: true");
        builder.AppendLine($"# source: {displayName}");
        builder.AppendLine("...");
        foreach (ScelEntry entry in entries
                     .OrderByDescending(item => item.Weight)
                     .ThenBy(item => item.Text, StringComparer.Ordinal))
        {
            builder.AppendLine($"{entry.Text}\t{entry.Pinyin}\t{entry.Weight}");
        }

        return builder.ToString();
    }

    private static void ValidateDictionaryYamlEncoding(string yamlContent, string dictionaryId)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new InvalidOperationException($"词库 {dictionaryId} 转换结果为空，无法继续安装。");
        }

        if (yamlContent.Any(c => (c >= '\u0000' && c <= '\u0008') || c == '\u000B' || c == '\u000C' || (c >= '\u000E' && c <= '\u001F') || (c >= '\u007F' && c <= '\u009F')))
        {
            throw new InvalidOperationException($"词库 {dictionaryId} 转换结果包含非法控制字符，可能由 SCEL 解析越界导致，无法继续安装。");
        }

        byte[] utf8Bytes = Encoding.UTF8.GetBytes(yamlContent);
        string decoded = Encoding.UTF8.GetString(utf8Bytes);
        if (!string.Equals(yamlContent, decoded, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"词库 {dictionaryId} 转换结果编码验证失败（UTF-8 往返不匹配），无法继续安装。");
        }

        int entryCount = yamlContent.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains('\t') && !line.StartsWith('#') && !line.StartsWith("---", StringComparison.Ordinal) && !line.StartsWith("name:", StringComparison.Ordinal) && !line.StartsWith("version:", StringComparison.Ordinal) && !line.StartsWith("sort:", StringComparison.Ordinal) && !line.StartsWith("use_", StringComparison.Ordinal));
        if (entryCount == 0)
        {
            throw new InvalidOperationException($"词库 {dictionaryId} 转换后有效词条数为零，无法继续安装。");
        }
    }

    private static string GuessTextExtension(string source)
    {
        if (source.EndsWith(".dict.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return ".dict.yaml";
        }
        if (source.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return ".yaml";
        }
        if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return ".json";
        }
        if (source.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return ".txt";
        }

        return ".html";
    }

    private static bool IsArchiveResource(string source)
    {
        return source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("/zip/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBinaryResource(string source)
    {
        return source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
               source.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
               source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static void ExtractArchiveIntoDirectory(string archivePath, string destinationDirectory)
    {
        string extractionRoot = Path.Combine(destinationDirectory, "__extract");
        if (Directory.Exists(extractionRoot))
        {
            FileHelper.DeleteDirectoryWithBackoff(extractionRoot, maxRetries: 10, baseDelayMs: 300, maxDelayMs: 4000);
        }

        Directory.CreateDirectory(extractionRoot);
        ZipFile.ExtractToDirectory(archivePath, extractionRoot, overwriteFiles: true);

        string sourceRoot = ResolveSingleExtractionRoot(extractionRoot);
        foreach (string file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                string relativePath = Path.GetRelativePath(sourceRoot, file);
                string targetPath = Path.Combine(destinationDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                FileHelper.CopyFileWithBackoff(file, targetPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ExtractArchive] 复制失败: {file} — {ex.Message}");
            }
        }

        FileHelper.DeleteDirectoryWithBackoff(extractionRoot, maxRetries: 10, baseDelayMs: 300, maxDelayMs: 4000);
    }

    private static string ResolveSingleExtractionRoot(string extractionRoot)
    {
        string[] directories = Directory.GetDirectories(extractionRoot);
        string[] files = Directory.GetFiles(extractionRoot);
        if (directories.Length == 1 && files.Length == 0)
        {
            return directories[0];
        }

        return extractionRoot;
    }

    private static string BuildExtractionNote(string installRoot)
    {
        string[] files = Directory.GetFiles(installRoot, "*", SearchOption.AllDirectories);
        return files.Length == 0
            ? "未提取出可用文件"
            : $"已提取 {files.Length} 个文件";
    }

    private sealed class ScelEntry
    {
        public string Text { get; init; } = string.Empty;

        public string Pinyin { get; init; } = string.Empty;

        public int Weight { get; init; }
    }

    private static (IReadOnlyDictionary<ushort, string> Map, int StartChinese) ParseScelPinyinMap(byte[] bytes)
    {
        int bestPosition = 0;
        Dictionary<ushort, string> bestMap = new();

        foreach (int startPy in new[] { 0x1540, 0x1544 })
        {
            Dictionary<ushort, string> map = new();
            int position = startPy;
            while (position + 4 <= bytes.Length)
            {
                ushort index = BitConverter.ToUInt16(bytes, position);
                ushort length = BitConverter.ToUInt16(bytes, position + 2);
                if (length == 0 ||
                    length % 2 != 0 ||
                    length > 64 ||
                    position + 4 + length > bytes.Length)
                {
                    break;
                }

                string value = Encoding.Unicode.GetString(bytes, position + 4, length).TrimEnd('\0', ' ');
                if (!IsLikelyScelPinyinValue(value))
                {
                    break;
                }

                map[index] = value;
                position += 4 + length;
            }

            if (map.Count > bestMap.Count)
            {
                bestMap = map;
                bestPosition = position;
            }
        }

        return (bestMap, bestPosition);
    }

    private static bool IsLikelyScelPinyinValue(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.All(ch => char.IsLetter(ch) || ch == '\'');
    }

    private static IReadOnlyList<ScelEntry> ParseScelEntries(byte[] bytes, IReadOnlyDictionary<ushort, string> pinyinMap, int startChinese)
    {
        List<ScelEntry> entries = [];
        int position = startChinese;
        while (position + 4 <= bytes.Length)
        {
            ushort samePinyinWordCount = BitConverter.ToUInt16(bytes, position);
            position += 2;
            ushort pinyinTableLength = BitConverter.ToUInt16(bytes, position);
            position += 2;
            if (samePinyinWordCount == 0 || pinyinTableLength == 0 || position + pinyinTableLength > bytes.Length
                || samePinyinWordCount > 5000 || pinyinTableLength > 500)
            {
                break;
            }

            List<string> pinyinSegments = [];
            int pinyinCount = pinyinTableLength / 2;
            for (int index = 0; index < pinyinCount; index++)
            {
                ushort mapIndex = BitConverter.ToUInt16(bytes, position);
                position += 2;
                if (pinyinMap.TryGetValue(mapIndex, out string? mappedValue))
                {
                    pinyinSegments.Add(mappedValue);
                }
            }

            string pinyin = string.Join(" ", pinyinSegments);
            for (int wordIndex = 0; wordIndex < samePinyinWordCount; wordIndex++)
            {
                if (position + 2 > bytes.Length)
                {
                    return entries;
                }

                ushort wordLength = BitConverter.ToUInt16(bytes, position);
                position += 2;
                if (wordLength == 0 || position + wordLength > bytes.Length || wordLength > 128)
                {
                    if (wordLength > 128)
                    {
                        position += wordLength;
                        continue;
                    }
                    return entries;
                }

                string word = Encoding.Unicode.GetString(bytes, position, wordLength).TrimEnd('\0', ' ');
                position += wordLength;
                if (word.Any(c => c <= '\u001F' || c == '\u007F'))
                {
                    if (position + 2 > bytes.Length)
                    {
                        return entries;
                    }

                    ushort skipExtensionLength = BitConverter.ToUInt16(bytes, position);
                    position += 2 + skipExtensionLength;
                    continue;
                }
                if (position + 2 > bytes.Length)
                {
                    return entries;
                }

                ushort extensionLength = BitConverter.ToUInt16(bytes, position);
                position += 2;
                int weight = 1;
                if (extensionLength > 0)
                {
                    if (position + extensionLength > bytes.Length)
                    {
                        return entries;
                    }

                    if (extensionLength >= 2)
                    {
                        weight = Math.Max(1, (int)BitConverter.ToUInt16(bytes, position));
                    }

                    position += extensionLength;
                }

                if (!string.IsNullOrWhiteSpace(word) && !string.IsNullOrWhiteSpace(pinyin))
                {
                    entries.Add(new ScelEntry
                    {
                        Text = word,
                        Pinyin = pinyin,
                        Weight = weight,
                    });
                }
            }
        }

        return entries;
    }

    private static (string InstalledVersion, string Note) DownloadBinaryResource(string downloadUrl, string targetPath)
    {
        return ResumableDownloader.DownloadToFile(downloadUrl, targetPath);
    }

    private static (string InstalledVersion, string Note) DownloadTextLikeResource(string source, string targetPath)
    {
        string body = ResumableDownloader.DownloadToString(source);
        RepositoryContext.WriteUtf8(targetPath, body);
        return ("downloaded", $"已下载文本资源：{Path.GetFileName(targetPath)}");
    }

    private static string NormalizeVersionStrategy(string? strategy)
    {
        return string.Equals(strategy, "pinned", StringComparison.OrdinalIgnoreCase)
            ? "pinned"
            : "latest";
    }
}
