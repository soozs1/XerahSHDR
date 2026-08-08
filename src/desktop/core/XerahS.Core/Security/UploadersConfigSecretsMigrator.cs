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

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using XerahS.Common;
using XerahS.Core.Uploaders;
using XerahS.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Core.Security;

/// <summary>
/// One-time migration of plaintext secrets from UploadersConfig into the SecretStore.
/// After a successful migration the legacy values are cleared so they no longer
/// appear in UploadersConfig.json or its backups.
/// </summary>
public static class UploadersConfigSecretsMigrator
{
    private const string ValueName = "value";
    private const int MaxDepth = 4;

    // Nested properties that are handled by dedicated steps with their own key schemes
    // (read by the mobile code and the instance migrator) — excluded from the generic walk.
    private static readonly HashSet<string> RootExcludedProperties = new(StringComparer.Ordinal)
    {
        "AmazonS3Settings",
        "FTPAccountList",
        "LocalhostAccountList",
        "ServiceSettings",
        "CustomUploaders"
    };

    /// <summary>
    /// Migrates all legacy secrets found in <paramref name="config"/> into <paramref name="store"/>
    /// and clears them from the config. Returns the total number of migrated values.
    /// </summary>
    public static int Migrate(UploadersConfig config, ISecretStore store)
    {
        if (config == null || store == null)
        {
            return 0;
        }

        int count = 0;

        // Step A: direct [JsonEncrypt] string properties on UploadersConfig
        count += MigrateDirectSecrets(config, store);

        // Step B: AmazonS3Settings.SecretAccessKey
        count += MigrateAmazonS3(config, store);

        // Step C: FTPAccountList and LocalhostAccountList
        count += MigrateAccountList(config.FTPAccountList, "ftp", store);
        count += MigrateAccountList(config.LocalhostAccountList, "localhost", store);

        // Step D/E: recursive walk of nested settings objects (PastebinSettings, PlikSettings,
        // ImageShackSettings, MegaAuthInfos, ...), polymorphic ServiceSettings/CustomUploaders
        // and OAuth info. OAuth tokens are only cleared here (never stored — live tokens are
        // managed by the provider plugins' SecretStore usage), but every cleared value still
        // counts toward the migrated total so the config is persisted after the migration.
        count += MigrateNestedObjects(config, store);

        return count;
    }

