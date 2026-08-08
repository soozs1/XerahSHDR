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

using NUnit.Framework;
using XerahS.Core.Security;

namespace XerahS.Tests.Security;

[TestFixture]
public class McpApiKeyVaultTests
{
    [Test]
    public void Generate_Returns48HexCharacters()
    {
        var apiKey = McpApiKeyVault.Generate();

        Assert.That(apiKey, Has.Length.EqualTo(48));
        Assert.That(apiKey, Does.Match("^[0-9A-F]{48}$"));
    }

    [Test]
    public void MigrateLegacyValue_MigratesLegacyValueIntoStoreAndReturnsIt()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"mcpvault_{Guid.NewGuid():N}.json");

        try
        {
            var secrets = new SecretStore(storePath);
            const string legacyValue = "legacy-api-key";

            var migrated = McpApiKeyVault.MigrateLegacyValue(secrets, legacyValue);

            Assert.That(migrated, Is.EqualTo(legacyValue));
            Assert.That(secrets.HasSecret("mcp", "server", "api-key"), Is.True);
            Assert.That(secrets.GetSecret("mcp", "server", "api-key"), Is.EqualTo(legacyValue));

            // Second call with an empty legacy value returns the value already stored.
            var stored = McpApiKeyVault.MigrateLegacyValue(secrets, null);
            Assert.That(stored, Is.EqualTo(legacyValue));
        }
        finally
        {
            if (File.Exists(storePath))
            {
                File.Delete(storePath);
            }
        }
    }
}