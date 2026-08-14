#region License Information (GPL v3)

/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using XerahS.Common;
using XerahS.Assistant.Configuration;
using XerahS.Assistant.Models;
using XerahS.Assistant.Providers;
using XerahS.Core;
using XerahS.Core.Security;
using XerahS.Core.SendTo;
using XerahS.Platform.Abstractions;

namespace XerahS.UI.ViewModels
{
    public partial class SettingsViewModel
    {
        private bool _isLoadingAssistantProvider;

        // Integration Settings
        [ObservableProperty]
        private bool _isPluginExtensionRegistered;

        [ObservableProperty]
        private bool _supportsFileAssociations;

        [ObservableProperty]
        private bool _supportsContextMenuIntegration;

        [ObservableProperty]
        private bool _supportsSendToIntegration;

        [ObservableProperty]
        private SendToFolderPolicy _sendToDefaultFolderPolicy;

        [ObservableProperty]
        private SendToBatchExecutionPolicy _sendToDefaultBatchPolicy;

        [ObservableProperty]
        private int _sendToDefaultBatchThreshold;

        public SendToFolderPolicy[] SendToDefaultFolderPolicyOptions =>
            (SendToFolderPolicy[])Enum.GetValues(typeof(SendToFolderPolicy));

        public SendToBatchExecutionPolicy[] SendToDefaultBatchPolicyOptions =>
            (SendToBatchExecutionPolicy[])Enum.GetValues(typeof(SendToBatchExecutionPolicy));

        public bool HasMcpApiKey => !string.IsNullOrWhiteSpace(McpApiKeyVault.Get());

        public bool HasRememberedSendToChoices => SettingsManager.Settings.SendToRememberedChoices.Count > 0;

        public string RememberedSendToChoicesSummary
        {
            get
            {
                var choices = SettingsManager.Settings.SendToRememberedChoices;
                if (choices.Count == 0)
                {
                    return Resources.GetString("SendTo_NoRememberedChoices");
                }

                return string.Join(
                    Environment.NewLine,
                    choices
                        .OrderBy(choice => choice.Scope)
                        .Select(choice =>
                            $"{SendToPolicyResolver.FormatRememberScope(choice.Scope)}: {FormatSendToAction(choice.Action)}, " +
                            $"{SendToPolicyResolver.FormatFolderPolicy(choice.FolderPolicy)}, " +
                            $"{SendToPolicyResolver.FormatBatchPolicy(choice.BatchExecutionPolicy, choice.BatchConfirmThreshold)}"));
            }
        }

        public string McpApiKeyDisplay
        {
            get
            {
                var apiKey = McpApiKeyVault.Get();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return Resources.GetString("Mcp_NotGenerated");
                }

                var suffixLength = Math.Min(4, apiKey.Length);
                return $"{new string('*', Math.Max(apiKey.Length - suffixLength, 0))}{apiKey[^suffixLength..]}";
            }
        }

        public string McpApiKeyStatusText => HasMcpApiKey
            ? Resources.GetString("Mcp_KeyConfigured")
            : Resources.GetString("Mcp_KeyNotGenerated");

        public string McpManifestUrl => "https://xerahs.com/.well-known/mcp/manifest.json";

        private string _assistantHotkeyStatusText = Resources.GetString("Assistant_HotkeyStatusText");
        private string _captureCommandPaletteStatusText = Resources.GetString("Assistant_PaletteStatusText");

        public ObservableCollection<AssistantProviderOptionViewModel> AssistantProviderOptions { get; } = new(
            AssistantProviderCatalog.GetProviders().Select(provider => new AssistantProviderOptionViewModel(provider.Id, provider.DisplayName)));

        [ObservableProperty]
        private AssistantProviderOptionViewModel? _selectedAssistantProvider;

        [ObservableProperty]
        private string _assistantProviderModelId = string.Empty;

        [ObservableProperty]
        private string _assistantProviderBaseUrl = string.Empty;

        [ObservableProperty]
        private string _assistantProviderApiKey = string.Empty;

        [ObservableProperty]
        private string _assistantProviderStatusText = Resources.GetString("Assistant_LocalCommandsAvailable");

