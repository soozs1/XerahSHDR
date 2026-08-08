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

namespace XerahS.Platform.Abstractions;

/// <summary>
/// Central guard for URLs that come from untrusted sources (uploader responses, history entries,
/// toast configuration, other server-provided data) and may be handed to the operating system shell.
/// </summary>
public static class UrlSafety
{
    /// <summary>
    /// Determines whether a URL is safe to open through the OS shell / default browser.
    /// Only absolute http/https URLs are accepted.
    /// </summary>
    /// <remarks>
    /// All other schemes are rejected because server-provided data must never be able to:
    /// <list type="bullet">
    /// <item><description>open or exfiltrate local files via <c>file://</c> URLs,</description></item>
    /// <item><description>force UNC connections such as <c>\\server\share</c>, which on Windows
    /// leak the user's NTLM hash to a remote host,</description></item>
    /// <item><description>invoke arbitrary custom protocol handlers (<c>steam://</c>,
    /// <c>ms-settings:</c>, <c>vbscript:</c>, etc.),</description></item>
    /// <item><description>execute script or inline-content schemes such as <c>javascript:</c> or
    /// <c>data:</c>.</description></item>
    /// </list>
    /// Length is intentionally not part of the check: a URL longer than 2083 characters must pass
    /// the exact same scheme allowlist as a short one.
    /// </remarks>
    /// <param name="url">The URL to validate. May be null or empty.</param>
    /// <returns>True only for absolute http/https URLs; otherwise false.</returns>
    public static bool IsSafeExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
