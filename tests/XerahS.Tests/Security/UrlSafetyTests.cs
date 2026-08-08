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
using XerahS.Common;
using XerahS.Platform.Abstractions;

namespace XerahS.Tests.Security;

[TestFixture]
public class UrlSafetyTests
{
    private static readonly string[] SafeUrls =
    [
        "http://example.com",
        "https://example.com",
        "http://example.com:8080",
        "https://example.com:8443/path/to/resource",
        "https://example.com/path?query=value&other=2",
        "http://sub.domain.example.co.uk/a/b?q=1#fragment"
    ];

    private static readonly string?[] UnsafeUrls =
    [
        null,
        "",
        "   ",
        "not a url at all",
        "file:///C:/secret.txt",
        @"\\attacker\share\file",
        "javascript:alert(1)",
        "vbscript:x",
        "steam://open",
        "ms-settings:",
        "data:text/html,<script>alert(1)</script>",
        "ftp://example.com/file.zip"
    ];

    private static string CreateLongJavascriptUrl()
    {
        // Longer than 2083 characters: must not bypass the scheme allowlist
        // the way the old URLHelpers.IsValidURL length branch did.
        return "javascript:" + new string('a', 2100);
    }

    [TestCaseSource(nameof(SafeUrls))]
    public void IsSafeExternalUrl_AcceptsHttpAndHttps(string url)
    {
        Assert.That(UrlSafety.IsSafeExternalUrl(url), Is.True, $"Expected URL to be accepted: {url}");
    }

    [TestCaseSource(nameof(UnsafeUrls))]
    public void IsSafeExternalUrl_RejectsUnsafeSchemes(string? url)
    {
        Assert.That(UrlSafety.IsSafeExternalUrl(url), Is.False, $"Expected URL to be rejected: {url}");
    }

    [Test]
    public void IsSafeExternalUrl_RejectsJavascriptUrlLongerThan2083Characters()
    {
        string url = CreateLongJavascriptUrl();

        Assert.Multiple(() =>
        {
            Assert.That(url.Length, Is.GreaterThan(2083));
            Assert.That(UrlSafety.IsSafeExternalUrl(url), Is.False);
        });
    }

    [TestCaseSource(nameof(SafeUrls))]
    public void IsValidURL_AcceptsHttpAndHttps(string url)
    {
        Assert.That(URLHelpers.IsValidURL(url), Is.True, $"Expected URL to be accepted: {url}");
    }

    [TestCaseSource(nameof(UnsafeUrls))]
    public void IsValidURL_RejectsUnsafeSchemes(string? url)
    {
        Assert.That(URLHelpers.IsValidURL(url!), Is.False, $"Expected URL to be rejected: {url}");
    }

    [Test]
    public void IsValidURL_RejectsJavascriptUrlLongerThan2083Characters()
    {
        string url = CreateLongJavascriptUrl();

        Assert.Multiple(() =>
        {
            Assert.That(url.Length, Is.GreaterThan(2083));
            Assert.That(URLHelpers.IsValidURL(url), Is.False);
        });
    }

    [Test]
    public void SteamScheme_IsRejectedAtAnyLength()
    {
        string longSteamUrl = "steam://open/" + new string('a', 2100);

        Assert.Multiple(() =>
        {
            Assert.That(longSteamUrl.Length, Is.GreaterThan(2083));
            Assert.That(URLHelpers.IsValidURL("steam://open"), Is.False);
            Assert.That(URLHelpers.IsValidURL(longSteamUrl), Is.False);
            Assert.That(UrlSafety.IsSafeExternalUrl("steam://open"), Is.False);
            Assert.That(UrlSafety.IsSafeExternalUrl(longSteamUrl), Is.False);
        });
    }
}
