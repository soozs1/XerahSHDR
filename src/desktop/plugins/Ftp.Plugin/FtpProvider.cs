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
using XerahS.Uploaders.FileUploaders;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Ftp.Plugin;

/// <summary>
/// FTP / FTPS / SFTP file uploader provider. Supports File, Image, and Text categories.
/// Uses FluentFTP (FTP/FTPS) and SSH.NET (SFTP) for robust, modern transfers.
/// Credentials (Password/Passphrase) are kept in ISecretStore and referenced by SecretKey.
/// </summary>
public class FtpProvider : UploaderProviderBase, IInstanceSecretMigrator
{
    public override string ProviderId => "ftp";
    public override string Name => "FTP / FTPS / SFTP";
    public override string Description => "Upload files via FTP, FTPS (FTP over SSL/TLS), or SFTP (SSH)";
    public override Version Version => new Version(1, 0, 0);
    public override UploaderCategory[] SupportedCategories => new[] { UploaderCategory.File, UploaderCategory.Image, UploaderCategory.Text };
    public override Type ConfigModelType => typeof(FtpConfigModel);

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
        jsonChanged |= TryMigrateSecret(json, secrets, secretKey, "Passphrase", "passphrase", ref migratedSecretCount);

        if (jsonChanged)
        {
            updatedSettingsJson = json.ToString(Formatting.Indented);
        }

        return jsonChanged;
    }

    public override Uploader CreateInstance(string settingsJson)
    {
        var config = JsonConvert.DeserializeObject<FtpConfigModel>(settingsJson);
        if (config == null)
            throw new InvalidOperationException("Failed to deserialize FTP settings");

        ResolveCredentials(config, settingsJson, out string password, out string passphrase);
        FTPAccount account = ToFtpAccount(config, password, passphrase);
        return new FtpUploader(account);
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

    private void ResolveCredentials(FtpConfigModel config, string settingsJson,
        out string password, out string passphrase)
    {
        password = string.Empty;
        passphrase = string.Empty;

        if (!string.IsNullOrWhiteSpace(config.SecretKey) && Secrets != null)
        {
            password = Secrets.GetSecret(ProviderId, config.SecretKey, "password") ?? string.Empty;
            passphrase = Secrets.GetSecret(ProviderId, config.SecretKey, "passphrase") ?? string.Empty;
        }

        // Legacy fallback: plaintext credentials may still be present in SettingsJson
        // when the instance has not been migrated to the secret store yet.
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passphrase))
        {
            TryGetLegacyCredentials(settingsJson, out string legacyPassword, out string legacyPassphrase);
            if (string.IsNullOrEmpty(password))
            {
                password = legacyPassword;
            }
            if (string.IsNullOrEmpty(passphrase))
            {
                passphrase = legacyPassphrase;
            }
        }
    }

    private static void TryGetLegacyCredentials(string settingsJson, out string password, out string passphrase)
    {
        password = string.Empty;
        passphrase = string.Empty;

        try
        {
            var json = JObject.Parse(settingsJson);
            password = json.Value<string>("Password") ?? string.Empty;
            passphrase = json.Value<string>("Passphrase") ?? string.Empty;
        }
        catch { }
    }

    private static FTPAccount ToFtpAccount(FtpConfigModel c, string password, string passphrase)
    {
        FTPProtocol protocol = NormalizeEnum(c.Protocol, FTPProtocol.FTP);
        BrowserProtocol browserProtocol = NormalizeEnum(c.BrowserProtocol, BrowserProtocol.http);
        FTPSEncryption ftpsEncryption = NormalizeEnum(c.FTPSEncryption, FTPSEncryption.Explicit);

        return new FTPAccount
        {
            Name = c.AccountName ?? "FTP Account",
            Protocol = protocol,
            Host = NormalizeHost(c.Host),
            Port = IsValidPort(c.Port) ? c.Port : GetDefaultPort(protocol, ftpsEncryption),
            Username = c.Username ?? "",
            Password = password,
            IsActive = c.IsActive,
            SubFolderPath = c.SubFolderPath ?? "",
            BrowserProtocol = browserProtocol,
            HttpHomePath = c.HttpHomePath ?? "",
            HttpHomePathAutoAddSubFolderPath = c.HttpHomePathAutoAddSubFolderPath,
            HttpHomePathNoExtension = c.HttpHomePathNoExtension,
            FTPSEncryption = ftpsEncryption,
            Keypath = c.Keypath ?? "",
            Passphrase = passphrase
        };
    }

    public override Dictionary<UploaderCategory, string[]> GetSupportedFileTypes()
    {
        return new Dictionary<UploaderCategory, string[]>
        {
            { UploaderCategory.File, new[] { "*" } },
            { UploaderCategory.Image, new[] { "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico" } },
            { UploaderCategory.Text, new[] { "txt", "log", "json", "xml", "md", "html", "css", "js", "cs" } }
        };
    }

    public override object? CreateConfigView()
    {
        return new Views.FtpConfigView();
    }

    public override IUploaderConfigViewModel? CreateConfigViewModel()
    {
        return new ViewModels.FtpConfigViewModel();
    }

    private static string NormalizeHost(string? host) => host?.Trim() ?? string.Empty;

    private static int GetDefaultPort(FTPProtocol protocol, FTPSEncryption ftpsEncryption)
    {
        return protocol switch
        {
            FTPProtocol.SFTP => 22,
            FTPProtocol.FTPS when ftpsEncryption == FTPSEncryption.Implicit => 990,
            _ => 21
        };
    }

    private static bool IsValidPort(int port) => port is > 0 and <= 65535;

    private static TEnum NormalizeEnum<TEnum>(TEnum value, TEnum fallback) where TEnum : struct, Enum
    {
        return Enum.IsDefined(typeof(TEnum), value) ? value : fallback;
    }
}
