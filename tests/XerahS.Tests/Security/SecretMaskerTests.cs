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
using XerahS.Common.Helpers;

namespace XerahS.Tests.Security;

public class SecretMaskerTests
{
    [Test]
    public void MaskSecrets_WhenJsonContainsSecretKeys_MasksValues()
    {
        string input = "{\"access_token\": \"secret-token-123\", \"password\": \"hunter2\", \"authorization\": \"Bearer abcdef\"}";

        string masked = SecretMasker.MaskSecrets(input);

        Assert.That(masked, Does.Contain("\"access_token\": \"***\""));
        Assert.That(masked, Does.Contain("\"password\": \"***\""));
        Assert.That(masked, Does.Contain("\"authorization\": \"***\""));
        Assert.That(masked, Does.Not.Contain("secret-token-123"));
        Assert.That(masked, Does.Not.Contain("hunter2"));
        Assert.That(masked, Does.Not.Contain("Bearer abcdef"));
    }

    [Test]
    public void MaskSecrets_WhenJsonContainsOtherSecretKeys_MasksValues()
    {
        string input = "{\"data\": {\"refresh_token\": \"r1\", \"api_key\": \"k1\", \"client_secret\": \"c1\", \"secret\": \"s1\"}}";

        string masked = SecretMasker.MaskSecrets(input);

        Assert.That(masked, Does.Contain("\"refresh_token\": \"***\""));
        Assert.That(masked, Does.Contain("\"api_key\": \"***\""));
        Assert.That(masked, Does.Contain("\"client_secret\": \"***\""));
        Assert.That(masked, Does.Not.Contain("\"r1\""));
        Assert.That(masked, Does.Not.Contain("\"k1\""));
    }

    [Test]
    public void MaskSecrets_WhenJsonContainsNonSecretKeys_LeavesValuesUntouched()
    {
        string input = "{\"url\": \"https://example.com/capture.png\", \"expires_in\": 3600, \"message\": \"ok\"}";

        string masked = SecretMasker.MaskSecrets(input);

        Assert.That(masked, Is.EqualTo(input));
    }

    [Test]
    public void MaskSecrets_WhenPlainTextWithoutSecrets_ReturnsTextUnchanged()
    {
        string input = "The quick brown fox jumps over the lazy dog. Update your password to stay safe.";

        string masked = SecretMasker.MaskSecrets(input);

        Assert.That(masked, Is.EqualTo(input));
    }

    [Test]
    public void MaskSecrets_WhenJwtTokenInText_MasksToken()
    {
        string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
        string input = $"Server rejected token: {jwt}";

        string masked = SecretMasker.MaskSecrets(input);

        Assert.That(masked, Does.Contain("Server rejected token: ***"));
        Assert.That(masked, Does.Not.Contain(jwt));
    }

    [Test]
    public void MaskSecrets_WhenLongHexSequenceInText_MasksSequence()
    {
        string secret = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
        string input = $"Auth failed with digest {secret}";

        string masked = SecretMasker.MaskSecrets(input);

        Assert.That(masked, Does.Contain("Auth failed with digest ***"));
        Assert.That(masked, Does.Not.Contain(secret));
    }

    [Test]
    public void MaskSecrets_WhenNull_ReturnsEmptyString()
    {
        Assert.That(SecretMasker.MaskSecrets(null), Is.Empty);
    }

    [Test]
    public void MaskSecrets_WhenEmptyOrWhitespace_ReturnsEmptyString()
    {
        Assert.That(SecretMasker.MaskSecrets(string.Empty), Is.Empty);
        Assert.That(SecretMasker.MaskSecrets("   "), Is.Empty);
    }
}