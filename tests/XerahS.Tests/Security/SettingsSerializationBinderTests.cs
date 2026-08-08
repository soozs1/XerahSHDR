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
using NUnit.Framework;
using XerahS.Common;
using XerahS.Uploaders;
using XerahS.Uploaders.Configuration;

namespace XerahS.Tests.Security;

[TestFixture]
public class SettingsSerializationBinderTests
{
    [SetUp]
    public void SetUp()
    {
        CanaryType.InstantiationCount = 0;
    }

    private static JsonSerializerSettings CreateBinderSettings()
    {
        return new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = SettingsSerializationBinder.Shared
        };
    }

    [Test]
    public void BindToType_TypeFromDisallowedAssembly_Throws()
    {
        JsonSerializationException? exception = Assert.Throws<JsonSerializationException>(() =>
            SettingsSerializationBinder.Shared.BindToType("System.Diagnostics.Process", "System.Diagnostics.Process"));

        Assert.That(exception!.Message, Does.Contain("Blocked or unknown settings type"));
    }

    [Test]
    public void Deserialize_TypeFromTestAssembly_IsBlockedWithoutInstantiation()
    {
        // The XerahS.Tests assembly is intentionally NOT on the binder allow-list, so this
        // verifies the blocking mechanism itself: the "$type" payload must be rejected before
        // any instance of CanaryType is created.
        string json = """
            {
              "$type": "XerahS.Tests.Security.SettingsSerializationBinderTests+CanaryType, XerahS.Tests",
              "Value": 42
            }
            """;

        object? result = null;
        JsonSerializationException? exception = Assert.Catch<JsonSerializationException>(() =>
        {
            result = JsonConvert.DeserializeObject<object>(json, CreateBinderSettings());
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(result, Is.Null);
            Assert.That(CanaryType.InstantiationCount, Is.Zero);
        });
    }

    [Test]
    public void UploadersConfig_WithPolymorphicServiceSettings_RoundTrips()
    {
        var source = new UploadersConfig();
        var ftpConfig = new FtpConfig
        {
            Name = "Roundtrip FTP"
        };
        ftpConfig.AccountList.Add(new ShareX.UploadersLib.FileUploaders.FTPAccount
        {
            Name = "account",
            Host = "ftp.example.com",
            Port = 2121,
            Username = "user"
        });
        source.ServiceSettings[UploaderType.FTP] = ftpConfig;

        JsonSerializerSettings settings = CreateBinderSettings();

        string json = JsonConvert.SerializeObject(source, settings);
        UploadersConfig? clone = JsonConvert.DeserializeObject<UploadersConfig>(json, settings);

        Assert.That(clone, Is.Not.Null);

        FtpConfig? clonedFtp = clone!.GetServiceSettings<FtpConfig>(UploaderType.FTP);
        Assert.That(clonedFtp, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(clonedFtp!.Id, Is.EqualTo(ftpConfig.Id));
            Assert.That(clonedFtp.Name, Is.EqualTo("Roundtrip FTP"));
            Assert.That(clonedFtp.AccountList, Has.Count.EqualTo(1));
            Assert.That(clonedFtp.AccountList[0].Host, Is.EqualTo("ftp.example.com"));
            Assert.That(clonedFtp.AccountList[0].Port, Is.EqualTo(2121));
            Assert.That(clonedFtp.AccountList[0].Username, Is.EqualTo("user"));
        });
    }

    [Test]
    public void BindToType_LegacyFullNames_ResolveIgnoringDeclaredAssembly()
    {
        // Legacy ShareX configs carry historical assembly names like "ShareX.UploadersLib".
        // The binder must ignore the declared assembly name and resolve by type name only.
        Type legacyStub = SettingsSerializationBinder.Shared.BindToType(
            "ShareX.UploadersLib", "ShareX.UploadersLib.FileUploaders.FTPAccount");
        Type current = SettingsSerializationBinder.Shared.BindToType(
            "Totally.Unrelated.Assembly", "XerahS.Uploaders.FileUploaders.FTPAccount");

        Assert.Multiple(() =>
        {
            Assert.That(legacyStub, Is.EqualTo(typeof(ShareX.UploadersLib.FileUploaders.FTPAccount)));
            Assert.That(current, Is.EqualTo(typeof(XerahS.Uploaders.FileUploaders.FTPAccount)));
        });
    }

    [Test]
    public void BindToType_ShortTypeName_Resolves()
    {
        Type resolved = SettingsSerializationBinder.Shared.BindToType(null, "FtpConfig");

        Assert.That(resolved, Is.EqualTo(typeof(FtpConfig)));
    }

    public sealed class CanaryType
    {
        public static int InstantiationCount;

        public CanaryType()
        {
            InstantiationCount++;
        }

        public int Value { get; set; }
    }
}
