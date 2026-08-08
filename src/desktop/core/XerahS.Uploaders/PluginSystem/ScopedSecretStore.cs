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

using XerahS.Common;

namespace XerahS.Uploaders.PluginSystem;

/// <summary>
/// Restricts access to an <see cref="ISecretStore"/> to secrets belonging to a single provider.
/// Any access attempt with a mismatched provider id is denied and logged.
/// </summary>
public sealed class ScopedSecretStore : ISecretStore
{
    private readonly ISecretStore _inner;
    private readonly string _providerId;

    public ScopedSecretStore(ISecretStore inner, string providerId)
    {
        _inner = inner;
        _providerId = providerId;
    }

    public string? GetSecret(string providerId, string secretKey, string name)
    {
        if (!HasAccess(providerId))
        {
            return null;
        }

        return _inner.GetSecret(providerId, secretKey, name);
    }

    public void SetSecret(string providerId, string secretKey, string name, string value)
    {
        if (!HasAccess(providerId))
        {
            return;
        }

        _inner.SetSecret(providerId, secretKey, name, value);
    }

    public void DeleteSecret(string providerId, string secretKey, string name)
    {
        if (!HasAccess(providerId))
        {
            return;
        }

        _inner.DeleteSecret(providerId, secretKey, name);
    }

    public bool HasSecret(string providerId, string secretKey, string name)
    {
        if (!HasAccess(providerId))
        {
            return false;
        }

        return _inner.HasSecret(providerId, secretKey, name);
    }

    private bool HasAccess(string providerId)
    {
        if (string.Equals(providerId, _providerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        DebugHelper.WriteLine("[Secrets] Scoped access denied: provider '{0}' attempted to access secrets outside its scope (scoped to '{1}')", providerId, _providerId);
        return false;
    }
}