        public bool AssistantEnabled
        {
            get => SettingsManager.Settings.AssistantEnabled;
            set
            {
                if (SettingsManager.Settings.AssistantEnabled == value)
                {
                    return;
                }

                SettingsManager.Settings.AssistantEnabled = value;
                OnPropertyChanged();
            }
        }

        public bool AssistantPromptHistoryEnabled
        {
            get => SettingsManager.Settings.AssistantPromptHistoryEnabled;
            set
            {
                if (SettingsManager.Settings.AssistantPromptHistoryEnabled == value)
                {
                    return;
                }

                SettingsManager.Settings.AssistantPromptHistoryEnabled = value;
                OnPropertyChanged();
            }
        }

        public string AssistantHotkeyText => SettingsManager.Settings.AssistantHotkey.GetDisplayString();

        public bool CaptureCommandPaletteEnabled
        {
            get => SettingsManager.Settings.CaptureCommandPaletteEnabled;
            set
            {
                if (SettingsManager.Settings.CaptureCommandPaletteEnabled == value)
                {
                    return;
                }

                SettingsManager.Settings.CaptureCommandPaletteEnabled = value;
                OnPropertyChanged();
            }
        }

        public string CaptureCommandPaletteHotkeyText => SettingsManager.Settings.CaptureCommandPaletteHotkey.GetDisplayString();

        public bool AssistantProviderNeedsApiKey => SelectedAssistantProvider?.Id != "ollama";

        public bool AssistantProviderHasApiKey =>
            SelectedAssistantProvider != null && AssistantProviderSecrets.HasApiKey(SelectedAssistantProvider.Id);

        public string AssistantProviderKeyStatus => AssistantProviderHasApiKey
            ? Resources.GetString("Assistant_KeyStored")
            : AssistantProviderNeedsApiKey
                ? Resources.GetString("Assistant_KeyNotConfigured")
                : Resources.GetString("Assistant_OllamaNoKeyNeeded");

        public string AssistantHotkeyStatusText
        {
            get => _assistantHotkeyStatusText;
            private set => SetProperty(ref _assistantHotkeyStatusText, value);
        }

        public string CaptureCommandPaletteStatusText
        {
            get => _captureCommandPaletteStatusText;
            private set => SetProperty(ref _captureCommandPaletteStatusText, value);
        }

        [RelayCommand]
        private void TestAssistantShortcut()
        {
            AssistantHotkeyStatusText = PlatformServices.IsInitialized
                ? Resources.GetString("Assistant_CurrentShortcut", AssistantHotkeyText)
                : Resources.GetString("Assistant_PlatformServicesNotInitialized");
        }

        [RelayCommand]
        private void TestCaptureCommandPaletteShortcut()
        {
            CaptureCommandPaletteStatusText = PlatformServices.IsInitialized
                ? Resources.GetString("Assistant_CurrentShortcut", CaptureCommandPaletteHotkeyText)
                : Resources.GetString("Assistant_PlatformServicesNotInitialized");
        }

        [RelayCommand]
        private void SaveAssistantProviderSettings()
        {
            if (SelectedAssistantProvider == null)
            {
                AssistantProviderStatusText = Resources.GetString("Assistant_SelectProviderFirst");
                return;
            }

            SaveSelectedAssistantProviderSettings();
            AssistantProviderStatusText = Resources.GetString("Assistant_ActiveProvider", SelectedAssistantProvider.DisplayName);
        }

        [RelayCommand]
        private async Task ValidateAssistantProviderAsync()
        {
            if (SelectedAssistantProvider == null)
            {
                AssistantProviderStatusText = Resources.GetString("Assistant_SelectProviderFirst");
                return;
            }

            SaveSelectedAssistantProviderSettings();
            if (!AssistantProviderSettingsResolver.TryGetActive(out AssistantProviderRuntimeSettings runtimeSettings))
            {
                AssistantProviderStatusText = AssistantProviderNeedsApiKey
                    ? Resources.GetString("Assistant_SaveApiKeyBeforeValidate")
                    : Resources.GetString("Assistant_SettingsIncomplete");
                return;
            }

            AssistantProviderStatusText = Resources.GetString("Assistant_Validating", SelectedAssistantProvider.DisplayName);
            IAssistantModelProvider provider = AssistantModelProviderFactory.Create(runtimeSettings);
            AssistantModelResult result = await provider.ValidateAsync(runtimeSettings.ModelId, CancellationToken.None);

            if (result.Kind == AssistantModelResultKind.Text)
            {
                AssistantProviderConfig config = AssistantProviderSettingsResolver.GetOrCreateConfig(runtimeSettings.Metadata.Id);
                config.LastValidatedAt = DateTime.UtcNow;
                SettingsManager.SaveApplicationConfig();
                AssistantProviderStatusText = Resources.GetString("Assistant_ValidatedSuccessfully", SelectedAssistantProvider.DisplayName);
            }
            else
            {
                AssistantProviderStatusText = result.Text ?? Resources.GetString("Assistant_ValidationFailed");
            }
        }

