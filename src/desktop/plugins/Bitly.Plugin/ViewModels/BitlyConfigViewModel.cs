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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XerahS.Uploaders.PluginSystem;

namespace ShareX.Bitly.Plugin.ViewModels;

public partial class BitlyConfigViewModel : ObservableObject, IUploaderConfigViewModel, IProviderContextAware
{
    private string _secretKey = Guid.NewGuid().ToString("N");
    private ISecretStore? _secrets;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _domain = string.Empty;

    [ObservableProperty]
    private string _accessToken = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public void LoadFromJson(string json)
    {
        try
        {
            var config = JsonConvert.DeserializeObject<BitlyConfigModel>(json);
            if (config != null)
            {
                _secretKey = string.IsNullOrWhiteSpace(config.SecretKey) ? Guid.NewGuid().ToString("N") : config.SecretKey;
                ClientId = config.ClientId ?? string.Empty;
                ClientSecret = ResolveSecret(json, "ClientSecret", "clientSecret");
                Domain = config.Domain ?? string.Empty;
                AccessToken = ResolveSecret(json, "AccessToken", "accessToken");
            }
        }
        catch
        {
            StatusMessage = "Failed to load configuration";
        }
    }

    public string ToJson()
    {
        var config = new BitlyConfigModel
        {
            ClientId = ClientId ?? string.Empty,
            SecretKey = _secretKey,
            Domain = Domain ?? string.Empty
        };

        if (_secrets != null)
        {
            if (string.IsNullOrWhiteSpace(ClientSecret))
            {
                _secrets.DeleteSecret("bitly", _secretKey, "clientSecret");
            }
            else
            {
                _secrets.SetSecret("bitly", _secretKey, "clientSecret", ClientSecret);
            }

            if (string.IsNullOrWhiteSpace(AccessToken))
            {
                _secrets.DeleteSecret("bitly", _secretKey, "accessToken");
            }
            else
            {
                _secrets.SetSecret("bitly", _secretKey, "accessToken", AccessToken);
            }
        }

        return JsonConvert.SerializeObject(config, Formatting.Indented);
    }

    public void SetContext(IProviderContext context)
    {
        _secrets = context.Secrets;
    }

    private string ResolveSecret(string settingsJson, string legacyJsonProperty, string secretName)
    {
        if (!string.IsNullOrWhiteSpace(_secretKey) && _secrets != null)
        {
            string? stored = _secrets.GetSecret("bitly", _secretKey, secretName);
            if (!string.IsNullOrEmpty(stored))
            {
                return stored;
            }
        }

        return TryGetLegacyValue(settingsJson, legacyJsonProperty);
    }

    private static string TryGetLegacyValue(string settingsJson, string propertyName)
    {
        try
        {
            var json = JObject.Parse(settingsJson);
            return json.Value<string>(propertyName) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            StatusMessage = "Access token is required. Authorize at https://bitly.com/oauth/ then paste the token here.";
            return false;
        }

        StatusMessage = null;
        return true;
    }
}
