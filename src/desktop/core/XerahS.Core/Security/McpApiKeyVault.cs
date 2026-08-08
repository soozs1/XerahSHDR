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

using System.Security.Cryptography;
using XerahS.Core.Managers;
using XerahS.Core.Uploaders;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Core.Security;

public static class McpApiKeyVault
{
    public const string ProviderId = "mcp";
    public const string SecretKey = "server";
    public const string Name = "api-key";

    public static string? Get()
    {
        var secrets = ProviderContextManager.EnsureProviderContext().Secrets;
        var stored = secrets.GetSecret(ProviderId, SecretKey, Name);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        var legacyValue = SettingsManager.Settings.McpApiKey;
        if (!string.IsNullOrWhiteSpace(legacyValue))
        {
            var migrated = MigrateLegacyValue(secrets, legacyValue);
            SettingsManager.Settings.McpApiKey = string.Empty;
            SettingsManager.SaveApplicationConfig();
            return migrated;
        }

        return null;
    }

    public static void Set(string value)
    {
        var secrets = ProviderContextManager.EnsureProviderContext().Secrets;
        secrets.SetSecret(ProviderId, SecretKey, Name, value);

        // Migration: if the legacy field still held an old value, it is already in
        // the store (Get() migrates on first access), so clearing it here is safe.
        SettingsManager.Settings.McpApiKey = string.Empty;
        SettingsManager.SaveApplicationConfig();
    }

    public static string EnsureGenerated()
    {
        var existing = Get();
        if (existing != null)
        {
            return existing;
        }

        var generated = Generate();
        Set(generated);
        return generated;
    }

    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    /// <summary>Generates and stores a new API key, replacing any existing key.</summary>
    public static string Rotate()
    {
        var value = Generate();
        Set(value);
        return value;
    }

    internal static string? MigrateLegacyValue(ISecretStore secrets, string? legacyValue)
    {
        if (!string.IsNullOrWhiteSpace(legacyValue))
        {
            secrets.SetSecret(ProviderId, SecretKey, Name, legacyValue);
            return legacyValue;
        }

        return secrets.GetSecret(ProviderId, SecretKey, Name);
    }
}