        [RelayCommand]
        private void RemoveAssistantProviderKey()
        {
            if (SelectedAssistantProvider == null)
            {
                return;
            }

            AssistantProviderSecrets.DeleteApiKey(SelectedAssistantProvider.Id);
            AssistantProviderApiKey = string.Empty;
            AssistantProviderStatusText = Resources.GetString("Assistant_ApiKeyRemoved", SelectedAssistantProvider.DisplayName);
            NotifyAssistantProviderSecretProperties();
        }

        partial void OnSelectedAssistantProviderChanged(AssistantProviderOptionViewModel? value)
        {
            if (_isLoadingAssistantProvider || value == null)
            {
                return;
            }

            LoadSelectedAssistantProviderSettings(value.Id);
        }

        private void LoadAssistantProviderSettings()
        {
            string providerId = string.IsNullOrWhiteSpace(SettingsManager.Settings.AssistantActiveProviderId)
                ? "openai"
                : SettingsManager.Settings.AssistantActiveProviderId;

            SelectedAssistantProvider = AssistantProviderOptions.FirstOrDefault(provider =>
                string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase)) ?? AssistantProviderOptions.FirstOrDefault();

            if (SelectedAssistantProvider != null)
            {
                LoadSelectedAssistantProviderSettings(SelectedAssistantProvider.Id);
            }
        }

        private void LoadSelectedAssistantProviderSettings(string providerId)
        {
            _isLoadingAssistantProvider = true;

            AssistantProviderMetadata? metadata = AssistantProviderCatalog.Find(providerId);
            AssistantProviderConfig config = AssistantProviderSettingsResolver.GetOrCreateConfig(providerId);
            AssistantProviderModelId = string.IsNullOrWhiteSpace(config.ModelId) ? metadata?.DefaultModelId ?? string.Empty : config.ModelId;
            AssistantProviderBaseUrl = string.IsNullOrWhiteSpace(config.BaseUrl) ? metadata?.DefaultBaseUrl ?? string.Empty : config.BaseUrl;
            AssistantProviderApiKey = string.Empty;
            AssistantProviderStatusText = string.Equals(SettingsManager.Settings.AssistantActiveProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                ? Resources.GetString("Assistant_ProviderActive", metadata?.DisplayName ?? providerId)
                : Resources.GetString("Assistant_ProviderSelected", metadata?.DisplayName ?? providerId);

            _isLoadingAssistantProvider = false;
            NotifyAssistantProviderSecretProperties();
        }

        private void SaveSelectedAssistantProviderSettings()
        {
            if (SelectedAssistantProvider == null)
            {
                return;
            }

            AssistantProviderConfig config = AssistantProviderSettingsResolver.GetOrCreateConfig(SelectedAssistantProvider.Id);
            config.ModelId = AssistantProviderModelId.Trim();
            config.BaseUrl = AssistantProviderBaseUrl.Trim().TrimEnd('/');
            SettingsManager.Settings.AssistantActiveProviderId = SelectedAssistantProvider.Id;

            if (AssistantProviderNeedsApiKey && !string.IsNullOrWhiteSpace(AssistantProviderApiKey))
            {
                AssistantProviderSecrets.SetApiKey(SelectedAssistantProvider.Id, AssistantProviderApiKey);
                AssistantProviderApiKey = string.Empty;
            }

            SettingsManager.SaveApplicationConfig();
            NotifyAssistantProviderSecretProperties();
        }

        private void NotifyAssistantProviderSecretProperties()
        {
            OnPropertyChanged(nameof(AssistantProviderNeedsApiKey));
            OnPropertyChanged(nameof(AssistantProviderHasApiKey));
            OnPropertyChanged(nameof(AssistantProviderKeyStatus));
        }

        partial void OnIsPluginExtensionRegisteredChanged(bool value)
        {
            if (_isLoading) return; // Don't trigger during initial load

            try
            {
                PlatformServices.ShellIntegration.SetPluginExtensionRegistration(value);
            }
            catch (InvalidOperationException)
            {
                // Shell integration not available on this platform
            }
        }

        [RelayCommand]
        private async Task CopyMcpApiKeyAsync()
        {
            var apiKey = McpApiKeyVault.Get();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = McpApiKeyVault.EnsureGenerated();
            }

            await PlatformServices.Clipboard.SetTextAsync(apiKey);
        }

        [RelayCommand]
        private async Task CopyMcpManifestUrlAsync()
        {
            await PlatformServices.Clipboard.SetTextAsync(McpManifestUrl);
        }

        [RelayCommand]
        private void GenerateMcpApiKey()
        {
            McpApiKeyVault.Rotate();
            SettingsManager.SaveApplicationConfig();
            NotifyMcpApiKeyChanged();
        }

        [RelayCommand]
        private void ClearRememberedSendToChoices()
        {
            SettingsManager.Settings.SendToRememberedChoices.Clear();
            SettingsManager.SaveApplicationConfig();
            NotifyRememberedSendToChoicesChanged();
        }

        private void NotifyMcpApiKeyChanged()
        {
            OnPropertyChanged(nameof(HasMcpApiKey));
            OnPropertyChanged(nameof(McpApiKeyDisplay));
            OnPropertyChanged(nameof(McpApiKeyStatusText));
        }

        private void NotifyRememberedSendToChoicesChanged()
        {
            OnPropertyChanged(nameof(HasRememberedSendToChoices));
            OnPropertyChanged(nameof(RememberedSendToChoicesSummary));
        }

        private static string FormatSendToAction(SendToAction action) => action switch
        {
            SendToAction.UploadNow => Resources.GetString("SendToAction_UploadNow"),
            SendToAction.OpenUploadContent => Resources.GetString("SendToAction_OpenUploadContent"),
            SendToAction.OpenImageEditor => Resources.GetString("SendToAction_OpenImageEditor"),
            SendToAction.PinToScreen => Resources.GetString("SendToAction_PinToScreen"),
            SendToAction.IndexFolders => Resources.GetString("SendToAction_IndexFolders"),
            SendToAction.Cancel => Resources.GetString("SendToAction_Cancel"),
            _ => action.ToString()
        };

        private static bool ApplyContextMenuPreference(bool enable)
        {
            try
            {
                if (!PlatformServices.IsInitialized)
                {
                    return false;
                }

                IShellIntegrationService? shellIntegration = PlatformServices.GetShellIntegrationIfAvailable();
                if (shellIntegration == null || !shellIntegration.SupportsContextMenuIntegration)
                {
                    return !enable;
                }

                return shellIntegration.SetContextMenuIntegration(enable);
            }
            catch (InvalidOperationException ex)
            {
                DebugHelper.WriteException(ex, "SettingsViewModel: ContextMenu platform services not ready.");
                return false;
            }
        }

        private static bool ApplySendToPreference(bool enable)
        {
            try
            {
                if (!PlatformServices.IsInitialized)
                {
                    return false;
                }

                IShellIntegrationService? shellIntegration = PlatformServices.GetShellIntegrationIfAvailable();
                if (shellIntegration == null || !shellIntegration.SupportsSendToIntegration)
                {
                    return !enable;
                }

                return shellIntegration.SetSendToIntegration(enable);
            }
            catch (InvalidOperationException ex)
            {
                DebugHelper.WriteException(ex, "SettingsViewModel: SendTo platform services not ready.");
                return false;
            }
        }

        private static bool ApplyStartupPreference(bool enable)
        {
            try
            {
                if (!PlatformServices.IsInitialized)
                {
                    return false;
                }

                return PlatformServices.Startup.SetRunAtStartup(enable);
            }
            catch (InvalidOperationException ex)
            {
                DebugHelper.WriteException(ex, "SettingsViewModel: RunAtStartup platform services not ready.");
                return false;
            }
        }
    }

    public sealed record AssistantProviderOptionViewModel(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
