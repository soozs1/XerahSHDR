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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Pastebin.Plugin;

/// <summary>
/// Pastebin text uploader provider (supports Text category only)
/// Credentials (Password/UserKey) are kept in ISecretStore and referenced by SecretKey.
/// </summary>
public class PastebinProvider : UploaderProviderBase, IInstanceSecretMigrator
{
    public override string ProviderId => "pastebin";
    public override string Name => "Pastebin";
    public override string Description => "Upload text to Pastebin";
    public override Version Version => new Version(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => new[] { UploaderCategory.Text };
    public override Type ConfigModelType => typeof(PastebinConfigModel);

    public bool TryMigrateSecrets(string settingsJson, ISecretStore secrets,
        out string updatedSettingsJson, out int migratedSecretCount)
    {
        updatedSettingsJson = settingsJson;
        migratedSecretCount = 0;

        JObject? json;
        try { json = JObject.Parse(settingsJson); }
        catch { return false; }

        var secretKey = json.Value<string>("SecretKey");
        bool jsonChanged = false;

        if (string.IsNullOrWhiteSpace(secretKey))
        {
            secretKey = Guid.NewGuid().ToString("N");
            json["SecretKey"] = secretKey;
            jsonChanged = true;
        }

        jsonChanged |= TryMigrateSecret(json, secrets, secretKey, "Password", "password", ref migratedSecretCount);
        jsonChanged |= TryMigrateSecret(json, secrets, secretKey, "UserKey", "userKey", ref migratedSecretCount);

        if (jsonChanged)
        {
            updatedSettingsJson = json.ToString(Formatting.Indented);
        }

        return jsonChanged;
    }

    public override Uploader CreateInstance(string settingsJson)
    {
        var config = JsonConvert.DeserializeObject<PastebinConfigModel>(settingsJson);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize Pastebin settings");
        }

        ResolveCredentials(config, settingsJson);
        return new PastebinUploader(config);
    }

    private bool TryMigrateSecret(JObject json, ISecretStore secrets, string secretKey,
        string jsonProperty, string secretName, ref int migratedSecretCount)
    {
        var value = json.Value<string>(jsonProperty);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            secrets.SetSecret(ProviderId, secretKey, secretName, value);
            if (secrets.HasSecret(ProviderId, secretKey, secretName))
            {
                json.Remove(jsonProperty);
                migratedSecretCount++;
                return true;
            }
        }
        catch { }

        return false;
    }

    private void ResolveCredentials(PastebinConfigModel config, string settingsJson)
    {
        if (!string.IsNullOrWhiteSpace(config.SecretKey) && Secrets != null)
        {
            config.UserKey = Secrets.GetSecret(ProviderId, config.SecretKey, "userKey") ?? string.Empty;
            config.Password = Secrets.GetSecret(ProviderId, config.SecretKey, "password") ?? string.Empty;
        }

        // Legacy fallback: plaintext credentials may still be present in SettingsJson
        // when the instance has not been migrated to the secret store yet.
        if (string.IsNullOrEmpty(config.UserKey) || string.IsNullOrEmpty(config.Password))
        {
            TryGetLegacyCredentials(settingsJson, out string legacyUserKey, out string legacyPassword);
            if (string.IsNullOrEmpty(config.UserKey))
            {
                config.UserKey = legacyUserKey;
            }
            if (string.IsNullOrEmpty(config.Password))
            {
                config.Password = legacyPassword;
            }
        }
    }

    private static void TryGetLegacyCredentials(string settingsJson, out string userKey, out string password)
    {
        userKey = string.Empty;
        password = string.Empty;

        try
        {
            var json = JObject.Parse(settingsJson);
            userKey = json.Value<string>("UserKey") ?? string.Empty;
            password = json.Value<string>("Password") ?? string.Empty;
        }
        catch { }
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes()
    {
        return new Dictionary<UploaderCategory, string[]>
        {
            { UploaderCategory.Text, new[] { "txt", "log", "json", "xml", "md", "html", "css", "js", "cs", "py", "sql" } }
        };
    }

    public override object? CreateConfigView()
    {
        return new Views.PastebinConfigView();
    }

    public override IUploaderConfigViewModel? CreateConfigViewModel()
    {
        return new ViewModels.PastebinConfigViewModel();
    }
}
