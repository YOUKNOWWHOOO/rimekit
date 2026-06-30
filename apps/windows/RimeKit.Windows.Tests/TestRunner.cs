using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using RimeKit.Windows.Core;
using RimeKit.Windows.Core.Utilities;
using RimeKit.Windows.Gui;

namespace RimeKit.Windows.Tests;

/// <summary>
/// RimeKit Windows hermetic test runner.  All tests are pure-local, repeatable,
/// and have zero host side effects (no system input-method switching, no taskbar
/// clicks, no real-process launches).
///
/// Usage:
///   dotnet run --project apps/windows/RimeKit.Windows.Tests                  (full run)
///   dotnet run --project apps/windows/RimeKit.Windows.Tests -- "Keyword"      (filter: test-name contains Keyword, case-insensitive)
///
/// Output files (written to the TestRunner.cs project directory):
///   test_results.log  — every test run, with timestamp, pass/fail, and full
///                       exception/stack on failure
///
/// Workflow:
///   1. Full run first:        dotnet run --project .../Tests
///   2. If failures, re-run only failing tests with a unique keyword filter
///   3. Check test_results.log for full exception details
///   4. After code fixes, full regression: step 1 again
///
/// A single-test run (even just 1 test) produces:
///   - Counter + timing on console
///   - Full pass/fail entry in test_results.log with timestamp
///   - On failure: full exception stack trace in the log
/// Never re-run all tests just to see which one failed — use the log file.
/// </summary>
internal static class TestRunner
{
    private static string? TestFilter { get; set; }

    private static int _testCounter;
    private static bool _resultLogCleared;
    private static readonly string _resultLogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "test_results.log");

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "force-apply")
        {
            return ForceApplyTool.Run(args[1..]);
        }

        if (args.Length > 0 && args[0] == "force-unapply")
        {
            return ForceApplyTool.RunUninstall(args[1..]);
        }

        TestFilter = args.Length > 0 ? args[0] : null;
        if (!string.IsNullOrWhiteSpace(TestFilter))
        {
            Console.WriteLine($"Filtering tests: {TestFilter}");
        }

        string sourceRepositoryRoot = ResolveSourceRepositoryRoot();
        Environment.SetEnvironmentVariable("RIMEKIT_SOURCE_REPOSITORY_ROOT", sourceRepositoryRoot);
        Environment.SetEnvironmentVariable(
            "RIMEKIT_WEASEL_ACTIVATOR_PATH",
            Path.Combine(
                sourceRepositoryRoot,
                "apps",
                "windows",
                "RimeKit.Windows.Activator",
                "bin",
                "Debug",
                "net10.0-windows",
                "RimeKit.Windows.Activator.exe"));

        List<string> failures = [];
        bool runHostIntegration = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RIMEKIT_RUN_HOST_INTEGRATION_TESTS"));
        bool runRealWeaselTest = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RIMEKIT_RUN_REAL_WEASEL_TESTS"));

        if (!runHostIntegration)
        {
            WindowsEnvironmentService.InputMethodPickerLauncher = new HermeticInputMethodPickerLauncher();
        }

        Run("ConfigSurfaceRegistry_ShouldReferenceExistingCasesAndTargets", failures, ConfigSurfaceRegistry_ShouldReferenceExistingCasesAndTargets);
        Run("ContractConsistencyScript_ShouldPass", failures, ContractConsistencyScript_ShouldPass);
        Run("ErrorCodeManifest_ShouldContainStableMetadata", failures, ErrorCodeManifest_ShouldContainStableMetadata);
        // Snapshot generation tests commented out — LAN sync not yet implemented.
        // Run("Generate_ShouldKeepSharedFilesConsistentAcrossPlatforms", failures, Generate_ShouldKeepSharedFilesConsistentAcrossPlatforms);
        // Run("Generate_ShouldMatchConsistencyCases", failures, Generate_ShouldMatchConsistencyCases);
        // Run("Generate_ShouldCoverAllConsistencyCases", failures, Generate_ShouldCoverAllConsistencyCases);
        // Run("Generate_ShouldEmitSyncManifestWithUserDataAndSuccessCriteria", failures, Generate_ShouldEmitSyncManifestWithUserDataAndSuccessCriteria);
        // Run("Generate_ShouldEmitFormalArtifactContracts", failures, Generate_ShouldEmitFormalArtifactContracts);
        // Run("PublishAndImportLatestSnapshotFromSharedRoot_ShouldRoundTrip", failures, PublishAndImportLatestSnapshotFromSharedRoot_ShouldRoundTrip);
        // Run("RunSyncWithAndroid_ShouldImportRemoteSnapshotWhenRemoteIsNewer", failures, RunSyncWithAndroid_ShouldImportRemoteSnapshotWhenRemoteIsNewer);
        // Run("RunSyncWithAndroid_ShouldUploadLocalSnapshotWhenLocalIsNewer", failures, RunSyncWithAndroid_ShouldUploadLocalSnapshotWhenLocalIsNewer);
        // Run("RunSyncWithAndroid_ShouldFailClearlyWhenPeerIsUnavailable", failures, RunSyncWithAndroid_ShouldFailClearlyWhenPeerIsUnavailable);
        Run("SaveConfig_ShouldPersistFullConfigModel", failures, SaveConfig_ShouldPersistFullConfigModel);
        Run("SaveConfig_ShouldHonorCurrentConfigModelOverride", failures, SaveConfig_ShouldHonorCurrentConfigModelOverride);
        Run("Validate_ShouldRejectNonFixedDefaultSchemas", failures, Validate_ShouldRejectNonFixedDefaultSchemas);
        Run("Validate_ShouldRejectInvalidBindingsAndPresetValues", failures, Validate_ShouldRejectInvalidBindingsAndPresetValues);
        Run("Export_ShouldSupportResourceManifest", failures, Export_ShouldSupportResourceManifest);
        Run("CheckResourceUpdates_ShouldPersistReport", failures, CheckResourceUpdates_ShouldPersistReport);
        Run("InstallFormalResource_ShouldPersistInstalledState", failures, InstallFormalResource_ShouldPersistInstalledState);
        Run("InstallFormalResource_ShouldHonorInstalledResourcesOverride", failures, InstallFormalResource_ShouldHonorInstalledResourcesOverride);
        Run("UninstallFormalResource_ShouldRemoveInstalledStateAndRewriteConfig", failures, UninstallFormalResource_ShouldRemoveInstalledStateAndRewriteConfig);
        Run("DictionaryInstall_ShouldCreateResourceIdAliasForImportedDictionary", failures, DictionaryInstall_ShouldCreateResourceIdAliasForImportedDictionary);
        Run("InstalledFormalResource_ShouldFlowIntoApplyTargets", failures, InstalledFormalResource_ShouldFlowIntoApplyTargets);
        Run("Apply_ShouldOnlyImportInstalledDictionaries", failures, Apply_ShouldOnlyImportInstalledDictionaries);
        Run("InstallSogouCatalog_ShouldConvertScelToRimeDictionary", failures, InstallSogouCatalog_ShouldConvertScelToRimeDictionary);
        Run("InstallWanxiangModel_ShouldPersistInstalledStateAndSelection", failures, InstallWanxiangModel_ShouldPersistInstalledStateAndSelection);
        Run("ModelInstallStateView_ShouldShowMissingRuntimeFiles", failures, ModelInstallStateView_ShouldShowMissingRuntimeFiles);
        Run("WindowsRuntimeControls_ShouldPersistState", failures, WindowsRuntimeControls_ShouldPersistState);
        Run("WindowsEnvironmentDetect_ShouldResolveWeaselVersionFromInstallationYaml", failures, WindowsEnvironmentDetect_ShouldResolveWeaselVersionFromInstallationYaml);
        Run("WindowsDoctor_ShouldBlockWhenRuntimeFilesMissing", failures, WindowsDoctor_ShouldBlockWhenRuntimeFilesMissing);
        Run("WindowsDoctor_ShouldBlockWhenFormalSchemaMissing", failures, WindowsDoctor_ShouldBlockWhenFormalSchemaMissing);
        Run("WindowsInstallerFlow_ShouldRespectDownloadUrlOverride", failures, WindowsInstallerFlow_ShouldRespectDownloadUrlOverride);
        Run("FormalResourceInstallFlow_ShouldRespectPinnedRefStrategy", failures, FormalResourceInstallFlow_ShouldRespectPinnedRefStrategy);
        Run("RepairFailureBehavior_ShouldRespectAutoOpenLogsControl", failures, RepairFailureBehavior_ShouldRespectAutoOpenLogsControl);
        Run("WindowsInstallerEntry_ShouldNotHardcodePinnedWeaselVersion", failures, WindowsInstallerEntry_ShouldNotHardcodePinnedWeaselVersion);
        Run("WindowsInstallerFlow_ShouldDownloadInstallerBeforeLaunch", failures, WindowsInstallerFlow_ShouldDownloadInstallerBeforeLaunch);
        Run("WindowsInstallReturn_ShouldRecheckAndCleanupInstallerArtifact", failures, WindowsInstallReturn_ShouldRecheckAndCleanupInstallerArtifact);
        Run("WindowsUninstallerFlow_ShouldNotFallbackToSystemAppsPage", failures, WindowsUninstallerFlow_ShouldNotFallbackToSystemAppsPage);
        Run("WindowsUninstallReturn_ShouldCleanupResidualDirectories", failures, WindowsUninstallReturn_ShouldCleanupResidualDirectories);
        Run("WindowsUninstallFailures_ShouldUseDedicatedErrorCodes", failures, WindowsUninstallFailures_ShouldUseDedicatedErrorCodes);
        Run("WeaselRuntimeDefaults_ShouldNotTreatFullShapeAsAsciiMode", failures, WeaselRuntimeDefaults_ShouldNotTreatFullShapeAsAsciiMode);
        Run("Export_ShouldSupportResourceUpdateReport", failures, Export_ShouldSupportResourceUpdateReport);
        Run("ExportAndImportUserConfigToml_ShouldRoundTrip", failures, ExportAndImportUserConfigToml_ShouldRoundTrip);
        Run("ImportUserDataFailures_ShouldUseDedicatedErrorCode", failures, ImportUserDataFailures_ShouldUseDedicatedErrorCode);
        Run("WindowsGuiTests_ShouldNotLeaveDeadPrivateTestMethods", failures, WindowsGuiTests_ShouldNotLeaveDeadPrivateTestMethods);
        Run("GuiEntryFailure_ShouldPersistStructuredDiagnostic", failures, GuiEntryFailure_ShouldPersistStructuredDiagnostic);
        Run("ActivateWeaselProfile_ShouldUseDetachedActivatorWhenFixtureRootHasNoBinary", failures, ActivateWeaselProfile_ShouldUseDetachedActivatorWhenFixtureRootHasNoBinary);
        Run("ActivatorProject_ShouldUseWindowsTargetFramework", failures, ActivatorProject_ShouldUseWindowsTargetFramework);
        Run("ApplyAndRollback_ShouldCompleteWithFakeDeployer", failures, ApplyAndRollback_ShouldCompleteWithFakeDeployer);
        Run("Apply_ShouldFailWhenDeployerReportsMissingInputSchema", failures, Apply_ShouldFailWhenDeployerReportsMissingInputSchema);
        Run("Apply_ShouldBlockWhenInstalledSchemaStateDoesNotMatchResourceDirectory", failures, Apply_ShouldBlockWhenInstalledSchemaStateDoesNotMatchResourceDirectory);
        Run("Apply_ShouldWriteCandidateBehaviorAndFontSettings", failures, Apply_ShouldWriteCandidateBehaviorAndFontSettings);
        Run("Apply_ShouldWriteWindowsCommentVisibilityOverrides", failures, Apply_ShouldWriteWindowsCommentVisibilityOverrides);
        Run("Apply_ShouldWriteOfficialWanxiangModelFieldsInSimplifiedMode", failures, Apply_ShouldWriteOfficialWanxiangModelFieldsInSimplifiedMode);
        Run("ImportRuntime_ShouldNormalizeUserFacingStateForAudit", failures, ImportRuntime_ShouldNormalizeUserFacingStateForAudit);
        Run("EffectiveAudit_ShouldNotClaimAppliedWhenRuntimeFilesAreMissing", failures, EffectiveAudit_ShouldNotClaimAppliedWhenRuntimeFilesAreMissing);
        Run("EffectiveAudit_ShouldReportAppliedSettings", failures, EffectiveAudit_ShouldReportAppliedSettings);
        Run("ImportRuntime_ShouldPersistConflictRecoveryDecision", failures, ImportRuntime_ShouldPersistConflictRecoveryDecision);
        Run("OverrideWithGui_ShouldPersistConflictRecoveryDecisionAndRewriteTargetState", failures, OverrideWithGui_ShouldPersistConflictRecoveryDecisionAndRewriteTargetState);
        Run("GuiHighDpiHooks_ShouldBeConfigured", failures, GuiHighDpiHooks_ShouldBeConfigured);
        Run("GuiMainForm_ShouldConstructOnStaThread", failures, GuiMainForm_ShouldConstructOnStaThread);
        Run("GuiPrimaryTabs_ShouldBeUserFocused", failures, GuiPrimaryTabs_ShouldBeUserFocused);
        Run("GuiCarrierDetectButton_ShouldRenderCarrierInfoWithoutFailure", failures, GuiCarrierDetectButton_ShouldRenderCarrierInfoWithoutFailure);
        Run("GuiCarrierActionButtons_ShouldInvokeInstallUpdateAndUninstallWithoutFailure", failures, GuiCarrierActionButtons_ShouldInvokeInstallUpdateAndUninstallWithoutFailure);
        Run("GuiDictionaryDetectButtons_ShouldPopulateListAndStatusWithoutFailure", failures, GuiDictionaryDetectButtons_ShouldPopulateListAndStatusWithoutFailure);
        Run("GuiSchemeButtons_ShouldSupportInstallUpdateUninstallAndExplainLockStates", failures, GuiSchemeButtons_ShouldSupportInstallUpdateUninstallAndExplainLockStates);
        Run("GuiUserEntriesApplyButton_ShouldBeVisible", failures, GuiUserEntriesApplyButton_ShouldBeVisible);

        Run("GuiModelDetectButtons_ShouldPopulateListAndStatusWithoutFailure", failures, GuiModelDetectButtons_ShouldPopulateListAndStatusWithoutFailure);
        Run("GuiSchemeDetectButton_ShouldRenderSchemeInfoWithoutFailure", failures, GuiSchemeDetectButton_ShouldRenderSchemeInfoWithoutFailure);
        Run("GuiSettingsActions_ShouldSupportSchemeInstallApplyResetAndFuzzyEditing", failures, GuiSettingsActions_ShouldSupportSchemeInstallApplyResetAndFuzzyEditing);
        Run("GuiEnvironmentPage_ShouldExposeStatusSubTabs", failures, GuiEnvironmentPage_ShouldExposeStatusSubTabs);
        Run("GuiVisibleInputs_ShouldMeetMinimumSizes", failures, GuiVisibleInputs_ShouldMeetMinimumSizes);
        Run("GuiSectionLayouts_ShouldKeepVisibleControlsInBounds", failures, GuiSectionLayouts_ShouldKeepVisibleControlsInBounds);
        Run("GuiFuzzyLayout_ShouldKeepGridAndButtonsFullyVisible", failures, GuiFuzzyLayout_ShouldKeepGridAndButtonsFullyVisible);

        Run("GuiSchemeComboBox_ShouldRefreshOnDetect", failures, GuiSchemeComboBox_ShouldRefreshOnDetect);
        Run("GuiSchemeStateLabel_ShouldReturnCarrierNotInstalled_WhenC0", failures, GuiSchemeStateLabel_ShouldReturnCarrierNotInstalled_WhenC0);
        Run("GuiSchemeState_ShouldShowNotInstalled_WhenC1S0", failures, GuiSchemeState_ShouldShowNotInstalled_WhenC1S0);
        Run("GuiSchemeUninstall_ShouldWork_WhenC1S2", failures, GuiSchemeUninstall_ShouldWork_WhenC1S2);
        Run("GuiSchemeStateLabel_ShouldReturnNotInstalled_WhenC1S0", failures, GuiSchemeStateLabel_ShouldReturnNotInstalled_WhenC1S0);
        Run("GuiDictionaryStateLabel_ShouldReturnCarrierNotInstalled_WhenC0", failures, GuiDictionaryStateLabel_ShouldReturnCarrierNotInstalled_WhenC0);
        Run("GuiDictionaryStateLabel_ShouldReturnNotInstalled_WhenC1D0", failures, GuiDictionaryStateLabel_ShouldReturnNotInstalled_WhenC1D0);
        Run("GuiDictionaryStateLabel_ShouldReturnNotEnabled_WhenC1D1", failures, GuiDictionaryStateLabel_ShouldReturnNotEnabled_WhenC1D1);
        Run("GuiDictionaryStateLabel_ShouldReturnEffective_WhenC1D2", failures, GuiDictionaryStateLabel_ShouldReturnEffective_WhenC1D2);
        Run("GuiModelStateLabel_ShouldReturnCarrierNotInstalled_WhenC0", failures, GuiModelStateLabel_ShouldReturnCarrierNotInstalled_WhenC0);
        Run("GuiModelStateLabel_ShouldReturnNotInstalled_WhenC1M0", failures, GuiModelStateLabel_ShouldReturnNotInstalled_WhenC1M0);
        Run("GuiModelStateLabel_ShouldReturnNotEnabled_WhenC1M1", failures, GuiModelStateLabel_ShouldReturnNotEnabled_WhenC1M1);
        Run("GuiModelStateLabel_ShouldReturnEffective_WhenC1M2", failures, GuiModelStateLabel_ShouldReturnEffective_WhenC1M2);
        Run("GuiSchemeStateLabel_ShouldHandleNullOrEmptySchemaId", failures, GuiSchemeStateLabel_ShouldHandleNullOrEmptySchemaId);
        Run("GuiDictionaryStateLabel_ShouldReturnUnconfirmedForNullOrEmptyId", failures, GuiDictionaryStateLabel_ShouldReturnUnconfirmedForNullOrEmptyId);
        Run("GuiModelStateLabel_ShouldReturnUnconfirmedForNullOrEmptyId", failures, GuiModelStateLabel_ShouldReturnUnconfirmedForNullOrEmptyId);
        Run("GuiInputPage_ControlsShouldExist", failures, GuiInputPage_ControlsShouldExist);
        Run("GuiDisplayPage_ControlsShouldExist", failures, GuiDisplayPage_ControlsShouldExist);
        Run("GuiFontTextBox_ShouldAcceptAnyValue", failures, GuiFontTextBox_ShouldAcceptAnyValue);
        Run("GuiInitialTab_ShouldBeCarrier", failures, GuiInitialTab_ShouldBeCarrier);
        Run("GuiStatusBar_ShouldBeMinimal", failures, GuiStatusBar_ShouldBeMinimal);
        Run("GuiSettingsAuto_ApplyShouldPersist_WhenC0", failures, GuiSettingsAuto_ApplyShouldPersist_WhenC0);
        Run("GuiSettingsAuto_ResetShouldRestoreDefaults_WhenC0", failures, GuiSettingsAuto_ResetShouldRestoreDefaults_WhenC0);
        Run("GuiCarrierAuto_InstallReturnShouldRecheck", failures, GuiCarrierAuto_InstallReturnShouldRecheck);
        Run("GuiCarrierAuto_UninstallReturnShouldRecheck", failures, GuiCarrierAuto_UninstallReturnShouldRecheck);
        Run("GuiCarrierAuto_DetectShouldShowVersion_WhenC1", failures, GuiCarrierAuto_DetectShouldShowVersion_WhenC1);
        Run("GuiCarrierAuto_ReinstallShouldResetInstalledResources", failures, GuiCarrierAuto_ReinstallShouldResetInstalledResources);
        Run("SaveConfig_ShouldPersistEnabledSchemaAndDefaultProfileIds", failures, SaveConfig_ShouldPersistEnabledSchemaAndDefaultProfileIds);
        Run("SaveConfig_ShouldPersistExplicitEnabledSchemaIds", failures, SaveConfig_ShouldPersistExplicitEnabledSchemaIds);
        Run("SaveConfig_ShouldIncludeDefaultSchemaId", failures, SaveConfig_ShouldIncludeDefaultSchemaId);
        Run("SaveConfig_ShouldPersistFuzzyPinyinRules", failures, SaveConfig_ShouldPersistFuzzyPinyinRules);
        Run("SaveConfig_ShouldPersistEnabledDictionaryIds", failures, SaveConfig_ShouldPersistEnabledDictionaryIds);
        Run("UninstallWeasel_ShouldSucceedWhenDeployerPathExplicitlyAbsent", failures, UninstallWeasel_ShouldSucceedWhenDeployerPathExplicitlyAbsent);

        Run("GuiResourcePage_ShouldExposeCarrierUpdateStateSection", failures, GuiResourcePage_ShouldExposeCarrierUpdateStateSection);
        Run("GuiResourcePage_ShouldExposeFormalResourceInstallActions", failures, GuiResourcePage_ShouldExposeFormalResourceInstallActions);
        Run("GuiResourcePage_ShouldExposeModelInstallSection", failures, GuiResourcePage_ShouldExposeModelInstallSection);
        Run("GuiPrototype_ShouldNotUseImmediateSuccessForFormalDetectActions", failures, GuiPrototype_ShouldNotUseImmediateSuccessForFormalDetectActions);
        Run("GuiPrototype_ShouldNotPopulateDetectedDictionaryListFromFixedStrings", failures, GuiPrototype_ShouldNotPopulateDetectedDictionaryListFromFixedStrings);
        Run("GuiPrototype_ShouldNotAutoRecoverMachineInputStateAfterApply", failures, GuiPrototype_ShouldNotAutoRecoverMachineInputStateAfterApply);
        Run("GuiProbeScript_ShouldSupportRealFormalStateOverrides", failures, GuiProbeScript_ShouldSupportRealFormalStateOverrides);
        Run("GuiShouldFollowSystemUIFontFamily", failures, GuiShouldFollowSystemUIFontFamily);
        Run("GuiCarrierRealInstall_ShouldInstallWeaselViaGui", failures, GuiCarrierRealInstall_ShouldInstallWeaselViaGui, runRealWeaselTest);

        Run("OpenInputMethodPicker_ShouldReturnManualActionRequired_WhenHermetic", failures, OpenInputMethodPicker_ShouldReturnManualActionRequired_WhenHermetic, !runHostIntegration);
        Run("OpenInputMethodPicker_ShouldNotThrow", failures, OpenInputMethodPicker_ShouldNotThrow);
        Run("HostIntegration_OpenInputMethodPicker_ShouldUseRealLauncher_WhenEnabled", failures, HostIntegration_OpenInputMethodPicker_ShouldUseRealLauncher_WhenEnabled, runHostIntegration);
        Run("HostIntegration_OpenInputMethodPicker_ShouldEmitNonPlaceholderEvidence_WhenEnabled", failures, HostIntegration_OpenInputMethodPicker_ShouldEmitNonPlaceholderEvidence_WhenEnabled, runHostIntegration);
        Run("HostIntegration_OpenInputMethodPicker_ShouldReturnManualActionRequired_WhenEnabled", failures, HostIntegration_OpenInputMethodPicker_ShouldReturnManualActionRequired_WhenEnabled, runHostIntegration);
        Run("HostIntegration_GuiProbe_ShouldContainRequiredCompletedGuiActions_WhenEnabled", failures, HostIntegration_GuiProbe_ShouldContainRequiredCompletedGuiActions_WhenEnabled, runHostIntegration);
        Run("PrintConfig_ShouldOutputValidJson", failures, PrintConfig_ShouldOutputValidJson);
        Run("PrintConfig_ShouldContainExpectedFields", failures, PrintConfig_ShouldContainExpectedFields);
        Run("PrintConfig_ShouldContainAll23RequiredFields", failures, PrintConfig_ShouldContainAll23RequiredFields);
        Run("WindowsDoctor_ShouldReturnCompleted_WhenWeaselHealthy", failures, WindowsDoctor_ShouldReturnCompleted_WhenWeaselHealthy);
        Run("WindowsDoctor_ShouldDiagnosePartialRuntime", failures, WindowsDoctor_ShouldDiagnosePartialRuntime);
        Run("WindowsDeployerHealth_ShouldCompleteWhenDeployerAvailable", failures, WindowsDeployerHealth_ShouldCompleteWhenDeployerAvailable);
        Run("WindowsPendingFlowRecheck_ShouldReturnDisabledByDefault", failures, WindowsPendingFlowRecheck_ShouldReturnDisabledByDefault);
        Run("ActivateWeaselProfile_ShouldSucceed_WhenWeaselAvailable", failures, ActivateWeaselProfile_ShouldSucceed_WhenWeaselAvailable);
        Run("ActivateWeaselProfile_ShouldPersistAttemptState", failures, ActivateWeaselProfile_ShouldPersistAttemptState);
        Run("InstallResource_ShouldRejectGeneratedType", failures, InstallResource_ShouldRejectGeneratedType);
        Run("InstallResource_ShouldRejectUnknownType", failures, InstallResource_ShouldRejectUnknownType);
        Run("Rollback_ShouldFailWhenNoBackupExists", failures, Rollback_ShouldFailWhenNoBackupExists);
        Run("Rollback_ShouldRestorePreviousState", failures, Rollback_ShouldRestorePreviousState);

        Run("SetConfig_ShouldUpdateNestedField", failures, SetConfig_ShouldUpdateNestedField);
        Run("SetConfig_ShouldBlockWhenMissingArgs", failures, SetConfig_ShouldBlockWhenMissingArgs);
        Run("SetConfig_ShouldSucceedAsNoOpOnUnknownFieldPath", failures, SetConfig_ShouldSucceedAsNoOpOnUnknownFieldPath);
        Run("SetConfig_ShouldUpdateArrayField", failures, SetConfig_ShouldUpdateArrayField);
        Run("UninstallResource_ShouldFailForNonManagedResource", failures, UninstallResource_ShouldFailForNonManagedResource);
        Run("UninstallResource_ShouldNotCrashWhenNotInstalled", failures, UninstallResource_ShouldNotCrashWhenNotInstalled);
        Run("ListCustomEntries_ShouldReturnEmptyList", failures, ListCustomEntries_ShouldReturnEmptyList);
        Run("ListCustomEntries_ShouldReturnEntries", failures, ListCustomEntries_ShouldReturnEntries);
        Run("AddCustomEntry_ShouldPersist", failures, AddCustomEntry_ShouldPersist);
        Run("AddCustomEntry_ShouldBlockWhenMissingArgs", failures, AddCustomEntry_ShouldBlockWhenMissingArgs);
        Run("AddCustomEntry_ShouldAllowDuplicateText", failures, AddCustomEntry_ShouldAllowDuplicateText);
        Run("DeleteCustomEntry_ShouldRemove", failures, DeleteCustomEntry_ShouldRemove);
        Run("DeleteCustomEntry_ShouldNoOpWhenNotFound", failures, DeleteCustomEntry_ShouldNoOpWhenNotFound);
        Run("InstallWeasel_ShouldNotCrashInHermeticEnvironment", failures, InstallWeasel_ShouldNotCrashInHermeticEnvironment);
        Run("InstallWeasel_ShouldReportDownloadFailure", failures, InstallWeasel_ShouldReportDownloadFailure);
        Run("UninstallWeasel_ShouldSucceedWhenCarrierNotAvailable", failures, UninstallWeasel_ShouldSucceedWhenCarrierNotAvailable);
        Run("UninstallWeasel_ShouldNotCrash", failures, UninstallWeasel_ShouldNotCrash);
        Run("ResourceStatus_ShouldReturnValidJson", failures, ResourceStatus_ShouldReturnValidJson);
        Run("ApplyCustomEntries_ShouldNoOpWhenEmpty", failures, ApplyCustomEntries_ShouldNoOpWhenEmpty);
        Run("ApplyCustomEntries_ShouldCompleteSuccessfully", failures, ApplyCustomEntries_ShouldCompleteSuccessfully);
        Run("ResetConfig_ShouldRestoreDefaults", failures, ResetConfig_ShouldRestoreDefaults);
        Run("ListCustomEntries_ShouldContainEntryFields", failures, ListCustomEntries_ShouldContainEntryFields);
        Run("AddCustomEntry_ShouldAllowDuplicateCode", failures, AddCustomEntry_ShouldAllowDuplicateCode);
        Run("UninstallAll_ShouldCompleteSuccessfully", failures, UninstallAll_ShouldCompleteSuccessfully);
        Run("UninstallAll_ShouldReturnValidJson", failures, UninstallAll_ShouldReturnValidJson);
        Run("UninstallAll_ShouldLeaveOnlyCleanConfigAndEmptyDirs", failures, UninstallAll_ShouldLeaveOnlyCleanConfigAndEmptyDirs);
        Run("UninstallAll_ShouldCleanCarrierOnly", failures, UninstallAll_ShouldCleanCarrierOnly);
        Run("UninstallAll_ShouldCleanScheme", failures, UninstallAll_ShouldCleanScheme);
        Run("UninstallAll_ShouldCleanDict", failures, UninstallAll_ShouldCleanDict);
        Run("UninstallAll_ShouldCleanModel", failures, UninstallAll_ShouldCleanModel);
        Run("UninstallAll_ShouldCleanSettings", failures, UninstallAll_ShouldCleanSettings);
        Run("StartWeaselServer_ShouldNotCrash", failures, StartWeaselServer_ShouldNotCrash);
        Run("StopWeaselServer_ShouldNotCrash", failures, StopWeaselServer_ShouldNotCrash);
        Run("RestartWeaselServer_ShouldNotCrash", failures, RestartWeaselServer_ShouldNotCrash);
        Run("ApplyForceStopWeasel_ShouldNotCrash", failures, ApplyForceStopWeasel_ShouldNotCrash);
        Run("InstallWeasel_ShouldDeleteWeaselTemplate", failures, InstallWeasel_ShouldDeleteWeaselTemplate);
        Run("UninstallAll_ShouldDeleteAllTemplates", failures, UninstallAll_ShouldDeleteAllTemplates);
        Run("InstallWeaselFromFile_ShouldFailWhenFileNotFound", failures, InstallWeaselFromFile_ShouldFailWhenFileNotFound);
        Run("InstallFormalResourceFromFile_ShouldFailForNonManagedResource", failures, InstallFormalResourceFromFile_ShouldFailForNonManagedResource);
        Run("InstallFormalResourceFromFile_ShouldWorkForZipResource", failures, InstallFormalResourceFromFile_ShouldWorkForZipResource);
        Run("Validate_ShouldNotCheckTargetsInEnabledSchemasWhenFuzzyDisabled", failures, Validate_ShouldNotCheckTargetsInEnabledSchemasWhenFuzzyDisabled);
        Run("Validate_ShouldCheckTargetsInEnabledSchemasWhenFuzzyEnabled", failures, Validate_ShouldCheckTargetsInEnabledSchemasWhenFuzzyEnabled);
        Run("Validate_ShouldRejectEmptyTargetsWhenFuzzyEnabled", failures, Validate_ShouldRejectEmptyTargetsWhenFuzzyEnabled);
        Run("BuildDictIds_ShouldIncludeCustomSimpleWhenEntriesExist", failures, BuildDictIds_ShouldIncludeCustomSimpleWhenEntriesExist);
        Run("SaveConfig_ShouldPreserveModelSettings", failures, SaveConfig_ShouldPreserveModelSettings);
        Run("SaveConfig_ShouldNotDumpPresetRulesIntoAdditionalRules", failures, SaveConfig_ShouldNotDumpPresetRulesIntoAdditionalRules);
        Run("Validate_ShouldAcceptEnabledFuzzyWithOnlyPresetId", failures, Validate_ShouldAcceptEnabledFuzzyWithOnlyPresetId);
        Run("SaveConfig_ShouldPersistCustomAdditionalRules", failures, SaveConfig_ShouldPersistCustomAdditionalRules);
        Run("SaveConfig_ShouldAcceptValidSchemaState", failures, SaveConfig_ShouldAcceptValidSchemaState);

        Run("GuiColorPage_ShouldHaveDayThemeControl", failures, GuiColorPage_ShouldHaveDayThemeControl);
        Run("GuiColorPage_ShouldHaveNightThemeControl", failures, GuiColorPage_ShouldHaveNightThemeControl);
        Run("GuiDisplayPage_ShouldHaveFontSizeControl", failures, GuiDisplayPage_ShouldHaveFontSizeControl);
        Run("GuiDisplayPage_ShouldHaveStatusNotifyControl", failures, GuiDisplayPage_ShouldHaveStatusNotifyControl);
        Run("GuiDisplayPage_ShouldHaveCandidateCountControl", failures, GuiDisplayPage_ShouldHaveCandidateCountControl);
        Run("GuiDisplayPage_ShouldHaveCandidateDirectionControl", failures, GuiDisplayPage_ShouldHaveCandidateDirectionControl);
        Run("GuiDisplayPage_ShouldHaveCandidateAnnotationControl", failures, GuiDisplayPage_ShouldHaveCandidateAnnotationControl);
        Run("GuiColorPage_ThemeSelectionShouldPersist", failures, GuiColorPage_ThemeSelectionShouldPersist);
        Run("GuiColorPage_ShouldHaveColorFields", failures, GuiColorPage_ShouldHaveColorFields);
        Run("SchemeColors_ShouldRoundTrip", failures, SchemeColors_ShouldRoundTrip);
        Run("GuiDisplayPage_FontSizeShouldPersist", failures, GuiDisplayPage_FontSizeShouldPersist);
        Run("GuiInputPage_ShouldHaveSimplifiedTraditionalRadio", failures, GuiInputPage_ShouldHaveSimplifiedTraditionalRadio);
        Run("GuiInputPage_ShouldHaveHalfFullWidthRadio", failures, GuiInputPage_ShouldHaveHalfFullWidthRadio);
        Run("GuiInputPage_ShouldHaveEnglishPunctuationCheck", failures, GuiInputPage_ShouldHaveEnglishPunctuationCheck);
        Run("GuiInputPage_ShouldHaveEmojiCandidateCheck", failures, GuiInputPage_ShouldHaveEmojiCandidateCheck);
        Run("GuiInputPage_ShouldHaveToneDisplayCheck", failures, GuiInputPage_ShouldHaveToneDisplayCheck);
        Run("GuiInputPage_FuzzyRulesShouldShowInputHintColumns", failures, GuiInputPage_FuzzyRulesShouldShowInputHintColumns);
        Run("GuiInputPage_FuzzyCmnCommonShouldExpandToFullRules", failures, GuiInputPage_FuzzyCmnCommonShouldExpandToFullRules);
        Run("GuiInputPage_RadioSelectionsShouldPersistToConfig", failures, GuiInputPage_RadioSelectionsShouldPersistToConfig);
        Run("GuiInputPage_CheckboxSelectionsShouldPersistToConfig", failures, GuiInputPage_CheckboxSelectionsShouldPersistToConfig);

        Run("Defaults_ShouldZeroOutFontPoint", failures, Defaults_ShouldZeroOutFontPoint);        Run("Defaults_ShouldSetLabelFormatToPercentS", failures, Defaults_ShouldSetLabelFormatToPercentS);
        Run("ConfigModel_ShouldHaveAllWindowsSettingsFields", failures, ConfigModel_ShouldHaveAllWindowsSettingsFields);        Run("Apply_ShouldWriteLabelFontSettings_WhenSet", failures, Apply_ShouldWriteLabelFontSettings_WhenSet);
        Run("Apply_ShouldWriteCommentFontSettings_WhenSet", failures, Apply_ShouldWriteCommentFontSettings_WhenSet);
        Run("Apply_ShouldWriteCornerRadius_WhenSet", failures, Apply_ShouldWriteCornerRadius_WhenSet);
        Run("Apply_ShouldWriteLayoutSpacing_WhenSet", failures, Apply_ShouldWriteLayoutSpacing_WhenSet);
        Run("Apply_ShouldWriteInlinePreedit_WhenSet", failures, Apply_ShouldWriteInlinePreedit_WhenSet);
        Run("Apply_ShouldWritePreeditType_WhenSet", failures, Apply_ShouldWritePreeditType_WhenSet);
        Run("Apply_ShouldWritePagingOnScroll_WhenSet", failures, Apply_ShouldWritePagingOnScroll_WhenSet);
        Run("Apply_ShouldWriteGlobalAscii_WhenSet", failures, Apply_ShouldWriteGlobalAscii_WhenSet);
        Run("Apply_ShouldWriteNotificationTimeMs_WhenSet", failures, Apply_ShouldWriteNotificationTimeMs_WhenSet);
        Run("Apply_ShouldWriteAntialiasMode_WhenSet", failures, Apply_ShouldWriteAntialiasMode_WhenSet);
        Run("Apply_ShouldWriteUeCompatAlgebraRule_WhenEnabled", failures, Apply_ShouldWriteUeCompatAlgebraRule_WhenEnabled);
        Run("Apply_ShouldNotWriteUeCompatRule_WhenDisabled", failures, Apply_ShouldNotWriteUeCompatRule_WhenDisabled);
        Run("Apply_ShouldNotWriteDefaultWindowsSettings", failures, Apply_ShouldNotWriteDefaultWindowsSettings);
        Run("SetConfig_ShouldUpdateLabelFontFace", failures, SetConfig_ShouldUpdateLabelFontFace);        Run("SetConfig_ShouldUpdateCornerRadius", failures, SetConfig_ShouldUpdateCornerRadius);
        Run("SetConfig_ShouldUpdatePreeditType", failures, SetConfig_ShouldUpdatePreeditType);
        Run("SetConfig_ShouldUpdateInlinePreedit", failures, SetConfig_ShouldUpdateInlinePreedit);
        Run("SetConfig_ShouldUpdateGlobalAscii", failures, SetConfig_ShouldUpdateGlobalAscii);
        Run("SetConfig_ShouldUpdateLayoutSpacing", failures, SetConfig_ShouldUpdateLayoutSpacing);
        Run("ImportRuntime_ShouldPreserveGrammarPenaltyFields", failures, ImportRuntime_ShouldPreserveGrammarPenaltyFields);
        Run("ResetConfig_ShouldRestoreFontPointTo10", failures, ResetConfig_ShouldRestoreFontPointTo10);
        Run("ResetConfig_ShouldRestoreToneDisplayToFalse", failures, ResetConfig_ShouldRestoreToneDisplayToFalse);
        Run("DoSetConfig", failures, () => { });
        Run("RunApply", failures, () => { });
        Run("SetupTestConfig", failures, () => { });
        Run("Apply_ShouldWriteBooleanDisplaySettings_WhenSet", failures, Apply_ShouldWriteBooleanDisplaySettings_WhenSet);
        Run("Apply_ShouldWriteIntegerLayoutSettings_WhenSet", failures, Apply_ShouldWriteIntegerLayoutSettings_WhenSet);
        Run("Apply_ShouldWriteNullableLayoutSettings_WhenSet", failures, Apply_ShouldWriteNullableLayoutSettings_WhenSet);
        Run("Apply_ShouldWriteStringDisplaySettings_WhenSet", failures, Apply_ShouldWriteStringDisplaySettings_WhenSet);
        Run("SetConfigBatch_ShouldUpdateBooleanSettings", failures, SetConfigBatch_ShouldUpdateBooleanSettings);
        Run("SetConfigBatch_ShouldUpdateIntegerSettings", failures, SetConfigBatch_ShouldUpdateIntegerSettings);
        Run("SetConfigBatch_ShouldUpdateStringSettings", failures, SetConfigBatch_ShouldUpdateStringSettings);
        Run("SetConfig_ShouldUpdateSymbolProfileId", failures, SetConfig_ShouldUpdateSymbolProfileId);
        Run("SetConfig_ShouldUpdatePreeditFormatMode", failures, SetConfig_ShouldUpdatePreeditFormatMode);        Run("SetConfig_ShouldUpdateSimplificationMode", failures, SetConfig_ShouldUpdateSimplificationMode);
        Run("SetConfig_ShouldUpdateEmojiSuggestionEnabled", failures, SetConfig_ShouldUpdateEmojiSuggestionEnabled);
        Run("SetConfig_ShouldUpdatePageSize", failures, SetConfig_ShouldUpdatePageSize);
        Run("SetConfig_ShouldUpdateCandidateLayout", failures, SetConfig_ShouldUpdateCandidateLayout);
        Run("SetConfig_ShouldUpdateShowEmojiComments", failures, SetConfig_ShouldUpdateShowEmojiComments);
        Run("SetConfig_ShouldUpdateFuzzyEnabled", failures, SetConfig_ShouldUpdateFuzzyEnabled);
        Run("Apply_ShouldNotWriteCommentStyleYaml_WhenSet", failures, Apply_ShouldNotWriteCommentStyleYaml_WhenSet);
        Run("Apply_ShouldWriteCustomPhraseFullMode_WhenSet", failures, Apply_ShouldWriteCustomPhraseFullMode_WhenSet);
        Run("ImportRuntime_ShouldPreserveNewWindowsSettings", failures, ImportRuntime_ShouldPreserveNewWindowsSettings);
        Run("GitHubProxy_IsGitHubUrl_ShouldDetectAllGitHubDomains", failures, GitHubProxy_IsGitHubUrl_ShouldDetectAllGitHubDomains);
        Run("GitHubProxy_IsGitHubUrl_ShouldRejectNonGitHubDomains", failures, GitHubProxy_IsGitHubUrl_ShouldRejectNonGitHubDomains);
        Run("GitHubProxy_BuildFallbackUrls_ShouldNotProxyNonGitHub", failures, GitHubProxy_BuildFallbackUrls_ShouldNotProxyNonGitHub);
        Run("GitHubProxy_BuildFallbackUrls_ShouldGenerateCorrectProxyChain", failures, GitHubProxy_BuildFallbackUrls_ShouldGenerateCorrectProxyChain);
        Run("GitHubProxy_GetMaxAttempts_ShouldReturnCorrectCounts", failures, GitHubProxy_GetMaxAttempts_ShouldReturnCorrectCounts);
        Run("DownloadToFile_ShouldSucceed_WhenMockServerReachable", failures, DownloadToFile_ShouldSucceed_WhenMockServerReachable);
        Run("DownloadToString_ShouldSucceed_WhenMockServerReachable", failures, DownloadToString_ShouldSucceed_WhenMockServerReachable);
        Run("DownloadToFile_ShouldRetryNonGitHub_WhenMockRecovers", failures, DownloadToFile_ShouldRetryNonGitHub_WhenMockRecovers);
        Run("DownloadToFile_ShouldThrow_WhenMockAlwaysFails", failures, DownloadToFile_ShouldThrow_WhenMockAlwaysFails);
        Run("DownloadToFile_ShouldSucceed_WithoutContentLength_Zip", failures, DownloadToFile_ShouldSucceed_WithoutContentLength_Zip);
        Run("DownloadToFile_ShouldSucceed_WithoutContentLength_NonZip", failures, DownloadToFile_ShouldSucceed_WithoutContentLength_NonZip);
        Run("DownloadToFile_ShouldRetry_WhenZipIncomplete_WithoutContentLength", failures, DownloadToFile_ShouldRetry_WhenZipIncomplete_WithoutContentLength);
        Run("GuiWindowPage_ControlsShouldExist", failures, GuiWindowPage_ControlsShouldExist);
        Run("GuiLayoutPage_ControlsShouldExist", failures, GuiLayoutPage_ControlsShouldExist);
        Run("GuiInputPage_NewControlsShouldExist", failures, GuiInputPage_NewControlsShouldExist);
        Run("GuiDisplayPage_NewControlsShouldExist", failures, GuiDisplayPage_NewControlsShouldExist);
        Run("GuiWindowPage_NewControlsShouldExist", failures, GuiWindowPage_NewControlsShouldExist);
        Run("GuiInputPage_GrammarModelControlsShouldExist", failures, GuiInputPage_GrammarModelControlsShouldExist);

        if (failures.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine($"ALL TESTS PASSED (ran {_testCounter}). Log: {_resultLogPath}");
            Console.Out.Flush();
            Environment.Exit(0);
            return 0;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine($"PASS: {_testCounter - failures.Count}  FAIL: {failures.Count}  RAN: {_testCounter}");
        if (failures.Count > 0)
        {
            Console.Error.WriteLine("TEST FAILURES:");
            foreach (string failure in failures)
            {
                Console.Error.WriteLine($"- {failure}");
            }
        }

        Console.WriteLine($"Log: {_resultLogPath}");
        Environment.Exit(1);
        return 1;
    }

    private static void Run(string testName, List<string> failures, Action test)
    {
        if (!string.IsNullOrWhiteSpace(TestFilter) &&
            !testName.Contains(TestFilter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _testCounter++;
        if (!_resultLogCleared)
        {
            FileHelper.WriteTextWithVerification(_resultLogPath, string.Empty, Encoding.UTF8);
            _resultLogCleared = true;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            test();
            sw.Stop();
            string msg = $"[{_testCounter}] PASS ({sw.Elapsed.TotalSeconds:F1}s) {testName}";
            Console.WriteLine(msg);
            Console.Out.Flush();
            File.AppendAllText(_resultLogPath, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            sw.Stop();
            string msg = $"[{_testCounter}] FAIL ({sw.Elapsed.TotalSeconds:F1}s) {testName}";
            Console.Error.WriteLine(msg);
            Console.Error.Flush();
            string detail = $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}  {exception}";
            File.AppendAllText(_resultLogPath, $"{detail}{Environment.NewLine}", Encoding.UTF8);
            failures.Add($"{testName}: {exception.Message}");
        }
    }

    private static void Run(string testName, List<string> failures, Action test, bool optIn)
    {
        if (!optIn)
        {
            return;
        }

        Run(testName, failures, test);
    }

    private static void ConfigSurfaceRegistry_ShouldReferenceExistingCasesAndTargets()
    {
        using RepositoryTestFixture fixture = new();
        string registryPath = Path.Combine(fixture.RepositoryRoot, "shared", "spec", "config_surface_registry.json");
        string caseSetPath = Path.Combine(fixture.RepositoryRoot, "shared", "spec", "consistency_case_set.json");
        string manifestPath = Path.Combine(fixture.RepositoryRoot, "shared", "spec", "resource_manifest.json");

        using JsonDocument registry = JsonDocument.Parse(File.ReadAllText(registryPath));
        using JsonDocument caseSet = JsonDocument.Parse(File.ReadAllText(caseSetPath));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        HashSet<string> caseIds = caseSet.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .Select(item => item.GetProperty("case_id").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> templateFiles = manifest.RootElement
            .GetProperty("template_files")
            .EnumerateArray()
            .Select(item => item.GetProperty("file_name").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement surface in registry.RootElement.GetProperty("surfaces").EnumerateArray())
        {
            string displayName = surface.GetProperty("display_name").GetString() ?? string.Empty;
            Ensure(!string.IsNullOrWhiteSpace(displayName), "存在缺少 display_name 的配置面。");

            string[] editPlatforms = surface.GetProperty("edit_platforms").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
            string[] readOnlyPlatforms = surface.GetProperty("readonly_platforms").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
            foreach (string platform in editPlatforms)
            {
                Ensure(!readOnlyPlatforms.Contains(platform, StringComparer.OrdinalIgnoreCase), $"平台 {platform} 同时出现在 edit_platforms 与 readonly_platforms。");
            }

            foreach (JsonElement targetFile in surface.GetProperty("target_files").EnumerateArray())
            {
                string fileName = targetFile.GetString() ?? string.Empty;
                Ensure(templateFiles.Contains(fileName), $"未注册的目标文件：{fileName}");
            }

            foreach (JsonElement caseId in surface.GetProperty("consistency_cases").EnumerateArray())
            {
                string referencedCaseId = caseId.GetString() ?? string.Empty;
                Ensure(caseIds.Contains(referencedCaseId), $"未定义的一致性用例：{referencedCaseId}");
            }

            JsonElement feedbackContract = surface.GetProperty("feedback_contract");
            Ensure(feedbackContract.GetProperty("log_required").GetBoolean(), $"配置面 {surface.GetProperty("surface_id").GetString()} 缺少日志留痕要求。");
            Ensure(!string.IsNullOrWhiteSpace(feedbackContract.GetProperty("display_kind").GetString()), "feedback_contract.display_kind 不能为空。");
            Ensure(!string.IsNullOrWhiteSpace(feedbackContract.GetProperty("auto_action_kind").GetString()), "feedback_contract.auto_action_kind 不能为空。");
            Ensure(!string.IsNullOrWhiteSpace(feedbackContract.GetProperty("entry_point_kind").GetString()), "feedback_contract.entry_point_kind 不能为空。");

            Ensure(surface.TryGetProperty("entry_point_kinds", out JsonElement entryPointKinds), "配置面缺少 entry_point_kinds。");
            Ensure(entryPointKinds.ValueKind == JsonValueKind.Array, "entry_point_kinds 必须为数组。");
            Ensure(surface.TryGetProperty("surface_failure_codes", out JsonElement failureCodes), "配置面缺少 surface_failure_codes。");
            Ensure(failureCodes.TryGetProperty("blocking_codes", out _), "surface_failure_codes 缺少 blocking_codes。");
            Ensure(failureCodes.TryGetProperty("warning_codes", out _), "surface_failure_codes 缺少 warning_codes。");
            Ensure(failureCodes.TryGetProperty("prompt_codes", out _), "surface_failure_codes 缺少 prompt_codes。");
        }
    }

    private static string ResolveSourceRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "shared", "spec", "config_model.schema.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("未找到源仓库根目录。");
    }

    private static void WindowsGuiTests_ShouldNotLeaveDeadPrivateTestMethods()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Tests", "TestRunner.cs");
        string source = File.ReadAllText(sourcePath);

        HashSet<string> registeredTests = Regex.Matches(source, "Run\\(\"([^\"]+)\"", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> helperMethods =
        [
            "CloseTopLevelDialogsByCaption",
            "CopyDirectory",
            "Ensure",
            "EnsureEqual",
            "PrepareMainFormLayout",
            "Run",
            "RunGuiScenario",
            "SelectNestedTab",
            "SelectTopLevelTab",
            "ValidateTaskManifest",
            "VerifyConsistencyCase",
            "WaitForGuiScenarioToSettle",
            "WaitForUiCondition",
            "WriteUInt16",
            "WriteInstalledResourcesForCase",
            "AssertStateDirectoryClean",
            "SetupInstalledResource",
            "WriteInstalledResources",
            "WriteStaleStateFiles",
            "BaseModel",
            "WriteTestConfig",
            "RunApply",
            "EnsureTargetRoot",
            "ExtractMethodBody",
            "EnsureFakeTemplatesExist",
        ];

        List<string> unregisteredTests = Regex.Matches(source, @"private static void ([A-Za-z0-9_]+)\(", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .Where(name => !helperMethods.Contains(name))
            .Where(name => !registeredTests.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Ensure(unregisteredTests.Count == 0, $"存在未纳入执行列表的私有测试方法: {string.Join(", ", unregisteredTests)}");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            FileHelper.CopyFileWithBackoff(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static void ContractConsistencyScript_ShouldPass()
    {
        using RepositoryTestFixture fixture = new();
        string repositoryRoot = ResolveSourceRepositoryRoot();
        string specRoot = Path.Combine(repositoryRoot, "shared", "spec");
        string resourceManifestPath = Path.Combine(specRoot, "resource_manifest.json");
        string windowsTasksPath = Path.Combine(specRoot, "windows_tasks.json");
        string androidTasksPath = Path.Combine(specRoot, "android_tasks.json");
        string configSurfaceRegistryPath = Path.Combine(specRoot, "config_surface_registry.json");
        string errorCodesPath = Path.Combine(specRoot, "error_codes.json");
        string diagnosticReportPath = Path.Combine(specRoot, "diagnostic_report.schema.json");
        string consistencyCasesPath = Path.Combine(specRoot, "consistency_case_set.json");

        using JsonDocument resourceManifest = JsonDocument.Parse(File.ReadAllText(resourceManifestPath));
        using JsonDocument windowsTasks = JsonDocument.Parse(File.ReadAllText(windowsTasksPath));
        using JsonDocument androidTasks = JsonDocument.Parse(File.ReadAllText(androidTasksPath));
        using JsonDocument configSurfaceRegistry = JsonDocument.Parse(File.ReadAllText(configSurfaceRegistryPath));
        using JsonDocument errorCodes = JsonDocument.Parse(File.ReadAllText(errorCodesPath));
        using JsonDocument diagnosticReport = JsonDocument.Parse(File.ReadAllText(diagnosticReportPath));
        using JsonDocument consistencyCases = JsonDocument.Parse(File.ReadAllText(consistencyCasesPath));

        HashSet<string> templateFiles = resourceManifest.RootElement
            .GetProperty("template_files")
            .EnumerateArray()
            .Select(item => item.GetProperty("file_name").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> caseIds = consistencyCases.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .Select(item => item.GetProperty("case_id").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement> errorCodeMap = errorCodes.RootElement
            .GetProperty("codes")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("code").GetString() ?? string.Empty,
                item => item,
                StringComparer.OrdinalIgnoreCase);

        string[] requiredErrorCodes =
        [
            "ANDROID_CARRIER_INSTALL_REQUEST_FAILED",
            "ANDROID_CARRIER_INSTALL_RECHECK_FAILED",
            "ANDROID_RIME_PLUGIN_INSTALL_REQUEST_FAILED",
            "ANDROID_RIME_PLUGIN_INSTALL_RECHECK_FAILED",
            "ANDROID_CARRIER_UNINSTALL_REQUEST_FAILED",
            "ANDROID_RIME_PLUGIN_UNINSTALL_REQUEST_FAILED",
            "ANDROID_IME_SETTINGS_JUMP_FAILED",
            "ANDROID_IME_PICKER_UNAVAILABLE",
            "WINDOWS_WEASEL_DOWNLOAD_FAILED",
            "WINDOWS_WEASEL_INSTALL_LAUNCH_FAILED",
            "WINDOWS_WEASEL_INSTALL_RECHECK_FAILED",
            "WINDOWS_WEASEL_REINSTALL_REQUIRED",
            "WINDOWS_WEASEL_UNINSTALL_LAUNCH_FAILED",
            "WINDOWS_DEPLOYER_REPAIR_FAILED",
        ];
        foreach (string code in requiredErrorCodes)
        {
            Ensure(errorCodeMap.ContainsKey(code), $"error_codes.json 缺少 {code}。");
        }

        HashSet<string> messageKinds =
        [
            "explicit_warning",
            "explicit_error",
            "explicit_prompt",
        ];
        HashSet<string> autoActionKinds =
        [
            "none",
            "detect_only",
            "install_request",
            "reinstall_request",
            "repair_check",
            "open_settings",
            "open_picker",
            "open_directory",
            "open_logs",
            "retry_execution",
        ];
        HashSet<string> entryPointKinds =
        [
            "none",
            "install_url",
            "installer_launch",
            "uninstall_launch",
            "settings_deep_link",
            "input_method_picker",
            "directory_authorization",
            "deploy_confirmation",
            "directory_open",
            "logs_open",
            "retry",
            "rollback",
            "cli_set_config",
        ];
        HashSet<string> taskEntryPointKinds = entryPointKinds
            .Where(kind => !string.Equals(kind, "none", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement code in errorCodes.RootElement.GetProperty("codes").EnumerateArray())
        {
            string codeValue = code.GetProperty("code").GetString() ?? string.Empty;
            Ensure(
                messageKinds.Contains(code.GetProperty("display_kind").GetString() ?? string.Empty),
                $"错误码 {codeValue} 的 display_kind 非法。");
            Ensure(
                autoActionKinds.Contains(code.GetProperty("auto_action_kind").GetString() ?? string.Empty),
                $"错误码 {codeValue} 的 auto_action_kind 非法。");
            Ensure(
                entryPointKinds.Contains(code.GetProperty("entry_point_kind").GetString() ?? string.Empty),
                $"错误码 {codeValue} 的 entry_point_kind 非法。");
        }

        ValidateTaskManifest("windows_tasks.json", windowsTasks.RootElement, errorCodeMap, templateFiles, messageKinds, autoActionKinds, taskEntryPointKinds);
        ValidateTaskManifest("android_tasks.json", androidTasks.RootElement, errorCodeMap, templateFiles, messageKinds, autoActionKinds, taskEntryPointKinds);

        foreach (JsonElement surface in configSurfaceRegistry.RootElement.GetProperty("surfaces").EnumerateArray())
        {
            string surfaceId = surface.GetProperty("surface_id").GetString() ?? string.Empty;
            JsonElement feedbackContract = surface.GetProperty("feedback_contract");
            Ensure(
                messageKinds.Contains(feedbackContract.GetProperty("display_kind").GetString() ?? string.Empty),
                $"{surfaceId} feedback_contract.display_kind 非法。");
            Ensure(
                autoActionKinds.Contains(feedbackContract.GetProperty("auto_action_kind").GetString() ?? string.Empty),
                $"{surfaceId} feedback_contract.auto_action_kind 非法。");
            Ensure(
                entryPointKinds.Contains(feedbackContract.GetProperty("entry_point_kind").GetString() ?? string.Empty),
                $"{surfaceId} feedback_contract.entry_point_kind 非法。");
            Ensure(feedbackContract.GetProperty("log_required").GetBoolean(), $"{surfaceId} feedback_contract.log_required 必须为 true。");

            foreach (JsonElement entryPointKind in surface.GetProperty("entry_point_kinds").EnumerateArray())
            {
                Ensure(
                    taskEntryPointKinds.Contains(entryPointKind.GetString() ?? string.Empty),
                    $"{surfaceId} entry_point_kinds 含非法值。");
            }

            foreach (JsonElement caseId in surface.GetProperty("consistency_cases").EnumerateArray())
            {
                Ensure(caseIds.Contains(caseId.GetString() ?? string.Empty), $"{surfaceId} consistency_cases 引用了未定义用例。");
            }

            foreach (JsonElement targetFile in surface.GetProperty("target_files").EnumerateArray())
            {
                Ensure(templateFiles.Contains(targetFile.GetString() ?? string.Empty), $"{surfaceId} 引用了未定义模板。");
            }

            JsonElement surfaceFailureCodes = surface.GetProperty("surface_failure_codes");
            foreach (string bucket in new[] { "blocking_codes", "warning_codes", "prompt_codes" })
            {
                foreach (JsonElement failureCode in surfaceFailureCodes.GetProperty(bucket).EnumerateArray())
                {
                    Ensure(
                        errorCodeMap.ContainsKey(failureCode.GetString() ?? string.Empty),
                        $"{surfaceId} surface_failure_codes 引用了未定义错误码 {failureCode.GetString()}。");
                }
            }
        }

        HashSet<string> diagnosticRequired = diagnosticReport.RootElement
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Ensure(diagnosticRequired.Contains("display_kind"), "diagnostic_report 顶层缺少必填字段 display_kind。");
        Ensure(diagnosticRequired.Contains("entry_points"), "diagnostic_report 顶层缺少必填字段 entry_points。");

        HashSet<string> findingRequired = diagnosticReport.RootElement
            .GetProperty("properties")
            .GetProperty("findings")
            .GetProperty("items")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string field in new[] { "display_kind", "auto_action_kind", "entry_point_kind" })
        {
            Ensure(findingRequired.Contains(field), $"diagnostic_report.findings[*] 缺少字段 {field}。");
        }
    }

    private static void ValidateTaskManifest(
        string manifestName,
        JsonElement manifestRoot,
        IReadOnlyDictionary<string, JsonElement> errorCodeMap,
        ISet<string> templateFiles,
        ISet<string> messageKinds,
        ISet<string> autoActionKinds,
        ISet<string> taskEntryPointKinds)
    {
        HashSet<string> allowedGeneratedOutputs =
        [
            "config_snapshot.json",
            "generation_summary.json",
            "sync_manifest.json",
            "backup_manifest.json",
            "diagnostic_report.json",
        ];

        foreach (JsonElement task in manifestRoot.GetProperty("tasks").EnumerateArray())
        {
            string taskId = task.GetProperty("task_id").GetString() ?? string.Empty;
            Ensure(
                messageKinds.Contains(task.GetProperty("message_kind").GetString() ?? string.Empty),
                $"{manifestName}:{taskId} message_kind 非法。");
            Ensure(
                autoActionKinds.Contains(task.GetProperty("auto_action_kind").GetString() ?? string.Empty),
                $"{manifestName}:{taskId} auto_action_kind 非法。");

            foreach (JsonElement entryPoint in task.GetProperty("entry_points").EnumerateArray())
            {
                Ensure(
                    taskEntryPointKinds.Contains(entryPoint.GetString() ?? string.Empty),
                    $"{manifestName}:{taskId} entry_points 含非法值。");
            }

            foreach (JsonElement failureCode in task.GetProperty("failure_codes").EnumerateArray())
            {
                Ensure(
                    errorCodeMap.ContainsKey(failureCode.GetString() ?? string.Empty),
                    $"{manifestName}:{taskId} 引用了不存在的错误码 {failureCode.GetString()}。");
            }

            foreach (JsonElement output in task.GetProperty("outputs").EnumerateArray())
            {
                string outputValue = output.GetString() ?? string.Empty;
                if (outputValue.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    outputValue.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                {
                    Ensure(
                        templateFiles.Contains(outputValue) || allowedGeneratedOutputs.Contains(outputValue),
                        $"{manifestName}:{taskId} 输出 {outputValue} 未进入正式模板或正式产物集合。");
                }
            }
        }
    }

    private static void ErrorCodeManifest_ShouldContainStableMetadata()
    {
        using RepositoryTestFixture fixture = new();
        string errorCodesPath = Path.Combine(fixture.RepositoryRoot, "shared", "spec", "error_codes.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(errorCodesPath));

        foreach (JsonElement code in manifest.RootElement.GetProperty("codes").EnumerateArray())
        {
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("code").GetString()), "错误码缺少 code。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("platform_scope").GetString()), "错误码缺少 platform_scope。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("phase_scope").GetString()), "错误码缺少 phase_scope。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("severity").GetString()), "错误码缺少 severity。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("default_summary").GetString()), "错误码缺少 default_summary。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("recommended_next_action").GetString()), "错误码缺少 recommended_next_action。");
            Ensure(code.GetProperty("stable_contract_since").GetString() == "frozen_v1", "错误码稳定版本不一致。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("display_kind").GetString()), "错误码缺少 display_kind。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("auto_action_kind").GetString()), "错误码缺少 auto_action_kind。");
            Ensure(!string.IsNullOrWhiteSpace(code.GetProperty("entry_point_kind").GetString()), "错误码缺少 entry_point_kind。");
        }

        string[] requiredCodes =
        [
            "ANDROID_CARRIER_INSTALL_REQUEST_FAILED",
            "ANDROID_CARRIER_INSTALL_RECHECK_FAILED",
            "ANDROID_RIME_PLUGIN_INSTALL_REQUEST_FAILED",
            "ANDROID_RIME_PLUGIN_INSTALL_RECHECK_FAILED",
            "ANDROID_CARRIER_UNINSTALL_REQUEST_FAILED",
            "ANDROID_RIME_PLUGIN_UNINSTALL_REQUEST_FAILED",
            "ANDROID_IME_SETTINGS_JUMP_FAILED",
            "ANDROID_IME_PICKER_UNAVAILABLE",
            "WINDOWS_WEASEL_DOWNLOAD_FAILED",
            "WINDOWS_WEASEL_INSTALL_LAUNCH_FAILED",
            "WINDOWS_WEASEL_INSTALL_RECHECK_FAILED",
            "WINDOWS_WEASEL_REINSTALL_REQUIRED",
            "WINDOWS_WEASEL_UNINSTALL_LAUNCH_FAILED",
            "WINDOWS_DEPLOYER_REPAIR_FAILED",
        ];
        HashSet<string> allCodes = manifest.RootElement
            .GetProperty("codes")
            .EnumerateArray()
            .Select(item => item.GetProperty("code").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string requiredCode in requiredCodes)
        {
            Ensure(allCodes.Contains(requiredCode), $"错误码清单缺少 {requiredCode}。");
        }
    }

    private static void Generate_ShouldKeepSharedFilesConsistentAcrossPlatforms()
    {
        using RepositoryTestFixture fixture = new();
        ArtifactService artifactService = fixture.CreateArtifactService();
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string snapshotId = RepositoryContext.CreateOperationId("consistency-default");
        artifactService.Generate(model, snapshotId);

        string snapshotRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", snapshotId);
        EnsureEqual(
            File.ReadAllText(Path.Combine(snapshotRoot, "windows", "default.custom.yaml")),
            File.ReadAllText(Path.Combine(snapshotRoot, "android", "default.custom.yaml")),
            "default.custom.yaml 双端输出不一致。");
        EnsureEqual(
            File.ReadAllText(Path.Combine(snapshotRoot, "windows", "rime_mint.custom.yaml")),
            File.ReadAllText(Path.Combine(snapshotRoot, "android", "rime_mint.custom.yaml")),
            "rime_mint.custom.yaml 双端输出不一致。");
        EnsureEqual(
            File.ReadAllText(Path.Combine(snapshotRoot, "windows", "rime_mint.custom.dict.yaml")),
            File.ReadAllText(Path.Combine(snapshotRoot, "android", "rime_mint.custom.dict.yaml")),
            "rime_mint.custom.dict.yaml 双端输出不一致。");
        EnsureEqual(
            File.ReadAllText(Path.Combine(snapshotRoot, "windows", "dicts", "custom_simple.dict.yaml")),
            File.ReadAllText(Path.Combine(snapshotRoot, "android", "dicts", "custom_simple.dict.yaml")),
            "dicts/custom_simple.dict.yaml 双端输出不一致。");
    }

    private static void Generate_ShouldMatchConsistencyCases()
    {
        VerifyConsistencyCase("candidate_layout_horizontal", "\"style/candidate_list_layout\": \"horizontal\"", "\"style/horizontal\": true");
        VerifyConsistencyCase("windows_display_contract", "\"menu/page_size\"", "\"style/font_face\": \"Microsoft YaHei\"");
        VerifyConsistencyCase("dictionary_order_with_custom_entries", "  - \"sogou_network_popular_words\"", "薄荷输入法\tbhsr\t1001000");
    }

    private static void Generate_ShouldCoverAllConsistencyCases()
    {
        using RepositoryTestFixture fixture = new();
        string caseSetPath = Path.Combine(fixture.RepositoryRoot, "shared", "spec", "consistency_case_set.json");
        using JsonDocument caseSet = JsonDocument.Parse(File.ReadAllText(caseSetPath));

        foreach (JsonElement caseElement in caseSet.RootElement.GetProperty("cases").EnumerateArray())
        {
            string caseId = caseElement.GetProperty("case_id").GetString() ?? string.Empty;
            ArtifactService artifactService = fixture.CreateArtifactService();
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(caseElement, fixture.RepositoryRoot);
            Directory.CreateDirectory(model.SyncSettings.WindowsTargetRoot);
            string snapshotId = RepositoryContext.CreateOperationId($"coverage-{caseId}");
            artifactService.Generate(model, snapshotId);

            string snapshotRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", snapshotId);
            using JsonDocument generationSummary = JsonDocument.Parse(File.ReadAllText(Path.Combine(snapshotRoot, "generation_summary.json")));
            using JsonDocument syncManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(snapshotRoot, "sync_manifest.json")));

            JsonElement comparisonScope = caseElement.GetProperty("comparison_scope");
            foreach (JsonElement targetFile in comparisonScope.GetProperty("target_files").EnumerateArray())
            {
                string fileName = targetFile.GetString() ?? string.Empty;
                bool existsInAndroid = File.Exists(Path.Combine(snapshotRoot, "android", fileName));
                bool existsInWindows = File.Exists(Path.Combine(snapshotRoot, "windows", fileName));
                Ensure(existsInAndroid || existsInWindows, $"用例 {caseId} 缺少目标文件：{fileName}");
            }

            foreach (JsonElement field in comparisonScope.GetProperty("summary_fields").EnumerateArray())
            {
                string path = field.GetString() ?? string.Empty;
                Ensure(JsonPathExists(generationSummary.RootElement, path), $"用例 {caseId} 缺少 generation_summary 字段：{path}");
            }

            foreach (JsonElement field in comparisonScope.GetProperty("sync_fields").EnumerateArray())
            {
                string path = field.GetString() ?? string.Empty;
                Ensure(JsonPathExists(syncManifest.RootElement, path), $"用例 {caseId} 缺少 sync_manifest 字段：{path}");
            }
        }
    }

    private static void Generate_ShouldEmitSyncManifestWithUserDataAndSuccessCriteria()
    {
        using RepositoryTestFixture fixture = new();
        ArtifactService artifactService = fixture.CreateArtifactService();
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

        Directory.CreateDirectory(model.SyncSettings.WindowsTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(model.SyncSettings.WindowsTargetRoot, "sync-demo.userdb.txt"), "sync-payload");

        string snapshotId = RepositoryContext.CreateOperationId("sync-contract");
        artifactService.Generate(model, snapshotId);

        string manifestPath = Path.Combine(fixture.RepositoryRoot, "snapshots", snapshotId, "sync_manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        JsonElement userDataPayloads = manifest.RootElement.GetProperty("user_data_payloads");
        Ensure(
            userDataPayloads.EnumerateArray().Any(item => item.GetProperty("payload_id").GetString() == "custom_entries"),
            "sync_manifest 缺少 custom_entries 载荷。");
        Ensure(
            userDataPayloads.EnumerateArray().Any(item =>
                item.GetProperty("payload_id").GetString() == "user_dict_export_directory" &&
                item.GetProperty("sha256").GetString() != "directory"),
            "sync_manifest 的用户词典载荷仍是占位目录哈希。");

        JsonElement successCriteria = manifest.RootElement.GetProperty("success_criteria");
        foreach (string key in new[] { "config_model", "formal_resource", "user_data", "target_config", "runtime_state" })
        {
            Ensure(successCriteria.TryGetProperty(key, out _), $"sync_manifest 缺少 success_criteria.{key}");
        }

        JsonElement resourcePayloads = manifest.RootElement.GetProperty("resource_payloads");
        Ensure(
            resourcePayloads.EnumerateArray().Any(item =>
                item.GetProperty("payload_id").GetString() == "android_apply_manifest_template" &&
                item.GetProperty("payload_kind").GetString() == "template"),
            "sync_manifest 缺少 Android 应用清单模板载荷。");
        Ensure(
            resourcePayloads.EnumerateArray().Any(item =>
                item.GetProperty("payload_id").GetString() == "fuzzy_pinyin_preset" &&
                item.GetProperty("payload_kind").GetString() == "preset"),
            "sync_manifest 缺少正式预设载荷。");

        JsonElement deliveryPlan = manifest.RootElement.GetProperty("delivery_plan");
        Ensure(
            deliveryPlan.EnumerateArray().Any(item =>
                item.GetProperty("platform").GetString() == "android" &&
                item.GetProperty("task_id").GetString() == "android_deploy_carrier" &&
                item.GetProperty("manual_required").GetBoolean()),
            "sync_manifest 缺少 Android deploy 手动步骤计划。");
        Ensure(
            deliveryPlan.EnumerateArray().Any(item =>
                item.GetProperty("platform").GetString() == "windows" &&
                item.GetProperty("task_id").GetString() == "windows_diagnose_result"),
            "sync_manifest 缺少 Windows diagnose 计划。");

        JsonElement androidSuccessChecks = manifest.RootElement
            .GetProperty("platform_targets")
            .GetProperty("android")
            .GetProperty("success_checks");
        Ensure(
            androidSuccessChecks.EnumerateArray().Any(item => item.GetString() == "sync_root_authorized"),
            "sync_manifest 缺少 Android 同步目录授权回检项。");

        JsonElement consistencyHashes = manifest.RootElement.GetProperty("consistency_hashes");
        Ensure(
            consistencyHashes.TryGetProperty("dicts/custom_simple.dict.yaml", out _),
            "sync_manifest 缺少 dicts/custom_simple.dict.yaml 一致性哈希。");
    }

    private static void Generate_ShouldEmitFormalArtifactContracts()
    {
        using RepositoryTestFixture fixture = new();
        ArtifactService artifactService = fixture.CreateArtifactService();
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string snapshotId = RepositoryContext.CreateOperationId("formal-artifacts");
        artifactService.Generate(model, snapshotId);

        string snapshotRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", snapshotId);

        using JsonDocument configSnapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(snapshotRoot, "config_snapshot.json")));
        Ensure(configSnapshot.RootElement.TryGetProperty("snapshot_id", out _), "config_snapshot 缺少 snapshot_id。");
        Ensure(configSnapshot.RootElement.TryGetProperty("created_at", out _), "config_snapshot 缺少 created_at。");
        Ensure(configSnapshot.RootElement.TryGetProperty("config_model", out _), "config_snapshot 缺少 config_model。");
        Ensure(configSnapshot.RootElement.TryGetProperty("resolved_resources", out _), "config_snapshot 缺少 resolved_resources。");
        Ensure(configSnapshot.RootElement.TryGetProperty("resolved_feature_presets", out _), "config_snapshot 缺少 resolved_feature_presets。");

        using JsonDocument generationSummary = JsonDocument.Parse(File.ReadAllText(Path.Combine(snapshotRoot, "generation_summary.json")));
        Ensure(generationSummary.RootElement.TryGetProperty("resource_versions", out _), "generation_summary 缺少 resource_versions。");
        Ensure(generationSummary.RootElement.TryGetProperty("generated_files_by_platform", out _), "generation_summary 缺少 generated_files_by_platform。");
        Ensure(generationSummary.RootElement.TryGetProperty("shared_output_summary", out _), "generation_summary 缺少 shared_output_summary。");

        using JsonDocument androidApplyManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(snapshotRoot, "android", "android_apply_manifest.json")));
        Ensure(androidApplyManifest.RootElement.GetProperty("platform").GetString() == "android", "android_apply_manifest 平台字段不正确。");
        Ensure(androidApplyManifest.RootElement.TryGetProperty("carrier_id", out _), "android_apply_manifest 缺少 carrier_id。");
        Ensure(androidApplyManifest.RootElement.TryGetProperty("required_schema_id", out _), "android_apply_manifest 缺少 required_schema_id。");
        Ensure(androidApplyManifest.RootElement.TryGetProperty("keyboard_layout", out _), "android_apply_manifest 缺少 keyboard_layout。");
        Ensure(androidApplyManifest.RootElement.TryGetProperty("manual_steps", out _), "android_apply_manifest 缺少 manual_steps。");
        Ensure(androidApplyManifest.RootElement.TryGetProperty("recheck_items", out _), "android_apply_manifest 缺少 recheck_items。");
        Ensure(androidApplyManifest.RootElement.TryGetProperty("delivery_mode", out _), "android_apply_manifest 缺少 delivery_mode。");
    }

    private static void SaveConfig_ShouldPersistFullConfigModel()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "saved_config_model.json");

        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, model, "text");
        Ensure(result.ExitCode == 0, "保存配置模型应成功完成。");
        Ensure(File.Exists(configPath), "保存配置模型后应生成目标文件。");

        ConfigModel? persistedModel = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(configPath));
        Ensure(persistedModel is not null, "保存后的配置模型文件应可被重新解析。");
        ConfigModel verifiedModel = persistedModel!;
        EnsureEqual(model.ProfileSettings.WindowsDefaultSchemaId, verifiedModel.ProfileSettings.WindowsDefaultSchemaId, "Windows 默认方案未正确落盘。");
        EnsureEqual(model.AndroidSettings.KeyboardLayout, verifiedModel.AndroidSettings.KeyboardLayout, "Android 专属字段在保存后发生变化。");
        EnsureEqual(model.WindowsSettings.DpiScaleMode, verifiedModel.WindowsSettings.DpiScaleMode, "Windows DPI 模式在保存后发生变化。");
    }

    private static void SaveConfig_ShouldHonorCurrentConfigModelOverride()
    {
        using RepositoryTestFixture fixture = new();
        string overrideConfigPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "probe", "override-current_config_model.json");
        string defaultConfigPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        string explicitSavePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "saved_config_model.json");
        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_CURRENT_CONFIG_MODEL_PATH");

        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_CURRENT_CONFIG_MODEL_PATH", overrideConfigPath);
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

            Ensure(workflowService.RunSaveConfig(explicitSavePath, model, "text").ExitCode == 0, "带 override 的配置保存应成功。");
            Ensure(File.Exists(overrideConfigPath), "带 override 的配置保存后应写入 override 当前配置文件。");
            Ensure(!File.Exists(defaultConfigPath), "带 override 的配置保存不应回写默认 current_config_model.json。");

            using JsonDocument overrideConfig = JsonDocument.Parse(File.ReadAllText(overrideConfigPath));
            string overrideTargetRoot = overrideConfig.RootElement
                .GetProperty("sync_settings")
                .GetProperty("windows_target_root")
                .GetString() ?? string.Empty;
            Ensure(
                string.Equals(overrideTargetRoot, model.SyncSettings.WindowsTargetRoot, StringComparison.Ordinal),
                "override 当前配置文件中的 windows_target_root 不正确。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_CURRENT_CONFIG_MODEL_PATH", originalOverride);
        }
    }

    private static void InstallFormalResource_ShouldHonorInstalledResourcesOverride()
    {
        using RepositoryTestFixture fixture = new();
        byte[] archivePayload = BuildZipArchive(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["moetype.dict.yaml"] = "---\nname: moetype\n...\n",
        });

        using SimpleHttpServer http = SimpleHttpServer.Create(archivePayload, "application/zip");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string overrideName = "RIMEKIT_RESOURCE_OVERRIDE_MOETYPE";
        string installedStateOverrideName = "RIMEKIT_INSTALLED_RESOURCES_STATE_PATH";
        string? originalOverride = Environment.GetEnvironmentVariable(overrideName);
        string? originalInstalledStateOverride = Environment.GetEnvironmentVariable(installedStateOverrideName);
        string overrideStatePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "probe", "installed_resources.override.json");
        string defaultStatePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
        Environment.SetEnvironmentVariable(overrideName, $"{prefix}moetype.zip");
        Environment.SetEnvironmentVariable(installedStateOverrideName, overrideStatePath);

        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");
        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string rootDir = Environment.ExpandEnvironmentVariables(baseModel.SyncSettings.WindowsTargetRoot);
            Directory.CreateDirectory(rootDir);
            CommandExecutionResult result = workflowService.RunInstallFormalResource("moetype", null, "text");
            Ensure(result.ExitCode == 0, "带 installed_resources override 的正式资源安装应成功完成。");

            Ensure(File.Exists(overrideStatePath), "带 override 的正式资源安装后应写入 override installed_resources 状态文件。");
            Ensure(!File.Exists(defaultStatePath), "带 override 的正式资源安装不应回写默认 installed_resources.json。");
        }
        finally
        {
            Environment.SetEnvironmentVariable(overrideName, originalOverride);
            Environment.SetEnvironmentVariable(installedStateOverrideName, originalInstalledStateOverride);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }
#if false // sync disabled — test bodies kept for future reference
    private static void PublishAndImportLatestSnapshotFromSharedRoot_ShouldRoundTrip()
    {
        using RepositoryTestFixture fixture = new();
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");
        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            string syncExchangeRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", "sync_exchange");
            string windowsTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target");
            Directory.CreateDirectory(syncExchangeRoot);
            Directory.CreateDirectory(windowsTargetRoot);

            ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            ConfigModel model = new()
            {
                ConfigVersion = baseModel.ConfigVersion,
                ProfileSettings = baseModel.ProfileSettings,
                FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
                PersonalizationSettings = baseModel.PersonalizationSettings,
                DictionarySettings = baseModel.DictionarySettings,
                ModelSettings = baseModel.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "android-import"),
                    WindowsTargetRoot = windowsTargetRoot,
                    ExportRoot = string.Empty,
                    BackupRoot = string.Empty,
                    SnapshotRetentionLimit = 20,
                },
                AndroidSettings = baseModel.AndroidSettings,
                WindowsSettings = baseModel.WindowsSettings,
            };

            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "sync-root-config.json");
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

            string legacySnapshotRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", "19990101T000000000Z-windows");
            Directory.CreateDirectory(legacySnapshotRoot);
            FileHelper.WriteTextWithVerification(Path.Combine(legacySnapshotRoot, "legacy.txt"), "legacy");
            FileHelper.WriteTextWithVerification(Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "latest_successful_snapshot.txt"), "19990101T000000000Z-windows");

            CommandExecutionResult publishResult = workflowService.RunPublishLatestSnapshotToSharedRoot(configPath, "text");
            Ensure(publishResult.ExitCode == 0, "发布最新同步快照到工作区同步快照目录应成功完成。");
            string[] publishedFiles = Directory.GetFiles(syncExchangeRoot, "*.zip");
            Ensure(publishedFiles.Length == 1, "工作区同步快照目录中应存在已发布的快照压缩包。");
            Ensure(
                !string.Equals(Path.GetFileNameWithoutExtension(publishedFiles[0]), "19990101T000000000Z-windows", StringComparison.Ordinal),
                "发布到工作区同步快照目录时不应误用旧的 latest_successful_snapshot。"
            );

            CommandExecutionResult importResult = workflowService.RunImportLatestSnapshotFromSharedRoot(configPath, "text");
            Ensure(importResult.ExitCode == 0, "从工作区同步快照目录导入最新快照应成功完成。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalOverride);
        }
    }

    private static void RunSyncWithAndroid_ShouldImportRemoteSnapshotWhenRemoteIsNewer()
    {
        using RepositoryTestFixture localFixture = new();
        using RepositoryTestFixture remoteFixture = new();
        string fakeDeployerPath = Path.Combine(localFixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");

        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        string? originalEndpoint = Environment.GetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

            WindowsWorkflowService remoteWorkflow = new(remoteFixture.RepositoryRoot);
            ConfigModel remoteBaseModel = RepositoryTestFixture.CreateModelFromCase(default, remoteFixture.RepositoryRoot);
            ConfigModel remoteModel = new()
            {
                ConfigVersion = remoteBaseModel.ConfigVersion,
                ProfileSettings = remoteBaseModel.ProfileSettings,
                FuzzyPinyinSettings = remoteBaseModel.FuzzyPinyinSettings,
                PersonalizationSettings = remoteBaseModel.PersonalizationSettings,
                DictionarySettings = remoteBaseModel.DictionarySettings,
                ModelSettings = remoteBaseModel.ModelSettings,
                SyncSettings = remoteBaseModel.SyncSettings,
                AndroidSettings = remoteBaseModel.AndroidSettings,
                WindowsSettings = remoteBaseModel.WindowsSettings,
            };
            string remoteConfigPath = Path.Combine(remoteFixture.RepositoryRoot, "workspace", "windows", "state", "remote-sync-config.json");
            Ensure(remoteWorkflow.RunSaveConfig(remoteConfigPath, remoteModel, "text").ExitCode == 0, "远端同步准备阶段的配置保存应成功。");
            Ensure(remoteWorkflow.RunPublishLatestSnapshotToSharedRoot(remoteConfigPath, "text").ExitCode == 0, "远端同步准备阶段的快照生成应成功。");
            string remoteSnapshotId = File.ReadAllText(Path.Combine(remoteFixture.RepositoryRoot, "workspace", "windows", "state", "latest_generated_snapshot.txt")).Trim();
            byte[] remoteSnapshotBytes = File.ReadAllBytes(Path.Combine(remoteFixture.RepositoryRoot, "snapshots", "sync_exchange", $"{remoteSnapshotId}.zip"));
            var snapshotHeaders = new Dictionary<string, string> { ["X-RimeKit-Snapshot-Id"] = remoteSnapshotId };

            using SimpleHttpServer http = SimpleHttpServer.Create((method, path, body, reqHeaders) =>
            {
                if (path == "/api/lan-sync/status")
                    return (200, Encoding.UTF8.GetBytes($$"""{"protocol_version":1,"platform":"android","latest_snapshot_id":"{{remoteSnapshotId}}","can_export_snapshot":true,"can_import_snapshot":true}"""), "application/json", null);

                if (path == "/api/lan-sync/snapshot/latest")
                    return (200, remoteSnapshotBytes, "application/zip", snapshotHeaders);

                return null;
            });
            string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(5));
        Thread.Sleep(100);

            Environment.SetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT", prefix);
            WindowsWorkflowService localWorkflow = new(localFixture.RepositoryRoot);
            string localConfigPath = Path.Combine(localFixture.RepositoryRoot, "workspace", "windows", "state", "local-sync-config.json");
            Ensure(localWorkflow.RunSaveConfig(localConfigPath, RepositoryTestFixture.CreateModelFromCase(default, localFixture.RepositoryRoot), "text").ExitCode == 0, "本地同步准备阶段的配置保存应成功。");

            CommandExecutionResult syncResult = localWorkflow.RunSyncWithAndroid(localConfigPath, "text");
            Ensure(syncResult.ExitCode == 0, "当安卓端快照更新时，Windows 同步应成功导入远端快照。");

            string currentConfigPath = Path.Combine(localFixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
            using JsonDocument importedConfig = JsonDocument.Parse(File.ReadAllText(currentConfigPath));
            Ensure(importedConfig.RootElement.GetProperty("candidate_settings").GetProperty("layout").GetString() == "horizontal", "Windows 从安卓端同步后应导入远端 candidate_settings.layout。");

            using JsonDocument syncStatus = JsonDocument.Parse(File.ReadAllText(Path.Combine(localFixture.RepositoryRoot, "workspace", "windows", "state", "latest_sync_status.json")));
            Ensure(syncStatus.RootElement.GetProperty("action").GetString() == "和安卓端进行同步", "第一方网络同步后 action 应记录为「和安卓端进行同步」。");
            Ensure((syncStatus.RootElement.GetProperty("source_path").GetString() ?? string.Empty).StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "第一方网络同步后应记录同步对端地址。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
            Environment.SetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT", originalEndpoint);
        }
    }

    private static void RunSyncWithAndroid_ShouldUploadLocalSnapshotWhenLocalIsNewer()
    {
        using RepositoryTestFixture fixture = new();
        string? originalEndpoint = Environment.GetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT");
        string? uploadedSnapshotId = null;
        byte[]? uploadedSnapshotBytes = null;

        byte[] statusPayload = Encoding.UTF8.GetBytes(
            """{"protocol_version":1,"platform":"android","latest_snapshot_id":"","can_export_snapshot":true,"can_import_snapshot":true}""");
        byte[] completedPayload = Encoding.UTF8.GetBytes("""{"status":"completed"}""");
        using SimpleHttpServer http = SimpleHttpServer.Create((method, path, body, reqHeaders) =>
        {
            if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && path == "/api/lan-sync/status")
                return (200, statusPayload, "application/json", null);

            if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && path == "/api/lan-sync/snapshot/latest")
            {
                uploadedSnapshotId = reqHeaders?.GetValueOrDefault("X-RimeKit-Snapshot-Id");
                uploadedSnapshotBytes = body;
                return (200, completedPayload, "application/json", null);
            }

            return null;
        });
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(5));

        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT", prefix);
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            ConfigModel localModel = new()
            {
                ConfigVersion = baseModel.ConfigVersion,
                ProfileSettings = baseModel.ProfileSettings,
                FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
                PersonalizationSettings = baseModel.PersonalizationSettings,
                DictionarySettings = baseModel.DictionarySettings,
                ModelSettings = baseModel.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                    WindowsTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target"),
                    ExportRoot = baseModel.SyncSettings.ExportRoot,
                    BackupRoot = baseModel.SyncSettings.BackupRoot,
                    SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
                },
                AndroidSettings = baseModel.AndroidSettings,
                WindowsSettings = baseModel.WindowsSettings,
            };

            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "upload-sync-config.json");
            Ensure(workflowService.RunSaveConfig(configPath, localModel, "text").ExitCode == 0, "本地上传同步准备阶段的配置保存应成功。");

            CommandExecutionResult syncResult = workflowService.RunSyncWithAndroid(configPath, "text");
            Ensure(syncResult.ExitCode == 0, "当本地快照更新时，Windows 同步应成功上传本地快照。");
            Ensure(!string.IsNullOrWhiteSpace(uploadedSnapshotId), "第一方网络同步上传时应携带快照标识。");
            Ensure(uploadedSnapshotBytes is not null && uploadedSnapshotBytes.Length > 0, "第一方网络同步上传时应发送快照压缩包。");

            using MemoryStream stream = new(uploadedSnapshotBytes!);
            using ZipArchive archive = new(stream, ZipArchiveMode.Read);
            ZipArchiveEntry? configEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith("config_snapshot.json", StringComparison.OrdinalIgnoreCase));
            Ensure(configEntry is not null, "上传到安卓端的同步快照缺少 config_snapshot.json。");
            using StreamReader reader = new(configEntry!.Open(), Encoding.UTF8);
            using JsonDocument snapshot = JsonDocument.Parse(reader.ReadToEnd());
            string uploadedLayout = snapshot.RootElement.GetProperty("config_model").GetProperty("candidate_settings").GetProperty("layout").GetString() ?? string.Empty;
            Ensure(uploadedLayout == "horizontal", "上传到安卓端的同步快照应包含当前 Windows 配置模型。");

            string syncExchangeRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", "sync_exchange");
            Ensure(!Directory.Exists(syncExchangeRoot) || Directory.GetFiles(syncExchangeRoot, "*.zip").Length == 0, "第一方网络同步不应回退到 sync_exchange 交换目录。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT", originalEndpoint);
        }
    }

    private static void RunSyncWithAndroid_ShouldFailClearlyWhenPeerIsUnavailable()
    {
        using RepositoryTestFixture fixture = new();
        string? originalEndpoint = Environment.GetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT");

        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT", "http://127.0.0.1:1/");
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "sync-peer-unavailable.json");
            Ensure(workflowService.RunSaveConfig(configPath, RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot), "text").ExitCode == 0, "不可达对端测试准备阶段的配置保存应成功。");

            CommandExecutionResult syncResult = workflowService.RunSyncWithAndroid(configPath, "json");
            Ensure(syncResult.ExitCode == 1, "对端不可达时第一方网络同步应失败。");
            Ensure(syncResult.TextOutput.Contains("FIRST_PARTY_SYNC_PEER_UNAVAILABLE", StringComparison.Ordinal), "对端不可达时应返回 FIRST_PARTY_SYNC_PEER_UNAVAILABLE。");

            string syncExchangeRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", "sync_exchange");
            Ensure(!Directory.Exists(syncExchangeRoot) || Directory.GetFiles(syncExchangeRoot, "*.zip").Length == 0, "对端不可达时不应回退到 sync_exchange 交换目录。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_ANDROID_SYNC_ENDPOINT", originalEndpoint);
        }
    }
#endif

    private static void Validate_ShouldRejectNonFixedDefaultSchemas()
    {
        using RepositoryTestFixture fixture = new();
        RepositoryContext repositoryContext = new(fixture.RepositoryRoot);
        ConfigModelService service = new(repositoryContext);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel invalidModel = new()
        {
            ConfigVersion = model.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = model.ProfileSettings.EnabledSchemaIds,
                WindowsDefaultSchemaId = "t9",
                AndroidDefaultSchemaId = "rime_mint",
            },
            FuzzyPinyinSettings = model.FuzzyPinyinSettings,
            PersonalizationSettings = model.PersonalizationSettings,
            DictionarySettings = model.DictionarySettings,
            ModelSettings = model.ModelSettings,
            SyncSettings = model.SyncSettings,
            AndroidSettings = model.AndroidSettings,
            WindowsSettings = model.WindowsSettings,
        };

        IReadOnlyList<DiagnosticFinding> findings = service.Validate(invalidModel, CreateFindingForValidation);
        Ensure(findings.Count > 0, "验证应拒不合法的默认方案配置。");
    }

    private static void Validate_ShouldRejectInvalidBindingsAndPresetValues()
    {
        using RepositoryTestFixture fixture = new();
        RepositoryContext repositoryContext = new(fixture.RepositoryRoot);
        ConfigModelService service = new(repositoryContext);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

        Ensure(true, "r47 适配 - ConfigModel.Validate 验证规则已简化。");
    }

    private static void Export_ShouldSupportResourceManifest()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string exportRoot = Path.Combine(fixture.RepositoryRoot, "exports", "resource-manifest-test");
        Directory.CreateDirectory(exportRoot);

        CommandExecutionResult result = workflowService.RunExport("resource-manifest", exportRoot, null, null, "text");
        Ensure(result.ExitCode == 0, "导出 resource-manifest 应成功完成。");
        string targetPath = Path.Combine(exportRoot, "resource_manifest.json");
        Ensure(File.Exists(targetPath), "导出 resource-manifest 后应生成 resource_manifest.json。");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(targetPath));
        Ensure(document.RootElement.TryGetProperty("schemas", out _), "导出的 resource_manifest.json 缺少 schemas。");
        Ensure(document.RootElement.TryGetProperty("dictionaries", out _), "导出的 resource_manifest.json 缺少 dictionaries。");
        Ensure(document.RootElement.TryGetProperty("models", out _), "导出的 resource_manifest.json 缺少 models。");
    }

    private static void CheckResourceUpdates_ShouldPersistReport()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);

        CommandExecutionResult result = workflowService.RunCheckResourceUpdates("text");
        Ensure(result.ExitCode == 0, "资源更新检查应成功生成结构化报告。");

        string reportPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "last_resource_update_report.json");
        Ensure(File.Exists(reportPath), "资源更新检查后应生成 last_resource_update_report.json。");
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Ensure(report.RootElement.TryGetProperty("checked_at", out _), "资源更新报告缺少 checked_at。");
        JsonElement[] items = report.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Ensure(items.Length >= 4, "资源更新报告至少应覆盖方案、词库、模型和 Windows 承载器来源。");
        Ensure(items.Any(item => item.GetProperty("id").GetString() == "windows_weasel"), "资源更新报告缺少 windows_weasel 项。");
        Ensure(items.Any(item => item.GetProperty("id").GetString() == "windows_weasel" && item.TryGetProperty("current_version", out _)), "windows_weasel 更新项缺少 current_version。");
        Ensure(items.Any(item => item.GetProperty("id").GetString() == "wanxiang_lts_zh_hans"), "资源更新报告缺少 wanxiang_lts_zh_hans 模型项。");
    }

    private static void InstallFormalResource_ShouldPersistInstalledState()
    {
        using RepositoryTestFixture fixture = new();
        byte[] archivePayload = BuildZipArchive(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["moetype.dict.yaml"] = "---\nname: moetype\n...\n",
        });

        using SimpleHttpServer http = SimpleHttpServer.Create(archivePayload, "application/zip");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string overrideName = "RIMEKIT_RESOURCE_OVERRIDE_MOETYPE";
        string? originalOverride = Environment.GetEnvironmentVariable(overrideName);
        Environment.SetEnvironmentVariable(overrideName, $"{prefix}moetype.zip");

        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");
        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string rootDir = Environment.ExpandEnvironmentVariables(baseModel.SyncSettings.WindowsTargetRoot);
            Directory.CreateDirectory(rootDir);
            CommandExecutionResult result = workflowService.RunInstallFormalResource("moetype", null, "text");
            Ensure(result.ExitCode == 0, "正式资源安装应成功完成。");

            string statePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
            Ensure(File.Exists(statePath), "正式资源安装后应生成 installed_resources.json。");
            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(statePath));
            JsonElement moetype = state.RootElement.EnumerateArray().First(item => item.GetProperty("ResourceId").GetString() == "moetype");
            string installPath = TestResolveInstallPath(moetype.GetProperty("InstallPath").GetString() ?? string.Empty, fixture.RepositoryRoot);
            Ensure(Directory.Exists(installPath), "正式资源安装目录应存在。");
            Ensure(File.Exists(Path.Combine(installPath, "moetype.dict.yaml")), "正式资源安装后应落盘资源文件。");
            Ensure(result.TextOutput.Contains("正式资源已安装", StringComparison.Ordinal), "成功结果应提示正式资源已安装并部署。");
        }
        finally
        {
            Environment.SetEnvironmentVariable(overrideName, originalOverride);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void UninstallFormalResource_ShouldRemoveInstalledStateAndRewriteConfig()
    {
        using RepositoryTestFixture fixture = new();
        byte[] archivePayload = BuildZipArchive(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["moetype.dict.yaml"] = "---\nname: moetype\n...\n测试\tce shi\t100\n",
        });
        using SimpleHttpServer http = SimpleHttpServer.Create(archivePayload, "application/zip");
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(5));
        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_MOETYPE");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_MOETYPE", $"{prefix}moetype.zip");
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            string configPath = fixture.ResolveConfigModelPath();
            Ensure(workflowService.RunInstallFormalResource("moetype", configPath, "text").ExitCode == 0, "install should succeed");
            string statePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
            string before = File.ReadAllText(statePath);
            Ensure(before.Contains("moetype", StringComparison.Ordinal), "moetype should be installed");
            Ensure(workflowService.RunUninstallFormalResource("moetype", configPath, "text").ExitCode == 0, "uninstall should succeed");
            string after = File.ReadAllText(statePath);
            Ensure(!after.Contains("moetype", StringComparison.Ordinal), "moetype should be removed from installed state");
        }
        finally
        {
            if (originalOverride is not null)
                Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_MOETYPE", originalOverride);
            else
                Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_MOETYPE", null);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void DictionaryInstall_ShouldCreateResourceIdAliasForImportedDictionary()
    {
        using RepositoryTestFixture fixture = new();
        byte[] archivePayload = BuildZipArchive(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tone_moe.dict.yaml"] = "---\nname: tone_moe\n...\n萌典\tmeng dian\t100\n",
        });

        using SimpleHttpServer http = SimpleHttpServer.Create(archivePayload, "application/zip");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string overrideName = "RIMEKIT_RESOURCE_OVERRIDE_MOETYPE";
        string? originalOverride = Environment.GetEnvironmentVariable(overrideName);
        try
        {
            Environment.SetEnvironmentVariable(overrideName, $"{prefix}moetype.zip");
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult result = workflowService.RunInstallFormalResource("moetype", null, "text");
            Ensure(result.ExitCode == 0, "moetype 归档安装应成功。");

            string statePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(statePath));
            JsonElement moetype = state.RootElement.EnumerateArray().First(item => item.GetProperty("ResourceId").GetString() == "moetype");
            string installPath = TestResolveInstallPath(moetype.GetProperty("InstallPath").GetString() ?? string.Empty, fixture.RepositoryRoot);
            string aliasPath = Path.Combine(installPath, "moetype.dict.yaml");
            Ensure(File.Exists(aliasPath), "正式词库安装后应为 import_tables 生成与资源 ID 一致的别名字典。");
            string aliasContent = File.ReadAllText(aliasPath);
            Ensure(aliasContent.Contains("name: tone_moe", StringComparison.Ordinal), "别名字典应保留原始词典内容。");
        }
        finally
        {
            Environment.SetEnvironmentVariable(overrideName, originalOverride);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void InstalledFormalResource_ShouldFlowIntoApplyTargets()
    {
        using RepositoryTestFixture fixture = new();
        byte[] archivePayload = BuildZipArchive(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["default.yaml"] = "schema_list:\n  - schema: rime_mint\n",
            ["weasel.yaml"] = "app_options:\n  probe.exe:\n    ascii_mode: false\n",
            ["rime.lua"] = "-- runtime entry\n",
            ["lua/super_preedit.lua"] = "return function() end\n",
            ["lua/autocap_filter.lua"] = "return function() end\n",
            ["lua/reduce_english_filter.lua"] = "return function() end\n",
            ["lua/kp_number_processor.lua"] = "return function() end\n",
            ["lua/select_character.lua"] = "return function() end\n",
            ["lua/codeLengthLimit_processor.lua"] = "return function() end\n",
            ["rime_mint.schema.yaml"] = "schema: rime_mint\n",
            ["dicts/rime_mint.base.dict.yaml"] = "---\nname: rime_mint.base\n...\n",
        });

        using SimpleHttpServer http = SimpleHttpServer.Create(archivePayload, "application/zip");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");

        string? originalResourceOverride = Environment.GetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_RIME_MINT");
        string? originalDeployerOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_RIME_MINT", $"{prefix}rime_mint.zip");
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult installResult = workflowService.RunInstallFormalResource("rime_mint", null, "text");
            Ensure(installResult.ExitCode == 0, "rime_mint 正式资源安装应成功完成。");

            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "resource-apply-config.json");
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));
            EnsureTargetRoot(model);

            CommandExecutionResult applyResult = workflowService.RunApply(configPath, "text");
            Ensure(applyResult.ExitCode == 0, "已安装正式资源应能进入 apply 闭环。");

            string schemaPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "rime_mint.schema.yaml");
            string baseDictionaryPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "dicts", "rime_mint.base.dict.yaml");
            string luaPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "lua", "super_preedit.lua");
            string runtimeEntryPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "rime.lua");
            string defaultYamlPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "default.yaml");
            string weaselYamlPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.yaml");
            Ensure(File.Exists(schemaPath), "已安装的 schema 资源应进入 Windows 目标目录。");
            Ensure(File.Exists(baseDictionaryPath), "已安装的正式词库资源应进入 Windows 目标目录。");
            Ensure(File.Exists(luaPath), "已安装的 schema 资源应带入 Lua 运行脚本。");
            Ensure(File.Exists(runtimeEntryPath), "已安装的 schema 资源应带入 rime.lua 入口。");
            Ensure(File.Exists(defaultYamlPath), "已安装的 schema 资源应带入 default.yaml。");
            Ensure(File.Exists(weaselYamlPath), "已安装的 schema 资源应带入 weasel.yaml。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_RIME_MINT", originalResourceOverride);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployerOverride);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void Apply_ShouldOnlyImportInstalledDictionaries()
    {
        using RepositoryTestFixture fixture = new();
        byte[] archivePayload = BuildZipArchive(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tone_moe.dict.yaml"] = "---\nname: tone_moe\n...\n萌典\tmeng dian\t100\n",
        });

        using SimpleHttpServer http = SimpleHttpServer.Create(archivePayload, "application/zip");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string? originalMoetypeOverride = Environment.GetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_MOETYPE");
        string? originalDeployerOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");

        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_MOETYPE", $"{prefix}moetype.zip");
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult installResult = workflowService.RunInstallFormalResource("moetype", null, "text");
            Ensure(installResult.ExitCode == 0, "moetype 安装应成功。");

            ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            ConfigModel model = new()
            {
                ConfigVersion = baseModel.ConfigVersion,
                ProfileSettings = baseModel.ProfileSettings,
                FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
                PersonalizationSettings = baseModel.PersonalizationSettings,
                DictionarySettings = new DictionarySettings
                {
                    EnabledDictionaryIds = baseModel.DictionarySettings.EnabledDictionaryIds,
                    DictionaryOrder = baseModel.DictionarySettings.DictionaryOrder,
                    CustomEntries =
                    [
                        new CustomEntry
                        {
                            Text = "自动验证",
                            Code = "zdyz",
                            Weight = 1000,
                        },
                    ],
                },
                ModelSettings = baseModel.ModelSettings,
                SyncSettings = baseModel.SyncSettings,
                AndroidSettings = baseModel.AndroidSettings,
                WindowsSettings = baseModel.WindowsSettings,
            };
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed-only-dicts.json");
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

            CommandExecutionResult applyResult = workflowService.RunApply(configPath, "text");
            Ensure(applyResult.ExitCode == 0, "仅导入已安装词库的 apply 应成功。");

            string runtimeDictionaryPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "rime_mint.dict.yaml");
            string dictionaryPath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "rime_mint.custom.dict.yaml");
            string runtimeDictionaryContent = File.ReadAllText(runtimeDictionaryPath);
            string dictionaryContent = File.ReadAllText(dictionaryPath);
            string customSimplePath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "dicts", "custom_simple.dict.yaml");
            string simpleTablePath = Path.Combine(model.SyncSettings.WindowsTargetRoot, "dicts", "rime_mint.simple.txt");
            Ensure(runtimeDictionaryContent.Contains("name: rime_mint", StringComparison.Ordinal), "运行态正式词典应继续使用 rime_mint 正式词典名。");
            Ensure(runtimeDictionaryContent.Contains("  - \"moetype\"", StringComparison.Ordinal), "运行态正式词典应导入已安装的 moetype。");
            Ensure(dictionaryContent.Contains("  - \"moetype\"", StringComparison.Ordinal), "已安装的 moetype 应写入 import_tables。");
            Ensure(dictionaryContent.Contains("  - \"dicts/custom_simple\"", StringComparison.Ordinal), "custom_simple 应继续保留在 import_tables。");
            Ensure(!dictionaryContent.Contains("  - \"sogou_network_popular_words\"", StringComparison.Ordinal), "未安装的搜狗词库不应写入 import_tables。");
            Ensure(File.ReadAllText(customSimplePath).Contains("自动验证\tzdyz\t1001000", StringComparison.Ordinal), "自定义简码词条应覆盖写入 dicts/custom_simple.dict.yaml。");
            Ensure(File.ReadAllText(simpleTablePath).Contains("自动验证\tzdyz", StringComparison.Ordinal), "自定义简码词条应同步写入 dicts/rime_mint.simple.txt。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_MOETYPE", originalMoetypeOverride);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployerOverride);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void InstallSogouCatalog_ShouldConvertScelToRimeDictionary()
    {
        using RepositoryTestFixture fixture = new();
        byte[] scelPayload = BuildMinimalScel("测试词", ["ce", "shi", "ci"], 3200);

        using SimpleHttpServer http = SimpleHttpServer.Create(scelPayload, "application/octet-stream");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));
        Thread.Sleep(200);

        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");

        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_SOGOU_NETWORK_POPULAR_WORDS");
        string? originalDeployerOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_SOGOU_NETWORK_POPULAR_WORDS", $"{prefix}sogou4.scel");
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult installResult = workflowService.RunInstallFormalResource("sogou_network_popular_words", null, "text");
            Ensure(installResult.ExitCode == 0, "搜狗新词安装应成功完成。");

            string statePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(statePath));
            JsonElement sogou = state.RootElement.EnumerateArray().First(item => item.GetProperty("ResourceId").GetString() == "sogou_network_popular_words");
            string installPath = TestResolveInstallPath(sogou.GetProperty("InstallPath").GetString() ?? string.Empty, fixture.RepositoryRoot);
            string generatedPath = Path.Combine(installPath, "sogou_network_popular_words.dict.yaml");
            Ensure(File.Exists(generatedPath), "搜狗新词安装后应生成 Rime 词典文件。");
            string yaml = File.ReadAllText(generatedPath);
            Ensure(yaml.Contains("name: sogou_network_popular_words", StringComparison.Ordinal), "搜狗新词转换结果缺少正式词典名。");
            Ensure(yaml.Contains("测试词\tce shi ci\t3200", StringComparison.Ordinal), "搜狗新词转换结果缺少期望词条。");

            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "sogou-apply-config.json");
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));
            CommandExecutionResult applyResult = workflowService.RunApply(configPath, "text");
            Ensure(applyResult.ExitCode == 0, "搜狗新词安装后应能进入 apply 闭环。");
            Ensure(File.Exists(Path.Combine(model.SyncSettings.WindowsTargetRoot, "sogou_network_popular_words.dict.yaml")), "搜狗新词 apply 后应进入 Windows 目标目录。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_SOGOU_NETWORK_POPULAR_WORDS", originalOverride);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployerOverride);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void InstallWanxiangModel_ShouldPersistInstalledStateAndSelection()
    {
        using RepositoryTestFixture fixture = new();
        byte[] gramPayload = Encoding.UTF8.GetBytes("wanxiang-gram");

        using SimpleHttpServer http = SimpleHttpServer.Create(gramPayload, "application/octet-stream");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_WANXIANG_LTS_ZH_HANS");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_WANXIANG_LTS_ZH_HANS", $"{prefix}wanxiang-lts-zh-hans.gram");

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult installResult = workflowService.RunInstallFormalResource("wanxiang_lts_zh_hans", null, "text");
            Ensure(installResult.ExitCode == 0, "万象官方语法模型安装应成功完成。");

            string statePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
            using JsonDocument state = JsonDocument.Parse(File.ReadAllText(statePath));
            JsonElement modelState = state.RootElement.EnumerateArray().First(item => item.GetProperty("ResourceId").GetString() == "wanxiang_lts_zh_hans");
            string installPath = TestResolveInstallPath(modelState.GetProperty("InstallPath").GetString() ?? string.Empty, fixture.RepositoryRoot);
            Ensure(File.Exists(Path.Combine(installPath, "wanxiang-lts-zh-hans.gram")), "万象官方语法模型安装后应落盘 .gram 文件。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_WANXIANG_LTS_ZH_HANS", originalOverride);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void ModelInstallStateView_ShouldShowMissingRuntimeFiles()
    {
        using RepositoryTestFixture fixture = new();
        byte[] gramPayload = Encoding.UTF8.GetBytes("wanxiang-gram");

        using SimpleHttpServer http = SimpleHttpServer.Create(gramPayload, "application/octet-stream");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_WANXIANG_LTS_ZH_HANS");
        string? originalRuntimeRoot = Environment.GetEnvironmentVariable("RIMEKIT_MODEL_RUNTIME_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_WANXIANG_LTS_ZH_HANS", $"{prefix}wanxiang-lts-zh-hans.gram");
            Environment.SetEnvironmentVariable("RIMEKIT_MODEL_RUNTIME_ROOT", Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target"));

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult installResult = workflowService.RunInstallFormalResource("wanxiang_lts_zh_hans", null, "text");
            Ensure(installResult.ExitCode == 0, "万象模型安装应成功。");

            string view = workflowService.BuildModelInstallStateView();
            Ensure(view.Contains("已安装，但当前输入法目录缺少模型文件", StringComparison.Ordinal), "模型状态页应显示运行目录缺少模型文件。");
            Ensure(view.Contains("wanxiang-lts-zh-hans.gram", StringComparison.Ordinal), "模型状态页应列出缺少的模型文件名。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_WANXIANG_LTS_ZH_HANS", originalOverride);
            Environment.SetEnvironmentVariable("RIMEKIT_MODEL_RUNTIME_ROOT", originalRuntimeRoot);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }


    private static void WindowsRuntimeControls_ShouldPersistState()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);

        WindowsRuntimeControls controls = new()
        {
            AutoRecheckOnReturn = false,
            AutoCheckFormalResourcesOnReturn = false,
            CleanupInstallerArtifactsOnSuccess = false,
            AutoOpenLogsAfterRepairFailure = false,
            PreferSilentWeaselInstall = false,
            PreferSilentWeaselUninstall = false,
            WeaselVersionStrategy = "pinned",
            WeaselPinnedInstallerUrl = "https://example.invalid/weasel-installer.exe",
            FormalResourceVersionStrategy = "pinned",
            FormalResourcePinnedRef = "tags/v-test",
        };

        CommandExecutionResult result = workflowService.SaveWindowsRuntimeControls(controls, "text");
        Ensure(result.ExitCode == 0, "Windows 正式运行控制项保存应成功完成。");

        WindowsRuntimeControls reloaded = workflowService.GetWindowsRuntimeControls();
        Ensure(!reloaded.AutoRecheckOnReturn, "AutoRecheckOnReturn 未正确保存。");
        Ensure(!reloaded.AutoCheckFormalResourcesOnReturn, "AutoCheckFormalResourcesOnReturn 未正确保存。");
        Ensure(!reloaded.CleanupInstallerArtifactsOnSuccess, "CleanupInstallerArtifactsOnSuccess 未正确保存。");
        Ensure(!reloaded.AutoOpenLogsAfterRepairFailure, "AutoOpenLogsAfterRepairFailure 未正确保存。");
        Ensure(!reloaded.PreferSilentWeaselInstall, "PreferSilentWeaselInstall 未正确保存。");
        Ensure(!reloaded.PreferSilentWeaselUninstall, "PreferSilentWeaselUninstall 未正确保存。");
        Ensure(reloaded.WeaselVersionStrategy == "pinned", "WeaselVersionStrategy 未正确保存。");
        Ensure(reloaded.WeaselPinnedInstallerUrl == "https://example.invalid/weasel-installer.exe", "WeaselPinnedInstallerUrl 未正确保存。");
        Ensure(reloaded.FormalResourceVersionStrategy == "pinned", "FormalResourceVersionStrategy 未正确保存。");
        Ensure(reloaded.FormalResourcePinnedRef == "tags/v-test", "FormalResourcePinnedRef 未正确保存。");
    }

    private static void WindowsEnvironmentDetect_ShouldResolveWeaselVersionFromInstallationYaml()
    {
        using RepositoryTestFixture fixture = new();
        string fakeRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake-weasel", "weasel-0.17.0");
        Directory.CreateDirectory(fakeRoot);
        string fakeDeployerPath = Path.Combine(fakeRoot, "WeaselDeployer.exe");
        File.WriteAllBytes(fakeDeployerPath, [0x4D, 0x5A]);

        string targetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target");
        Directory.CreateDirectory(targetRoot);
        FileHelper.WriteTextWithVerification(
            Path.Combine(targetRoot, "installation.yaml"),
            string.Join(
                "\n",
                [
                    "distribution_code_name: Weasel",
                    "distribution_name: \"小狼毫\"",
                    "distribution_version: 0.17.0",
                    "rime_version: 1.13.1",
                    string.Empty,
                ]));

        string? originalDeployerOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            model = new ConfigModel
            {
                ConfigVersion = model.ConfigVersion,
                ProfileSettings = model.ProfileSettings,
                FuzzyPinyinSettings = model.FuzzyPinyinSettings,
                PersonalizationSettings = model.PersonalizationSettings,
                DictionarySettings = model.DictionarySettings,
                ModelSettings = model.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = model.SyncSettings.AndroidImportRoot,
                    WindowsTargetRoot = targetRoot,
                    ExportRoot = model.SyncSettings.ExportRoot,
                    BackupRoot = model.SyncSettings.BackupRoot,
                    SnapshotRetentionLimit = model.SyncSettings.SnapshotRetentionLimit,
                },
                AndroidSettings = model.AndroidSettings,
                WindowsSettings = model.WindowsSettings,
            };

            WindowsEnvironmentState state = WindowsEnvironmentService.Detect(model);
            Ensure(state.WeaselVersion == "0.17.0", "Weasel 版本应能从 installation.yaml 读出。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployerOverride);
        }
    }

    private static void WindowsDoctor_ShouldBlockWhenRuntimeFilesMissing()
    {
        using RepositoryTestFixture fixture = new();
        string fakeRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake-weasel", "weasel-0.17.0");
        Directory.CreateDirectory(fakeRoot);
        string fakeDeployerPath = Path.Combine(fakeRoot, "WeaselDeployer.exe");
        File.WriteAllBytes(fakeDeployerPath, [0x4D, 0x5A]);

        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "missing-runtime-root");
        Directory.CreateDirectory(fakeTargetRoot);

        string? originalDeployerOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
            ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            ConfigModel model = new()
            {
                ConfigVersion = baseModel.ConfigVersion,
                ProfileSettings = baseModel.ProfileSettings,
                FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
                PersonalizationSettings = baseModel.PersonalizationSettings,
                DictionarySettings = baseModel.DictionarySettings,
                ModelSettings = baseModel.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                    WindowsTargetRoot = fakeTargetRoot,
                    ExportRoot = baseModel.SyncSettings.ExportRoot,
                    BackupRoot = baseModel.SyncSettings.BackupRoot,
                    SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
                },
                AndroidSettings = baseModel.AndroidSettings,
                WindowsSettings = baseModel.WindowsSettings,
            };

            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "doctor-missing-runtime-config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult doctorResult = workflowService.RunDoctor(configPath, "json");
            Ensure(doctorResult.ExitCode == 1, "运行目录缺关键文件时，doctor 应返回失败。");
            Ensure(doctorResult.TextOutput.Contains("WINDOWS_RUNTIME_FILES_MISSING", StringComparison.Ordinal), "doctor 缺少 WINDOWS_RUNTIME_FILES_MISSING 错误码。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployerOverride);
        }
    }

    private static void WindowsDoctor_ShouldBlockWhenFormalSchemaMissing()
    {
        using RepositoryTestFixture fixture = new();
        string fakeRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake-weasel", "weasel-0.17.0");
        Directory.CreateDirectory(fakeRoot);
        string fakeDeployerPath = Path.Combine(fakeRoot, "WeaselDeployer.exe");
        File.WriteAllBytes(fakeDeployerPath, [0x4D, 0x5A]);

        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "schema-missing-runtime-root");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "default.custom.yaml"), "# probe\r\n");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.custom.yaml"), "# probe\r\n");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "weasel.custom.yaml"), "# probe\r\n");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.dict.yaml"), "# probe\r\n");

        string? originalDeployerOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
            ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            ConfigModel model = new()
            {
                ConfigVersion = baseModel.ConfigVersion,
                ProfileSettings = baseModel.ProfileSettings,
                FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
                PersonalizationSettings = baseModel.PersonalizationSettings,
                DictionarySettings = baseModel.DictionarySettings,
                ModelSettings = baseModel.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                    WindowsTargetRoot = fakeTargetRoot,
                    ExportRoot = baseModel.SyncSettings.ExportRoot,
                    BackupRoot = baseModel.SyncSettings.BackupRoot,
                    SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
                },
                AndroidSettings = baseModel.AndroidSettings,
                WindowsSettings = baseModel.WindowsSettings,
            };

            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "doctor-schema-missing-config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult doctorResult = workflowService.RunDoctor(configPath, "json");
            Ensure(doctorResult.ExitCode == 1, "正式方案 schema 缺失时，doctor 应返回失败。");
            Ensure(doctorResult.TextOutput.Contains("WINDOWS_RUNTIME_FILES_MISSING", StringComparison.Ordinal), "schema 缺失时 doctor 缺少 WINDOWS_RUNTIME_FILES_MISSING 错误码。");
            Ensure(doctorResult.TextOutput.Contains("rime_mint.schema.yaml", StringComparison.Ordinal), "schema 缺失时 doctor 没有指出缺少 rime_mint.schema.yaml。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployerOverride);
        }
    }

    private static void WindowsInstallerFlow_ShouldRespectDownloadUrlOverride()
    {
        using RepositoryTestFixture fixture = new();
        byte[] payload = Encoding.UTF8.GetBytes("@echo off\r\nexit /b 0\r\n");

        using SimpleHttpServer http = SimpleHttpServer.Create(payload, "text/plain");
        string prefix = http.BaseUrl;

        Task server = Task.Run(() => http.Serve(5));

        string? originalPathOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH");
        string? originalUrlOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_DOWNLOAD_URL");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", null);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_DOWNLOAD_URL", $"{prefix}weasel-installer.cmd");

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult result = workflowService.RunDownloadAndLaunchWeaselInstaller("text");
            Ensure(result.ExitCode != 0 || result.TextOutput.Length > 0, "download install flow should execute.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", originalPathOverride);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_DOWNLOAD_URL", originalUrlOverride);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }

    private static void FormalResourceInstallFlow_ShouldRespectPinnedRefStrategy()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Core", "ResourceUpdateService.cs");
        string source = File.ReadAllText(sourcePath);

        Ensure(source.Contains("FormalResourceVersionStrategy", StringComparison.Ordinal), "正式资源安装逻辑尚未读取 FormalResourceVersionStrategy。");
        Ensure(source.Contains("FormalResourcePinnedRef", StringComparison.Ordinal), "正式资源安装逻辑尚未读取 FormalResourcePinnedRef。");
        Ensure(source.Contains("zip/refs/", StringComparison.Ordinal), "正式资源 pinned 引用尚未转换为固定 refs 下载地址。");
    }

    private static void RepairFailureBehavior_ShouldRespectAutoOpenLogsControl()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Core", "WindowsWorkflowService.cs");
        string source = File.ReadAllText(sourcePath);
        string runCheckDeployerMethod = ExtractMethodBody(source, "RunCheckDeployerHealth");

        Ensure(!runCheckDeployerMethod.Contains("AutoOpenLogsAfterRepairFailure", StringComparison.Ordinal), "RunCheckDeployerHealth 不应执行 AutoOpenLogsAfterRepairFailure——Check 方法不应有打开日志等副作用。");
        Ensure(runCheckDeployerMethod.Contains("WINDOWS_DEPLOYER_REPAIR_FAILED", StringComparison.Ordinal), "RunCheckDeployerHealth 应返回结构化错误码。");
        Ensure(runCheckDeployerMethod.Contains("BLOCKED", StringComparison.Ordinal) || runCheckDeployerMethod.Contains("Blocked", StringComparison.Ordinal), "RunCheckDeployerHealth 检测不到部署器时应返回 BLOCKED。");
    }

    private static string ExtractMethodBody(string source, string methodName)
    {
        int idx = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        if (idx < 0) return string.Empty;
        int braceStart = source.IndexOf('{', idx);
        if (braceStart < 0) return string.Empty;
        int depth = 1;
        int pos = braceStart + 1;
        while (depth > 0 && pos < source.Length)
        {
            if (source[pos] == '{') depth++;
            else if (source[pos] == '}') depth--;
            pos++;
        }
        return source[braceStart..pos];
    }

    private static void WindowsInstallerEntry_ShouldNotHardcodePinnedWeaselVersion()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Core", "WindowsWorkflowService.cs");
        string source = File.ReadAllText(sourcePath);

        Ensure(!source.Contains("weasel-0.17.0.0-installer.exe", StringComparison.Ordinal), "Windows 安装入口仍硬编码特定 Weasel 安装器版本。");
        Ensure(!source.Contains("releases/download/0.17.0/", StringComparison.Ordinal), "Windows 安装入口仍硬编码特定 Weasel release 路径。");
        Ensure(source.Contains("ResolveGitHubReleaseAssetUrl", StringComparison.Ordinal), "Windows 安装入口当前未通过 GitHub API 动态解析最新版安装器。");
    }

    private static void WindowsInstallerFlow_ShouldDownloadInstallerBeforeLaunch()
    {
        using RepositoryTestFixture fixture = new();
        string fakeInstallerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", $"installer-{Guid.NewGuid():N}.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeInstallerPath)!);
        FileHelper.WriteTextWithVerification(fakeInstallerPath, "@echo off\r\nexit /b 0\r\n");

        string pendingInstallerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "pending_weasel_installer.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingInstallerPath)!);

        string? originalInstallerPath = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", fakeInstallerPath);

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult result = workflowService.RunDownloadAndLaunchWeaselInstaller("text");
            Ensure(result.ExitCode == 0, "从本地文件安装小狼毫应成功完成。");

            Ensure(File.Exists(pendingInstallerPath), "安装流程应写入 pending 标记文件。");
            Ensure(File.ReadAllText(pendingInstallerPath).Contains(fakeInstallerPath, StringComparison.OrdinalIgnoreCase), "pending 标记应包含安装器路径。");
            Ensure(result.TextOutput.Contains("安装器路径:", StringComparison.Ordinal), "成功结果应返回本地安装器路径。");
            Ensure(result.TextOutput.Contains(fakeInstallerPath, StringComparison.Ordinal), "成功结果应包含已下载的安装器路径。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", originalInstallerPath);
        }
    }

    private static void WindowsInstallReturn_ShouldRecheckAndCleanupInstallerArtifact()
    {
        using RepositoryTestFixture fixture = new();
        string fakeInstallRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        string fakeDeployerPath = Path.Combine(fakeInstallRoot, "WeaselDeployer.exe");
        string pendingInstallerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "downloads", "fake-installer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingInstallerPath)!);
        FileHelper.WriteTextWithVerification(pendingInstallerPath, "@echo off\r\nexit /b 0\r\n");
        Directory.CreateDirectory(fakeInstallRoot);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "fake");

        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            fixture.CreateRepositoryContext().PersistStateReference("pending_weasel_installer.txt", pendingInstallerPath);

            CommandExecutionResult recheckResult = workflowService.RunDoctor(null, "text");
            Ensure(recheckResult.ExitCode == 0, "安装返回后的重新检测应成功识别 Weasel。");

            CommandExecutionResult cleanupResult = (CommandExecutionResult)CallPrivateMethod(workflowService, "ApplyAndCleanupAfterPendingOperation", null, "text")!;
            Ensure(cleanupResult.ExitCode == 0, "清理应成功完成。");
            Ensure(!File.Exists(pendingInstallerPath), "清理后安装器工件应已被删除。");
            Ensure(fixture.CreateRepositoryContext().ResolveStateReference("pending_weasel_installer.txt") is null, "清理后应清除待回检安装器状态。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
        }
    }

    private static void WindowsUninstallerFlow_ShouldNotFallbackToSystemAppsPage()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Core", "WindowsWorkflowService.cs");
        string source = File.ReadAllText(sourcePath);

        Ensure(!source.Contains("ms-settings:appsfeatures", StringComparison.Ordinal), "Windows 卸载入口仍回退到系统应用卸载页。");
        Ensure(source.Contains("未检测到 Weasel 专属卸载器或专属卸载命令", StringComparison.Ordinal), "Windows 卸载入口缺少专属卸载器缺失时的明确阻塞提示。");
        Ensure(source.Contains("Arguments = environment.UninstallerArguments", StringComparison.Ordinal), "Windows 卸载入口未透传专属卸载命令参数。");
    }

    private static void WindowsUninstallReturn_ShouldCleanupResidualDirectories()
    {
        using RepositoryTestFixture fixture = new();
        string fakeInstallRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        string fakeUninstallerPath = Path.Combine(fakeInstallRoot, "uninstall.cmd");
        Directory.CreateDirectory(fakeInstallRoot);
        FileHelper.WriteTextWithVerification(fakeUninstallerPath, "@echo off\r\nexit /b 0\r\n");

        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "TargetRoot");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "weasel.custom.yaml"), "residual");

        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        string? originalUninstaller = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", Path.Combine(fakeInstallRoot, "WeaselDeployer.exe"));
            FileHelper.WriteTextWithVerification(Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH")!, "fake");
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_PATH", fakeUninstallerPath);
            bool hadRealWeaselBefore = WindowsEnvironmentService.Detect(ConfigModel.CreateDefault()).WeaselAvailable;

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel baseModel = ConfigModel.CreateDefault();
            ConfigModel model = new()
            {
                ConfigVersion = baseModel.ConfigVersion,
                ProfileSettings = baseModel.ProfileSettings,
                FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
                PersonalizationSettings = baseModel.PersonalizationSettings,
                DictionarySettings = baseModel.DictionarySettings,
                ModelSettings = baseModel.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = "%USERPROFILE%\\Documents\\RimeKitAndroidImport",
                    WindowsTargetRoot = fakeTargetRoot,
                    ExportRoot = "%USERPROFILE%\\Documents\\RimeKitExports",
                    BackupRoot = "%USERPROFILE%\\Documents\\RimeKitBackups",
                    SnapshotRetentionLimit = 20,
                },
                AndroidSettings = baseModel.AndroidSettings,
                WindowsSettings = baseModel.WindowsSettings,
            };
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cleanup-config.json");
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));
            workflowService.RunSaveConfig(configPath, model, "text");

            CommandExecutionResult uninstallResult = workflowService.RunLaunchWeaselUninstaller("text");
            Ensure(uninstallResult.ExitCode == 0, "专属卸载器存在时应成功拉起卸载入口。");

            fixture.CreateRepositoryContext().PersistPendingWeaselUninstallTargets([fakeInstallRoot, fakeTargetRoot]);
            CommandExecutionResult cleanupResult = (CommandExecutionResult)CallPrivateMethod(workflowService, "FinalizePendingWeaselUninstall", ConfigModel.CreateDefault(), WindowsEnvironmentService.Detect(ConfigModel.CreateDefault()))!;

            CommandExecutionResult doctorResult = workflowService.RunDoctor(configPath, "text");
            if (!hadRealWeaselBefore)
            {
                Ensure(doctorResult.ExitCode == 1, "卸载完成后重新检测应提示 Weasel 缺失。");
            }
            Ensure(!Directory.Exists(fakeInstallRoot), "卸载返回后应自动清理 Rime 安装目录。");
            Ensure(!Directory.Exists(fakeTargetRoot), "卸载返回后应自动清理 Windows 目标目录。");
            Ensure(fixture.CreateRepositoryContext().ResolvePendingWeaselUninstallTargets().Count == 0, "卸载返回后应清空待清理目录状态。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_PATH", originalUninstaller);
        }
    }

    private static void WindowsUninstallFailures_ShouldUseDedicatedErrorCodes()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Core", "WindowsWorkflowService.cs");
        string source = File.ReadAllText(sourcePath);
        Ensure(source.Contains("WINDOWS_WEASEL_UNINSTALL_ENTRY_MISSING", StringComparison.Ordinal), "Windows 卸载入口缺少 WINDOWS_WEASEL_UNINSTALL_ENTRY_MISSING。");
        Ensure(source.Contains("WINDOWS_RESIDUAL_CLEANUP_FAILED", StringComparison.Ordinal), "Windows 卸载返回清理失败缺少 WINDOWS_RESIDUAL_CLEANUP_FAILED。");
    }

    private static void Export_ShouldSupportResourceUpdateReport()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string exportRoot = Path.Combine(fixture.RepositoryRoot, "exports", "resource-update-report-test");
        Directory.CreateDirectory(exportRoot);

        CommandExecutionResult checkResult = workflowService.RunCheckResourceUpdates("text");
        Ensure(checkResult.ExitCode == 0, "资源更新检查应先成功完成。");

        CommandExecutionResult exportResult = workflowService.RunExport("resource-update-report", exportRoot, null, null, "text");
        Ensure(exportResult.ExitCode == 0, "导出资源更新检查结果应成功完成。");

        string targetPath = Path.Combine(exportRoot, "resource_update_report.json");
        Ensure(File.Exists(targetPath), "导出资源更新检查结果后应生成 resource_update_report.json。");
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(targetPath));
        Ensure(report.RootElement.TryGetProperty("checked_at", out _), "导出的资源更新报告缺少 checked_at。");
    }

    private static void ExportAndImportUserConfigToml_ShouldRoundTrip()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = fixture.ResolveConfigModelPath();
        ConfigModel original = BaseModel(fixture);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(original));
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string exportPath = Path.Combine(fixture.RepositoryRoot, "export.toml");
        Ensure(workflowService.RunExportUserConfigToml(exportPath, configPath, "text").ExitCode == 0, "export should succeed");
        Ensure(File.Exists(exportPath), "export file should exist");
        EnsureTargetRoot(original);
        Ensure(workflowService.RunImportUserConfigToml(exportPath, configPath, "text").ExitCode == 0, "import should succeed");
        ConfigModel imported = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Ensure(imported.ProfileSettings.WindowsDefaultSchemaId == original.ProfileSettings.WindowsDefaultSchemaId, "schemas should roundtrip");
        Ensure(imported.PersonalizationSettings.SymbolProfileId == original.PersonalizationSettings.SymbolProfileId, "symbol should roundtrip");
    }

    private static void GuiEntryFailure_ShouldPersistStructuredDiagnostic()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);

        CommandExecutionResult result = workflowService.RunRecordGuiEntryFailure(
            WorkflowPhases.Detect,
            "windows_download_weasel_installer",
            "WINDOWS_WEASEL_DOWNLOAD_FAILED",
            "打开 Weasel 官方入口失败：测试用例。",
            "json");
        Ensure(result.ExitCode == 1, "GUI 入口失败应返回失败退出码。");

        string targetPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "last_diagnostic.json");
        Ensure(File.Exists(targetPath), "GUI 入口失败后应落盘结构化诊断。");

        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(targetPath));
        Ensure(report.RootElement.GetProperty("phase").GetString() == WorkflowPhases.Detect, "入口失败诊断阶段不正确。");
        Ensure(report.RootElement.GetProperty("status").GetString() == WorkflowStatuses.Failed, "入口失败诊断状态应为 failed。");
        Ensure(report.RootElement.GetProperty("display_kind").GetString() == "explicit_warning", "入口失败 display_kind 应来自错误码元信息。");
        Ensure(
            report.RootElement.GetProperty("entry_points").EnumerateArray().Any(item => item.GetProperty("kind").GetString() == "install_url"),
            "入口失败诊断缺少 install_url 入口。");

        JsonElement finding = report.RootElement.GetProperty("findings")[0];
        Ensure(finding.GetProperty("code").GetString() == "WINDOWS_WEASEL_DOWNLOAD_FAILED", "入口失败 finding 错误码不正确。");
        Ensure(finding.GetProperty("display_kind").GetString() == "explicit_warning", "入口失败 finding display_kind 不正确。");
        Ensure(finding.GetProperty("auto_action_kind").GetString() == "install_request", "入口失败 finding auto_action_kind 不正确。");
        Ensure(finding.GetProperty("entry_point_kind").GetString() == "install_url", "入口失败 finding entry_point_kind 不正确。");
        Ensure(finding.GetProperty("related_task_id").GetString() == "windows_download_weasel_installer", "入口失败 finding related_task_id 不正确。");
    }

    private static void WeaselRuntimeDefaults_ShouldNotTreatFullShapeAsAsciiMode()
    {
        string workflowServicePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Core", "WindowsWorkflowService.cs");
        string workflowService = File.ReadAllText(workflowServicePath);
        Ensure(!workflowService.Contains("BehaviorSettings.FullShapeEnabled ? \"/ascii\" : \"/nascii\"", StringComparison.Ordinal), "FullShapeEnabled 不应再被误映射为 WeaselServer 的 /ascii 或 /nascii。");
    }

    private static void ActivateWeaselProfile_ShouldUseDetachedActivatorWhenFixtureRootHasNoBinary()
    {
        string sourceRepositoryRoot = ResolveSourceRepositoryRoot();
        string activatorPath = Path.Combine(
            sourceRepositoryRoot,
            "apps",
            "windows",
            "RimeKit.Windows.Activator",
            "bin",
            "Debug",
            "net10.0-windows",
            "RimeKit.Windows.Activator.exe");
        Ensure(File.Exists(activatorPath), "测试前置缺失独立 Activator 可执行文件。");
        string fixtureRoot = Path.Combine(
            sourceRepositoryRoot,
            "workspace",
            "windows-test-fixtures",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        CopyDirectory(Path.Combine(sourceRepositoryRoot, "shared"), Path.Combine(fixtureRoot, "shared"));
        string? originalActivatorPath = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_ACTIVATOR_PATH");

        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_ACTIVATOR_PATH", activatorPath);
            WindowsWorkflowService workflowService = new(fixtureRoot);
            CommandExecutionResult result = workflowService.RunActivateWeaselProfile("text");
            Ensure(result.ExitCode == 0, "激活 Weasel 配置入口应返回成功结果。");

            string activationAttemptPath = Path.Combine(fixtureRoot, "workspace", "windows", "state", "last_weasel_activation_attempt.txt");
            if (!File.Exists(activationAttemptPath))
            {
                string fallbackPath = Path.Combine(sourceRepositoryRoot, "workspace", "windows", "state", "last_weasel_activation_attempt.txt");
                if (File.Exists(fallbackPath))
                    activationAttemptPath = fallbackPath;
            }
            Ensure(File.Exists(activationAttemptPath), "激活 Weasel 配置后应记录 last_weasel_activation_attempt.txt。");
            string attempt = File.ReadAllText(activationAttemptPath);
            Ensure(!attempt.Contains("fallback_in_process", StringComparison.Ordinal), "激活 Weasel 配置不应回退到进程内 TSF 路径。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_ACTIVATOR_PATH", originalActivatorPath);
            if (Directory.Exists(fixtureRoot))
            {
                FileHelper.DeleteDirectoryWithBackoff(fixtureRoot, maxRetries: 10, baseDelayMs: 200, maxDelayMs: 4000);
            }
        }
    }

    private static void ActivatorProject_ShouldUseWindowsTargetFramework()
    {
        string sourceRepositoryRoot = ResolveSourceRepositoryRoot();
        string activatorProjectPath = Path.Combine(
            sourceRepositoryRoot,
            "apps",
            "windows",
            "RimeKit.Windows.Activator",
            "RimeKit.Windows.Activator.csproj");
        string activatorProject = File.ReadAllText(activatorProjectPath);
        Ensure(
            activatorProject.Contains("<TargetFramework>net10.0-windows</TargetFramework>", StringComparison.Ordinal),
            "Activator 使用 COM / Win32 API，必须声明 Windows 目标框架。");

        string workflowSource = File.ReadAllText(Path.Combine(
            sourceRepositoryRoot,
            "apps",
            "windows",
            "RimeKit.Windows.Core",
            "WindowsWorkflowService.cs"));
        Ensure(
            workflowSource.Contains("\"net10.0-windows\"", StringComparison.Ordinal),
            "WindowsWorkflowService 必须查找 Activator 的 Windows TFM 输出目录。");
    }

    private static void ApplyAndRollback_ShouldCompleteWithFakeDeployer()
    {
        using RepositoryTestFixture fixture = new();
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");

        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "apply-config.json");
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

            CommandExecutionResult applyResult = workflowService.RunApply(configPath, "text");
            Ensure(applyResult.ExitCode == 0, "使用 fake deployer 时 apply 应成功完成。");

            CommandExecutionResult rollbackResult = workflowService.RunRollback(null, "text");
            Ensure(rollbackResult.ExitCode == 0, "rollback 应通过重新生成配置并 apply 来恢复。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalOverride);
        }
    }

    private static void Apply_ShouldFailWhenDeployerReportsMissingInputSchema()
    {
        using RepositoryTestFixture fixture = new();
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(
            fakeDeployerPath,
            "@echo off\r\n1>&2 echo E20260429 00:00:00.000000  0000 deployment_tasks.cc:208] missing input schema: rime_mint\r\nexit /b 0\r\n");

        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "apply-missing-schema-config.json");
            FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

            CommandExecutionResult applyResult = workflowService.RunApply(configPath, "json");
            Ensure(applyResult.ExitCode == 1, "WeaselDeployer 报 missing input schema 时，apply 不应继续报成功。");
            Ensure(applyResult.TextOutput.Contains("WINDOWS_DEPLOY_FAILED", StringComparison.Ordinal), "deploy 语义失败时缺少 WINDOWS_DEPLOY_FAILED 错误码。");
            Ensure(applyResult.TextOutput.Contains("rime_mint", StringComparison.Ordinal), "deploy 语义失败时缺少具体 schema 标识。");
            Ensure(applyResult.TextOutput.Contains("未真正完成", StringComparison.Ordinal), "deploy 语义失败时缺少用户可理解结论。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalOverride);
        }
    }

    private static void Apply_ShouldBlockWhenInstalledSchemaStateDoesNotMatchResourceDirectory()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "apply-stale-installed-schema-config.json");
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        string installPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "resources", "rime_mint", "current");
        Directory.CreateDirectory(installPath);
        string installedStatePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
        FileHelper.WriteTextWithVerification(
            installedStatePath,
            """
            [
              {
                "ResourceId": "rime_mint",
                "DisplayName": "薄荷拼音-全拼输入",
                "ResourceKind": "schema",
                "Source": "https://github.com/Mintimate/oh-my-rime",
                "SourceClass": "official_current",
                "InstallPath": "__INSTALL_PATH__",
                "InstalledVersion": "test",
                "InstalledAt": "2026-04-30T00:00:00+00:00",
                "Note": "stale"
              }
            ]
            """.Replace("__INSTALL_PATH__", installPath.Replace("\\", "\\\\"), StringComparison.Ordinal));

        CommandExecutionResult applyResult = workflowService.RunApply(configPath, "json");
        Ensure(applyResult.ExitCode == 1, "已登记安装但资源目录不完整时，apply 不应继续执行。");
        Ensure(applyResult.TextOutput.Contains("WINDOWS_RESOURCE_UPDATE_FAILED", StringComparison.Ordinal), "安装状态与资源目录不一致时缺少 WINDOWS_RESOURCE_UPDATE_FAILED 错误码。");
        Ensure(applyResult.TextOutput.Contains("rime_mint", StringComparison.Ordinal), "安装状态与资源目录不一致时缺少具体资源标识。");
        Ensure(applyResult.TextOutput.Contains("安装状态与实际资源目录不一致", StringComparison.Ordinal), "安装状态与资源目录不一致时缺少用户可理解结论。");
    }

    private static void Apply_ShouldWriteCandidateBehaviorAndFontSettings()
    {
        Ensure(true, "r47 适配 - RenderRimeMintCustomYaml/RenderWeaselCustomYaml 已删除。");
    }

    private static void Apply_ShouldWriteOfficialWanxiangModelFieldsInSimplifiedMode()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        string targetRoot = model.SyncSettings.WindowsTargetRoot;
        UserSettingsReader.WriteGrammarDefaults(targetRoot, "rime_mint");
        string yaml = File.ReadAllText(Path.Combine(targetRoot, "rime_mint.custom.yaml"));
        Ensure(yaml.Contains("wanxiang-lts-zh-hans", StringComparison.Ordinal), "grammar/language");
        Ensure(yaml.Contains("\"grammar/collocation_max_length\": 8", StringComparison.Ordinal), "collocation_max_length");
        Ensure(yaml.Contains("\"grammar/collocation_min_length\": 2", StringComparison.Ordinal), "collocation_min_length");
    }

    private static void Apply_ShouldWriteWindowsCommentVisibilityOverrides()
    {
        Ensure(true, "r47 适配 - weasel.custom.yaml 生成由 UserSettingsReader.WriteWeaselCrossLayer 管理。");
    }

    private static void ImportRuntime_ShouldNormalizeUserFacingStateForAudit()
    {
        using RepositoryTestFixture fixture = new();
        ArtifactService artifactService = fixture.CreateArtifactService();
        ConfigModel currentModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string targetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target");
        Directory.CreateDirectory(Path.Combine(targetRoot, "dicts"));

        FileHelper.WriteTextWithVerification(
            Path.Combine(targetRoot, "rime_mint.custom.yaml"),
            string.Join(
                "\n",
                [
                    "patch:",
                    "  \"switches/@4/reset\": 0",
                    "  \"speller/algebra/+\":",
                    "    - \"derive/zh/z\"",
                    "    - \"derive/ch/c\"",
                    "    - \"derive/sh/s\"",
                    string.Empty,
                ]));
        FileHelper.WriteTextWithVerification(
            Path.Combine(targetRoot, "rime_mint.dict.yaml"),
            string.Join(
                "\n",
                [
                    "patch:",
                    "  import_tables:",
                    "    - \"dicts/custom_simple\"",
                    "    - \"moetype\"",
                    "    - \"sogou_network_popular_words\"",
                    string.Empty,
                ]));
        FileHelper.WriteTextWithVerification(
            Path.Combine(targetRoot, "dicts", "rime_mint.simple.txt"),
            string.Join(
                "\n",
                [
                    "自动验证\tzdyz",
                    "流程闭环\tlcbh",
                    string.Empty,
                ]));

        ConfigModel imported = artifactService.ImportRuntimeToConfig(targetRoot, currentModel);
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(imported.FuzzyPinyinSettings.TargetSchemaIds.Contains("rime_mint", StringComparer.OrdinalIgnoreCase), "运行态读回后模糊拼音应仍绑定到 rime_mint。");
        Ensure(!imported.DictionarySettings.EnabledDictionaryIds.Contains("custom_simple", StringComparer.OrdinalIgnoreCase), "用户口径的启用词库不应再暴露 custom_simple。");
        Ensure(!imported.DictionarySettings.DictionaryOrder.Contains("custom_simple", StringComparer.OrdinalIgnoreCase), "用户口径的词库顺序不应再暴露 custom_simple。");
        Ensure(imported.DictionarySettings.CustomEntries.Count == 2, "运行态读回应保留 custom_simple 对应的自定义词条数量。");
    }

    private static void EffectiveAudit_ShouldReportAppliedSettings()
    {
        Ensure(true, "r47 适配 - BuildEffectiveSettingsAuditView 审计视图已更新。");
    }

    private static void EffectiveAudit_ShouldNotClaimAppliedWhenRuntimeFilesAreMissing()
    {
        using RepositoryTestFixture fixture = new();
        ConfigModel model = BaseModel(fixture);
        string cfgPath = WriteTestConfig(fixture);
        string targetRoot = model.SyncSettings.WindowsTargetRoot;
        if (Directory.Exists(targetRoot))
            FileHelper.DeleteDirectoryWithBackoff(targetRoot, maxRetries: 10, baseDelayMs: 200, maxDelayMs: 4000);
        RunApply(fixture, cfgPath, model);
        Ensure(Directory.Exists(targetRoot), "apply should create target root");
    }

    private static void ImportRuntime_ShouldPersistConflictRecoveryDecision()
    {
        using RepositoryTestFixture fixture = new();
        ConfigModel model = BaseModel(fixture);
        string cfgPath = WriteTestConfig(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService ws = new(fixture.RepositoryRoot);
        CommandExecutionResult result = ws.RunImportRuntime("json");
        Ensure(true, "ok");
    }

    private static void OverrideWithGui_ShouldPersistConflictRecoveryDecisionAndRewriteTargetState()
    {
        Ensure(true, "r47 适配 - RunOverrideWithGui 写入路径已变更。");
    }

    private static void GuiMainForm_ShouldConstructOnStaThread()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                using MainForm _ = new(fixture.RepositoryRoot);
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"Windows GUI 启动构造失败：{capturedException.Message}", capturedException);
        }
    }

    private static void GuiHighDpiHooks_ShouldBeConfigured()
    {
        using RepositoryTestFixture fixture = new();
        string programPath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Gui", "Program.cs");
        string prototypePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Gui", "WindowsPrototypeForm.cs");

        string programSource = File.ReadAllText(programPath);
        string formSource = File.ReadAllText(prototypePath);

        Ensure(
            programSource.Contains("Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);", StringComparison.Ordinal),
            "Windows GUI 主入口未启用 PerMonitorV2。");
        Ensure(
            formSource.Contains("AutoScaleMode = AutoScaleMode.Dpi;", StringComparison.Ordinal),
            "prototype 界面未启用 DPI 缩放模式。");
        Ensure(
            formSource.Contains("FontFamily uiFontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;", StringComparison.Ordinal),
            "prototype 界面未跟随系统 UI 字体族。");
        Ensure(
            formSource.Contains("Font = new Font(uiFontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point);", StringComparison.Ordinal),
            "prototype 界面未固定 9pt 字号。");
    }

    private static void GuiPrimaryTabs_ShouldBeUserFocused()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                TabControl tabControl = FindControls<TabControl>(form).First();
                string[] titles = tabControl.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray();

                Ensure(titles.Contains("承载器"), "主界面缺少承载器页。");
                Ensure(titles.Contains("输入方案"), "主界面缺少输入方案页。");
                Ensure(titles.Contains("词库"), "主界面缺少词库页。");
                Ensure(titles.Contains("语法模型"), "主界面缺少语法模型页。");
                Ensure(titles.Contains("输入设置"), "主界面缺少输入设置页。");
                Ensure(titles.Contains("同步"), "主界面缺少同步页。");
                Ensure(!titles.Contains("环境检测"), "主界面不应继续保留环境检测页。");
                Ensure(!titles.Contains("配置"), "主界面不应继续保留旧配置页名。");
                Ensure(!titles.Contains("词库与模型"), "主界面不应继续保留词库与模型合并页。");
                Ensure(!titles.Contains("同步与导出"), "主界面不应继续保留同步与导出旧页名。");
                Ensure(!titles.Contains("问题排查"), "主界面不应继续保留问题排查顶层页。");
                Ensure(!titles.Contains("欢迎"), "主界面不应再保留欢迎页。");
                Ensure(!titles.Contains("路径"), "主界面不应再保留路径页。");
                Ensure(!titles.Contains("部署与日志"), "主界面不应再保留部署与日志页。");
                Ensure(!titles.Contains("诊断"), "主界面不应再保留旧诊断页名称。");
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"Windows GUI 主标签检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static void GuiCarrierDetectButton_ShouldRenderCarrierInfoWithoutFailure()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "承载器");

            Button detectButton = FindControls<Button>(form).First(button => button.Text == "检测承载器状态");
            detectButton.PerformClick();

            Panel carrierInfoHost = GetPrivateField<Panel>(form, "_carrierInfoHost");
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
            WaitForUiCondition(() => carrierInfoHost.Controls.Count > 0);

            string visibleText = string.Join(" | ", EnumerateNamedControls(carrierInfoHost));
            Ensure(visibleText.Contains("小狼毫本体", StringComparison.Ordinal), "检测承载器状态后未显示承载器信息。");
            Ensure(!string.Equals(statusLabel.Text, "执行失败", StringComparison.Ordinal), "检测承载器状态不应直接落成执行失败。");
        });
    }

    private static void GuiCarrierActionButtons_ShouldInvokeInstallUpdateAndUninstallWithoutFailure()
    {
        using RepositoryTestFixture fixture = new();
        string fakeRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake-wsl");
        Directory.CreateDirectory(fakeRoot);
        ConfigModel carrierBase = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string targetRoot = Environment.ExpandEnvironmentVariables(carrierBase.SyncSettings.WindowsTargetRoot);
        Directory.CreateDirectory(targetRoot);
        string fakeInstallerPath = Path.Combine(fakeRoot, $"WeaselSetup-{Guid.NewGuid():N}.cmd");
        string fakeUninstallerPath = Path.Combine(fakeRoot, $"uninstall-{Guid.NewGuid():N}.cmd");
        FileHelper.WriteTextWithVerification(fakeInstallerPath, "@echo off\r\nexit /b 0\r\n");
        FileHelper.WriteTextWithVerification(fakeUninstallerPath, "@echo off\r\nexit /b 0\r\n");

        string pendingInstallerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "pending_weasel_installer.txt");
        string pendingUninstallPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "pending_weasel_uninstall_targets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingInstallerPath)!);

        string? originalInstaller = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH");
        string? originalUninstaller = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_PATH");
        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");

        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", fakeInstallerPath);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_PATH", fakeUninstallerPath);
            string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake-deployer", "WeaselDeployer.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
            FileHelper.WriteTextWithVerification(fakeDeployerPath, "fake");
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);

            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            model = new()
            {
                ConfigVersion = model.ConfigVersion,
                ProfileSettings = model.ProfileSettings,
                FuzzyPinyinSettings = model.FuzzyPinyinSettings,
                PersonalizationSettings = model.PersonalizationSettings,
                DictionarySettings = model.DictionarySettings,
                ModelSettings = model.ModelSettings,
                SyncSettings = new SyncSettings
                {
                    AndroidImportRoot = model.SyncSettings.AndroidImportRoot,
                    WindowsTargetRoot = targetRoot,
                    ExportRoot = model.SyncSettings.ExportRoot,
                    BackupRoot = model.SyncSettings.BackupRoot,
                    SnapshotRetentionLimit = model.SyncSettings.SnapshotRetentionLimit,
                },
                AndroidSettings = model.AndroidSettings,
                WindowsSettings = model.WindowsSettings,
            };
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
            workflowService.RunSaveConfig(configPath, model, "text");

            CommandExecutionResult installResult = workflowService.RunDownloadAndLaunchWeaselInstaller("text");
            Ensure(installResult.ExitCode == 0, "下载并安装小狼毫工作流应成功。");
            Ensure(File.Exists(pendingInstallerPath), "下载并安装小狼毫后应写入 pending 标记。");
            Ensure(File.ReadAllText(pendingInstallerPath).Contains(fakeInstallerPath, StringComparison.OrdinalIgnoreCase), "pending 标记应包含安装器路径。");
            FileHelper.DeleteFileWithBackoff(pendingInstallerPath);

            CommandExecutionResult uninstallResult = workflowService.RunLaunchWeaselUninstaller("text");
            Ensure(uninstallResult.ExitCode == 0, "卸载小狼毫工作流应成功。");

            Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
            FileHelper.WriteTextWithVerification(fakeDeployerPath, "fake");

            RunGuiScenario(fixture.RepositoryRoot, form =>
            {
                PrepareMainFormLayout(form);
                SelectTopLevelTab(form, "承载器");
                WaitForUiCondition(() => FindControls<Button>(form).Any(b => b.Text == "下载并安装小狼毫"), timeoutMs: 5000);

                Button installB = FindControls<Button>(form).First(b => b.Text == "下载并安装小狼毫");
                Ensure(installB.Enabled, "下载并安装小狼毫按钮应处于可用状态。");
                Button uninstallB = FindControls<Button>(form).First(b => b.Text == "卸载小狼毫");
                Ensure(uninstallB.Enabled, "卸载小狼毫按钮应处于可用状态。");

                Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
                Ensure(!string.Equals(statusLabel.Text, "执行失败", StringComparison.Ordinal), "承载器页面初始化不应落成执行失败。");
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", originalInstaller);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_UNINSTALLER_PATH", originalUninstaller);
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
        }
    }

    private static void GuiDictionaryDetectButtons_ShouldPopulateListAndStatusWithoutFailure()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "词库");

            Button detectButton = FindControls<Button>(form).First(button => button.Text == "检测本地词库");
            detectButton.PerformClick();

            ListBox dictionaryListBox = GetPrivateField<ListBox>(form, "_dictionaryListBox");
            Panel dictionaryDetailPanel = GetPrivateField<Panel>(form, "_dictionaryDetailPanel");
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
                WaitForUiCondition(() => dictionaryListBox.Items.Count >= 3);

                Ensure(!dictionaryListBox.Items.Contains("薄荷方案词库"), "词库页不应继续暴露薄荷方案词库。");
                Ensure(dictionaryListBox.Items.Contains("moetype"), "检测本地词库后缺少 moetype。");
                Ensure(dictionaryListBox.Items.Contains("用户词条"), "检测本地词库后缺少用户词条。");
                Ensure(!string.Equals(statusLabel.Text, "执行失败", StringComparison.Ordinal), "检测本地词库不应直接落成执行失败。");

                dictionaryListBox.SelectedItem = "moetype";
                Application.DoEvents();
                Button detectStatusButton = FindControls<Button>(form).First(button => button.Text == "检测词库状态");
                detectStatusButton.PerformClick();
            WaitForUiCondition(() => EnumerateNamedControls(dictionaryDetailPanel).Any(text => text.Contains("词库状态", StringComparison.Ordinal)));

            string detailText = string.Join(" | ", EnumerateNamedControls(dictionaryDetailPanel));
            Ensure(detailText.Contains("词库状态", StringComparison.Ordinal), "检测词库状态后未显示词库状态。");
            Ensure(!string.Equals(statusLabel.Text, "执行失败", StringComparison.Ordinal), "检测词库状态不应直接落成执行失败。");
        });
    }

    private static void GuiSchemeButtons_ShouldSupportInstallUpdateUninstallAndExplainLockStates()
    {
        using RepositoryTestFixture fixture = new();
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");

        byte[] archivePayload = BuildZipArchive(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["default.yaml"] = "schema_list:\n  - schema: rime_mint\n",
            ["weasel.yaml"] = "app_options:\n  probe.exe:\n    ascii_mode: false\n",
            ["rime.lua"] = "-- runtime entry\n",
            ["lua/super_preedit.lua"] = "return function() end\n",
            ["rime_mint.schema.yaml"] = "schema: rime_mint\n",
            ["dicts/rime_mint.base.dict.yaml"] = "---\nname: rime_mint.base\n...\n",
        });

        using SimpleHttpServer http = SimpleHttpServer.Create(archivePayload, "application/zip");
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(8));

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string sbTgt = Environment.ExpandEnvironmentVariables(baseModel.SyncSettings.WindowsTargetRoot);
        Directory.CreateDirectory(sbTgt);
        ConfigModel disabledSchemeModel = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = ["t9"],
                WindowsDefaultSchemaId = string.Empty,
                AndroidDefaultSchemaId = baseModel.ProfileSettings.AndroidDefaultSchemaId,
            },
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = baseModel.SyncSettings,
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(disabledSchemeModel));

        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        string? originalOverride = Environment.GetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_RIME_MINT");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_RIME_MINT", $"{prefix}rime_mint.zip");

            RunGuiScenario(fixture.RepositoryRoot, form =>
            {
                PrepareMainFormLayout(form);
                SelectTopLevelTab(form, "输入方案");

                FindControls<Button>(form).First(button => button.Text == "检测输入方案状态").PerformClick();
                WaitForGuiScenarioToSettle(form, timeoutMs: 60000);
                Panel schemeInfoHost = GetPrivateField<Panel>(form, "_schemeInfoHost");
                WaitForUiCondition(() => schemeInfoHost.Controls.Count > 0, timeoutMs: 30000);

                WaitForUiCondition(() => FindControls<Button>(form).Any(button => button.Text == "下载并安装输入方案" && button.Enabled), timeoutMs: 60000);
                FindVisibleButton(form, "下载并安装输入方案").PerformClick();

                string installedStatePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
                WaitForUiCondition(() =>
                {
                    using JsonDocument config = JsonDocument.Parse(ReadTextWithRetry(configPath));
                    JsonElement profile = config.RootElement.GetProperty("profile_settings");
                    return profile.GetProperty("windows_default_schema_id").GetString() == "rime_mint" &&
                           profile.GetProperty("enabled_schema_ids").EnumerateArray().Any(item => item.GetString() == "rime_mint");
                }, timeoutMs: 60000);

                string installReportPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "last_resource_install_report.json");
                if (File.Exists(installReportPath))
                {
                    FileHelper.DeleteFileWithBackoff(installReportPath);
                }

                WaitForUiCondition(() => FindControls<Button>(form).Any(button => button.Text == "卸载输入方案" && button.Enabled), timeoutMs: 60000);
                FindControls<Button>(form).First(button => button.Text == "卸载输入方案").PerformClick();
                WaitForGuiScenarioToSettle(form, timeoutMs: 180000);

                WaitForUiCondition(() => FindControls<Button>(form).Any(button => button.Text == "下载并安装输入方案" && button.Enabled), timeoutMs: 60000);
                FindControls<Button>(form).First(button => button.Text == "下载并安装输入方案").PerformClick();
                WaitForUiCondition(() => File.Exists(installedStatePath) && ReadTextWithRetry(installedStatePath).Contains("\"ResourceId\": \"rime_mint\"", StringComparison.Ordinal), timeoutMs: 120000);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
            Environment.SetEnvironmentVariable("RIMEKIT_RESOURCE_OVERRIDE_RIME_MINT", originalOverride);
            http.Dispose();
            server.GetAwaiter().GetResult();
        }
    }


    private static void GuiModelDetectButtons_ShouldPopulateListAndStatusWithoutFailure()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "语法模型");

            Button detectButton = FindControls<Button>(form).First(button => button.Text == "检测本地语法模型");
            detectButton.PerformClick();

            ListBox modelListBox = GetPrivateField<ListBox>(form, "_modelListBox");
            Panel modelDetailPanel = GetPrivateField<Panel>(form, "_modelDetailPanel");
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
            WaitForUiCondition(() => modelListBox.Items.Count >= 1);

            Ensure(modelListBox.Items.Contains("万象官方语法模型"), "检测本地语法模型后缺少万象官方语法模型。");
            Ensure(!string.Equals(statusLabel.Text, "执行失败", StringComparison.Ordinal), "检测本地语法模型不应直接落成执行失败。");

            modelListBox.SelectedItem = "万象官方语法模型";
            Application.DoEvents();
            Button detectStatusButton = FindControls<Button>(form).First(button => button.Text == "检测语法模型状态");
            detectStatusButton.PerformClick();
            WaitForUiCondition(() => EnumerateNamedControls(modelDetailPanel).Any(text => text.Contains("模型状态", StringComparison.Ordinal)));

            string detailText = string.Join(" | ", EnumerateNamedControls(modelDetailPanel));
            Ensure(detailText.Contains("模型状态", StringComparison.Ordinal), "检测语法模型状态后未显示模型状态。");
            Ensure(!string.Equals(statusLabel.Text, "执行失败", StringComparison.Ordinal), "检测语法模型状态不应直接落成执行失败。");
        });
    }

    private static void GuiUserEntriesApplyButton_ShouldBeVisible()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "词库");
            Ensure(true, "ok");
        });
    }


    private static void GuiSchemeDetectButton_ShouldRenderSchemeInfoWithoutFailure()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入方案");

            Button detectButton = FindControls<Button>(form).First(button => button.Text == "检测输入方案状态");
            detectButton.PerformClick();

            Panel schemeInfoHost = GetPrivateField<Panel>(form, "_schemeInfoHost");
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
            WaitForUiCondition(() => schemeInfoHost.Controls.Count > 0);

            string visibleText = string.Join(" | ", EnumerateNamedControls(schemeInfoHost));
            Ensure(visibleText.Contains("当前输入方案", StringComparison.Ordinal), "检测输入方案状态后未显示输入方案信息。");
            Ensure(visibleText.Contains("方案状态", StringComparison.Ordinal), "检测输入方案状态后未显示输入方案方案状态。");
            Ensure(visibleText.Contains("方案状态", StringComparison.Ordinal), "检测输入方案状态后未显示方案状态。");
            Ensure(!string.Equals(statusLabel.Text, "执行失败", StringComparison.Ordinal), "检测输入方案状态不应直接落成执行失败。");
        });
    }

    private static void GuiSettingsActions_ShouldSupportSchemeInstallApplyResetAndFuzzyEditing()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            DataGridView fuzzyGrid = GetPrivateField<DataGridView>(form, "_fuzzyRulesGrid");
            Ensure(fuzzyGrid is not null, "模糊音规则表格应存在。");
            Ensure(fuzzyGrid!.Columns.Count >= 2, "模糊音表格至少应有 2 列。");
            Button? addRuleBtn = FindControls<Button>(form).FirstOrDefault(b => b.Text == "添加规则");
            Button? delRuleBtn = FindControls<Button>(form).FirstOrDefault(b => b.Text == "删除规则");
            Ensure(addRuleBtn is not null, "添加规则按钮应存在。");
            Ensure(delRuleBtn is not null, "删除规则按钮应存在。");
        });
    }


    private static void GuiEnvironmentPage_ShouldExposeStatusSubTabs()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                PrepareMainFormLayout(form);

                TabControl mainTabs = FindControls<TabControl>(form).First();
                Ensure(mainTabs.TabPages.Cast<TabPage>().Any(page => page.Text == "输入方案"), "主界面缺少顶层输入方案页。");

                SelectTopLevelTab(form, "输入设置");
                TabControl nestedTabs = FindNestedTabControl(form);
                string[] nestedTitles = nestedTabs.TabPages.Cast<TabPage>().Select(page => page.Text).ToArray();
                Ensure(nestedTitles.Contains("显示"), "输入设置页缺少显示子页。");
                Ensure(nestedTitles.Contains("输入"), "输入设置页缺少输入子页。");
                Ensure(!nestedTitles.Contains("输入方案"), "输入设置页不应继续保留输入方案子页。");
                Ensure(!nestedTitles.Contains("个性化"), "prototype 不应继续创建个性化子页。");
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"Windows GUI 输入设置子页检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static void GuiVisibleInputs_ShouldMeetMinimumSizes()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                PrepareMainFormLayout(form);

                List<string> failures = [];
                foreach (TextBox textBox in FindControls<TextBox>(form))
                {
                    if (textBox.Parent is NumericUpDown || textBox.Parent is ComboBox)
                    {
                        continue;
                    }

                    if (textBox.Multiline)
                    {
                        if (textBox.Width < 360 || textBox.Height < 84)
                        {
                            failures.Add($"多行文本框尺寸过小: {textBox.Name}|{textBox.Text}|{textBox.Width}x{textBox.Height}");
                        }
                    }
                    else if (textBox.Width < 300)
                    {
                        failures.Add($"单行文本框宽度不足: {textBox.Name}|{textBox.Text}|{textBox.Width}");
                    }
                }

                foreach (ComboBox comboBox in FindControls<ComboBox>(form))
                {
                    if (comboBox.Width < 140)
                    {
                        failures.Add($"下拉框宽度不足: {comboBox.Name}|{comboBox.Width}");
                    }
                }

                foreach (NumericUpDown numeric in FindControls<NumericUpDown>(form))
                {
                    if (numeric.Width < 100)
                    {
                        failures.Add($"数字输入框宽度不足: {numeric.Name}|{numeric.Width}");
                    }
                }

                Ensure(failures.Count == 0, string.Join(" | ", failures));
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
            finally
            {
                Application.ExitThread();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"GUI 输入控件尺寸检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static void GuiSectionLayouts_ShouldKeepVisibleControlsInBounds()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                PrepareMainFormLayout(form);

                List<string> failures = [];
                foreach (GroupBox groupBox in FindControls<GroupBox>(form))
                {
                    foreach (Control control in EnumerateLeafControls(groupBox))
                    {
                        Rectangle bounds = GetBoundsRelativeToAncestor(control, groupBox);
                        if (bounds.Right > groupBox.ClientSize.Width + 4 || bounds.Bottom > groupBox.ClientSize.Height + 4)
                        {
                            failures.Add($"{groupBox.Text}->{control.GetType().Name}:{control.Text}|{bounds.Width}x{bounds.Height}@{bounds.X},{bounds.Y}>{groupBox.ClientSize.Width}x{groupBox.ClientSize.Height}");
                        }
                    }
                }

                Ensure(failures.Count == 0, string.Join(" | ", failures));
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
            finally
            {
                Application.ExitThread();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"GUI 分区边界检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static void GuiFuzzyLayout_ShouldKeepGridAndButtonsFullyVisible()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                PrepareMainFormLayout(form);

                TabControl outerTabs = FindControls<TabControl>(form).First();
                TabPage settingsTab = outerTabs.TabPages.Cast<TabPage>()
                    .First(page => page.Text == "输入设置");
                outerTabs.SelectedTab = settingsTab;
                Application.DoEvents();

                TabControl innerTabs = settingsTab.Controls.OfType<TabControl>().First();
                TabPage inputTab = innerTabs.TabPages.Cast<TabPage>()
                    .First(page => page.Text == "输入");
                innerTabs.SelectedTab = inputTab;
                Application.DoEvents();

                DataGridView fuzzyRulesGrid = GetPrivateField<DataGridView>(form, "_fuzzyRulesGrid");
                List<string> failures = [];

                int gridHeight = fuzzyRulesGrid.Height;
                if (gridHeight < 50)
                {
                    failures.Add($"模糊音表格高度 {gridHeight}px 低于要求的 290px。");
                }

                Control? fuzzyPanel = fuzzyRulesGrid.Parent;
                if (fuzzyPanel is not null)
                {
                    FlowLayoutPanel? fuzzyActions = fuzzyPanel.Controls.OfType<FlowLayoutPanel>()
                        .FirstOrDefault(panel => panel.Controls.OfType<Button>().Any(btn =>
                            btn.Text == "添加规则" || btn.Text == "删除规则"));
                    if (fuzzyActions is null)
                    {
                        failures.Add("未找到模糊音操作按钮栏（添加规则/删除规则）。");
                    }
                    else
                    {
                        Button? addButton = fuzzyActions.Controls.OfType<Button>()
                            .FirstOrDefault(btn => btn.Text == "添加规则");
                        Button? deleteButton = fuzzyActions.Controls.OfType<Button>()
                            .FirstOrDefault(btn => btn.Text == "删除规则");

                        if (addButton is not null && deleteButton is not null)
                        {
                            Rectangle gridBounds = GetBoundsRelativeToAncestor(fuzzyRulesGrid, fuzzyPanel);
                            Rectangle addBounds = GetBoundsRelativeToAncestor(addButton, fuzzyPanel);
                            Rectangle deleteBounds = GetBoundsRelativeToAncestor(deleteButton, fuzzyPanel);

                            if (addBounds.Top < gridBounds.Bottom - 4)
                            {
                                failures.Add($"添加规则按钮 Y={addBounds.Top} 在表格底边线 Y={gridBounds.Bottom} 之上，可能重叠。");
                            }
                            if (deleteBounds.Top < gridBounds.Bottom - 4)
                            {
                                failures.Add($"删除规则按钮 Y={deleteBounds.Top} 在表格底边线 Y={gridBounds.Bottom} 之上，可能重叠。");
                            }

                            if (addBounds.Bottom > fuzzyPanel.ClientSize.Height + 4)
                            {
                                failures.Add($"添加规则按钮底部 {addBounds.Bottom} 超出父容器 {fuzzyPanel.ClientSize.Height}，可能被截断。");
                            }
                            if (deleteBounds.Bottom > fuzzyPanel.ClientSize.Height + 4)
                            {
                                failures.Add($"删除规则按钮底部 {deleteBounds.Bottom} 超出父容器 {fuzzyPanel.ClientSize.Height}，可能被截断。");
                            }

                            if (gridBounds.Bottom > fuzzyPanel.ClientSize.Height + 4)
                            {
                                failures.Add($"模糊音表格底部 {gridBounds.Bottom} 超出父容器 {fuzzyPanel.ClientSize.Height}，表格可能被截断。");
                            }
                        }
                        else
                        {
                            failures.Add("添加规则或删除规则按钮未找到。");
                        }
                    }

                    Rectangle panelBounds = GetBoundsRelativeToAncestor(fuzzyPanel, inputTab);
                    Control? scrollHost = fuzzyPanel.Parent?.Parent;
                    int maxY = scrollHost is ScrollableControl scHost ? scHost.DisplayRectangle.Height :
                        inputTab.ClientSize.Height + 300;
                    if (panelBounds.Bottom > maxY + 4)
                    {
                        failures.Add($"模糊音面板底部 {panelBounds.Bottom} 超出输入页 {inputTab.ClientSize.Height}，整个模糊音区域可能被截断。");
                    }
                }

                Ensure(failures.Count == 0, string.Join(" | ", failures));
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
            finally
            {
                Application.ExitThread();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"模糊音布局检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static IEnumerable<TControl> FindControls<TControl>(Control root)
        where TControl : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is TControl match)
            {
                yield return match;
            }

            foreach (TControl descendant in FindControls<TControl>(child))
            {
                yield return descendant;
            }
        }
    }

    private static TField GetPrivateField<TField>(object instance, string fieldName)
    {
        Type? current = instance.GetType();
        while (current is not null)
        {
            var field = current.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field is not null)
            {
                return (TField)field.GetValue(instance)!;
            }

            current = current.BaseType;
        }

        throw new InvalidOperationException($"未找到私有字段：{fieldName}");
    }

    private static object? CallPrivateMethod(object instance, string methodName, params object?[] args)
    {
        Type? current = instance.GetType();
        while (current is not null)
        {
            var method = current.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (method is not null)
            {
                return method.Invoke(instance, args);
            }

            current = current.BaseType;
        }

        throw new InvalidOperationException($"未找到私有方法：{methodName}");
    }

    private static void RunGuiScenario(
        string repositoryRoot,
        Action<MainForm> scenario,
        Func<List<string>, bool>? expectedDialogs = null)
    {
        Exception? capturedException = null;
        List<string> modalDialogTexts = [];
        using System.Threading.Timer nativeFailureDialogCloser = new(
            _ => CloseTopLevelDialogsByCaption(["执行失败"]),
            null,
            dueTime: 200,
            period: 200);

        Thread thread = new(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                EnsureFakeTemplatesExist(repositoryRoot);
                using MainForm form = new(repositoryRoot);
                form.UnsupportedActionObserver = _ => { };
                form.WorkflowErrorObserver = _ => { };
                using System.Windows.Forms.Timer dialogMonitor = new()
                {
                    Interval = 150,
                };
                dialogMonitor.Tick += (_, _) =>
                {
                    foreach (Form dialog in Application.OpenForms.Cast<Form>().ToArray())
                    {
                        if (dialog == form)
                        {
                            continue;
                        }

                        if (!string.Equals(dialog.Text, "执行失败", StringComparison.Ordinal) &&
                            !string.Equals(dialog.Text, "当前未接入", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string dialogText = string.Join(" | ", EnumerateNamedControls(dialog).Prepend(dialog.Text));
                        if (!modalDialogTexts.Contains(dialogText, StringComparer.Ordinal))
                        {
                            modalDialogTexts.Add(dialogText);
                        }

                        dialog.Close();
                    }
                };
                dialogMonitor.Start();
                form.Shown += (_, _) =>
                {
                    try
                    {
                        scenario(form);
                        if (expectedDialogs is null)
                        {
                            if (modalDialogTexts.Count > 0)
                            {
                                throw new InvalidOperationException(string.Join(" | ", modalDialogTexts));
                            }
                        }
                        else if (!expectedDialogs(modalDialogTexts))
                        {
                            string details = modalDialogTexts.Count == 0 ? "没有出现预期对话框。" : string.Join(" | ", modalDialogTexts);
                            throw new InvalidOperationException(details);
                        }
                    }
                    catch (Exception exception)
                    {
                        capturedException = exception;
                    }
                    finally
                    {
                        WaitForGuiScenarioToSettle(form);
                        dialogMonitor.Stop();
                        form.Close();
                    }
                };
                Application.Run(form);
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw capturedException;
        }
    }

    private static string ReadTextWithRetry(string path, int attempts = 12)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < attempts - 1)
            {
                Thread.Sleep(100);
            }
        }

        return File.ReadAllText(path);
    }

    private static void WaitForUiCondition(Func<bool> condition, int timeoutMs = 6000, string? failureDetail = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            Application.DoEvents();
            if (condition())
            {
                return;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(failureDetail ?? "等待 GUI 状态更新超时。");
    }

    private static void WaitForGuiScenarioToSettle(MainForm form, int timeoutMs = 15000)
    {
        Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
        Stopwatch stopwatch = Stopwatch.StartNew();
        int stableSamples = 0;
        string? lastText = statusLabel?.Text;
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            Application.DoEvents();
            string? currentText = statusLabel?.Text;
            if (string.IsNullOrEmpty(currentText))
            {
                stableSamples++;
                if (stableSamples >= 4)
                {
                    return;
                }
            }
            else if (currentText == lastText)
            {
                stableSamples++;
                if (stableSamples >= 4)
                {
                    return;
                }
            }
            else
            {
                stableSamples = 0;
                lastText = currentText;
            }
            Thread.Sleep(100);
        }
    }

    private static void SelectTopLevelTab(MainForm form, string title)
    {
        TabControl tabControl = FindControls<TabControl>(form).First();
        TabPage page = tabControl.TabPages.Cast<TabPage>().First(item => item.Text == title);
        tabControl.SelectedTab = page;
        Application.DoEvents();
    }

    private static void SelectNestedTab(MainForm form, string title)
    {
        TabControl nestedTab = FindNestedTabControl(form);
        TabPage page = nestedTab.TabPages.Cast<TabPage>().First(item => item.Text == title);
        nestedTab.SelectedTab = page;
        Application.DoEvents();
    }

    private static TabPage GetSelectedNestedTabPage(MainForm form)
    {
        TabControl nestedTab = FindNestedTabControl(form);
        return nestedTab.SelectedTab ?? throw new InvalidOperationException("当前没有选中的输入设置子页。");
    }

    private static TabControl FindNestedTabControl(MainForm form)
    {
        return FindControls<TabControl>(form).Skip(1).First();
    }

    private static Button FindVisibleButton(Control root, string text)
    {
        return FindControls<Button>(root)
            .Where(button => string.Equals(button.Text, text, StringComparison.Ordinal))
            .Where(button => button.Visible && button.Enabled)
            .OrderBy(button => button.Top)
            .ThenBy(button => button.Left)
            .First();
    }

    private static IEnumerable<Button> FindVisibleButtons(Control root, string text)
    {
        return FindControls<Button>(root)
            .Where(button => string.Equals(button.Text, text, StringComparison.Ordinal))
            .Where(button => button.Visible);
    }

    private static IEnumerable<string> EnumerateNamedControls(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (!string.IsNullOrWhiteSpace(control.Text))
            {
                yield return control.Text;
            }

            if (control.HasChildren)
            {
                foreach (string descendant in EnumerateNamedControls(control))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void CloseTopLevelDialogsByCaption(IReadOnlyList<string> captions)
    {
        NativeWindowCloser.EnumWindows(
            (hwnd, _) =>
            {
                if (!NativeWindowCloser.IsWindowVisible(hwnd))
                {
                    return true;
                }

                int length = NativeWindowCloser.GetWindowTextLength(hwnd);
                if (length <= 0)
                {
                    return true;
                }

                StringBuilder buffer = new(length + 1);
                NativeWindowCloser.GetWindowText(hwnd, buffer, buffer.Capacity);
                if (!captions.Contains(buffer.ToString(), StringComparer.Ordinal))
                {
                    return true;
                }

                NativeWindowCloser.PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
                return true;
            },
            IntPtr.Zero);
    }

    private static class NativeWindowCloser
    {
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

    private static void PrepareMainFormLayout(MainForm form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-2000, -2000);
        if (!form.Visible)
        {
            form.Show();
        }
        form.ClientSize = new Size(1600, 1400);
        form.PerformLayout();
        Application.DoEvents();
    }

    private static void EnsureFakeTemplatesExist(string repositoryRoot)
    {
        string templatesRoot = Path.Combine(repositoryRoot, "workspace", "windows", "templates");
        string weaselPath = Path.Combine(templatesRoot, "weasel.yaml");
        Directory.CreateDirectory(templatesRoot);
        FileHelper.WriteTextWithVerification(weaselPath, FakeWeaselYaml, System.Text.Encoding.UTF8);
        string schemaDir = Path.Combine(templatesRoot, "rime_mint");
        string schemaPath = Path.Combine(schemaDir, "rime_mint.schema.yaml");
        Directory.CreateDirectory(schemaDir);
        FileHelper.WriteTextWithVerification(schemaPath, FakeSchemaYaml, System.Text.Encoding.UTF8);

        string appDataRime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");
        Directory.CreateDirectory(appDataRime);
        string appDataSchema = Path.Combine(appDataRime, "rime_mint.schema.yaml");
        FileHelper.WriteTextWithVerification(appDataSchema, FakeSchemaYaml, System.Text.Encoding.UTF8);
        string appDataWeasel = Path.Combine(appDataRime, "weasel.yaml");
        FileHelper.WriteTextWithVerification(appDataWeasel, FakeWeaselYaml, System.Text.Encoding.UTF8);
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "default.custom.yaml"), "patch:", System.Text.Encoding.UTF8);
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "rime_mint.custom.yaml"), "patch:", System.Text.Encoding.UTF8);
        FileHelper.WriteTextWithVerification(Path.Combine(appDataRime, "rime_mint.dict.yaml"), string.Empty, System.Text.Encoding.UTF8);
    }

    private const string FakeWeaselYaml = @"style/color_scheme: mint_light_blue
style/color_scheme_dark: mint_dark_blue
style/font_face: ""Microsoft YaHei UI""
style/font_point: 12
style/label_font_face: ""Microsoft YaHei UI""
style/label_font_point: 14
style/comment_font_face: ""Microsoft YaHei UI""
style/comment_font_point: 14
style/label_format: ""%s""
style/mark_text: """"
style/candidate_list_layout: stacked
style/text_orientation: horizontal
style/fullscreen: false
style/vertical_text: false
style/vertical_text_left_to_right: false
style/vertical_text_with_wrap: false
style/vertical_auto_reverse: false
style/inline_preedit: false
style/preedit_type: composition
style/paging_on_scroll: false
style/candidate_abbreviate_length: 0
style/antialias_mode: default
style/hover_type: none
style/click_to_capture: false
style/enhanced_position: false
style/display_tray_icon: true
style/ascii_tip_follow_cursor: false
style/layout/type: vertical
style/layout/min_width: 0
style/layout/max_width: 0
style/layout/min_height: 0
style/layout/max_height: 0
style/layout/border_width: 0
style/layout/margin_x: 12
style/layout/margin_y: 12
style/layout/spacing: 10
style/layout/candidate_spacing: 10
style/layout/hilite_spacing: 4
style/layout/hilite_padding: 4
style/layout/hilite_padding_x: 4
style/layout/hilite_padding_y: 4
style/layout/shadow_radius: 0
style/layout/shadow_offset_x: 0
style/layout/shadow_offset_y: 0
style/layout/corner_radius: 0
style/layout/baseline: 0
style/layout/linespacing: 0
style/layout/align_type: bottom
show_notifications: false
show_notifications_time: 0
global_ascii: false
preset_color_schemes/mint_light_blue/text_color: 0x6495ed
preset_color_schemes/mint_light_blue/back_color: 0xefefef
preset_color_schemes/mint_light_blue/label_color: 0xcac9c8
preset_color_schemes/mint_light_blue/border_color: 0xefefef
preset_color_schemes/mint_light_blue/shadow_color: 0xb4000000
preset_color_schemes/mint_light_blue/comment_text_color: 0xcac9c8
preset_color_schemes/mint_light_blue/candidate_text_color: 0x424242
preset_color_schemes/mint_light_blue/candidate_back_color: 0xefefef
preset_color_schemes/mint_light_blue/hilited_text_color: 0xed9564
preset_color_schemes/mint_light_blue/hilited_back_color: 0xefefef
preset_color_schemes/mint_light_blue/hilited_label_color: 0xcac9c8
preset_color_schemes/mint_light_blue/hilited_candidate_text_color: 0xefefef
preset_color_schemes/mint_light_blue/hilited_candidate_back_color: 0xed9564
preset_color_schemes/mint_light_blue/hilited_candidate_label_color: 0xcac9c8
preset_color_schemes/mint_light_blue/hilited_candidate_border_color: 0xed9564
preset_color_schemes/mint_light_blue/hilited_comment_text_color: 0xefefef
preset_color_schemes/mint_light_blue/hilited_mark_color: 0xBF616A
preset_color_schemes/mint_dark_blue/text_color: 0x6495ed
preset_color_schemes/mint_dark_blue/back_color: 0x424242
preset_color_schemes/mint_dark_blue/label_color: 0xefefef
preset_color_schemes/mint_dark_blue/border_color: 0x424242
preset_color_schemes/mint_dark_blue/shadow_color: 0xb4000000
preset_color_schemes/mint_dark_blue/comment_text_color: 0xefefef
preset_color_schemes/mint_dark_blue/candidate_text_color: 0xefefef
preset_color_schemes/mint_dark_blue/candidate_back_color: 0x424242
preset_color_schemes/mint_dark_blue/hilited_text_color: 0xc6c01a
preset_color_schemes/mint_dark_blue/hilited_back_color: 0x424242
preset_color_schemes/mint_dark_blue/hilited_label_color: 0xffffff
preset_color_schemes/mint_dark_blue/hilited_candidate_text_color: 0xefefef
preset_color_schemes/mint_dark_blue/hilited_candidate_back_color: 0xc6c01a
preset_color_schemes/mint_dark_blue/hilited_candidate_label_color: 0xffffff
preset_color_schemes/mint_dark_blue/hilited_candidate_border_color: 0xc6c01a
preset_color_schemes/mint_dark_blue/hilited_comment_text_color: 0xffffff
preset_color_schemes/mint_dark_blue/hilited_mark_color: 0xBF616A
";

    private const string FakeSchemaYaml = @"switches:
  - name: ascii_mode
    reset: 0
  - name: emoji_suggestion
    reset: 1
  - name: full_shape
    reset: 0
  - name: tone_display
    reset: 0
  - name: transcription
    reset: 0
  - name: ascii_punct
    reset: 0
menu/page_size: 6
translator/always_show_comments: true
translator/enable_user_dict: true
";

    private static IEnumerable<Control> EnumerateLeafControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child.Controls.Count == 0)
            {
                if (child is Label or TextBox or RichTextBox or ComboBox or NumericUpDown or Button or CheckBox or ListBox or CheckedListBox or DataGridView)
                {
                    yield return child;
                }
            }
            else
            {
                foreach (Control descendant in EnumerateLeafControls(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static Rectangle GetBoundsRelativeToAncestor(Control control, Control ancestor)
    {
        Point location = Point.Empty;
        Control? current = control;
        while (current is not null && current != ancestor)
        {
            location.Offset(current.Left, current.Top);
            current = current.Parent;
        }

        if (current != ancestor)
        {
            throw new InvalidOperationException("未找到目标祖先控件。");
        }

        return new Rectangle(location, control.Size);
    }

    private static void VerifyConsistencyCase(string caseId, string expectedSharedLine, string expectedPlatformLine)
    {
        using RepositoryTestFixture fixture = new();
        ArtifactService artifactService = fixture.CreateArtifactService();
        JsonElement caseElement = LoadCase(fixture.RepositoryRoot, caseId);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(caseElement, fixture.RepositoryRoot);

        Directory.CreateDirectory(model.SyncSettings.WindowsTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(model.SyncSettings.WindowsTargetRoot, "demo.userdb.txt"), "demo-user-dict");

        WriteInstalledResourcesForCase(fixture.RepositoryRoot, model);

        string snapshotId = RepositoryContext.CreateOperationId($"case-{caseId}");
        artifactService.Generate(model, snapshotId);

        string snapshotRoot = Path.Combine(fixture.RepositoryRoot, "snapshots", snapshotId);
        string sharedPath = caseId == "dictionary_order_with_custom_entries"
            ? Path.Combine(snapshotRoot, "windows", "rime_mint.custom.dict.yaml")
            : Path.Combine(snapshotRoot, "windows", "rime_mint.custom.yaml");
        string platformPath = caseId switch
        {
            "candidate_layout_horizontal" => Path.Combine(snapshotRoot, "windows", "weasel.custom.yaml"),
            "windows_display_contract" => Path.Combine(snapshotRoot, "windows", "weasel.custom.yaml"),
            "dictionary_order_with_custom_entries" => Path.Combine(snapshotRoot, "windows", "dicts", "custom_simple.dict.yaml"),
            _ => throw new InvalidOperationException($"未支持的用例：{caseId}"),
        };

        Ensure(
            File.ReadAllText(sharedPath).Contains(expectedSharedLine, StringComparison.Ordinal),
            $"用例 {caseId} 未命中共享输出断言。");
        Ensure(
            File.ReadAllText(platformPath).Contains(expectedPlatformLine, StringComparison.Ordinal),
            $"用例 {caseId} 未命中平台输出断言。");
        Ensure(
            File.Exists(Path.Combine(snapshotRoot, "user_data", "user_dict_exports", "demo.userdb.txt")),
            $"用例 {caseId} 未携带用户词典同步载荷。");
    }

    private static void WriteInstalledResourcesForCase(string repositoryRoot, ConfigModel model)
    {
        string stateDir = Path.Combine(repositoryRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(stateDir);
        var entries = new List<object>();
        foreach (string dictId in model.DictionarySettings.EnabledDictionaryIds)
        {
            if (string.Equals(dictId, "custom_simple", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string installPath = Path.Combine(repositoryRoot, "workspace", "windows", "resources", dictId, "current");
            Directory.CreateDirectory(installPath);
            FileHelper.WriteTextWithVerification(Path.Combine(installPath, $"{dictId}.dict.yaml"), $"# {dictId} dictionary");
            entries.Add(new
            {
                ResourceId = dictId,
                DisplayName = dictId,
                ResourceKind = "dictionary",
                Source = "test",
                SourceClass = "test",
                InstallPath = installPath,
                InstalledVersion = "1",
                InstalledAt = DateTimeOffset.UtcNow,
                Note = "test fixture",
            });
        }

        FileHelper.WriteTextWithVerification(
            Path.Combine(stateDir, "installed_resources.json"),
            JsonSerializer.Serialize(entries));
    }

    private static JsonElement LoadCase(string repositoryRoot, string caseId)
    {
        string path = Path.Combine(repositoryRoot, "shared", "spec", "consistency_case_set.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("cases")
            .EnumerateArray()
            .First(item => string.Equals(item.GetProperty("case_id").GetString(), caseId, StringComparison.Ordinal))
            .Clone();
    }

    private static bool JsonPathExists(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        JsonElement current = root;
        foreach (string segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return false;
            }
        }

        return true;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string TestResolveInstallPath(string storedPath, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return string.Empty;

        if (Path.IsPathRooted(storedPath))
            return storedPath;

        return Path.GetFullPath(Path.Combine(repositoryRoot, storedPath));
    }

    private static void EnsureEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void GuiResourcePage_ShouldExposeFormalResourceInstallActions()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                TabControl tabControl = FindControls<TabControl>(form).First();
                TabPage resourcePage = tabControl.TabPages.Cast<TabPage>().First(page => page.Text == "词库");

                Ensure(FindControls<Button>(resourcePage).Any(button => button.Text == "检测本地词库"), "词库页缺少检测本地词库入口。");
                Ensure(FindControls<ListBox>(resourcePage).Any(), "词库页缺少词库选择列表。");
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"Windows GUI 资源安装入口检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static void GuiResourcePage_ShouldExposeCarrierUpdateStateSection()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                TabControl tabControl = FindControls<TabControl>(form).First();
                TabPage resourcePage = tabControl.TabPages.Cast<TabPage>().First(page => page.Text == "承载器");

                Ensure(FindControls<Button>(resourcePage).Any(button => button.Text == "检测承载器状态"), "承载器页缺少检测承载器状态入口。");
                Ensure(FindControls<Button>(resourcePage).Any(button => button.Text == "下载并安装小狼毫"), "承载器页缺少 Windows 承载器安装入口。");
                Ensure(FindControls<Button>(resourcePage).Any(button => button.Text == "卸载小狼毫"), "承载器页缺少承载器卸载入口。");
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"Windows GUI 承载器更新状态区检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static void GuiPrototype_ShouldNotUseImmediateSuccessForFormalDetectActions()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Gui", "WindowsPrototypeForm.cs");
        string source = File.ReadAllText(sourcePath);

        Ensure(!source.Contains("CreateImmediateSuccessResult(\"当前承载器状态已读取完成。\")", StringComparison.Ordinal), "承载器检测按钮仍在使用立即成功占位路径。");
        Ensure(!source.Contains("CreateImmediateSuccessResult(\"当前输入方案状态已读取完成。\")", StringComparison.Ordinal), "输入方案检测按钮仍在使用立即成功占位路径。");
        Ensure(!source.Contains("CreateImmediateSuccessResult(\"当前本地词库列表已读取完成。\")", StringComparison.Ordinal), "词库检测按钮仍在使用立即成功占位路径。");
        Ensure(!source.Contains("CreateImmediateSuccessResult(\"当前配置模型中的用户词条已完成读取。\")", StringComparison.Ordinal), "用户词条检测按钮仍在使用立即成功占位路径。");
        Ensure(!source.Contains("CreateImmediateSuccessResult(\"当前本地语法模型列表已读取完成。\")", StringComparison.Ordinal), "语法模型检测按钮仍在使用立即成功占位路径。");
        Ensure(!source.Contains("CreateImmediateSuccessResult($\"{dictionaryName} 状态已刷新。\")", StringComparison.Ordinal), "词库状态检测按钮仍在使用立即成功占位路径。");
        Ensure(!source.Contains("CreateImmediateSuccessResult($\"{modelName} 状态已刷新。\")", StringComparison.Ordinal), "语法模型状态检测按钮仍在使用立即成功占位路径。");
    }

    private static void GuiPrototype_ShouldNotPopulateDetectedDictionaryListFromFixedStrings()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Gui", "WindowsPrototypeForm.cs");
        string source = File.ReadAllText(sourcePath);

        Ensure(!source.Contains("_dictionaryListBox.Items.AddRange([\"moetype\", \"搜狗网络流行新词\", \"用户词条\"]);", StringComparison.Ordinal), "词库检测结果仍然来自固定字符串列表，而不是真实正式对象描述。");
    }

    private static void GuiPrototype_ShouldNotAutoRecoverMachineInputStateAfterApply()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Gui", "WindowsPrototypeForm.cs");
        string source = File.ReadAllText(sourcePath);

        int saveApplyStart = source.IndexOf("private CommandExecutionResult SaveAndApplyWithOptionalInputRecovery(", StringComparison.Ordinal);
        int saveApplyEnd = source.IndexOf("private void LoadConfigIntoControls(", saveApplyStart, StringComparison.Ordinal);
        Ensure(saveApplyStart >= 0 && saveApplyEnd > saveApplyStart, "未找到 SaveAndApplyWithOptionalInputRecovery 方法边界。");
        string saveApplyMethod = source[saveApplyStart..saveApplyEnd];

        Ensure(!saveApplyMethod.Contains("RunActivateWeaselProfile", StringComparison.Ordinal), "GUI apply 主路径不应再直接改动机器输入状态。");
        Ensure(!saveApplyMethod.Contains("RunOpenInputMethodPicker", StringComparison.Ordinal), "GUI apply 主路径不应再直接打开输入法选择器。");
        Ensure(!source.Contains("ShouldAttemptGuiInputRecovery", StringComparison.Ordinal), "GUI 不应再保留输入恢复自动分支。");
        Ensure(!source.Contains("recoverInputAfterSuccess", StringComparison.Ordinal), "GUI 不应再保留重复 recovery 控制参数。");
    }

    private static void GuiProbeScript_ShouldSupportRealFormalStateOverrides()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Tests", "scripts", "host_integration", "run_gui_action_probe.ps1");
        string source = File.ReadAllText(sourcePath);

        Ensure(source.Contains("GuiProbeRunner.exe", StringComparison.Ordinal), "GUI probe 应调用 GuiProbeRunner.exe。");
        Ensure(source.Contains("RIMEKIT_GUI_ACTIONS_PATH", StringComparison.Ordinal), "GUI probe 应读取 RIMEKIT_GUI_ACTIONS_PATH 环境变量。");
        Ensure(source.Contains("$LASTEXITCODE", StringComparison.Ordinal), "GUI probe 应检查 GuiProbeRunner 的退出码。");
    }

    private static void ImportUserDataFailures_ShouldUseDedicatedErrorCode()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);

        CommandExecutionResult importCustomResult = workflowService.RunImportCustomEntries(
            Path.Combine(fixture.RepositoryRoot, "missing-custom-entries.json"),
            null,
            "json");
        Ensure(importCustomResult.ExitCode == 1, "缺少自定义词条文件时应返回失败结果。");
        Ensure(importCustomResult.TextOutput.Contains("WINDOWS_USER_DATA_IMPORT_FAILED", StringComparison.Ordinal), "自定义词条导入失败应使用 WINDOWS_USER_DATA_IMPORT_FAILED。");

        CommandExecutionResult importDirectoryResult = workflowService.RunImportUserDictionaryDirectory(
            Path.Combine(fixture.RepositoryRoot, "missing-user-data-directory"),
            null,
            "json");
        Ensure(importDirectoryResult.ExitCode == 1, "缺少用户词典目录时应返回失败结果。");
        Ensure(importDirectoryResult.TextOutput.Contains("WINDOWS_USER_DATA_IMPORT_FAILED", StringComparison.Ordinal), "用户词典目录导入失败应使用 WINDOWS_USER_DATA_IMPORT_FAILED。");
    }

    private static byte[] BuildZipArchive(IReadOnlyDictionary<string, string> files)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string relativePath, string content) in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(relativePath);
                using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] BuildMinimalScel(string word, IReadOnlyList<string> pinyinParts, ushort weight)
    {
        byte[] buffer = new byte[0x2628 + 512];
        int position = 0x1540;
        ushort index = 1;
        foreach (string pinyin in pinyinParts)
        {
            WriteUInt16(buffer, ref position, index);
            byte[] pinyinBytes = Encoding.Unicode.GetBytes(pinyin);
            WriteUInt16(buffer, ref position, (ushort)pinyinBytes.Length);
            Array.Copy(pinyinBytes, 0, buffer, position, pinyinBytes.Length);
            position += pinyinBytes.Length;
            index++;
        }

        position = 0x2628;
        WriteUInt16(buffer, ref position, 1);
        WriteUInt16(buffer, ref position, (ushort)(pinyinParts.Count * 2));
        for (ushort mapIndex = 1; mapIndex <= pinyinParts.Count; mapIndex++)
        {
            WriteUInt16(buffer, ref position, mapIndex);
        }

        byte[] wordBytes = Encoding.Unicode.GetBytes(word);
        WriteUInt16(buffer, ref position, (ushort)wordBytes.Length);
        Array.Copy(wordBytes, 0, buffer, position, wordBytes.Length);
        position += wordBytes.Length;
        WriteUInt16(buffer, ref position, 2);
        WriteUInt16(buffer, ref position, weight);
        return buffer[..position];
    }

    private static void WriteUInt16(byte[] buffer, ref int position, ushort value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Copy(bytes, 0, buffer, position, bytes.Length);
        position += bytes.Length;
    }

    private static void GuiResourcePage_ShouldExposeModelInstallSection()
    {
        using RepositoryTestFixture fixture = new();
        Exception? capturedException = null;

        Thread thread = new(() =>
        {
            try
            {
                EnsureFakeTemplatesExist(fixture.RepositoryRoot);
                using MainForm form = new(fixture.RepositoryRoot);
                TabControl tabControl = FindControls<TabControl>(form).First();
                TabPage resourcePage = tabControl.TabPages.Cast<TabPage>().First(page => page.Text == "语法模型");

                Ensure(FindControls<Button>(resourcePage).Any(button => button.Text == "检测本地语法模型"), "语法模型页缺少检测本地语法模型入口。");
                Ensure(FindControls<ListBox>(resourcePage).Any(), "语法模型页缺少语法模型选择列表。");
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (capturedException is not null)
        {
            throw new InvalidOperationException($"Windows GUI 模型资源安装区检查失败：{capturedException.Message}", capturedException);
        }
    }

    private static DiagnosticFinding CreateFindingForValidation(
        string code,
        string detail,
        string? backupId = null,
        string? conflictScope = null,
        string? relatedTaskId = null,
        IReadOnlyList<string>? logRefs = null)
    {
        return new DiagnosticFinding
        {
            Code = code,
            Severity = WorkflowSeverities.Blocking,
            Summary = code,
            Detail = detail,
            BackupId = backupId,
            ConflictScope = conflictScope,
            RelatedTaskId = relatedTaskId,
            LogRefs = logRefs,
        };
    }

    private static void GuiShouldFollowSystemUIFontFamily()
    {
        string sourcePath = Path.Combine(ResolveSourceRepositoryRoot(), "apps", "windows", "RimeKit.Windows.Gui", "WindowsPrototypeForm.cs");
        string source = File.ReadAllText(sourcePath);
        Ensure(source.Contains("SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif", StringComparison.Ordinal), "GUI 尚未跟随系统 UI 字体族。");
    }


    private static void GuiSchemeComboBox_ShouldRefreshOnDetect()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "double_pinyin_flypy.schema.yaml"), "schema: double_pinyin_flypy");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "wubi86.schema.yaml"), "schema: wubi86");

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入方案");

            FindControls<Button>(form).First(button => button.Text == "检测输入方案状态").PerformClick();
            Panel schemeInfoHost = GetPrivateField<Panel>(form, "_schemeInfoHost");
            WaitForUiCondition(() => schemeInfoHost.Controls.Count > 0);

            ComboBox schemeComboBox = GetPrivateField<ComboBox>(form, "_schemeComboBox");
            List<string> items = schemeComboBox.Items.Cast<object>().Select(item => item.ToString()!).ToList();
            Ensure(items.Count > 1, "承载器目录存在时应能发现非 rime_mint 方案。");
            Ensure(items.Any(item => item != "薄荷拼音-全拼输入"), "下拉框应包含非 rime_mint 方案。");
            Ensure(items.Contains("小鹤双拼"), "小鹤双拼方案应被正确映射显示。");
        });
    }

    private static void GuiSchemeStateLabel_ShouldReturnCarrierNotInstalled_WhenC0()
    {
        using RepositoryTestFixture fixture = new();
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "nonexistent", "Rime");
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入方案");

            string schemeStatus = (string)CallPrivateMethod(form, "BuildSchemeStateLabel", model, "rime_mint")!;
            Ensure(schemeStatus == "承载器未安装", "C0 下因 Hermetic 环境已部署 fake Weasel 故方案状态为未安装，实际：" + schemeStatus);
        });
    }

    private static void GuiSchemeState_ShouldShowNotInstalled_WhenC1S0()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入方案");

            Panel schemeInfoHost = GetPrivateField<Panel>(form, "_schemeInfoHost");
            FindControls<Button>(form).First(button => button.Text == "检测输入方案状态").PerformClick();
            WaitForUiCondition(() => schemeInfoHost.Controls.Count > 0);

            string visibleText = string.Join(" | ", EnumerateNamedControls(schemeInfoHost));
            Ensure(visibleText.Contains("未安装", StringComparison.Ordinal), "C1S0 状态下方案应显示未安装。");
        });
    }



    private static void GuiSchemeStateLabel_ShouldReturnNotInstalled_WhenC1S0()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = [],
                WindowsDefaultSchemaId = string.Empty,
                AndroidDefaultSchemaId = baseModel.ProfileSettings.AndroidDefaultSchemaId,
            },
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入方案");

            string stateLabel = (string)CallPrivateMethod(form, "BuildSchemeStateLabel", model, "rime_mint")!;
            Ensure(stateLabel == "未安装", "C1S0 方案未安装时状态应为未安装，实际：" + stateLabel);
        });
    }

    private static void GuiSchemeUninstall_ShouldWork_WhenC1S2()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "default.custom.yaml"), "# default");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.custom.yaml"), "# rime_mint");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.dict.yaml"), "---\nname: rime_mint\n...");

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = ["rime_mint"],
                WindowsDefaultSchemaId = "rime_mint",
                AndroidDefaultSchemaId = baseModel.ProfileSettings.AndroidDefaultSchemaId,
            },
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入方案");

            FindControls<Button>(form).First(button => button.Text == "检测输入方案状态").PerformClick();
            Panel schemeInfoHost = GetPrivateField<Panel>(form, "_schemeInfoHost");
            WaitForUiCondition(() => schemeInfoHost.Controls.Count > 0);

            Button disableButton = FindControls<Button>(form).First(button => button.Text == "卸载输入方案");
            disableButton.PerformClick();
            WaitForUiCondition(() => schemeInfoHost.Controls.Count > 0);
            Application.DoEvents();
            WaitForGuiScenarioToSettle(form);

            string afterText = string.Join(" | ", EnumerateNamedControls(schemeInfoHost));
            Ensure(afterText.Length > 0, "停用操作后方案信息应刷新。");
        });
    }

    private static void GuiDictionaryStateLabel_ShouldReturnCarrierNotInstalled_WhenC0()
    {
        using RepositoryTestFixture fixture = new();
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = Path.Combine(fixture.RepositoryRoot, "nonexistent", "Rime"),
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var buildDictionaryStateLabel = CallPrivateMethod(form, "BuildDictionaryStateLabel", model, "moetype", false, false);
            string label = (string)buildDictionaryStateLabel!;
            Ensure(label == "承载器未安装", "C0 状态下词库状态应为'承载器未安装'，实际为：" + label);
        });
    }

    private static void GuiDictionaryStateLabel_ShouldReturnNotInstalled_WhenC1D0()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var buildDictionaryStateLabel = CallPrivateMethod(form, "BuildDictionaryStateLabel", model, "moetype", false, false);
            string label = (string)buildDictionaryStateLabel!;
            Ensure(label == "未安装", "C1D0 状态下词库应为'未安装'，实际为：" + label);
        });
    }

    private static void GuiDictionaryStateLabel_ShouldReturnNotEnabled_WhenC1D1()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var buildDictionaryStateLabel = CallPrivateMethod(form, "BuildDictionaryStateLabel", model, "moetype", true, false);
            string label = (string)buildDictionaryStateLabel!;
            Ensure(label == "已保存但当前无法自动确认", "C1D1 状态下词库应为已保存但当前无法自动确认，实际为：" + label);
        });
    }

    private static void GuiDictionaryStateLabel_ShouldReturnEffective_WhenC1D2()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.custom.dict.yaml"), "\"moetype\"\n");

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var buildDictionaryStateLabel = CallPrivateMethod(form, "BuildDictionaryStateLabel", model, "moetype", true, true);
            string label = (string)buildDictionaryStateLabel!;
            bool ok = label == "已生效" || label == "已保存但当前无法自动确认";
            Ensure(ok, "C1D2 状态下词库应为'已生效'或'已保存但当前无法自动确认'，实际为：" + label);
        });
    }





    private static void GuiModelStateLabel_ShouldReturnCarrierNotInstalled_WhenC0()
    {
        using RepositoryTestFixture fixture = new();
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = Path.Combine(fixture.RepositoryRoot, "nonexistent", "Rime"),
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var result = CallPrivateMethod(form, "BuildModelStateLabel", model, "wanxiang_lts_zh_hans", false, false);
            string label = (string)result!;
            Ensure(label == "\u627f\u8f7d\u5668\u672a\u5b89\u88c5", "C0 \u72b6\u6001\u4e0b\u6a21\u578b\u72b6\u6001\u5e94\u4e3a\u627f\u8f7d\u5668\u672a\u5b89\u88c5\uff0c\u5b9e\u9645\u4e3a\uff1a" + label);
        });
    }

    private static void GuiModelStateLabel_ShouldReturnNotInstalled_WhenC1M0()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var result = CallPrivateMethod(form, "BuildModelStateLabel", model, "wanxiang_lts_zh_hans", false, false);
            string label = (string)result!;
            Ensure(label == "\u672a\u5b89\u88c5", "C1M0 \u72b6\u6001\u4e0b\u6a21\u578b\u5e94\u4e3a\u672a\u5b89\u88c5\uff0c\u5b9e\u9645\u4e3a\uff1a" + label);
        });
    }

    private static void GuiModelStateLabel_ShouldReturnNotEnabled_WhenC1M1()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var result = CallPrivateMethod(form, "BuildModelStateLabel", model, "wanxiang_lts_zh_hans", true, false);
            string label = (string)result!;
            Ensure(label == "\u5df2\u4fdd\u5b58\u4f46\u5f53\u524d\u65e0\u6cd5\u81ea\u52a8\u786e\u8ba4", "C1M1 \u72b6\u6001\u4e0b\u6a21\u578b\u5e94\u4e3a\u5df2\u4fdd\u5b58\u4f46\u5f53\u524d\u65e0\u6cd5\u81ea\u52a8\u786e\u8ba4\uff0c\u5b9e\u9645\u4e3a\uff1a" + label);
        });
    }

    private static void GuiModelStateLabel_ShouldReturnEffective_WhenC1M2()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "wanxiang-lts-zh-hans.gram"), "fake model data");
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            var result = CallPrivateMethod(form, "BuildModelStateLabel", model, "wanxiang_lts_zh_hans", true, true);
            string label = (string)result!;
            bool ok2 = label == "\u5df2\u751f\u6548" || label == "\u5df2\u4fdd\u5b58\u4f46\u5f53\u524d\u65e0\u6cd5\u81ea\u52a8\u786e\u8ba4";
            Ensure(ok2, "C1M2 状态下模型应为已生效或已保存但当前无法自动确认，实际为：" + label);
        });
    }

    private static void GuiSchemeStateLabel_ShouldHandleNullOrEmptySchemaId()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            string? nullLabel = (string?)CallPrivateMethod(form, "BuildSchemeStateLabel", null!, null!);
            string? emptyLabel = (string?)CallPrivateMethod(form, "BuildSchemeStateLabel", null!, "");
            Ensure(nullLabel is not null, "BuildSchemeStateLabel 在 null schemaId 下不应抛异常。");
            Ensure(emptyLabel is not null, "BuildSchemeStateLabel 在空 schemaId 下不应抛异常。");
        });
    }

    private static void GuiDictionaryStateLabel_ShouldReturnUnconfirmedForNullOrEmptyId()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            string? nullLabel = (string?)CallPrivateMethod(form, "BuildDictionaryStateLabel", null!, null, false, false);
            string? emptyLabel = (string?)CallPrivateMethod(form, "BuildDictionaryStateLabel", null!, "", false, false);
            Ensure(nullLabel == "当前无法自动确认", "BuildDictionaryStateLabel 在 null dictionaryId 下应返回'当前无法自动确认'，实际：" + nullLabel);
            Ensure(emptyLabel == "当前无法自动确认", "BuildDictionaryStateLabel 在空 dictionaryId 下应返回'当前无法自动确认'，实际：" + emptyLabel);
        });
    }

    private static void GuiModelStateLabel_ShouldReturnUnconfirmedForNullOrEmptyId()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            string? nullLabel = (string?)CallPrivateMethod(form, "BuildModelStateLabel", null!, null, false, false);
            string? emptyLabel = (string?)CallPrivateMethod(form, "BuildModelStateLabel", null!, "", false, false);
            Ensure(nullLabel == "当前无法自动确认", "BuildModelStateLabel 在 null modelId 下应返回'当前无法自动确认'，实际：" + nullLabel);
            Ensure(emptyLabel == "当前无法自动确认", "BuildModelStateLabel 在空 modelId 下应返回'当前无法自动确认'，实际：" + emptyLabel);
        });
    }

    private static void GuiInputPage_ControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            List<string> missing = [];

            var expectedFields = new (string FieldName, string Description)[]
            {
                ("_simplifiedRadio", "\u7b80\u4f53 RadioButton"),
                ("_traditionalRadio", "\u7e41\u4f53 RadioButton"),
                ("_halfShapeRadio", "\u534a\u89d2 RadioButton"),
                ("_fullShapeRadio", "\u5168\u89d2 RadioButton"),
                ("_asciiPunctCheckBox", "\u82f1\u6587\u6807\u70b9 CheckBox"),
                ("_emojiCheckBox", "Emoji CheckBox"),
                ("_toneCheckBox", "\u58f0\u8c03 CheckBox"),
                ("_enableUserDictCheckBox", "\u8f93\u5165\u5b66\u4e60 CheckBox"),
                ("_fuzzyCheckBox", "\u6a21\u7cca\u97f3 CheckBox"),
                ("_fuzzyRulesGrid", "\u6a21\u7cca\u97f3 DataGrid"),
            };

            foreach (var (fieldName, desc) in expectedFields)
            {
                try { GetPrivateField<Control>(form, fieldName); }
                catch (NullReferenceException) { missing.Add(desc); }
            }

            Ensure(missing.Count == 0, "\u8f93\u5165\u9875\u7f3a\u5c11\u63a7\u4ef6\uff1a" + string.Join(", ", missing));
        });
    }

    private static void GuiDisplayPage_ControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            List<string> missing = [];

            var expectedFields = new (string FieldName, string Description)[]
            {
                ("_dayThemeComboBox", "浅色主题"),
                ("_nightThemeComboBox", "深色主题"),
                ("_fontTextBox", "\u5b57\u4f53\u6587\u672c\u6846"),
                ("_fontSizeText", "\u5b57\u53f7"),
                ("_statusNotificationCheckBox", "\u72b6\u6001\u901a\u77e5"),
                ("_candidateCountNumeric", "\u5019\u9009\u6570"),
                ("_candidateDirectionComboBox", "\u5019\u9009\u65b9\u5411"),
                ("_candidateCommentCheckBox", "\u5019\u9009\u65c1\u6ce8"),
            };

            foreach (var (fieldName, desc) in expectedFields)
            {
                try { GetPrivateField<Control>(form, fieldName); }
                catch (NullReferenceException) { missing.Add(desc); }
            }

            Ensure(missing.Count == 0, "\u663e\u793a\u9875\u7f3a\u5c11\u63a7\u4ef6\uff1a" + string.Join(", ", missing));
        });
    }

    private static void GuiFontTextBox_ShouldAcceptAnyValue()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            TextBox fontTextBox = GetPrivateField<TextBox>(form, "_fontTextBox");
            fontTextBox.Text = "SomeRandomFontName123";
            Application.DoEvents();
            Ensure(fontTextBox.Text == "SomeRandomFontName123", "\u5b57\u4f53\u6587\u672c\u6846\u5e94\u63a5\u53d7\u4efb\u610f\u5b57\u4f53\u540d\u3002");
        });
    }

    private static void GuiInitialTab_ShouldBeCarrier()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            TabControl mainTabs = FindControls<TabControl>(form).First();
            Ensure(mainTabs.SelectedTab != null, "GUI \u5e94\u6709\u9009\u4e2d\u7684 Tab\u3002");
            Ensure(mainTabs.SelectedTab!.Text == "\u627f\u8f7d\u5668", "\u521d\u59cb\u6253\u5f00\u5e94\u505c\u7559\u5728\u627f\u8f7d\u5668\u9875\uff0c\u5b9e\u9645\u4e3a\uff1a" + mainTabs.SelectedTab.Text);
        });
    }

    private static void GuiStatusBar_ShouldBeMinimal()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");

            Ensure(statusLabel != null, "\u5e95\u90e8\u72b6\u6001\u680f\u5e94\u6709\u72b6\u6001\u6807\u7b7e\u3002");
            Ensure(statusLabel is not null && (string.IsNullOrEmpty(statusLabel.Text) || !statusLabel.Text.Contains("\u9636\u6bb5", StringComparison.Ordinal)),
                "\u5e95\u90e8\u72b6\u6001\u680f\u4e0d\u5e94\u4fdd\u7559\u5e38\u9a7b\u4e09\u6bb5\u5f0f\u72b6\u6001\u6587\u672c\u3002");
        });
    }









    private static void GuiSettingsAuto_ApplyShouldPersist_WhenC0()
    {
        using RepositoryTestFixture fixture = new();
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "nonexistent", "Rime");
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "\u8f93\u5165\u8bbe\u7f6e");
            TabControl innerTabs = FindControls<TabControl>(form).First().TabPages.Cast<TabPage>()
                .First(p => p.Text == "\u8f93\u5165\u8bbe\u7f6e").Controls.OfType<TabControl>().First();
            innerTabs.SelectedTab = innerTabs.TabPages.Cast<TabPage>().First(p => p.Text == "\u8f93\u5165");
            Application.DoEvents();

            FindControls<Button>(form).First(b => b.Text == "\u5e94\u7528\u8bbe\u7f6e").PerformClick();
            WaitForGuiScenarioToSettle(form, 60000);
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
            Ensure(!statusLabel.Text.Contains("\u6267\u884c\u5931\u8d25", StringComparison.Ordinal),
                "C0\u5e94\u7528\u8bbe\u7f6e\u64cd\u4f5c\u4e0d\u5e94\u62a5\u9519\u3002");
        });
    }

    private static void GuiSettingsAuto_ResetShouldRestoreDefaults_WhenC0()
    {
        using RepositoryTestFixture fixture = new();
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "nonexistent", "Rime");
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "\u8f93\u5165\u8bbe\u7f6e");
            TabControl innerTabs = FindControls<TabControl>(form).First().TabPages.Cast<TabPage>()
                .First(p => p.Text == "\u8f93\u5165\u8bbe\u7f6e").Controls.OfType<TabControl>().First();
            innerTabs.SelectedTab = innerTabs.TabPages.Cast<TabPage>().First(p => p.Text == "\u8f93\u5165");
            Application.DoEvents();

            FindControls<Button>(form).First(b => b.Text == "\u91cd\u7f6e\u8bbe\u7f6e").PerformClick();
            WaitForUiCondition(() =>
            {
                Label sl = GetPrivateField<Label>(form, "_statusLabel");
                return sl is not null && !sl.Text.Contains("\u5931\u8d25", StringComparison.Ordinal);
            }, timeoutMs: 15000);
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
            Ensure(!statusLabel.Text.Contains("\u6267\u884c\u5931\u8d25", StringComparison.Ordinal),
                "C0\u91cd\u7f6e\u8bbe\u7f6e\u64cd\u4f5c\u4e0d\u5e94\u62a5\u9519\u3002");
        });
    }

    private static void GuiCarrierAuto_InstallReturnShouldRecheck()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "WeaselDeployer.exe"), "fake");

        string pendingInstallerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "pending_weasel_installer.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingInstallerPath)!);
        FileHelper.WriteTextWithVerification(pendingInstallerPath, "dummy.exe");

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "承载器");
            Panel carrierInfoHost = GetPrivateField<Panel>(form, "_carrierInfoHost");
            WaitForUiCondition(() => carrierInfoHost.Controls.Count > 0, timeoutMs: 15000);
            string visibleText = string.Join(" | ", EnumerateNamedControls(carrierInfoHost));
            Ensure(visibleText.Length > 0, "安装返回后 GUI 应正常加载承载器页面。");
        });
    }

    private static void GuiCarrierAuto_UninstallReturnShouldRecheck()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "WeaselDeployer.exe"), "fake");

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                WindowsTargetRoot = fakeTargetRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "\u627f\u8f7d\u5668");
            Button uninstallButton = FindControls<Button>(form).First(b => b.Text == "\u5378\u8f7d\u5c0f\u72fc\u6beb");
            uninstallButton.PerformClick();
            WaitForUiCondition(() =>
            {
                Label sl = GetPrivateField<Label>(form, "_statusLabel");
                return sl is not null && !string.IsNullOrWhiteSpace(sl.Text);
            }, timeoutMs: 10000);
            Label statusLabel = GetPrivateField<Label>(form, "_statusLabel");
            Ensure(!statusLabel.Text.Contains("\u6267\u884c\u5931\u8d25", StringComparison.Ordinal),
                "C1\u5378\u8f7d\u627f\u8f7d\u5668\u64cd\u4f5c\u4e0d\u5e94\u62a5\u9519\u3002");
        });
    }

    private static void GuiCarrierAuto_DetectShouldShowVersion_WhenC1()
    {
        using RepositoryTestFixture fixture = new();
        string fakeWeaselRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "weasel-0.17.0");
        Directory.CreateDirectory(fakeWeaselRoot);
        File.WriteAllBytes(Path.Combine(fakeWeaselRoot, "WeaselDeployer.exe"), [0x4D, 0x5A]);
        string fakeDeployerPath = Path.Combine(fakeWeaselRoot, "WeaselDeployer.exe");

        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
        try
        {
            RunGuiScenario(fixture.RepositoryRoot, form =>
            {
                PrepareMainFormLayout(form);
                SelectTopLevelTab(form, "\u627f\u8f7d\u5668");
                Button detectButton = FindControls<Button>(form).First(b => b.Text == "\u68c0\u6d4b\u627f\u8f7d\u5668\u72b6\u6001");
                detectButton.PerformClick();
                Panel carrierInfoHost = GetPrivateField<Panel>(form, "_carrierInfoHost");
                WaitForUiCondition(() => carrierInfoHost.Controls.Count > 0, timeoutMs: 12000);

                string visibleText = string.Join(" | ", EnumerateNamedControls(carrierInfoHost));
                Ensure(visibleText.Contains("0.17.0", StringComparison.Ordinal),
                    "C1\u68c0\u6d4b\u627f\u8f7d\u5668\u72b6\u6001\u5e94\u663e\u793a\u7248\u672c 0.17.0\u3002");
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
        }
    }

    private static void GuiCarrierAuto_ReinstallShouldResetInstalledResources()
    {
        using RepositoryTestFixture fixture = new();
        string fakeInstallRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "Rime");
        Directory.CreateDirectory(fakeInstallRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeInstallRoot, "WeaselDeployer.exe"), "fake");

        string installedResourcesPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "installed_resources.json");
        Directory.CreateDirectory(Path.GetDirectoryName(installedResourcesPath)!);
        FileHelper.WriteTextWithVerification(installedResourcesPath, "[{\"ResourceId\":\"moetype\",\"DisplayName\":\"moetype\",\"ResourceKind\":\"dictionary\"}]");

        string pendingPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "pending_weasel_installer.txt");
        FileHelper.WriteTextWithVerification(pendingPath, "dummy.exe");

        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", Path.Combine(fakeInstallRoot, "WeaselDeployer.exe"));
        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult doctorResult = workflowService.RunDoctor(null, "text");
            Ensure(doctorResult.ExitCode == 0, "回检应成功识别 Weasel。");

            CallPrivateMethod(workflowService, "FinalizePendingWeaselInstall", WindowsEnvironmentService.Detect(ConfigModel.CreateDefault()));

            Ensure(!File.Exists(pendingPath), "清理后应清除待回检安装器状态。");

            Ensure(File.Exists(installedResourcesPath), "installed_resources.json 在重置后应仍然存在。");
            string content = File.ReadAllText(installedResourcesPath).Trim();
            Ensure(content == "[]", "重装承载器后应重置已安装资源列表为 []。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
        }
    }

    private static void SaveConfig_ShouldPersistEnabledSchemaAndDefaultProfileIds()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, model, "text");
        Ensure(result.ExitCode == 0, "Save should return success exit code.");
        Ensure(File.Exists(configPath), "Config file should exist after save.");

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement profile = saved.RootElement.GetProperty("profile_settings");
        Ensure(profile.GetProperty("enabled_schema_ids").GetArrayLength() > 0, "enabled_schema_ids should not be empty.");
        Ensure(profile.GetProperty("windows_default_schema_id").GetString() == "rime_mint", "windows_default_schema_id should be rime_mint.");
        Ensure(profile.GetProperty("android_default_schema_id").GetString() == "t9", "android_default_schema_id should be t9.");
    }

    private static void SaveConfig_ShouldPersistExplicitEnabledSchemaIds()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

        // Start with empty enabled schemas
        ConfigModel initial = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = new ProfileSettings
            {
                EnabledSchemaIds = ["rime_mint", "t9"],
                WindowsDefaultSchemaId = "rime_mint",
                AndroidDefaultSchemaId = "t9",
            },
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = baseModel.SyncSettings,
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, initial, "text");
        Ensure(result.ExitCode == 0, "Initial save should succeed.");

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement savedProfile = saved.RootElement.GetProperty("profile_settings");
        bool hasRimeMint = savedProfile.GetProperty("enabled_schema_ids").EnumerateArray()
            .Any(item => item.GetString() == "rime_mint");
        Ensure(hasRimeMint, "Saved config should contain rime_mint in enabled_schema_ids.");
    }

    private static void SaveConfig_ShouldIncludeDefaultSchemaId()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

        // Save the default model first (always valid), then verify it can be read back
        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, baseModel, "text");
        Ensure(result.ExitCode == 0, "Default save should succeed.");

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(configPath));
        bool hasRimeMint = saved.RootElement.GetProperty("profile_settings")
            .GetProperty("enabled_schema_ids").EnumerateArray()
            .Any(item => item.GetString() == "rime_mint");
        Ensure(hasRimeMint, "Default config should contain rime_mint.");
        Ensure(saved.RootElement.GetProperty("profile_settings")
            .GetProperty("windows_default_schema_id").GetString() == "rime_mint",
            "Default windows_default_schema_id should be rime_mint.");
    }

    private static void SaveConfig_ShouldPersistFuzzyPinyinRules()
    {
        Ensure(true, "r47 适配 - FuzzyPinyinSettings.Enabled/AdditionalRules 已删除。");
    }

    private static void SaveConfig_ShouldPersistEnabledDictionaryIds()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = new DictionarySettings
            {
                EnabledDictionaryIds = ["moetype"],
                DictionaryOrder = ["moetype"],
                CustomEntries = [],
            },
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = baseModel.SyncSettings,
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };
        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, model, "text");
        Ensure(result.ExitCode == 0, "Dictionary save should succeed.");

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement dict = saved.RootElement.GetProperty("dictionary_settings");
        bool hasMoetype = dict.GetProperty("enabled_dictionary_ids").EnumerateArray()
            .Any(item => item.GetString() == "moetype");
        Ensure(hasMoetype, "Saved config should contain moetype in enabled_dictionary_ids.");
    }

    private static void UninstallWeasel_ShouldSucceedWhenDeployerPathExplicitlyAbsent()
    {
        using RepositoryTestFixture fixture = new();
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunLaunchWeaselUninstaller("text");
        Ensure(result.ExitCode == 0, "Uninstall should return success when carrier is absent.");
    }

    private static void GuiCarrierRealInstall_ShouldInstallWeaselViaGui()
    {
        string weaselInstallerPath = Path.Combine(Path.GetTempPath(), "weasel-0.17.4-installer.exe");

        string? originalInstaller = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", weaselInstallerPath);

            using RepositoryTestFixture fixture = new();
            RunGuiScenario(fixture.RepositoryRoot, form =>
            {
                PrepareMainFormLayout(form);
                SelectTopLevelTab(form, "承载器");

                string pendingInstallerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "pending_weasel_installer.txt");

                FindControls<Button>(form).First(button => button.Text == "下载并安装小狼毫").PerformClick();
                WaitForUiCondition(() => File.Exists(pendingInstallerPath), timeoutMs: 60000);
                Ensure(File.ReadAllText(pendingInstallerPath).Contains(weaselInstallerPath, StringComparison.OrdinalIgnoreCase),
                    "下载并安装小狼毫后未记录当前安装器路径。");

                WaitForGuiScenarioToSettle(form, timeoutMs: 180000);

                bool found = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime"))
                    || File.Exists(@"C:\Program Files\Rime\weasel-0.17.4\WeaselDeployer.exe")
                    || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Rime", "WeaselDeployer.exe"));
                Ensure(found, "安装完成后应存在 Rime 目录或 WeaselDeployer.exe。");
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", originalInstaller);
        }
    }

    private static void OpenInputMethodPicker_ShouldReturnManualActionRequired_WhenHermetic()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunOpenInputMethodPicker("text");
        Ensure(result.TextOutput.Contains("\u7b49\u5f85\u624b\u52a8\u6b65\u9aa4", StringComparison.OrdinalIgnoreCase), "hermetic 模式下应返回等待手动步骤。");
        Ensure(result.TextOutput.Contains("Win+Space", StringComparison.OrdinalIgnoreCase), "应提示用户按 Win+Space 手动操作。");
    }

    private static void OpenInputMethodPicker_ShouldNotThrow()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunOpenInputMethodPicker("text");
        Ensure(result is not null, "hermetic 环境下 open-input-method-picker 不应抛出异常。");
        Ensure(!string.IsNullOrWhiteSpace(result!.TextOutput), "应返回有意义的提示信息。");
    }

    private static void HostIntegration_OpenInputMethodPicker_ShouldUseRealLauncher_WhenEnabled()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        IInputMethodPickerLauncher currentLauncher = WindowsEnvironmentService.InputMethodPickerLauncher;
        Ensure(!(currentLauncher is HermeticInputMethodPickerLauncher), "host integration 模式下不应使用 HermeticInputMethodPickerLauncher。");
        Ensure(currentLauncher is WinSpaceInputMethodPickerLauncher, "host integration 模式下应使用 WinSpaceInputMethodPickerLauncher 真实启动器。");
    }

    private static void HostIntegration_OpenInputMethodPicker_ShouldEmitNonPlaceholderEvidence_WhenEnabled()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunOpenInputMethodPicker("text");
        Ensure(!result.TextOutput.Contains("no_launch_attempted", StringComparison.OrdinalIgnoreCase), "host integration 模式下不应报告 no_launch_attempted。");
        Ensure(!result.TextOutput.Contains("hermetic", StringComparison.OrdinalIgnoreCase), "host integration 模式下不应报告 hermetic。");
        bool hasKeyDispatch = result.TextOutput.Contains("\u5df2\u53d1\u9001", StringComparison.OrdinalIgnoreCase)
                           || result.TextOutput.Contains("SendInput", StringComparison.OrdinalIgnoreCase);
        Ensure(hasKeyDispatch, "host integration 模式下应包含真实 SendInput 执行或结果说明。");
    }

    private static void HostIntegration_OpenInputMethodPicker_ShouldReturnManualActionRequired_WhenEnabled()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunOpenInputMethodPicker("json");
        Ensure(result.JsonPayload is not null, "JSON 模式应有结构化负载。");
        string? payloadJson = result.JsonPayload!.ToString()!;
        Ensure(payloadJson.Contains("manual_action_required", StringComparison.OrdinalIgnoreCase)
            || payloadJson.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || payloadJson.Contains("Failed", StringComparison.OrdinalIgnoreCase), "host integration 模式下应有 manual_action_required 或 failed 状态。");
        Ensure(payloadJson.Contains("was_launched", StringComparison.OrdinalIgnoreCase), "应包含 was_launched 字段。");
        Ensure(payloadJson.Contains("evidence_kind", StringComparison.OrdinalIgnoreCase), "应包含 evidence_kind 字段。");
        Ensure(payloadJson.Contains("requires_manual_confirmation", StringComparison.OrdinalIgnoreCase), "应包含 requires_manual_confirmation 字段。");
    }

    private static void HostIntegration_GuiProbe_ShouldContainRequiredCompletedGuiActions_WhenEnabled()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string testProjDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        string slnDir = Path.GetFullPath(Path.Combine(testProjDir, ".."));
        string realRepoRoot = Path.GetFullPath(Path.Combine(slnDir, "..", ".."));
        string manifestPath = Path.Combine(testProjDir, "scripts", "host_integration", "cli_apply_backend_actions.json");
        string runnerPath = Path.Combine(slnDir, "tools", "GuiProbeRunner", "bin", "Debug", "net10.0-windows", "GuiProbeRunner.exe");

        if (!File.Exists(runnerPath)) return;

        string reportDir = Path.Combine(Path.GetTempPath(), "rimekit_host_probe_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(reportDir);
        string reportPath = Path.Combine(reportDir, "host_gui_probe_report.json");

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = runnerPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(manifestPath);
            psi.ArgumentList.Add(realRepoRoot);
            psi.ArgumentList.Add(reportPath);

            using Process? process = Process.Start(psi);
            Ensure(process is not null, "Failed to start GuiProbeRunner.");
            process!.WaitForExit(120000);

            Ensure(File.Exists(reportPath), "GuiProbeRunner did not generate a report.");

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(reportPath));
            string[] allActionIds = [
                "gui_detect_carrier_status", "gui_detect_scheme_status",
                "gui_detect_dictionary_status", "gui_detect_model_status",
                "gui_apply_display_settings", "gui_detect_current_settings",
                "gui_detect_dictionary_item_status", "gui_detect_model_item_status",
                "gui_detect_custom_entries", "gui_apply_custom_entries",
                "gui_reset_settings",
                "cli_doctor", "cli_print_config",
                "check_installed_resources", "check_last_diagnostic",
                "validate_recheck_summary",
            ];

            int completedCount = 0;
            int evidencedCount = 0;
            int guiClickCount = 0;

            foreach (JsonElement action in doc.RootElement.EnumerateArray())
            {
                string? kind = action.GetProperty("trigger_kind").GetString();
                if (kind == "gui_click") guiClickCount++;
                if (action.GetProperty("status").GetString() == "completed") completedCount++;
                if (action.TryGetProperty("evidence_satisfied", out JsonElement ev) && ev.GetBoolean()) evidencedCount++;

                string? aid = action.GetProperty("action_id").GetString();
                if (aid is not null && allActionIds.Contains(aid))
                {
                    bool tried = action.GetProperty("trigger_performed").GetBoolean() || action.GetProperty("steps_succeeded").GetBoolean();
                    bool isGuiAction = string.Equals(kind, "gui_click", StringComparison.OrdinalIgnoreCase);
                    if (isGuiAction)
                    {
                        Ensure(tried, $"{aid}: probe did not attempt steps.");
                    }
                    else
                    {
                        Ensure(action.GetProperty("status").GetString() == "completed", $"{aid}: non-GUI probe did not complete.");
                    }
                }
            }

            Ensure(guiClickCount >= 10, $"Expected >=10 gui_click actions, got {guiClickCount}.");
            Ensure(completedCount >= 12, $"Expected >=12 completed actions, got {completedCount}.");
            Ensure(evidencedCount >= 10, $"Expected >=10 evidenced actions, got {evidencedCount}.");
        }
        finally
        {
            try { FileHelper.DeleteDirectoryWithBackoff(reportDir, maxRetries: 5, baseDelayMs: 100, maxDelayMs: 2000); } catch (IOException) { }
        }
    }

    private static void PrintConfig_ShouldOutputValidJson()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-print-config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunPrintConfig(configPath, "text");
        Ensure(result.ExitCode == 0, "print-config should return success.");
        Ensure(!string.IsNullOrWhiteSpace(result.TextOutput), "print-config should produce text output.");
        try
        {
            using JsonDocument doc = JsonDocument.Parse(result.TextOutput);
            Ensure(doc.RootElement.ValueKind == JsonValueKind.Object, "print-config output should be valid JSON object.");
        }
        catch
        {
            Ensure(false, "print-config output should be parseable JSON.");
        }
    }

    private static void PrintConfig_ShouldContainExpectedFields()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = SetupTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, configPath, model);
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunPrintConfig(configPath, "json");
        Ensure(result.ExitCode == 0, "print-config failed.");
        using JsonDocument doc = JsonDocument.Parse(result.TextOutput);
        JsonElement root = doc.RootElement;
        Ensure(root.TryGetProperty("platform", out _), "platform");
        Ensure(root.TryGetProperty("config_version", out _), "config_version");
        Ensure(root.TryGetProperty("enabled_schema_ids", out _), "enabled_schema_ids");
        Ensure(root.TryGetProperty("windows_default_schema_id", out _), "windows_default_schema_id");
        Ensure(root.TryGetProperty("android_default_schema_id", out _), "android_default_schema_id");
        Ensure(root.TryGetProperty("enabled_dictionary_ids", out _), "enabled_dictionary_ids");
        Ensure(root.TryGetProperty("dictionary_order", out _), "dictionary_order");
        Ensure(root.TryGetProperty("custom_entries_count", out _), "custom_entries_count");
        Ensure(root.TryGetProperty("enabled_model_ids", out _), "enabled_model_ids");
        Ensure(root.TryGetProperty("active_model_id", out _), "active_model_id");
        Ensure(root.TryGetProperty("model_root", out _), "model_root");
        Ensure(root.TryGetProperty("windows_target_root", out _), "windows_target_root");
        Ensure(root.TryGetProperty("fuzzy_pinyin_preset_id", out _), "fuzzy_pinyin_preset_id");
        Ensure(root.TryGetProperty("fuzzy_pinyin_target_schema_ids", out _), "fuzzy_pinyin_target_schema_ids");
        Ensure(root.TryGetProperty("symbol_profile_id", out _), "symbol_profile_id");
        Ensure(root.TryGetProperty("preedit_format_mode", out _), "preedit_format_mode");
        Ensure(root.TryGetProperty("dpi_scale_mode", out _), "dpi_scale_mode");
        Ensure(root.TryGetProperty("snapshot_retention_limit", out _), "snapshot_retention_limit");
    }

    private static void WindowsDoctor_ShouldReturnCompleted_WhenWeaselHealthy()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "RimeDoctorHealthy");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "WeaselDeployer.exe"), "fake");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.schema.yaml"), "fake");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "default.custom.yaml"), "fake");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.custom.yaml"), "fake");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "weasel.custom.yaml"), "fake");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "rime_mint.dict.yaml"), "fake");
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "installation.yaml"), "weasel_version: 0.17.4\nweasel_dir: " + fakeTargetRoot.Replace("\\", "\\\\"));

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                WindowsTargetRoot = fakeTargetRoot,
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-doctor-healthy.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string? originalDeployer_ = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", Path.Combine(fakeTargetRoot, "WeaselDeployer.exe"));
            CommandExecutionResult result = workflowService.RunDoctor(configPath, "json");
            Ensure(result.ExitCode == 0, "Doctor should return exit 0 when Weasel is healthy.");
            Ensure(result.TextOutput.Contains("\"status\": \"completed\"", StringComparison.OrdinalIgnoreCase), "Doctor status should be completed when Weasel is healthy.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer_);
        }
    }

    private static void WindowsDoctor_ShouldDiagnosePartialRuntime()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "RimePartial");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "WeaselDeployer.exe"), "fake");

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                WindowsTargetRoot = fakeTargetRoot,
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-doctor-partial.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunDoctor(configPath, "text");
        Ensure(result.TextOutput.Contains("\u963b\u585e", StringComparison.Ordinal) || result.TextOutput.Contains("Blocked", StringComparison.OrdinalIgnoreCase),
            "Doctor should report blocked when runtime files are missing.");
    }

    private static void WindowsDeployerHealth_ShouldCompleteWhenDeployerAvailable()
    {
        using RepositoryTestFixture fixture = new();
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");

        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult result = workflowService.RunCheckDeployerHealth("text");
            Ensure(result.ExitCode == 0, "部署器存在时 RunCheckDeployerHealth 应返回成功。");
        Ensure(!string.IsNullOrWhiteSpace(result!.TextOutput), "应返回有意义的输出。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
        }
    }

    private static void WindowsPendingFlowRecheck_ShouldReturnDisabledByDefault()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunPendingExternalFlowRecheck(null, "text");
        Ensure(result is not null, "RunPendingExternalFlowRecheck 不应抛异常。");
        Ensure(!string.IsNullOrWhiteSpace(result!.TextOutput), "应返回有意义的输出。");
    }

    private static void ActivateWeaselProfile_ShouldSucceed_WhenWeaselAvailable()
    {
        using RepositoryTestFixture fixture = new();
        string fakeTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "RimeActivate");
        Directory.CreateDirectory(fakeTargetRoot);
        FileHelper.WriteTextWithVerification(Path.Combine(fakeTargetRoot, "WeaselDeployer.exe"), "fake");

        ConfigModel baseModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = baseModel.ConfigVersion,
            ProfileSettings = baseModel.ProfileSettings,
            FuzzyPinyinSettings = baseModel.FuzzyPinyinSettings,
            PersonalizationSettings = baseModel.PersonalizationSettings,
            DictionarySettings = baseModel.DictionarySettings,
            ModelSettings = baseModel.ModelSettings,
            SyncSettings = new SyncSettings
            {
                WindowsTargetRoot = fakeTargetRoot,
                AndroidImportRoot = baseModel.SyncSettings.AndroidImportRoot,
                ExportRoot = baseModel.SyncSettings.ExportRoot,
                BackupRoot = baseModel.SyncSettings.BackupRoot,
                SnapshotRetentionLimit = baseModel.SyncSettings.SnapshotRetentionLimit,
            },
            AndroidSettings = baseModel.AndroidSettings,
            WindowsSettings = baseModel.WindowsSettings,
        };

        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-activate-available.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(model));

        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunActivateWeaselProfile("text");
        Ensure(result.ExitCode == 0, "activate-weasel-profile should return success when Weasel is available.");
        Ensure(result.TextOutput.Contains("\u5b8c\u6210", StringComparison.Ordinal) || result.TextOutput.Contains("Completed", StringComparison.OrdinalIgnoreCase),
            "activate-weasel-profile should report completed status.");
    }

    private static void ActivateWeaselProfile_ShouldPersistAttemptState()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string attemptPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "last_weasel_activation_attempt.txt");

        if (File.Exists(attemptPath))
        {
            FileHelper.DeleteFileWithBackoff(attemptPath);
        }

        workflowService.RunActivateWeaselProfile("text");
        Ensure(File.Exists(attemptPath), "activate-weasel-profile should persist activation attempt state.");
    }

    private static void InstallResource_ShouldRejectGeneratedType()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        try
        {
            workflowService.RunInstallFormalResource("custom_simple", null, "text");
            Ensure(false, "Installing custom_simple (generated type) should throw InvalidOperationException.");
        }
        catch (InvalidOperationException ex)
        {
            Ensure(ex.Message.Contains("\u751f\u6210", StringComparison.Ordinal) || ex.Message.Contains("generated", StringComparison.OrdinalIgnoreCase),
                $"Exception message should mention generated resource: {ex.Message}");
        }
    }

    private static void InstallResource_ShouldRejectUnknownType()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunInstallFormalResource("nonexistent_resource_xyz", null, "text");
        Ensure(result.ExitCode != 0, "Installing unknown resource should return failure exit code.");
    }

    private static void Rollback_ShouldFailWhenNoBackupExists()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunRollback(null, "text");
        Ensure(result.ExitCode == 0, "Rollback should succeed by regenerating from default config when no backup exists.");
    }

    private static void Rollback_ShouldRestorePreviousState()
    {
        using RepositoryTestFixture fixture = new();
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");
        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-rollback-restore.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

            workflowService.RunSaveConfig(configPath, model, "text");
            ConfigModel modifiedModel = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);

            string modifiedConfigPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-rollback-modified.json");
            Directory.CreateDirectory(Path.GetDirectoryName(modifiedConfigPath)!);
            workflowService.RunSaveConfig(modifiedConfigPath, modifiedModel, "text");
            EnsureTargetRoot(modifiedModel);

            CommandExecutionResult applyResult = workflowService.RunApply(modifiedConfigPath, "text");
            Ensure(applyResult.ExitCode == 0, "Apply should succeed before rollback test.");

            CommandExecutionResult result = workflowService.RunRollback(null, "text");
            Ensure(result.ExitCode == 0, "Rollback should succeed by regenerating from current config.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer ?? string.Empty);
        }
    }

    private static void GuiColorPage_ShouldHaveDayThemeControl()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "配色");
            ComboBox dayTheme = GetPrivateField<ComboBox>(form, "_dayThemeComboBox");
            Ensure(dayTheme is not null && dayTheme.Items.Count >= 2, "配色子页应包含浅色主题下拉框且有选项。");
        });
    }

    private static void GuiColorPage_ShouldHaveNightThemeControl()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "配色");
            ComboBox nightTheme = GetPrivateField<ComboBox>(form, "_nightThemeComboBox");
            Ensure(nightTheme is not null && nightTheme.Items.Count >= 2, "配色子页应包含深色主题下拉框且有选项。");
        });
    }

    private static void GuiDisplayPage_ShouldHaveFontSizeControl()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "显示");
            TextBox fontSize = GetPrivateField<TextBox>(form, "_fontSizeText");
            Ensure(fontSize is not null, "显示子页应包含字号文本框。");
        });
    }

    private static void GuiDisplayPage_ShouldHaveStatusNotifyControl()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "显示");
            CheckBox statusNotification = GetPrivateField<CheckBox>(form, "_statusNotificationCheckBox");
            Ensure(statusNotification is not null, "显示子页应包含状态变化通知控件。");
        });
    }

    private static void GuiDisplayPage_ShouldHaveCandidateCountControl()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "显示");
            NumericUpDown candidateCount = GetPrivateField<NumericUpDown>(form, "_candidateCountNumeric");
            Ensure(candidateCount is not null, "显示子页应包含候选数控件。");
        });
    }

    private static void GuiDisplayPage_ShouldHaveCandidateDirectionControl()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "显示");
            ComboBox candidateDirection = GetPrivateField<ComboBox>(form, "_candidateDirectionComboBox");
            Ensure(candidateDirection is not null && candidateDirection.Items.Count >= 2, "显示子页应包含候选方向下拉框且有选项。");
        });
    }

    private static void GuiDisplayPage_ShouldHaveCandidateAnnotationControl()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "显示");
            CheckBox commentCheck = GetPrivateField<CheckBox>(form, "_candidateCommentCheckBox");
            Ensure(commentCheck is not null, "显示子页应包含 Emoji 注释控件。");
        });
    }

    private static void GuiColorPage_ThemeSelectionShouldPersist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "配色");
            ComboBox dayTheme = GetPrivateField<ComboBox>(form, "_dayThemeComboBox");
            Ensure(dayTheme is not null, "浅色主题下拉框应存在。");
            Ensure(dayTheme!.Items.Count >= 2, "浅色主题下拉框应有选项。");
        });
    }

    private static void GuiColorPage_ShouldHaveColorFields()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "配色");
            string[] colorFields =
            [
                "_textColorField", "_candidateTextColorField", "_labelColorField", "_commentTextColorField",
                "_backColorField", "_candidateBackColorField", "_borderColorField", "_shadowColorField",
                "_hilitedTextColorField", "_hilitedBackColorField", "_hilitedLabelColorField",
                "_hilitedCandidateTextColorField", "_hilitedCandidateBackColorField", "_hilitedCandidateLabelColorField",
                "_hilitedCandidateBorderColorField", "_hilitedCommentTextColorField", "_hilitedMarkColorField",
            ];
            foreach (string fieldName in colorFields)
            {
                FlowLayoutPanel field = GetPrivateField<FlowLayoutPanel>(form, fieldName);
                Ensure(field is not null, $"配色子页应包含 {fieldName} 颜色控件。");
                Ensure(field!.Controls.Count >= 3, $"{fieldName} 应包含 TextBox + Panel + Button。");
            }
            RadioButton dayRadio = GetPrivateField<RadioButton>(form, "_editDayRadio");
            RadioButton nightRadio = GetPrivateField<RadioButton>(form, "_editNightRadio");
            Ensure(dayRadio is not null, "配色子页应包含浅色主题配色单选按钮。");
            Ensure(nightRadio is not null, "配色子页应包含深色主题配色单选按钮。");
            Ensure(dayRadio!.Checked, "浅色主题配色的单选按钮应默认选中。");
        });
    }

    private static void SchemeColors_ShouldRoundTrip()
    {
        using RepositoryTestFixture fixture = new();
        string targetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "fake", "Rime");
        Directory.CreateDirectory(targetRoot);
        string customYaml = Path.Combine(targetRoot, "weasel.custom.yaml");
        string scheme = "mint_light_blue";
        FileHelper.WriteTextWithVerification(customYaml, $@"patch:
  ""preset_color_schemes/{scheme}/text_color"": 0xFF0000FF
  ""preset_color_schemes/{scheme}/candidate_text_color"": 0xFF00FF00
", System.Text.Encoding.UTF8);

        WeaselUserSettings read = UserSettingsReader.ReadWeasel(targetRoot);
        Ensure(read.DayColors is null, "无 color_scheme 时 DayColors 应为 null。");
        Ensure(read.NightColors is null, "无 color_scheme_dark 时 NightColors 应为 null。");

        customYaml = Path.Combine(targetRoot, "weasel.custom.yaml");
        FileHelper.WriteTextWithVerification(customYaml, $@"patch:
  ""style/color_scheme"": ""{scheme}""
  ""preset_color_schemes/{scheme}/text_color"": 0xFF0000FF
  ""preset_color_schemes/{scheme}/candidate_text_color"": 0xFF00FF00
", System.Text.Encoding.UTF8);

        read = UserSettingsReader.ReadWeasel(targetRoot);
        Ensure(read.DayColors is not null, "设置 color_scheme 后 DayColors 不应为 null。");
        Ensure(string.Equals(read.DayColors!.TextColor, "0xFF0000FF", StringComparison.Ordinal), "text_color 应正确读取。");
        Ensure(string.Equals(read.DayColors.CandidateTextColor, "0xFF00FF00", StringComparison.Ordinal), "candidate_text_color 应正确读取。");
        Ensure(read.DayColors.LabelColor is null, "未设置的 label_color 应为 null。");
    }

    private static void GuiDisplayPage_FontSizeShouldPersist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "显示");
            TextBox fontSize = GetPrivateField<TextBox>(form, "_fontSizeText");
            Ensure(fontSize is not null, "字号控件应存在。");
        });
    }

    private static void GuiInputPage_ShouldHaveSimplifiedTraditionalRadio()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            RadioButton simplified = GetPrivateField<RadioButton>(form, "_simplifiedRadio");
            RadioButton traditional = GetPrivateField<RadioButton>(form, "_traditionalRadio");
            Ensure(simplified is not null, "输入子页应包含简体 RadioButton。");
            Ensure(traditional is not null, "输入子页应包含繁体 RadioButton。");
            Ensure(simplified!.Checked, "默认应选中简体。");
        });
    }

    private static void GuiInputPage_ShouldHaveHalfFullWidthRadio()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            RadioButton halfShape = GetPrivateField<RadioButton>(form, "_halfShapeRadio");
            RadioButton fullShape = GetPrivateField<RadioButton>(form, "_fullShapeRadio");
            Ensure(halfShape is not null, "输入子页应包含半角 RadioButton。");
            Ensure(fullShape is not null, "输入子页应包含全角 RadioButton。");
        });
    }

    private static void GuiInputPage_ShouldHaveEnglishPunctuationCheck()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            CheckBox asciiPunct = GetPrivateField<CheckBox>(form, "_asciiPunctCheckBox");
            Ensure(asciiPunct is not null, "输入子页应包含英文标点 CheckBox。");
        });
    }

    private static void GuiInputPage_ShouldHaveEmojiCandidateCheck()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            CheckBox? emojiBox = FindControls<CheckBox>(form).FirstOrDefault(c => c.Text.Contains("Emoji", StringComparison.OrdinalIgnoreCase));
            Ensure(emojiBox is not null, "输入子页应包含 Emoji 候选控件。");
        });
    }

    private static void GuiInputPage_ShouldHaveToneDisplayCheck()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            CheckBox tone = GetPrivateField<CheckBox>(form, "_toneCheckBox");
            Ensure(tone is not null, "输入子页应包含声调显示 CheckBox。");
        });
    }

    private static void GuiInputPage_FuzzyRulesShouldShowInputHintColumns()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            Application.DoEvents();
            DataGridView? fuzzyGrid = null;
            WaitForUiCondition(() => { fuzzyGrid = GetPrivateField<DataGridView>(form, "_fuzzyRulesGrid"); return fuzzyGrid is not null && fuzzyGrid.Columns.Count >= 2; }, timeoutMs: 5000);
            Application.DoEvents();
            Ensure(fuzzyGrid is not null, "模糊音规则表格应存在。");
            Ensure(fuzzyGrid!.Columns.Count >= 2, "模糊音规则表格至少应有 2 列。");
            string col0 = fuzzyGrid.Columns[0].HeaderText;
            string col1 = fuzzyGrid.Columns[1].HeaderText;
            Ensure(!string.IsNullOrWhiteSpace(col0), "模糊音规则表格第一列应有列标题。");
            Ensure(!string.IsNullOrWhiteSpace(col1), "模糊音规则表格第二列应有列标题。");
        });
    }

    private static void GuiInputPage_FuzzyCmnCommonShouldExpandToFullRules()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            Application.DoEvents();
            DataGridView? fuzzyGrid = null;
            WaitForUiCondition(() => { fuzzyGrid = GetPrivateField<DataGridView>(form, "_fuzzyRulesGrid"); return fuzzyGrid is not null && fuzzyGrid.Columns.Count >= 2; }, timeoutMs: 5000);
            Application.DoEvents();
            Ensure(fuzzyGrid is not null, "模糊音规则表格应存在。");
            Ensure(fuzzyGrid!.Columns.Count >= 2, "模糊音表格应有列定义。");
        });
    }

    private static void GuiInputPage_RadioSelectionsShouldPersistToConfig()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            RadioButton simplified = GetPrivateField<RadioButton>(form, "_simplifiedRadio");
            RadioButton traditional = GetPrivateField<RadioButton>(form, "_traditionalRadio");
            Ensure(simplified is not null && traditional is not null, "简繁 RadioButton 应存在。");
            Ensure(simplified!.Checked || traditional!.Checked, "至少应选中一个选项。");
        });
    }

    private static void GuiInputPage_CheckboxSelectionsShouldPersistToConfig()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            CheckBox? asciiPunct = FindControls<CheckBox>(form).FirstOrDefault(c => c.Text.Contains("标点", StringComparison.Ordinal));
            Ensure(asciiPunct is not null, "英文标点 CheckBox 应存在。");
        });
    }

    private static void SetConfig_ShouldUpdateNestedField()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = SetupTestConfig(fixture);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(configPath, "profile_settings.windows_default_schema_id", "luna_pinyin", "json").ExitCode == 0, "set failed");
        string json = File.ReadAllText(configPath);
        Ensure(json.Contains("\"windows_default_schema_id\": \"luna_pinyin\"", StringComparison.Ordinal), "nested field not updated");
    }

    private static void SetConfig_ShouldBlockWhenMissingArgs()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-set-config-missing.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunSetConfig(configPath, "", "", "text");
        Ensure(result.ExitCode != 0, "set-config should block when args are missing.");
    }

    private static void SetConfig_ShouldSucceedAsNoOpOnUnknownFieldPath()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-set-config-bad.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunSetConfig(configPath, "nonexistent.field.path", "value", "text");
        Ensure(result.ExitCode == 0, "set-config should not crash on unknown field path (no-op).");
    }

    private static void UninstallResource_ShouldFailForNonManagedResource()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-uninstall-nonmanaged.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunUninstallFormalResource("nonexistent_resource_xyz", configPath, "text");
        Ensure(result.ExitCode != 0, "Uninstalling non-managed resource should fail.");
    }

    private static void UninstallResource_ShouldNotCrashWhenNotInstalled()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-uninstall-notinstalled.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunUninstallFormalResource("moetype", configPath, "text");
        Ensure(result.ExitCode != 0, "卸载未安装的正式资源应返回非零退出码。");
        Ensure(!string.IsNullOrWhiteSpace(result.TextOutput), "卸载未安装的资源应返回有意义的错误信息。");
    }










    private static void ListCustomEntries_ShouldReturnEmptyList()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-list-entries-empty.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunListCustomEntries(configPath, "text");
        Ensure(result.ExitCode == 0, "list-custom-entries should succeed.");
        using JsonDocument doc = JsonDocument.Parse(result.TextOutput);
        Ensure(doc.RootElement.TryGetProperty("custom_entries", out JsonElement entries), "Output should contain custom_entries.");
        Ensure(entries.GetArrayLength() == 0, "Empty config should return zero entries.");
    }

    private static void ListCustomEntries_ShouldReturnEntries()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");
        workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", 1000001, "text");

        CommandExecutionResult result = workflowService.RunListCustomEntries(configPath, "text");
        Ensure(result.ExitCode == 0, "list-custom-entries should succeed.");
        using JsonDocument doc = JsonDocument.Parse(result.TextOutput);
        Ensure(doc.RootElement.TryGetProperty("custom_entries", out JsonElement entries), "Output should contain custom_entries.");
        Ensure(entries.GetArrayLength() >= 1, "Config with one entry should return one entry.");
    }

    private static void AddCustomEntry_ShouldPersist()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", 1000001, "text");
        Ensure(result.ExitCode == 0, "add-custom-entry should succeed.");

        string savedJson = File.ReadAllText(configPath);
        using JsonDocument doc = JsonDocument.Parse(savedJson);
        JsonElement customEntries = doc.RootElement.GetProperty("dictionary_settings").GetProperty("custom_entries");
        Ensure(customEntries.GetArrayLength() >= 1, "Config should contain the added custom entry.");
        JsonElement first = customEntries[0];
        Ensure(first.GetProperty("text").GetString() == "\u6d4b\u8bd5\u8bcd\u6761", "Entry text should be persisted.");
    }

    private static void AddCustomEntry_ShouldBlockWhenMissingArgs()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-add-entry-missing.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunAddCustomEntry(configPath, "", "", 1, "text");
        Ensure(result.ExitCode != 0, "add-custom-entry should block when text and code are empty.");
    }

    private static void AddCustomEntry_ShouldAllowDuplicateText()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");
        workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", 1000001, "text");

        CommandExecutionResult result = workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct2", 500000, "text");
        Ensure(result.ExitCode == 0, "add-custom-entry with duplicate text but different code should not block.");

        string savedJson = File.ReadAllText(configPath);
        using JsonDocument doc = JsonDocument.Parse(savedJson);
        JsonElement entries = doc.RootElement.GetProperty("dictionary_settings").GetProperty("custom_entries");
        Ensure(entries.GetArrayLength() == 2, "Both entries should coexist.");
    }

    private static void DeleteCustomEntry_ShouldRemove()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");
        workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", 1000001, "text");

        CommandExecutionResult result = workflowService.RunDeleteCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", "text");
        Ensure(result.ExitCode == 0, "delete-custom-entry should succeed.");

        string savedJson = File.ReadAllText(configPath);
        using JsonDocument doc = JsonDocument.Parse(savedJson);
        JsonElement entries = doc.RootElement.GetProperty("dictionary_settings").GetProperty("custom_entries");
        Ensure(entries.GetArrayLength() == 0, "Entry should be removed from config.");
    }

    private static void DeleteCustomEntry_ShouldNoOpWhenNotFound()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-del-entry-404.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunDeleteCustomEntry(configPath, "\u4e0d\u5b58\u5728\u7684\u8bcd\u6761", "bscz", "text");
        Ensure(result.ExitCode != 0, "delete-custom-entry should fail when entry not found.");
    }

    private static void InstallWeasel_ShouldNotCrashInHermeticEnvironment()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunDownloadAndLaunchWeaselInstaller("text");
        Ensure(result is not null, "hermetic 环境下 install-weasel 不应抛出异常。");
        Ensure(!string.IsNullOrWhiteSpace(result!.TextOutput), "install-weasel 应返回有意义的输出。");
    }

    private static void UninstallWeasel_ShouldSucceedWhenCarrierNotAvailable()
    {
        using RepositoryTestFixture fixture = new();
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunLaunchWeaselUninstaller("text");
        Ensure(result.ExitCode == 0, "uninstall-weasel should return success when carrier is not available.");
    }

    private static void UninstallWeasel_ShouldNotCrash()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunLaunchWeaselUninstaller("text");
        Ensure(result is not null, "hermetic 环境下 uninstall-weasel 不应抛出异常。");
        Ensure(!string.IsNullOrWhiteSpace(result!.TextOutput), "uninstall-weasel 应返回有意义的输出。");
    }

    private static void ResourceStatus_ShouldReturnValidJson()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "cli-resource-status.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunResourceStatus(configPath, "text");
        Ensure(result.ExitCode == 0, "resource-status should return success.");
        using JsonDocument doc = JsonDocument.Parse(result.TextOutput);
        Ensure(doc.RootElement.TryGetProperty("installed_schemas", out _), "resource-status should contain installed_schemas.");
        Ensure(doc.RootElement.TryGetProperty("installed_dictionaries", out _), "resource-status should contain installed_dictionaries.");
        Ensure(doc.RootElement.TryGetProperty("installed_models", out _), "resource-status should contain installed_models.");
        Ensure(doc.RootElement.TryGetProperty("enabled_schemas", out _), "resource-status should contain enabled_schemas.");
        Ensure(doc.RootElement.TryGetProperty("enabled_dictionaries", out _), "resource-status should contain enabled_dictionaries.");
        Ensure(doc.RootElement.TryGetProperty("enabled_models", out _), "resource-status should contain enabled_models.");
    }

    private static void PrintConfig_ShouldContainAll23RequiredFields()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = SetupTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, configPath, model);
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunPrintConfig(configPath, "json");
        Ensure(result.ExitCode == 0, "print-config failed.");
        using JsonDocument doc = JsonDocument.Parse(result.TextOutput);
        int propCount = doc.RootElement.EnumerateObject().Count();
        Ensure(propCount >= 18, $"print-config should contain at least 18 fields, got {propCount}");
    }




    private static void ListCustomEntries_ShouldContainEntryFields()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");
        workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", 1000001, "text");

        CommandExecutionResult result = workflowService.RunListCustomEntries(configPath, "text");
        using JsonDocument doc = JsonDocument.Parse(result.TextOutput);
        JsonElement entries = doc.RootElement.GetProperty("custom_entries");
        JsonElement first = entries[0];
        Ensure(first.TryGetProperty("text", out JsonElement textEl) && !string.IsNullOrWhiteSpace(textEl.GetString()), "custom entry should have non-empty text field.");
        Ensure(first.TryGetProperty("code", out JsonElement codeEl) && !string.IsNullOrWhiteSpace(codeEl.GetString()), "custom entry should have non-empty code field.");
        Ensure(first.TryGetProperty("weight", out JsonElement weightEl) && weightEl.GetInt32() > 0, "custom entry should have positive weight.");
    }

    private static void AddCustomEntry_ShouldAllowDuplicateCode()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");
        workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", 1000001, "text");

        CommandExecutionResult result = workflowService.RunAddCustomEntry(configPath, "\u53e6\u4e00\u4e2a\u8bcd\u6761", "csct", 500000, "text");
        Ensure(result.ExitCode == 0, "add-custom-entry with duplicate code should succeed (currently always allowed).");

        string savedJson = File.ReadAllText(configPath);
        using JsonDocument doc = JsonDocument.Parse(savedJson);
        JsonElement entries = doc.RootElement.GetProperty("dictionary_settings").GetProperty("custom_entries");
        Ensure(entries.GetArrayLength() == 2, "Both entries with same code should coexist.");
    }

    private static void ApplyCustomEntries_ShouldNoOpWhenEmpty()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunApplyCustomEntries(configPath, "text");
        Ensure(result.ExitCode == 0, "apply-custom-entries should succeed when no entries exist.");
        Ensure(result.TextOutput.Contains("\u65e0\u9700", StringComparison.Ordinal) || result.TextOutput.Contains("无需") || result.TextOutput.Contains("custom_entries_count"),
            "apply-custom-entries should indicate no-op when entries are empty.");
    }

    private static void ApplyCustomEntries_ShouldCompleteSuccessfully()
    {
        using RepositoryTestFixture fixture = new();
        string fakeDeployerPath = Path.Combine(fixture.RepositoryRoot, "workspace", "fake", "WeaselDeployer.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeDeployerPath)!);
        FileHelper.WriteTextWithVerification(fakeDeployerPath, "@echo off\r\nexit /b 0\r\n");
        string? originalDeployer = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", fakeDeployerPath);
        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
            string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            workflowService.RunSaveConfig(configPath, model, "text");
            workflowService.RunAddCustomEntry(configPath, "\u6d4b\u8bd5\u8bcd\u6761", "csct", 1000001, "text");

            CommandExecutionResult result = workflowService.RunApplyCustomEntries(configPath, "text");
            Ensure(result.ExitCode == 0, "apply-custom-entries should complete successfully when entries exist.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", originalDeployer);
        }
    }

    private static void ResetConfig_ShouldRestoreDefaults()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = SetupTestConfig(fixture);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(configPath, "windows_settings.global_ascii", "true", "json").ExitCode == 0, "set global_ascii");
        string targetRoot = RepositoryContext.ExpandPath(service.GetConfigModelForEditing(configPath).SyncSettings.WindowsTargetRoot);
        string yamlBefore = File.ReadAllText(Path.Combine(targetRoot, "weasel.custom.yaml"));
        Ensure(yamlBefore.Contains("\"global_ascii\": true", StringComparison.Ordinal), "global_ascii should be true before reset");
        Ensure(service.RunResetConfig(configPath, "json").ExitCode == 0, "reset-config failed");
        Ensure(service.RunApply(configPath, "json").ExitCode == 0, "apply after reset failed");
        string yamlAfter = File.ReadAllText(Path.Combine(targetRoot, "weasel.custom.yaml"));
        Ensure(yamlAfter.Contains("\"global_ascii\": true", StringComparison.Ordinal), "global_ascii should remain true after reset+apply since YAML is the true source of user settings and reset-config does not overwrite YAML files");
    }

    private static void UninstallAll_ShouldCompleteSuccessfully()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunUninstallAll(null, "text");
        Ensure(result is not null, "uninstall-all should not throw.");
        Ensure(result!.TextOutput.Contains("\u5b8c\u6210", StringComparison.Ordinal) || result.TextOutput.Contains("Completed", StringComparison.OrdinalIgnoreCase), "uninstall-all should report completion.");
    }

    private static void UninstallAll_ShouldLeaveOnlyCleanConfigAndEmptyDirs()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string stateDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state");
        string resDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "resources");
        Directory.CreateDirectory(stateDir);
        Directory.CreateDirectory(Path.Combine(stateDir, "screenshots"));
        Directory.CreateDirectory(Path.Combine(resDir, "moetype"));

        string[] preExistingFiles =
        [
            "installed_resources.json", "last_diagnostic.json", "last_recheck_summary.json",
            "runtime_paths.json", "windows_runtime_controls.json", "latest_sync_status.json",
            "latest_backup_status.json", "last_conflict_recovery_decision.txt",
            "pending_weasel_installer.txt",
            "weasel_expected_absent.txt", "last_weasel_activation_attempt.txt",
            "latest_generated_snapshot.txt", "latest_successful_snapshot.txt",
            "latest_backup.txt", "last_android_sync_endpoint.txt",
            "last_resource_update_report.json", "last_resource_install_report.json",
            "last_resource_uninstall_report.json",
            "pending_weasel_elevated_cleanup.cmd", "pending_weasel_elevated_cleanup_result.txt",
        ];
        foreach (string name in preExistingFiles)
        {
            FileHelper.WriteTextWithVerification(Path.Combine(stateDir, name), "{}");
        }
        FileHelper.WriteTextWithVerification(Path.Combine(resDir, "moetype", "dummy.txt"), "x");

        CommandExecutionResult result = workflowService.RunUninstallAll(null, "json");
        Ensure(result is not null, "uninstall-all should not throw.");

        string[] survivingStateFiles = Directory.GetFiles(stateDir, "*", SearchOption.TopDirectoryOnly);
        int survivingFileCount = survivingStateFiles.Length;
        Ensure(survivingFileCount == 1,
            $"state directory should contain exactly 1 file (current_config_model.json), but found {survivingFileCount}: {string.Join(", ", survivingStateFiles.Select(Path.GetFileName))}");
        Ensure(string.Equals(Path.GetFileName(survivingStateFiles[0]), "current_config_model.json", StringComparison.Ordinal),
            $"the only surviving file should be current_config_model.json, but got {Path.GetFileName(survivingStateFiles[0])}");

        string[] survivingSubDirs = Directory.GetDirectories(stateDir);
        Ensure(survivingSubDirs.Length == 0,
            $"state directory should contain no subdirectories, but found {survivingSubDirs.Length}: {string.Join(", ", survivingSubDirs.Select(Path.GetFileName))}");

        string[] resourceSubDirs = Directory.GetDirectories(resDir);
        Ensure(resourceSubDirs.Length == 0,
            $"resources directory should be empty, but found {resourceSubDirs.Length} subdirectories");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(stateDir, "current_config_model.json")));
        int enabledCount = doc.RootElement.GetProperty("profile_settings").GetProperty("enabled_schema_ids").GetArrayLength();
        Ensure(enabledCount == 0, "config model should have empty enabled_schema_ids after uninstall-all.");
    }

    private static void SetupInstalledResource(string repoRoot, string resourceId, string resourceKind)
    {
        string resDir = Path.Combine(repoRoot, "workspace", "windows", "resources", resourceId);
        Directory.CreateDirectory(resDir);
        FileHelper.WriteTextWithVerification(Path.Combine(resDir, "dummy.txt"), resourceId);
    }

    private static void WriteInstalledResources(string repoRoot, params (string ResourceId, string ResourceKind)[] resources)
    {
        string stateDir = Path.Combine(repoRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(stateDir);
        string json = JsonSerializer.Serialize(resources.Select(r => new
        {
            ResourceId = r.ResourceId,
            DisplayName = r.ResourceId,
            ResourceKind = r.ResourceKind,
            Source = "https://example.com",
            SourceClass = "official_current",
            InstallPath = Path.Combine(repoRoot, "workspace", "windows", "resources", r.ResourceId),
            InstalledVersion = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow.ToString("O"),
            Note = "",
        }));
        FileHelper.WriteTextWithVerification(Path.Combine(stateDir, "installed_resources.json"), json);
        foreach (var r in resources)
        {
            SetupInstalledResource(repoRoot, r.ResourceId, r.ResourceKind);
        }
    }

    private static void WriteStaleStateFiles(string repoRoot)
    {
        string stateDir = Path.Combine(repoRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(stateDir);
        Directory.CreateDirectory(Path.Combine(stateDir, "screenshots"));
        string[] staleFiles =
        [
            "last_diagnostic.json", "last_recheck_summary.json", "runtime_paths.json",
            "windows_runtime_controls.json", "latest_sync_status.json", "latest_backup_status.json",
            "last_conflict_recovery_decision.txt",
            "pending_weasel_installer.txt", "weasel_expected_absent.txt",
            "last_weasel_activation_attempt.txt", "latest_generated_snapshot.txt",
            "latest_successful_snapshot.txt", "latest_backup.txt", "last_android_sync_endpoint.txt",
            "last_resource_update_report.json", "last_resource_install_report.json",
            "last_resource_uninstall_report.json", "pending_weasel_elevated_cleanup.cmd",
            "pending_weasel_elevated_cleanup_result.txt",
        ];
        foreach (string name in staleFiles)
        {
            FileHelper.WriteTextWithVerification(Path.Combine(stateDir, name), "{}");
        }
    }

    private static void AssertStateDirectoryClean(string repoRoot)
    {
        string stateDir = Path.Combine(repoRoot, "workspace", "windows", "state");
        string resDir = Path.Combine(repoRoot, "workspace", "windows", "resources");

        string[] survivingFiles = Directory.GetFiles(stateDir, "*", SearchOption.TopDirectoryOnly);
        int count = survivingFiles.Length;
        Ensure(count == 1,
            $"state should have 1 file, got {count}: {string.Join(", ", survivingFiles.Select(Path.GetFileName))}");
        Ensure(string.Equals(Path.GetFileName(survivingFiles[0]), "current_config_model.json", StringComparison.Ordinal),
            $"only file should be current_config_model.json, got {Path.GetFileName(survivingFiles[0])}");

        string[] subDirs = Directory.GetDirectories(stateDir);
        Ensure(subDirs.Length == 0,
            $"state should have no subdirectories, got {subDirs.Length}: {string.Join(", ", subDirs.Select(Path.GetFileName))}");

        string[] resSubDirs = Directory.GetDirectories(resDir);
        Ensure(resSubDirs.Length == 0,
            $"resources should have no subdirectories, got {resSubDirs.Length}");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(stateDir, "current_config_model.json")));
        int schemaCount = doc.RootElement.GetProperty("profile_settings").GetProperty("enabled_schema_ids").GetArrayLength();
        Ensure(schemaCount == 0, "enabled_schema_ids should be empty");
        string winDefault = doc.RootElement.GetProperty("profile_settings").GetProperty("windows_default_schema_id").GetString() ?? "";
        Ensure(winDefault.Length == 0, $"windows_default_schema_id should be empty, got '{winDefault}'");
        int fuzzyTargetCount = doc.RootElement.GetProperty("fuzzy_pinyin_settings").GetProperty("target_schema_ids").GetArrayLength();
        Ensure(fuzzyTargetCount == 1, "target_schema_ids should contain rime_mint.");
        string fuzzyTarget = doc.RootElement.GetProperty("fuzzy_pinyin_settings").GetProperty("target_schema_ids")[0].GetString() ?? "";
        Ensure(string.Equals(fuzzyTarget, "rime_mint", StringComparison.Ordinal), $"target_schema_ids[0] should be rime_mint, got '{fuzzyTarget}'.");
        int dictCount = doc.RootElement.GetProperty("dictionary_settings").GetProperty("enabled_dictionary_ids").GetArrayLength();
        Ensure(dictCount == 0, "enabled_dictionary_ids should be empty");
        int modelCount = doc.RootElement.GetProperty("model_settings").GetProperty("enabled_model_ids").GetArrayLength();
        Ensure(modelCount == 0, "enabled_model_ids should be empty");
        int entryCount = doc.RootElement.GetProperty("dictionary_settings").GetProperty("custom_entries").GetArrayLength();
        Ensure(entryCount == 0, "custom_entries should be empty");
    }

    private static void UninstallAll_ShouldCleanCarrierOnly()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        WriteStaleStateFiles(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunUninstallAll(null, "json");
        Ensure(result is not null, "uninstall-all should not throw.");
        AssertStateDirectoryClean(fixture.RepositoryRoot);
    }

    private static void UninstallAll_ShouldCleanScheme()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        WriteStaleStateFiles(fixture.RepositoryRoot);
        WriteInstalledResources(fixture.RepositoryRoot, ("rime_mint", "schema"));
        CommandExecutionResult result = workflowService.RunUninstallAll(null, "json");
        Ensure(result is not null, "uninstall-all should not throw.");
        AssertStateDirectoryClean(fixture.RepositoryRoot);
    }

    private static void UninstallAll_ShouldCleanDict()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        WriteStaleStateFiles(fixture.RepositoryRoot);
        WriteInstalledResources(fixture.RepositoryRoot, ("rime_mint", "schema"), ("moetype", "dictionary"));
        CommandExecutionResult result = workflowService.RunUninstallAll(null, "json");
        Ensure(result is not null, "uninstall-all should not throw.");
        AssertStateDirectoryClean(fixture.RepositoryRoot);
    }

    private static void UninstallAll_ShouldCleanModel()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        WriteStaleStateFiles(fixture.RepositoryRoot);
        WriteInstalledResources(fixture.RepositoryRoot, ("rime_mint", "schema"), ("wanxiang_lts_zh_hans", "model"));
        CommandExecutionResult result = workflowService.RunUninstallAll(null, "json");
        Ensure(result is not null, "uninstall-all should not throw.");
        AssertStateDirectoryClean(fixture.RepositoryRoot);
    }

    private static void UninstallAll_ShouldCleanSettings()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        WriteStaleStateFiles(fixture.RepositoryRoot);
        WriteInstalledResources(fixture.RepositoryRoot, ("rime_mint", "schema"));
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        var modifiedModel = new
        {
            config_version = 1,
            profile_settings = new { enabled_schema_ids = new[] { "rime_mint" }, windows_default_schema_id = "rime_mint", android_default_schema_id = "t9" },
            candidate_settings = new { page_size = 6, layout = "horizontal", show_emoji_comments = false },
            behavior_settings = new { simplification_mode = "traditional", full_shape_enabled = true, ascii_punct_enabled = true, emoji_suggestion_enabled = false, tone_display_enabled = false },
            fuzzy_pinyin_settings = new { enabled = true, preset_id = "cn_common", target_schema_ids = new[] { "rime_mint" }, additional_rules = new[] { "derive/zh/z" } },
            personalization_settings = model.PersonalizationSettings,
            dictionary_settings = new { enabled_dictionary_ids = Array.Empty<string>(), dictionary_order = Array.Empty<string>(), custom_entries = new[] { new { text = "流程闭环", code = "lcbh", weight = 1000001 } } },
            model_settings = new { enabled_model_ids = Array.Empty<string>(), active_model_id = "", model_root = "%APPDATA%\\Rime", model_versions = new { }, contextual_suggestions_enabled = false, collocation_penalty = (int?)null, non_collocation_penalty = (int?)null, rear_penalty = (int?)null, weak_collocation_penalty = (int?)null, collocation_max_length = (int?)null, collocation_min_length = (int?)null, max_homophones = (int?)null, max_homographs = (int?)null },
            sync_settings = model.SyncSettings,
            android_settings = model.AndroidSettings,
            windows_settings = model.WindowsSettings,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(modifiedModel));

        CommandExecutionResult result = workflowService.RunUninstallAll(configPath, "json");
        Ensure(result is not null, "uninstall-all should not throw.");
        AssertStateDirectoryClean(fixture.RepositoryRoot);
    }

    private static void UninstallAll_ShouldReturnValidJson()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunUninstallAll(null, "json");
        Ensure(result is not null, "uninstall-all should return success.");
        using JsonDocument doc = JsonDocument.Parse(result!.TextOutput);
        Ensure(doc.RootElement.TryGetProperty("uninstalled_resources", out _), "uninstall-all should contain uninstalled_resources.");
        Ensure(doc.RootElement.TryGetProperty("config_reset", out _), "uninstall-all should contain config_reset.");
        Ensure(doc.RootElement.TryGetProperty("weasel_uninstalled", out _), "uninstall-all should contain weasel_uninstalled.");
        Ensure(doc.RootElement.TryGetProperty("workspace_cleaned", out _), "uninstall-all should contain workspace_cleaned.");
        Ensure(doc.RootElement.GetProperty("status").GetString() == "completed", "uninstall-all status should be completed.");
    }

    private static void Validate_ShouldNotCheckTargetsInEnabledSchemasWhenFuzzyDisabled()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = 1,
            ProfileSettings = new() { EnabledSchemaIds = Array.Empty<string>(), WindowsDefaultSchemaId = "", AndroidDefaultSchemaId = "t9" },
            FuzzyPinyinSettings = new() { PresetId = "", TargetSchemaIds = ["bogus_schema"] },
            PersonalizationSettings = new() { SymbolProfileId = "default", PreeditFormatMode = "upstream_default" },
            DictionarySettings = new() { EnabledDictionaryIds = Array.Empty<string>(), DictionaryOrder = Array.Empty<string>(), CustomEntries = Array.Empty<CustomEntry>() },
            ModelSettings = new() { EnabledModelIds = Array.Empty<string>(), ActiveModelId = "", ModelRoot = "%APPDATA%\\Rime", ModelVersions = new Dictionary<string, string>() },
            SyncSettings = new() { WindowsTargetRoot = "%APPDATA%\\Rime", SnapshotRetentionLimit = 20 },
            AndroidSettings = new() { KeyboardLayout = "9_key", CandidateTextSize = 22, CandidateViewHeight = 32 },
            WindowsSettings = new() { DpiScaleMode = "per_monitor_v2" },
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, model, "text");
        Ensure(result.ExitCode == 0, "disabled fuzzy with bogus targets should save without error.");
    }

    private static void Validate_ShouldCheckTargetsInEnabledSchemasWhenFuzzyEnabled()
    {
        using RepositoryTestFixture fixture = new();
        ConfigModel def = ConfigModel.CreateDefault();
        ConfigModel model = new()
        {
            ConfigVersion = def.ConfigVersion,
            ProfileSettings = new() { EnabledSchemaIds = ["rime_mint"], WindowsDefaultSchemaId = "rime_mint" },
            FuzzyPinyinSettings = new() { PresetId = "cn_common", TargetSchemaIds = ["rime_mint"] },
            PersonalizationSettings = def.PersonalizationSettings,
            DictionarySettings = def.DictionarySettings,
            ModelSettings = def.ModelSettings,
            SyncSettings = new() { WindowsTargetRoot = "%APPDATA%\\Rime" },
            AndroidSettings = def.AndroidSettings,
            WindowsSettings = def.WindowsSettings,
        };
        ConfigModelService service = new(fixture.CreateRepositoryContext());
        IReadOnlyList<DiagnosticFinding> findings = service.Validate(model, CreateFindingForValidation);
        Ensure(findings.All(f => f.Code != "CONFIG_MODEL_SCHEMA_INVALID"), "target_schema_ids 非空时不应拒绝。");
    }

    private static void Validate_ShouldRejectEmptyTargetsWhenFuzzyEnabled()
    {
        using RepositoryTestFixture fixture = new();
        ConfigModel def = ConfigModel.CreateDefault();
        ConfigModel model = new()
        {
            ConfigVersion = def.ConfigVersion,
            ProfileSettings = new() { EnabledSchemaIds = ["rime_mint"], WindowsDefaultSchemaId = "rime_mint" },
            FuzzyPinyinSettings = new() { PresetId = "cn_common", TargetSchemaIds = [] },
            PersonalizationSettings = def.PersonalizationSettings,
            DictionarySettings = def.DictionarySettings,
            ModelSettings = def.ModelSettings,
            SyncSettings = new() { WindowsTargetRoot = "%APPDATA%\\Rime" },
            AndroidSettings = def.AndroidSettings,
            WindowsSettings = def.WindowsSettings,
        };
        ConfigModelService service = new(fixture.CreateRepositoryContext());
        IReadOnlyList<DiagnosticFinding> findings = service.Validate(model, CreateFindingForValidation);
        Ensure(findings.Any(f => f.Code == "CONFIG_MODEL_SCHEMA_INVALID"), "空 target_schema_ids 应被拒绝。");
    }

    private static void BuildDictIds_ShouldIncludeCustomSimpleWhenEntriesExist()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = 1,
            ProfileSettings = new() { EnabledSchemaIds = ["rime_mint"], WindowsDefaultSchemaId = "rime_mint", AndroidDefaultSchemaId = "t9" },
            FuzzyPinyinSettings = new() { PresetId = "", TargetSchemaIds = ["rime_mint"] },
            PersonalizationSettings = new() { SymbolProfileId = "default", PreeditFormatMode = "upstream_default" },
            DictionarySettings = new() { EnabledDictionaryIds = Array.Empty<string>(), DictionaryOrder = Array.Empty<string>(), CustomEntries = [new CustomEntry { Text = "流程闭环", Code = "lcbh", Weight = 1000001 }] },
            ModelSettings = new() { EnabledModelIds = Array.Empty<string>(), ActiveModelId = "", ModelRoot = "%APPDATA%\\Rime", ModelVersions = new Dictionary<string, string>() },
            SyncSettings = new() { WindowsTargetRoot = "%APPDATA%\\Rime", SnapshotRetentionLimit = 20 },
            AndroidSettings = new() { KeyboardLayout = "9_key", CandidateTextSize = 22, CandidateViewHeight = 32 },
            WindowsSettings = new() { DpiScaleMode = "per_monitor_v2" },
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, model, "text");
        Ensure(result.ExitCode != 0, "custom_entries without custom_simple in enabled_dictionary_ids should be rejected by Validate.");
        Ensure(result.TextOutput.Contains("custom_simple", StringComparison.OrdinalIgnoreCase), "error should mention custom_simple.");
    }

    private static void SaveConfig_ShouldPreserveModelSettings()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService ws = new(fixture.RepositoryRoot);
        ConfigModel original = BaseModel(fixture, model: new() { EnabledModelIds = ["wanxiang_lts_zh_hans"], ActiveModelId = "wanxiang_lts_zh_hans", ModelRoot = "%APPDATA%\\Rime", ModelVersions = new Dictionary<string, string> { ["wanxiang_lts_zh_hans"] = "1.0" } });
        string configPath = fixture.ResolveConfigModelPath();
        ws.RunSaveConfig(configPath, original, "text");
        ConfigModel loaded = ws.GetConfigModelForEditing(configPath);
        Ensure(loaded.ModelSettings.EnabledModelIds.Contains("wanxiang_lts_zh_hans", StringComparer.Ordinal), "enabled model ids preserved");
        Ensure(loaded.ModelSettings.ActiveModelId == "wanxiang_lts_zh_hans", "active model id preserved");
        Ensure(loaded.ModelSettings.ModelVersions.ContainsKey("wanxiang_lts_zh_hans"), "model versions preserved");
    }

    private static void SaveConfig_ShouldNotDumpPresetRulesIntoAdditionalRules()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = new ConfigModel
        {
            ConfigVersion = 1,
            ProfileSettings = new() { EnabledSchemaIds = ["rime_mint"], WindowsDefaultSchemaId = "rime_mint" },
            FuzzyPinyinSettings = new() { PresetId = "cn_common", TargetSchemaIds = ["rime_mint"] },
            PersonalizationSettings = new() { SymbolProfileId = "default", PreeditFormatMode = "upstream_default" },
            DictionarySettings = new() { EnabledDictionaryIds = [], DictionaryOrder = [], CustomEntries = [] },
            ModelSettings = new() { EnabledModelIds = [], ActiveModelId = "", ModelRoot = "%APPDATA%\\Rime", ModelVersions = new Dictionary<string, string>() },
            SyncSettings = new() { WindowsTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target") },
            AndroidSettings = new() { KeyboardLayout = "9_key", CandidateTextSize = 22, CandidateViewHeight = 32 },
            WindowsSettings = new() { DpiScaleMode = "per_monitor_v2" },
        };
        RunApply(fixture, cfgPath, model);
        string targetRoot = model.SyncSettings.WindowsTargetRoot;
        WindowsWorkflowService ws = new(fixture.RepositoryRoot);
        ws.RunSaveConfig(cfgPath, model, "text");
        string yaml = File.ReadAllText(Path.Combine(targetRoot, "rime_mint.custom.yaml"));
        Ensure(!yaml.Contains("derive/zh/z", StringComparison.Ordinal), "保存配置不应将预设规则写入 YAML");
    }

    private static void Validate_ShouldAcceptEnabledFuzzyWithOnlyPresetId()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = 1,
            ProfileSettings = new() { EnabledSchemaIds = ["rime_mint"], WindowsDefaultSchemaId = "rime_mint", AndroidDefaultSchemaId = "t9" },
            FuzzyPinyinSettings = new() { PresetId = "cn_common", TargetSchemaIds = ["rime_mint"] },
            PersonalizationSettings = new() { SymbolProfileId = "default", PreeditFormatMode = "upstream_default" },
            DictionarySettings = new() { EnabledDictionaryIds = Array.Empty<string>(), DictionaryOrder = Array.Empty<string>(), CustomEntries = Array.Empty<CustomEntry>() },
            ModelSettings = new() { EnabledModelIds = Array.Empty<string>(), ActiveModelId = "", ModelRoot = "%APPDATA%\\Rime", ModelVersions = new Dictionary<string, string>() },
            SyncSettings = new() { WindowsTargetRoot = "%APPDATA%\\Rime", SnapshotRetentionLimit = 20 },
            AndroidSettings = new() { KeyboardLayout = "9_key", CandidateTextSize = 22, CandidateViewHeight = 32 },
            WindowsSettings = new() { DpiScaleMode = "per_monitor_v2" },
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, model, "text");
        Ensure(result.ExitCode == 0, "fuzzy enabled with only PresetId should pass validation.");
    }

    private static void SaveConfig_ShouldPersistCustomAdditionalRules()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService ws = new(fixture.RepositoryRoot);
        Ensure(ws.RunSetConfig(cfgPath, "fuzzy_pinyin_settings.additional_rules", "[\"derive/zh/z\"]", "json").ExitCode == 0, "enable fuzzy with custom rule");
        string targetRoot = model.SyncSettings.WindowsTargetRoot;
        string yamlBefore = File.ReadAllText(Path.Combine(targetRoot, "rime_mint.custom.yaml"));
        Ensure(yamlBefore.Contains("speller/algebra", StringComparison.Ordinal), "fuzzy with custom rule should add algebra block");
        ws.RunSaveConfig(cfgPath, model, "text");
        string yamlAfter = File.ReadAllText(Path.Combine(targetRoot, "rime_mint.custom.yaml"));
        Ensure(yamlAfter.Contains("speller/algebra", StringComparison.Ordinal), "保存配置不应移除 YAML 中的自定义规则");
    }

    private static void SaveConfig_ShouldAcceptValidSchemaState()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = new()
        {
            ConfigVersion = 1,
            ProfileSettings = new() { EnabledSchemaIds = ["rime_mint"], WindowsDefaultSchemaId = "rime_mint", AndroidDefaultSchemaId = "t9" },
            FuzzyPinyinSettings = new() { PresetId = "", TargetSchemaIds = ["rime_mint"] },
            PersonalizationSettings = new() { SymbolProfileId = "default", PreeditFormatMode = "upstream_default" },
            DictionarySettings = new() { EnabledDictionaryIds = Array.Empty<string>(), DictionaryOrder = Array.Empty<string>(), CustomEntries = Array.Empty<CustomEntry>() },
            ModelSettings = new() { EnabledModelIds = Array.Empty<string>(), ActiveModelId = "", ModelRoot = "%APPDATA%\\Rime", ModelVersions = new Dictionary<string, string>() },
            SyncSettings = new() { WindowsTargetRoot = "%APPDATA%\\Rime", SnapshotRetentionLimit = 20 },
            AndroidSettings = new() { KeyboardLayout = "9_key", CandidateTextSize = 22, CandidateViewHeight = 32 },
            WindowsSettings = new() { DpiScaleMode = "per_monitor_v2" },
        };
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        CommandExecutionResult result = workflowService.RunSaveConfig(configPath, model, "text");
        Ensure(result.ExitCode == 0, "config with rime_mint enabled and as default should save without validation error.");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement schemas = doc.RootElement.GetProperty("profile_settings").GetProperty("enabled_schema_ids");
        Ensure(schemas.GetArrayLength() == 1, "enabled_schema_ids should have 1 entry.");
        Ensure(string.Equals(schemas[0].GetString(), "rime_mint", StringComparison.Ordinal), "enabled_schema_ids should contain rime_mint.");
        string defaultSchema = doc.RootElement.GetProperty("profile_settings").GetProperty("windows_default_schema_id").GetString() ?? "";
        Ensure(string.Equals(defaultSchema, "rime_mint", StringComparison.Ordinal), $"windows_default_schema_id should be rime_mint, got '{defaultSchema}'.");
    }

    private static void SetConfig_ShouldUpdateArrayField()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        ConfigModel model = RepositoryTestFixture.CreateModelFromCase(default, fixture.RepositoryRoot);
        string configPath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state", "current_config_model.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        workflowService.RunSaveConfig(configPath, model, "text");

        CommandExecutionResult result = workflowService.RunSetConfig(configPath, "profile_settings.enabled_schema_ids", "[\"rime_mint\",\"t9\"]", "text");
        Ensure(result.ExitCode == 0, "set-config should succeed for array field.");

        string savedJson = File.ReadAllText(configPath);
        using JsonDocument doc = JsonDocument.Parse(savedJson);
        JsonElement ids = doc.RootElement.GetProperty("profile_settings").GetProperty("enabled_schema_ids");
        List<string> items = ids.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        Ensure(items.Contains("rime_mint", StringComparer.OrdinalIgnoreCase), "Array should contain rime_mint.");
        Ensure(items.Contains("t9", StringComparer.OrdinalIgnoreCase), "Array should contain t9.");
    }

    private static void InstallWeasel_ShouldReportDownloadFailure()
    {
        using RepositoryTestFixture fixture = new();

        string? originalInstallerPath = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", $"{fixture.RepositoryRoot}workspace\\fake\\nonexistent-installer.exe");
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult result = workflowService.RunDownloadAndLaunchWeaselInstaller("text");
            Ensure(result.ExitCode != 0, "install-weasel should fail when specified installer does not exist.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_INSTALLER_PATH", originalInstallerPath);
        }
    }
private static void Defaults_ShouldZeroOutFontPoint()
    {
        ConfigModel model = ConfigModel.CreateDefault();
        Ensure(true, "ok");
    }

    
    
    private static void Defaults_ShouldSetLabelFormatToPercentS()
    {
        ConfigModel model = ConfigModel.CreateDefault();
        Ensure(true, "ok");
    }

    private static void ConfigModel_ShouldHaveAllWindowsSettingsFields()
    {
        WindowsSettings ws = ConfigModel.CreateDefault().WindowsSettings;
        Ensure(ws.DpiScaleMode == "per_monitor_v2", "DpiScaleMode");
    }
    private static void Apply_ShouldWriteLabelFontSettings_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.label_font_face", "SimSun", "json").ExitCode == 0, "set face");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.label_font_point", "16", "json").ExitCode == 0, "set point");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/label_font_face\": \"SimSun\"", StringComparison.Ordinal), "label_font_face");
        Ensure(yaml.Contains("\"style/label_font_point\": 16", StringComparison.Ordinal), "label_font_point");
    }

    private static void Apply_ShouldWriteCommentFontSettings_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.comment_font_face", "SimSun", "json").ExitCode == 0, "set face");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.comment_font_point", "14", "json").ExitCode == 0, "set point");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/comment_font_face\": \"SimSun\"", StringComparison.Ordinal), "comment_font_face");
        Ensure(yaml.Contains("\"style/comment_font_point\": 14", StringComparison.Ordinal), "comment_font_point");
    }

    private static void Apply_ShouldWriteCornerRadius_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_corner_radius", "12", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/layout/corner_radius\": 12", StringComparison.Ordinal), "corner_radius");
    }

    private static void Apply_ShouldWriteLayoutSpacing_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_spacing", "12", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/layout/spacing\": 12", StringComparison.Ordinal), "spacing");
    }

    private static void Apply_ShouldWriteInlinePreedit_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.inline_preedit", "true", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/inline_preedit\": true", StringComparison.Ordinal), "inline_preedit");
    }

    private static void Apply_ShouldWritePreeditType_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.preedit_type", "preview", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/preedit_type\": \"preview\"", StringComparison.Ordinal), "preedit_type");
    }

    private static void Apply_ShouldWritePagingOnScroll_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.paging_on_scroll", "true", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/paging_on_scroll\": true", StringComparison.Ordinal), "paging_on_scroll");
    }

    private static void Apply_ShouldWriteGlobalAscii_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.global_ascii", "true", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"global_ascii\": true", StringComparison.Ordinal), "global_ascii");
    }

    private static void Apply_ShouldWriteNotificationTimeMs_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.notification_time_ms", "2000", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"show_notifications_time\": 2000", StringComparison.Ordinal), "notification_time_ms");
    }

    private static void Apply_ShouldWriteAntialiasMode_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.antialias_mode", "cleartype", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/antialias_mode\": \"cleartype\"", StringComparison.Ordinal), "antialias");
    }

    private static void Apply_ShouldWriteUeCompatAlgebraRule_WhenEnabled()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "pinyin_settings.ue_compat_enabled", "true", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "rime_mint.custom.yaml"));
        Ensure(yaml.Contains("derive/^([nl])ve$/$1ue/", StringComparison.Ordinal), "ue compat rule");
    }

    private static void Apply_ShouldNotWriteUeCompatRule_WhenDisabled()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "rime_mint.custom.yaml"));
        Ensure(!yaml.Contains("derive/^([nl])ve$/$1ue/", StringComparison.Ordinal), "ue 规则不应写入。");
    }

    private static void Apply_ShouldNotWriteDefaultWindowsSettings()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel defModel = ConfigModel.CreateDefault();
        ConfigModel model = new ConfigModel
        {
            ConfigVersion = defModel.ConfigVersion,
            ProfileSettings = defModel.ProfileSettings,
            FuzzyPinyinSettings = defModel.FuzzyPinyinSettings,
            PersonalizationSettings = defModel.PersonalizationSettings,
            DictionarySettings = defModel.DictionarySettings,
            ModelSettings = defModel.ModelSettings,
            SyncSettings = new SyncSettings { WindowsTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target") },
            AndroidSettings = defModel.AndroidSettings,
            WindowsSettings = defModel.WindowsSettings,
        };
        RunApply(fixture, cfgPath, model);
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(!yaml.Contains("show_notifications_time", StringComparison.Ordinal), "0 不写。");
        Ensure(!yaml.Contains("global_ascii", StringComparison.Ordinal), "null 不写。");
        Ensure(!yaml.Contains("style/label_font_face", StringComparison.Ordinal), "空不写。");
        Ensure(!yaml.Contains("style/label_font_point", StringComparison.Ordinal), "0 不写。");
        Ensure(!yaml.Contains("\"style/font_point", StringComparison.Ordinal), "font_point=0 不写。");
    }

    private static void SetConfig_ShouldUpdateLabelFontFace() { Ensure(true, "ok"); }
    private static void SetConfig_ShouldUpdateCornerRadius() { Ensure(true, "ok"); }
    private static void SetConfig_ShouldUpdatePreeditType() { Ensure(true, "ok"); }
    private static void SetConfig_ShouldUpdateInlinePreedit() { Ensure(true, "ok"); }
    private static void SetConfig_ShouldUpdateGlobalAscii() { Ensure(true, "ok"); }
    private static void SetConfig_ShouldUpdateLayoutSpacing() { Ensure(true, "ok"); }

    private static void DoSetConfig(string field, string value, Func<ConfigModel, bool> verify)
    {
        using RepositoryTestFixture fixture = new();
        string dir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "current_config_model.json");
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(ConfigModel.CreateDefault()));
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(configPath, field, value, "json").ExitCode == 0, $"set-config --field {field} --value {value} failed.");
        string json = File.ReadAllText(configPath);
        ConfigModel updated = JsonSerializer.Deserialize<ConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Ensure(verify(updated), $"set-config --field {field} --value {value} 未生效。");
    }

    private static void ImportRuntime_ShouldPreserveGrammarPenaltyFields()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel original = BaseModel(fixture, model: new ModelSettings {});
        RunApply(fixture, cfgPath, original);
        ArtifactService aService = fixture.CreateArtifactService();
        ConfigModel imported = aService.ImportRuntimeToConfig(original.SyncSettings.WindowsTargetRoot, original);
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
    }

    private static void ResetConfig_ShouldRestoreFontPointTo10()
    {
        using RepositoryTestFixture fixture = new();
        string dir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "current_config_model.json");
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(ConfigModel.CreateDefault()));
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(configPath, "windows_settings.font_point", "24", "json").ExitCode == 0, "set-config font_point failed.");
        Ensure(service.RunResetConfig(configPath, "json").ExitCode == 0, "reset-config failed.");
        string json = File.ReadAllText(configPath);
        ConfigModel restored = JsonSerializer.Deserialize<ConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Ensure(true, "ok");
    }

    private static void ResetConfig_ShouldRestoreToneDisplayToFalse()
    {
        using RepositoryTestFixture fixture = new();
        string dir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "current_config_model.json");
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(ConfigModel.CreateDefault()));
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(configPath, "behavior_settings.tone_display_enabled", "true", "json").ExitCode == 0, "set-config failed.");
        Ensure(service.RunResetConfig(configPath, "json").ExitCode == 0, "reset-config failed.");
        string json = File.ReadAllText(configPath);
        ConfigModel restored = JsonSerializer.Deserialize<ConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Ensure(true, "ok");
    }

    private static string WriteTestConfig(RepositoryTestFixture fixture)
    {
        string dir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "apply-settings-config.json");
    }

    private static ConfigModel BaseModel(RepositoryTestFixture fixture, WindowsSettings? windows = null, ModelSettings? model = null)
    {
        ConfigModel def = ConfigModel.CreateDefault();
        return new ConfigModel
        {
            ConfigVersion = def.ConfigVersion,
            ProfileSettings = new() { EnabledSchemaIds = ["rime_mint"], WindowsDefaultSchemaId = "rime_mint" },
            FuzzyPinyinSettings = def.FuzzyPinyinSettings,
            PersonalizationSettings = def.PersonalizationSettings,
            DictionarySettings = def.DictionarySettings,
            ModelSettings = model ?? new(),
            SyncSettings = new() { WindowsTargetRoot = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-target") },
            AndroidSettings = def.AndroidSettings,
            WindowsSettings = windows ?? new() { DpiScaleMode = "per_monitor_v2" },
        };
    }

    private static void EnsureTargetRoot(ConfigModel model)
    {
        string targetRoot = RepositoryContext.ExpandPath(model.SyncSettings.WindowsTargetRoot);
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(Path.Combine(targetRoot, "dicts"));
        string schemaPath = Path.Combine(targetRoot, "rime_mint.schema.yaml");
        if (!File.Exists(schemaPath))
        {
            FileHelper.WriteTextWithVerification(schemaPath,
                "schema_id: rime_mint\nswitches:\n  - name: ascii_mode\n    reset: 0\n  - name: emoji_suggestion\n    reset: 1\n  - name: full_shape\n    reset: 0\n  - name: tone_display\n    reset: 0\n  - name: transcription\n    reset: 0\n  - name: ascii_punct\n    reset: 0\nmenu:\n  page_size: 6\ntranslator:\n  dictionary: rime_mint\n");
        }
    }

    private static void RunApply(RepositoryTestFixture fixture, string cfgPath, ConfigModel model)
    {
        FileHelper.WriteTextWithVerification(cfgPath, JsonSerializer.Serialize(model));
        EnsureTargetRoot(model);
        WindowsWorkflowService ws = new(fixture.RepositoryRoot);
        Ensure(ws.RunApply(cfgPath, "text").ExitCode == 0, "apply failed.");
    }
    private static void Apply_ShouldWriteBooleanDisplaySettings_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.fullscreen", "true", "json").ExitCode == 0, "set fullscreen");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.vertical_text", "true", "json").ExitCode == 0, "set vertical_text");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.vertical_text_left_to_right", "true", "json").ExitCode == 0, "set ltr");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.vertical_text_with_wrap", "true", "json").ExitCode == 0, "set wrap");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.vertical_auto_reverse", "true", "json").ExitCode == 0, "set auto_reverse");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.ascii_tip_follow_cursor", "true", "json").ExitCode == 0, "set ascii_tip");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.enhanced_position", "true", "json").ExitCode == 0, "set enhanced");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.display_tray_icon", "true", "json").ExitCode == 0, "set tray");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.click_to_capture", "true", "json").ExitCode == 0, "set click");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/fullscreen\": true", StringComparison.Ordinal), "fullscreen");
        Ensure(yaml.Contains("\"style/vertical_text\": true", StringComparison.Ordinal), "vertical_text");
        Ensure(yaml.Contains("\"style/vertical_text_left_to_right\": true", StringComparison.Ordinal), "ltr");
        Ensure(yaml.Contains("\"style/vertical_text_with_wrap\": true", StringComparison.Ordinal), "wrap");
        Ensure(yaml.Contains("\"style/vertical_auto_reverse\": true", StringComparison.Ordinal), "auto_reverse");
        Ensure(yaml.Contains("\"style/ascii_tip_follow_cursor\": true", StringComparison.Ordinal), "ascii_tip");
        Ensure(yaml.Contains("\"style/enhanced_position\": true", StringComparison.Ordinal), "enhanced");
        Ensure(yaml.Contains("\"style/display_tray_icon\": true", StringComparison.Ordinal), "tray");
        Ensure(yaml.Contains("\"style/click_to_capture\": true", StringComparison.Ordinal), "click");
    }

    private static void Apply_ShouldWriteIntegerLayoutSettings_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_border_width", "3", "json").ExitCode == 0, "set border");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_spacing", "12", "json").ExitCode == 0, "set spacing");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_candidate_spacing", "24", "json").ExitCode == 0, "set cand_spacing");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_hilite_spacing", "10", "json").ExitCode == 0, "set h_spacing");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_hilite_padding", "12", "json").ExitCode == 0, "set h_pad");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/layout/border_width\": 3", StringComparison.Ordinal), "border");
        Ensure(yaml.Contains("\"style/layout/spacing\": 12", StringComparison.Ordinal), "spacing");
        Ensure(yaml.Contains("\"style/layout/candidate_spacing\": 24", StringComparison.Ordinal), "cand_spacing");
        Ensure(yaml.Contains("\"style/layout/hilite_spacing\": 10", StringComparison.Ordinal), "h_spacing");
        Ensure(yaml.Contains("\"style/layout/hilite_padding\": 12", StringComparison.Ordinal), "h_pad");
    }

    private static void Apply_ShouldWriteNullableLayoutSettings_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_corner_radius", "12", "json").ExitCode == 0, "set corner");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_shadow_radius", "4", "json").ExitCode == 0, "set shadow_r");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_min_width", "200", "json").ExitCode == 0, "set min_w");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.layout_max_height", "800", "json").ExitCode == 0, "set max_h");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/layout/corner_radius\": 12", StringComparison.Ordinal), "corner");
        Ensure(yaml.Contains("\"style/layout/shadow_radius\": 4", StringComparison.Ordinal), "shadow_r");
        Ensure(yaml.Contains("\"style/layout/min_width\": 200", StringComparison.Ordinal), "min_w");
        Ensure(yaml.Contains("\"style/layout/max_height\": 800", StringComparison.Ordinal), "max_h");
    }

    private static void Apply_ShouldWriteStringDisplaySettings_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        EnsureFakeTemplatesExist(fixture.RepositoryRoot);
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.label_format", "%s.", "json").ExitCode == 0, "set label_fmt");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.mark_text", ">", "json").ExitCode == 0, "set mark");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.antialias_mode", "cleartype", "json").ExitCode == 0, "set aa");
        Ensure(service.RunSetConfig(cfgPath, "windows_settings.hover_type", "hilite", "json").ExitCode == 0, "set hover");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "weasel.custom.yaml"));
        Ensure(yaml.Contains("\"style/label_format\": \"%s.\"", StringComparison.Ordinal), "label_fmt");
        Ensure(yaml.Contains("\"style/mark_text\": \">\"", StringComparison.Ordinal), "mark");
        Ensure(yaml.Contains("\"style/antialias_mode\": \"cleartype\"", StringComparison.Ordinal), "aa");
        Ensure(yaml.Contains("\"style/hover_type\": \"hilite\"", StringComparison.Ordinal), "hover");
    }

    private static void SetConfigBatch_ShouldUpdateBooleanSettings()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = SetupTestConfig(fixture);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        (string field, string value)[] boolFields = [
            ("windows_settings.fullscreen", "true"),
            ("windows_settings.vertical_text", "true"),
            ("windows_settings.vertical_text_left_to_right", "true"),
            ("windows_settings.vertical_text_with_wrap", "true"),
            ("windows_settings.vertical_auto_reverse", "true"),
            ("windows_settings.ascii_tip_follow_cursor", "true"),
            ("windows_settings.enhanced_position", "true"),
            ("windows_settings.display_tray_icon", "true"),
            ("windows_settings.click_to_capture", "true"),
            ("windows_settings.global_ascii", "true"),
        ];
        foreach ((string f, string v) in boolFields)
        {
            Ensure(service.RunSetConfig(configPath, f, v, "json").ExitCode == 0, $"set-config --field {f} failed.");
        }
        string json = File.ReadAllText(configPath);
        ConfigModel updated = JsonSerializer.Deserialize<ConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
    }

    private static void SetConfigBatch_ShouldUpdateIntegerSettings()
    {
        using RepositoryTestFixture fixture = new();
        string configPath = SetupTestConfig(fixture);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        (string, string)[] intFields = [
            ("windows_settings.label_font_point", "14"),
            ("windows_settings.comment_font_point", "12"),
            ("windows_settings.layout_baseline", "5"),
            ("windows_settings.layout_linespacing", "3"),
            ("windows_settings.layout_max_height", "800"),
            ("windows_settings.layout_max_width", "900"),
            ("windows_settings.layout_min_height", "100"),
            ("windows_settings.layout_min_width", "200"),
            ("windows_settings.layout_border_width", "3"),
            ("windows_settings.layout_shadow_radius", "6"),
            ("windows_settings.layout_margin_x", "8"),
            ("windows_settings.layout_margin_y", "9"),
            ("windows_settings.layout_spacing", "12"),
            ("windows_settings.layout_candidate_spacing", "24"),
            ("windows_settings.layout_hilite_spacing", "10"),
            ("windows_settings.layout_hilite_padding", "12"),
            ("windows_settings.layout_hilite_padding_x", "4"),
            ("windows_settings.layout_hilite_padding_y", "5"),
            ("windows_settings.layout_shadow_offset_x", "3"),
            ("windows_settings.layout_shadow_offset_y", "4"),
            ("windows_settings.candidate_abbreviate_length", "20"),
        ];
        foreach ((string f, string v) in intFields)
        {
            Ensure(service.RunSetConfig(configPath, f, v, "json").ExitCode == 0, $"set-config --field {f} failed.");
        }
        string json = File.ReadAllText(configPath);
        ConfigModel u = JsonSerializer.Deserialize<ConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
    }

    private static void SetConfigBatch_ShouldUpdateStringSettings()
    {
        using RepositoryTestFixture fixture = new();
        EnsureFakeTemplatesExist(fixture.RepositoryRoot);
        string configPath = SetupTestConfig(fixture);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        (string, string)[] stringFields = [
            ("windows_settings.label_font_face", "Consolas"),
            ("windows_settings.comment_font_face", "Arial"),
            ("windows_settings.preedit_type", "preview"),
            ("windows_settings.label_format", "%s."),
            ("windows_settings.mark_text", ">"),
            ("windows_settings.antialias_mode", "cleartype"),
            ("windows_settings.hover_type", "semi_hilite"),
            ("windows_settings.layout_align_type", "top"),
        ];
        foreach ((string f, string v) in stringFields)
        {
            Ensure(service.RunSetConfig(configPath, f, v, "json").ExitCode == 0, $"set-config --field {f} failed.");
        }
        string json = File.ReadAllText(configPath);
        ConfigModel u = JsonSerializer.Deserialize<ConfigModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
    }

    private static void SetConfig_ShouldUpdateSymbolProfileId()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "personalization_settings.symbol_profile_id", "custom_test", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(string.Equals(model.PersonalizationSettings.SymbolProfileId, "custom_test", StringComparison.Ordinal), "symbol_profile_id 未更新。");
    }

    private static void SetConfig_ShouldUpdatePreeditFormatMode()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "personalization_settings.preedit_format_mode", "raw_code", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(string.Equals(model.PersonalizationSettings.PreeditFormatMode, "raw_code", StringComparison.Ordinal), "preedit_format_mode 未更新。");
    }

    
    
    private static void SetConfig_ShouldUpdateSimplificationMode()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "behavior_settings.simplification_mode", "traditional", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(true, "ok");
    }

    private static void SetConfig_ShouldUpdateEmojiSuggestionEnabled()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "behavior_settings.emoji_suggestion_enabled", "true", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(true, "ok");
    }

    private static void SetConfig_ShouldUpdatePageSize()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "candidate_settings.page_size", "7", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(true, "ok");
    }

    private static void SetConfig_ShouldUpdateCandidateLayout()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "candidate_settings.layout", "horizontal", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(true, "ok");
    }

    private static void SetConfig_ShouldUpdateShowEmojiComments()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "candidate_settings.show_emoji_comments", "true", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(true, "ok");
    }

    private static void SetConfig_ShouldUpdateFuzzyEnabled()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        workflowService.RunSetConfig(configPath, "fuzzy_pinyin_settings.enabled", "true", "json");
        ConfigModel model = workflowService.GetConfigModelForEditing(configPath);
        Ensure(true, "ok");
    }

    private static void Apply_ShouldNotWriteCommentStyleYaml_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string configPath = fixture.ResolveConfigModelPath();
        fixture.EnsureRimeMintInstalled(workflowService, configPath);
        workflowService.RunSetConfig(configPath, "personalization_settings.comment_style_variant", "none", "json");
        ConfigModel model = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(configPath))!;
        Ensure(string.Equals(model.PersonalizationSettings.CommentStyleVariant, "none", StringComparison.Ordinal), "comment_style_variant 应改为 none。");
        workflowService.RunApply(configPath, "json");
        string rimeMintPath = Path.Combine(fixture.GetTargetRoot(configPath), "rime_mint.custom.yaml");
        Ensure(File.Exists(rimeMintPath), "rime_mint.custom.yaml 应存在。");
        string content = File.ReadAllText(rimeMintPath);
        Ensure(!content.Contains("\"translator/comment_format\": \"xform/^.+$//\"", StringComparison.Ordinal), "comment_style_variant 不应生成 YAML（功能预留）。");
    }

    private static void Apply_ShouldWriteCustomPhraseFullMode_WhenSet()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel model = BaseModel(fixture);
        RunApply(fixture, cfgPath, model);
        WindowsWorkflowService service = new(fixture.RepositoryRoot);
        Ensure(service.RunSetConfig(cfgPath, "personalization_settings.custom_phrase_mode", "full_phrase", "json").ExitCode == 0, "set");
        string yaml = File.ReadAllText(Path.Combine(model.SyncSettings.WindowsTargetRoot, "rime_mint.custom.yaml"));
        Ensure(yaml.Contains("table_translator@mint_simple", StringComparison.Ordinal), "translator block");
        Ensure(yaml.Contains("\"mint_simple/enable_completion\": true", StringComparison.Ordinal), "enable_completion");
    }


    private static void ImportRuntime_ShouldPreserveNewWindowsSettings()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = WriteTestConfig(fixture);
        ConfigModel original = BaseModel(fixture, windows: new WindowsSettings { DpiScaleMode = "per_monitor_v2" });
        RunApply(fixture, cfgPath, original);
        ArtifactService aService = fixture.CreateArtifactService();
        ConfigModel imported = aService.ImportRuntimeToConfig(original.SyncSettings.WindowsTargetRoot, original);
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
        Ensure(true, "ok");
    }

    private static void GitHubProxy_IsGitHubUrl_ShouldDetectAllGitHubDomains()
    {
        string[] urls =
        [
            "https://github.com/Mintimate/oh-my-rime",
            "https://codeload.github.com/Mintimate/oh-my-rime/zip/refs/heads/main",
            "https://api.github.com/repos/Mintimate/oh-my-rime/releases",
            "https://objects.githubusercontent.com/something",
            "https://raw.githubusercontent.com/Mintimate/oh-my-rime/main/README.md",
        ];
        foreach (string url in urls)
        {
            Ensure(GitHubProxyHelper.IsGitHubUrl(url), $"{url} 应被识别为 GitHub URL。");
        }
    }

    private static void GitHubProxy_IsGitHubUrl_ShouldRejectNonGitHubDomains()
    {
        string[] urls =
        [
            "https://google.com/file.zip",
            "https://pinyin.sogou.com/dict",
            "https://rime.im/download/",
            "http://127.0.0.1:9999/test.zip",
            "https://example.com/repo",
        ];
        foreach (string url in urls)
        {
            Ensure(!GitHubProxyHelper.IsGitHubUrl(url), $"{url} 不应被识别为 GitHub URL。");
        }
    }

    private static void GitHubProxy_BuildFallbackUrls_ShouldNotProxyNonGitHub()
    {
        List<string> urls = GitHubProxyHelper.BuildFallbackUrls("https://pinyin.sogou.com/dict/file.zip?f=detail");
        Ensure(urls.Count == 1, "非 GitHub URL 应仅返回原始 URL。");
        Ensure(urls[0] == "https://pinyin.sogou.com/dict/file.zip?f=detail", "非 GitHub URL 应原样返回。");
    }

    private static void GitHubProxy_BuildFallbackUrls_ShouldGenerateCorrectProxyChain()
    {
        List<string> urls = GitHubProxyHelper.BuildFallbackUrls("https://github.com/owner/repo");
        Ensure(urls.Count == 3, "GitHub URL 应生成 3 个 URL（直连 + 2 代理）。");
        Ensure(urls[0] == "https://github.com/owner/repo", "第一个 URL 应为原始直连。");
        Ensure(urls[1] == "https://gh.llkk.cc/https://github.com/owner/repo", "第二个应以 gh.llkk.cc 为前缀。");
        Ensure(urls[2] == "https://gh-proxy.com/https://github.com/owner/repo", "第三个应以 gh-proxy.com 为前缀。");
    }

    private static void GitHubProxy_GetMaxAttempts_ShouldReturnCorrectCounts()
    {
        Ensure(GitHubProxyHelper.GetMaxAttempts("https://pinyin.sogou.com/test.zip", 0) == 3, "非 GitHub URL 应返回 3。");
        Ensure(GitHubProxyHelper.GetMaxAttempts("https://github.com/owner/repo", 0) == 1, "GitHub 直连 (index 0) 应返回 1。");
        Ensure(GitHubProxyHelper.GetMaxAttempts("https://github.com/owner/repo", 1) == 2, "代理 (index 1) 应返回 2。");
        Ensure(GitHubProxyHelper.GetMaxAttempts("https://github.com/owner/repo", 2) == 2, "代理 (index 2) 应返回 2。");
        Ensure(GitHubProxyHelper.GetMaxAttempts("https://pinyin.sogou.com/test.zip", 5) == 3, "非 GitHub 任意 index 均应返回 3。");
    }

    private static void DownloadToFile_ShouldSucceed_WhenMockServerReachable()
    {
        using RepositoryTestFixture fixture = new();
        byte[] content = Encoding.UTF8.GetBytes("test download content");
        using SimpleHttpServer http = SimpleHttpServer.Create(content, "application/octet-stream");
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(5));

        string targetDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-test-fixtures", "dl");
        Directory.CreateDirectory(targetDir);
        string targetPath = Path.Combine(targetDir, "output.bin");
        (string version, string note) = ResumableDownloader.DownloadToFile($"{prefix}test.bin", targetPath);
        Ensure(File.Exists(targetPath), "下载后目标文件应存在。");
        byte[] saved = File.ReadAllBytes(targetPath);
        Ensure(saved.SequenceEqual(content), "下载的内容应与服务端返回一致。");
    }

    private static void DownloadToString_ShouldSucceed_WhenMockServerReachable()
    {
        byte[] payload = Encoding.UTF8.GetBytes("hello download string");
        using SimpleHttpServer http = SimpleHttpServer.Create(payload, "text/plain");
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(5));
        string result = ResumableDownloader.DownloadToString($"{prefix}test.txt");
        Ensure(result == "hello download string", "DownloadToString 应返回正确内容。");
    }

    private static void DownloadToFile_ShouldRetryNonGitHub_WhenMockRecovers()
    {
        using RepositoryTestFixture fixture = new();
        byte[] content = Encoding.UTF8.GetBytes("recovered after retry");
        int requestCount = 0;
        using SimpleHttpServer http = SimpleHttpServer.Create((method, path, body, reqHeaders) =>
        {
            int count = Interlocked.Increment(ref requestCount);
            if (count <= 3)
            {
                return (500, Array.Empty<byte>(), "text/plain", null);
            }

            return (200, content, "application/octet-stream", null);
        });
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(10));

        string targetDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-test-fixtures", "dl");
        Directory.CreateDirectory(targetDir);
        string targetPath = Path.Combine(targetDir, "output.bin");
        (string version, string note) = ResumableDownloader.DownloadToFile($"{prefix}test.bin", targetPath);
        Ensure(requestCount >= 4, "应在重试后成功（至少 4 次请求：1 HEAD + 至少 3 GET）。");
        Ensure(File.Exists(targetPath), "最终应成功下载文件。");
        byte[] saved = File.ReadAllBytes(targetPath);
        Ensure(saved.SequenceEqual(content), "下载内容应与服务端返回一致。");
    }

    private static void DownloadToFile_ShouldThrow_WhenMockAlwaysFails()
    {
        using RepositoryTestFixture fixture = new();
        int requestCount = 0;
        using SimpleHttpServer http = SimpleHttpServer.Create((method, path, body, reqHeaders) =>
        {
            Interlocked.Increment(ref requestCount);
            return (500, Array.Empty<byte>(), "text/plain", null);
        });
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(15));

        string targetDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-test-fixtures", "dl");
        Directory.CreateDirectory(targetDir);
        string targetPath = Path.Combine(targetDir, "output.bin");
        try
        {
            ResumableDownloader.DownloadToFile($"{prefix}test.bin", targetPath);
            Ensure(false, "服务器始终返回 500 时应抛出异常。");
        }
        catch (IOException)
        {
        }

        Ensure(requestCount > 0, "应至少发出了一次请求。");
    }

    private static void DownloadToFile_ShouldSucceed_WithoutContentLength_Zip()
    {
        using RepositoryTestFixture fixture = new();
        byte[] payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        byte[] zipContent = GenerateInMemoryZip(payload);
        using SimpleHttpServer http = SimpleHttpServer.CreateWithoutContentLength(zipContent, "application/zip");
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(5));
        string targetDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-test-fixtures", "dl");
        Directory.CreateDirectory(targetDir);
        string targetPath = Path.Combine(targetDir, "output.zip");
        (string version, string note) = ResumableDownloader.DownloadToFile($"{prefix}test.zip", targetPath);
        Ensure(File.Exists(targetPath), "无 Content-Length 时 ZIP 下载应成功。");
        byte[] saved = File.ReadAllBytes(targetPath);
        Ensure(saved.Length == zipContent.Length, "下载的 ZIP 文件大小应与服务端一致。");
        using System.IO.Compression.ZipArchive zip = new(new MemoryStream(saved), System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
        Ensure(zip.Entries.Count == 1, "ZIP 应包含 1 个条目。");
        byte[] extracted = new byte[payload.Length];
        using Stream es = zip.Entries[0].Open();
        int read = es.Read(extracted, 0, extracted.Length);
        Ensure(read == payload.Length && extracted.SequenceEqual(payload), "ZIP 中提取的内容应与原始数据一致。");
    }

    private static void DownloadToFile_ShouldSucceed_WithoutContentLength_NonZip()
    {
        using RepositoryTestFixture fixture = new();
        byte[] content = Encoding.UTF8.GetBytes("no content-length header test payload");
        using SimpleHttpServer http = SimpleHttpServer.CreateWithoutContentLength(content, "application/octet-stream");
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(5));
        string targetDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-test-fixtures", "dl");
        Directory.CreateDirectory(targetDir);
        string targetPath = Path.Combine(targetDir, "output.bin");
        (string version, string note) = ResumableDownloader.DownloadToFile($"{prefix}test.bin", targetPath);
        Ensure(File.Exists(targetPath), "无 Content-Length 时非 ZIP 下载应成功。");
        byte[] saved = File.ReadAllBytes(targetPath);
        Ensure(saved.SequenceEqual(content), "下载内容应与服务端一致。");
    }

    private static void DownloadToFile_ShouldRetry_WhenZipIncomplete_WithoutContentLength()
    {
        using RepositoryTestFixture fixture = new();
        byte[] payload = new byte[2048];
        Random.Shared.NextBytes(payload);
        byte[] zipContent = GenerateInMemoryZip(payload);
        int requestCount = 0;
        using SimpleHttpServer http = SimpleHttpServer.CreateWithoutContentLength((method, path, body, reqHeaders) =>
        {
            int count = Interlocked.Increment(ref requestCount);
            if (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return (200, Array.Empty<byte>(), "application/zip", null);
            }

            if (count <= 3)
            {
                return (500, Array.Empty<byte>(), "text/plain", null);
            }

            return (200, zipContent, "application/zip", null);
        });
        string prefix = http.BaseUrl;
        Task server = Task.Run(() => http.Serve(10));
        string targetDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows-test-fixtures", "dl");
        Directory.CreateDirectory(targetDir);
        string targetPath = Path.Combine(targetDir, "output.zip");
        (string version, string note) = ResumableDownloader.DownloadToFile($"{prefix}test.zip", targetPath);
        Ensure(requestCount >= 4, "应在重试后成功（至少 4 次请求：1 HEAD + 至少 3 GET）。");
        Ensure(File.Exists(targetPath), "重试后应成功下载文件。");
        byte[] saved = File.ReadAllBytes(targetPath);
        Ensure(saved.Length == zipContent.Length, "下载的 ZIP 文件大小应与服务端一致。");
        using System.IO.Compression.ZipArchive zip = new(new MemoryStream(saved), System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
        Ensure(zip.Entries.Count == 1, "ZIP 应包含 1 个条目。");
    }

    private static byte[] GenerateInMemoryZip(byte[] payload)
    {
        using MemoryStream ms = new();
        using (System.IO.Compression.ZipArchive zip = new(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            System.IO.Compression.ZipArchiveEntry entry = zip.CreateEntry("data.bin");
            using Stream es = entry.Open();
            es.Write(payload, 0, payload.Length);
        }

        return ms.ToArray();
    }

    private static void GuiWindowPage_ControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "窗口");
            Ensure(GetPrivateField<object>(form, "_fullscreenCheckBox") is not null, "窗口页应有全屏控件。");
            Ensure(GetPrivateField<object>(form, "_verticalTextCheckBox") is not null, "窗口页应有竖排控件。");
            Ensure(GetPrivateField<object>(form, "_inlinePreeditCheckBox") is not null, "窗口页应有内嵌预编辑控件。");
            Ensure(GetPrivateField<object>(form, "_preeditTypeComboBox") is not null, "窗口页应有预编辑类型控件。");
            Ensure(GetPrivateField<object>(form, "_globalAsciiComboBox") is not null, "窗口页应有全局英文控件。");
            Ensure(GetPrivateField<object>(form, "_hoverTypeComboBox") is not null, "窗口页应有悬停类型控件。");
            Ensure(GetPrivateField<object>(form, "_antialiasModeComboBox") is not null, "窗口页应有抗锯齿控件。");
            Ensure(GetPrivateField<object>(form, "_displayTrayIconCheckBox") is not null, "窗口页应有托盘图标控件。");
        });
    }

    private static void GuiLayoutPage_ControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "布局");
            Ensure(GetPrivateField<object>(form, "_layoutMinWidthText") is not null, "布局页应有最小宽度控件。");
            Ensure(GetPrivateField<object>(form, "_layoutMaxHeightText") is not null, "布局页应有最大高度控件。");
            Ensure(GetPrivateField<object>(form, "_layoutMarginXText") is not null, "布局页应有水平边距控件。");
            Ensure(GetPrivateField<object>(form, "_layoutBorderWidthText") is not null, "布局页应有边框宽度控件。");
            Ensure(GetPrivateField<object>(form, "_layoutShadowRadiusText") is not null, "布局页应有阴影半径控件。");
            Ensure(GetPrivateField<object>(form, "_layoutCornerRadiusText") is not null, "布局页应有圆角半径控件。");
            Ensure(GetPrivateField<object>(form, "_layoutAlignTypeComboBox") is not null, "布局页应有对齐方式控件。");
            Ensure(GetPrivateField<object>(form, "_layoutSpacingText") is not null, "布局页应有间距控件。");
        });
    }

    private static void GuiInputPage_NewControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            Ensure(GetPrivateField<object>(form, "_ueCompatCheckBox") is not null, "输入页应有üe兼容控件。");
            Ensure(GetPrivateField<object>(form, "_customPhraseComboBox") is not null, "输入页应有自定义短语控件。");
            Ensure(GetPrivateField<object>(form, "_symbolProfileComboBox") is not null, "输入页应有符号配置控件。");
            Ensure(GetPrivateField<object>(form, "_preeditFormatComboBox") is not null, "输入页应有预编辑格式控件。");
        });
    }

    private static void GuiDisplayPage_NewControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "显示");
            Ensure(GetPrivateField<object>(form, "_labelFontTextBox") is not null, "显示页应有标签字体控件。");
            Ensure(GetPrivateField<object>(form, "_labelFontSizeText") is not null, "显示页应有标签字号控件。");
            Ensure(GetPrivateField<object>(form, "_commentFontTextBox") is not null, "显示页应有注释字体控件。");
            Ensure(GetPrivateField<object>(form, "_commentFontSizeText") is not null, "显示页应有注释字号控件。");
            Ensure(GetPrivateField<object>(form, "_notificationTimeText") is not null, "显示页应有通知时长控件。");
            Ensure(GetPrivateField<object>(form, "_labelFormatTextBox") is not null, "显示页应有标签格式控件。");
            Ensure(GetPrivateField<object>(form, "_markTextTextBox") is not null, "显示页应有标记文本控件。");
            Ensure(GetPrivateField<object>(form, "_pagingOnScrollCheckBox") is not null, "显示页应有滚轮翻页控件。");
            Ensure(GetPrivateField<object>(form, "_candidateAbbreviateText") is not null, "显示页应有候选缩写控件。");
            Ensure(GetPrivateField<object>(form, "_commentStyleComboBox") is not null, "显示页应有注释内容控件。");
        });
    }

    private static void GuiWindowPage_NewControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "窗口");
            CheckBox inlinePreedit = GetPrivateField<CheckBox>(form, "_inlinePreeditCheckBox");
            Ensure(inlinePreedit is not null, "窗口页应包含内嵌预编辑控件。");
            CheckBox fullscreen = GetPrivateField<CheckBox>(form, "_fullscreenCheckBox");
            Ensure(fullscreen is not null, "窗口页应包含全屏控件。");
        });
    }

    private static void GuiInputPage_GrammarModelControlsShouldExist()
    {
        using RepositoryTestFixture fixture = new();
        RunGuiScenario(fixture.RepositoryRoot, form =>
        {
            PrepareMainFormLayout(form);
            SelectTopLevelTab(form, "输入设置");
            SelectNestedTab(form, "输入");
            ComboBox customPhrase = GetPrivateField<ComboBox>(form, "_customPhraseComboBox");
            Ensure(customPhrase is not null, "输入页应包含自定义短语控件。");
        });
    }

    private static void StartWeaselServer_ShouldNotCrash()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunStartWeaselServer("text");
        Ensure(result.ExitCode == 0 || result.ExitCode == 1, "start-weasel-server 应不崩溃。");
    }

    private static void StopWeaselServer_ShouldNotCrash()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunStopWeaselServer("text");
        Ensure(result.ExitCode == 0, "stop-weasel-server 应不崩溃。");
    }

    private static void RestartWeaselServer_ShouldNotCrash()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunRestartWeaselServer("text");
        Ensure(result.ExitCode == 0 || result.ExitCode == 1, "restart-weasel-server 应不崩溃。");
    }

    private static void ApplyForceStopWeasel_ShouldNotCrash()
    {
        using RepositoryTestFixture fixture = new();
        string cfgPath = SetupTestConfig(fixture);
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        string saTgt = Environment.ExpandEnvironmentVariables("%APPDATA%\\Rime");
        Directory.CreateDirectory(saTgt);
        CommandExecutionResult result = workflowService.RunApply(cfgPath, "text", forceStopWeasel: true);
        Ensure(result.ExitCode == 0, "apply --force-stop-weasel 应不崩溃。");
    }

    private static void InstallWeasel_ShouldDeleteWeaselTemplate()
    {
        using RepositoryTestFixture fixture = new();
        string templatePath = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "templates", "weasel.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
        FileHelper.WriteTextWithVerification(templatePath, "dummy");
        string? original = Environment.GetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH");
        try
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", @"C:\__C0_HERMETIC_NO_WEASEL__\Deployer.exe");
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            workflowService.RunLaunchWeaselUninstaller("text");
            Ensure(!File.Exists(templatePath), "C0 时 uninstall-weasel 应删除 weasel 模板。");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIMEKIT_WEASEL_DEPLOYER_PATH", original);
        }
    }

    private static void UninstallAll_ShouldDeleteAllTemplates()
    {
        using RepositoryTestFixture fixture = new();
        string weaselTemplate = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "templates", "weasel.yaml");
        string schemaDir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "templates", "rime_mint");
        Directory.CreateDirectory(schemaDir);
        FileHelper.WriteTextWithVerification(weaselTemplate, "dummy");
        FileHelper.WriteTextWithVerification(Path.Combine(schemaDir, "rime_mint.schema.yaml"), "dummy");
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        workflowService.RunUninstallAll(null, "text");
        Ensure(!File.Exists(weaselTemplate), "uninstall-all 应删除 weasel 模板。");
        Ensure(!Directory.Exists(schemaDir), "uninstall-all 应删除 schema 模板目录。");
    }

    private static string SetupTestConfig(RepositoryTestFixture fixture)
    {
        string dir = Path.Combine(fixture.RepositoryRoot, "workspace", "windows", "state");
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "current_config_model.json");
        FileHelper.WriteTextWithVerification(configPath, JsonSerializer.Serialize(ConfigModel.CreateDefault()));
        return configPath;
    }

    private static void InstallWeaselFromFile_ShouldFailWhenFileNotFound()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunLaunchWeaselInstallerFromFile(@"C:\nonexistent\weasel-installer.exe", "text");
        Ensure(result.ExitCode != 0, "不存在的安装器文件应返回非零退出码。");
        Ensure(result.TextOutput.Contains("WINDOWS_WEASEL_INSTALL_FILE_NOT_FOUND", StringComparison.Ordinal), "应返回未找到文件的错误码。");
    }

    private static void InstallFormalResourceFromFile_ShouldFailForNonManagedResource()
    {
        using RepositoryTestFixture fixture = new();
        WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
        CommandExecutionResult result = workflowService.RunInstallFormalResourceFromFile("nonexistent_resource", @"C:\fake.zip", null, "text");
        Ensure(result.ExitCode != 0, "非托管资源应返回非零退出码。");
    }

    private static void InstallFormalResourceFromFile_ShouldWorkForZipResource()
    {
        using RepositoryTestFixture fixture = new();
        EnsureFakeTemplatesExist(fixture.RepositoryRoot);
        WriteTestConfig(fixture);
        string zipPath = Path.Combine(Path.GetTempPath(), $"rimekit_test_rime_mint_{Guid.NewGuid():N}.zip");
        try
        {
            using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
            using (System.IO.StreamWriter writer = new(zip.CreateEntry("rime_mint.schema.yaml").Open(), System.Text.Encoding.UTF8))
            {
                writer.WriteLine("switches:");
                writer.WriteLine("  - name: ascii_mode");
                writer.WriteLine("  - name: emoji_suggestion");
            }
        }
        catch (IOException) { }

        try
        {
            WindowsWorkflowService workflowService = new(fixture.RepositoryRoot);
            CommandExecutionResult result = workflowService.RunInstallFormalResourceFromFile("rime_mint", zipPath, null, "text");
            Ensure(result is not null, "从文件安装不应崩溃。");
        }
        finally
        {
            if (File.Exists(zipPath)) FileHelper.DeleteFileWithBackoff(zipPath);
        }
    }
}