    /// <summary>
    /// Orchestrator: migrates SettingsManager.UploadersConfig into the provider context
    /// SecretStore and saves the config if anything was migrated.
    /// </summary>
    public static bool MigrateIfNeeded()
    {
        try
        {
            var config = SettingsManager.UploadersConfig;
            if (config == null)
            {
                return false;
            }

            var context = ProviderContextManager.EnsureProviderContext();
            var secrets = context?.Secrets;
            if (secrets == null)
            {
                return false;
            }

            int migrated = Migrate(config, secrets);
            if (migrated > 0)
            {
                DebugHelper.WriteLine($"[UploadersConfigSecretsMigrator] Migrated {migrated} legacy secret(s) to SecretStore; saving UploadersConfig.");
                SettingsManager.SaveUploadersConfig();
            }

            return migrated > 0;
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e, "UploadersConfigSecretsMigrator: one-time migration failed");
            return false;
        }
    }

    private static int MigrateDirectSecrets(UploadersConfig config, ISecretStore store)
    {
        int count = 0;

        foreach (PropertyInfo property in typeof(UploadersConfig).GetProperties())
        {
            try
            {
                if (property.GetCustomAttribute<JsonEncryptAttribute>() == null || property.PropertyType != typeof(string))
                {
                    continue;
                }

                string? value = property.GetValue(config) as string;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                store.SetSecret("legacy", property.Name, ValueName, value);
                property.SetValue(config, string.Empty);
                count++;
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, $"UploadersConfigSecretsMigrator: failed to migrate direct property {property.Name}");
            }
        }

        return count;
    }

    private static int MigrateAmazonS3(UploadersConfig config, ISecretStore store)
    {
        object? settings = config.AmazonS3Settings;
        if (settings == null)
        {
            return 0;
        }

        PropertyInfo? secretKeyProperty = settings.GetType().GetProperty("SecretAccessKey");
        if (secretKeyProperty == null || secretKeyProperty.PropertyType != typeof(string))
        {
            return 0;
        }

        try
        {
            string? value = secretKeyProperty.GetValue(settings) as string;
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            store.SetSecret("amazons3", "legacy", "secretAccessKey", value);
            secretKeyProperty.SetValue(settings, string.Empty);
            return 1;
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e, "UploadersConfigSecretsMigrator: failed to migrate AmazonS3 SecretAccessKey");
            return 0;
        }
    }

    private static int MigrateAccountList(IEnumerable accounts, string providerId, ISecretStore store)
    {
        int count = 0;
        int index = 0;

        foreach (object? account in accounts)
        {
            if (account != null)
            {
                foreach (PropertyInfo property in account.GetType().GetProperties())
                {
                    try
                    {
                        if (property.GetCustomAttribute<JsonEncryptAttribute>() == null || property.PropertyType != typeof(string))
                        {
                            continue;
                        }

                        string? value = property.GetValue(account) as string;
                        if (string.IsNullOrEmpty(value))
                        {
                            continue;
                        }

                        store.SetSecret(providerId, $"legacy:accounts[{index}]", property.Name.ToLowerInvariant(), value);
                        property.SetValue(account, string.Empty);
                        count++;
                    }
                    catch (Exception e)
                    {
                        DebugHelper.WriteException(e, $"UploadersConfigSecretsMigrator: failed to migrate {providerId} account [{index}] property {property.Name}");
                    }
                }
            }

            index++;
        }

        return count;
    }

    /// <summary>
    /// Recursively migrates [JsonEncrypt] string values found on <paramref name="target"/> and its
    /// nested object/collection properties. OAuth info objects are never stored here — their tokens
    /// are managed by the provider plugins' SecretStore usage — so they are only cleared.
    /// </summary>
    private static int WalkAndMigrate(object target, string propPath, ISecretStore store, HashSet<object> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(target))
        {
            return 0;
        }

        Type type = target.GetType();
        int count = 0;

        // OAuth2Info: drop the whole token object without storing it.
        if (type.Name.EndsWith("OAuth2Info", StringComparison.Ordinal))
        {
            return ClearOAuth2Token(target) ? 1 : 0;
        }

        bool clearOnly = IsOAuthClearOnlyType(type);

        foreach (PropertyInfo property in type.GetProperties())
        {
            try
            {
                if (property.GetCustomAttribute<JsonEncryptAttribute>() != null)
                {
                    if (property.PropertyType != typeof(string))
                    {
                        continue;
                    }

                    string? value = property.GetValue(target) as string;
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    if (clearOnly)
                    {
                        property.SetValue(target, string.Empty);
                        count++;
                        continue;
                    }

                    store.SetSecret("legacy", $"{propPath}.{property.Name}", ValueName, value);
                    property.SetValue(target, string.Empty);
                    count++;
                    continue;
                }

                // OAuth1 info: also clear plaintext token fields (never stored) so no
                // credentials survive in UploadersConfig.json.
                if (clearOnly && IsOAuth1ClearOnlyProperty(type, property))
                {
                    string? value = property.GetValue(target) as string;
                    if (!string.IsNullOrEmpty(value))
                    {
                        property.SetValue(target, string.Empty);
                        count++;
                    }

                    continue;
                }

                object? child = property.GetValue(target);
                if (child == null)
                {
                    continue;
                }

                if (child is IEnumerable enumerable && child is not string)
                {
                    int index = 0;
                    foreach (object? item in enumerable)
                    {
                        if (item != null)
                        {
                            count += WalkAndMigrate(item, $"{propPath}.{property.Name}[{index}]", store, visited, depth + 1);
                        }

                        index++;
                    }
                }
                else if (IsComplexType(property.PropertyType))
                {
                    count += WalkAndMigrate(child, $"{propPath}.{property.Name}", store, visited, depth + 1);
                }
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, $"UploadersConfigSecretsMigrator: failed to migrate property {property.Name} of {propPath}");
            }
        }

        return count;
    }

    /// <summary>
    /// Walks all nested settings objects of <paramref name="config"/> (except the ones handled by
    /// the dedicated Step A/B/C methods) plus the polymorphic ServiceSettings and CustomUploaders,
    /// which use their runtime type name as the path prefix.
    /// </summary>
    private static int MigrateNestedObjects(UploadersConfig config, ISecretStore store)
    {
        var visited = new HashSet<object>();
        int count = 0;

        foreach (PropertyInfo property in typeof(UploadersConfig).GetProperties())
        {
            if (RootExcludedProperties.Contains(property.Name))
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(config);
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, $"UploadersConfigSecretsMigrator: failed to read property {property.Name}");
                continue;
            }

            if (value == null)
            {
                continue;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                int index = 0;
                foreach (object? item in enumerable)
                {
                    if (item != null)
                    {
                        count += WalkAndMigrate(item, $"{property.Name}[{index}]", store, visited, 0);
                    }

                    index++;
                }

                continue;
            }

            count += WalkAndMigrate(value, property.Name, store, visited, 0);
        }

        if (config.ServiceSettings != null)
        {
            foreach (var pair in config.ServiceSettings)
            {
                if (pair.Value != null)
                {
                    count += WalkAndMigrate(pair.Value, pair.Value.GetType().Name, store, visited, 0);
                }
            }
        }

        if (config.CustomUploaders != null)
        {
            foreach (var custom in config.CustomUploaders)
            {
                if (custom != null)
                {
                    count += WalkAndMigrate(custom, custom.GetType().Name, store, visited, 0);
                }
            }
        }

        return count;
    }

    private static bool ClearOAuth2Token(object oauth2Info)
    {
        try
        {
            PropertyInfo? tokenProperty = oauth2Info.GetType().GetProperty("Token");
            if (tokenProperty == null || tokenProperty.GetValue(oauth2Info) == null)
            {
                return false;
            }

            // Token is declared non-nullable (= null!) but is legitimately nulled here.
            tokenProperty.SetValue(oauth2Info, null);
            return true;
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e, $"UploadersConfigSecretsMigrator: failed to clear OAuth2 token on {oauth2Info.GetType().Name}");
            return false;
        }
    }

    private static bool IsOAuthClearOnlyType(Type type)
    {
        string name = type.Name;
        return name.EndsWith("OAuthInfo", StringComparison.Ordinal) ||
            name.EndsWith("OAuth2Info", StringComparison.Ordinal) ||
            name == "OAuth2Token";
    }

    private static bool IsOAuth1ClearOnlyProperty(Type type, PropertyInfo property)
    {
        return type.Name.EndsWith("OAuthInfo", StringComparison.Ordinal) &&
            property.PropertyType == typeof(string) &&
            (property.Name == "ConsumerSecret" ||
             property.Name == "ConsumerPrivateKey" ||
             property.Name == "UserToken" ||
             property.Name == "AuthToken");
    }

    private static bool IsComplexType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) ||
            type == typeof(decimal) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid))
        {
            return false;
        }

        return type.IsClass || type.IsInterface;
    }
}