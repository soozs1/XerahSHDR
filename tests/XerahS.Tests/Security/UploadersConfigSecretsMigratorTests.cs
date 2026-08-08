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
using ShareX.UploadersLib.FileUploaders;
using XerahS.Core.Security;
using XerahS.Uploaders;

namespace XerahS.Tests.Security;

[TestFixture]
public class UploadersConfigSecretsMigratorTests
{
    private static JsonSerializerSettings SerializerSettings => new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Auto,
        SerializationBinder = XerahS.Common.SettingsSerializationBinder.Shared
    };

    [Test]
    public void Migrate_MovesSecretsIntoStoreAndClearsConfig()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig
            {
                VgymeUserKey = "vgy-user-key-value",
                AmazonS3Settings = new AmazonS3Settings
                {
                    SecretAccessKey = "s3-secret-key-value"
                }
            };
            config.FTPAccountList.Add(new FTPAccount { Name = "srv", Password = "ftp-password-value" });
            config.EnsurePolymorphicSettingsInitialized();

            int migrated = UploadersConfigSecretsMigrator.Migrate(config, store);

            Assert.That(migrated, Is.GreaterThan(0));
            Assert.That(store.GetSecret("amazons3", "legacy", "secretAccessKey"), Is.EqualTo("s3-secret-key-value"));
            Assert.That(store.GetSecret("ftp", "legacy:accounts[0]", "password"), Is.EqualTo("ftp-password-value"));
            Assert.That(store.GetSecret("legacy", "VgymeUserKey", "value"), Is.EqualTo("vgy-user-key-value"));

            Assert.That(config.VgymeUserKey, Is.Empty);
            Assert.That(config.AmazonS3Settings.SecretAccessKey, Is.Empty);
            Assert.That(config.FTPAccountList[0].Password, Is.Empty);
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    [Test]
    public void Migrate_IsIdempotent_SecondCallReturnsZero()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig
            {
                VgymeUserKey = "vgy-user-key-value",
                AmazonS3Settings = new AmazonS3Settings
                {
                    SecretAccessKey = "s3-secret-key-value"
                }
            };
            config.FTPAccountList.Add(new FTPAccount { Name = "srv", Password = "ftp-password-value" });
            config.EnsurePolymorphicSettingsInitialized();

            int first = UploadersConfigSecretsMigrator.Migrate(config, store);
            int second = UploadersConfigSecretsMigrator.Migrate(config, store);

            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(0));
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    [Test]
    public void Migrate_ClearsOAuth2Token_WithoutStoringIt()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig
            {
                ImgurOAuth2Info = new OAuth2Info("client-id", "client-secret")
                {
                    Token = new OAuth2Token
                    {
                        access_token = "imgur-access-token",
                        refresh_token = "imgur-refresh-token"
                    }
                }
            };
            config.EnsurePolymorphicSettingsInitialized();

            int migrated = UploadersConfigSecretsMigrator.Migrate(config, store);

            // OAuth tokens are not stored, but clearing them still counts so that
            // MigrateIfNeeded persists the config after the one-time migration.
            Assert.That(migrated, Is.GreaterThan(0));
            Assert.That(config.ImgurOAuth2Info!.Token, Is.Null);

            // Secret store keys are stored in plaintext (only values are encrypted),
            // so "oauth" keys would be visible in the file if they were written.
            if (File.Exists(storePath))
            {
                string json = File.ReadAllText(storePath);
                Assert.That(json.ToLowerInvariant(), Does.Not.Contain("oauth"));
            }
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    [Test]
    public void Migrate_SerializedConfigContainsNoSecretValues()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig
            {
                VgymeUserKey = "vgy-user-key-value",
                AmazonS3Settings = new AmazonS3Settings
                {
                    SecretAccessKey = "s3-secret-key-value"
                }
            };
            config.FTPAccountList.Add(new FTPAccount { Name = "srv", Password = "ftp-password-value" });
            config.EnsurePolymorphicSettingsInitialized();

            UploadersConfigSecretsMigrator.Migrate(config, store);

            string json = JsonConvert.SerializeObject(config, Formatting.Indented, SerializerSettings);

            Assert.That(json, Does.Not.Contain("vgy-user-key-value"));
            Assert.That(json, Does.Not.Contain("s3-secret-key-value"));
            Assert.That(json, Does.Not.Contain("ftp-password-value"));
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    [Test]
    public void Migrate_MigratesNestedPastebinSecretsAndClearsThem()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig();
            config.PastebinSettings.Password = "pastebin-password-value";
            config.PastebinSettings.UserKey = "pastebin-userkey-value";

            int migrated = UploadersConfigSecretsMigrator.Migrate(config, store);

            Assert.That(migrated, Is.EqualTo(2));
            Assert.That(store.GetSecret("legacy", "PastebinSettings.Password", "value"), Is.EqualTo("pastebin-password-value"));
            Assert.That(store.GetSecret("legacy", "PastebinSettings.UserKey", "value"), Is.EqualTo("pastebin-userkey-value"));
            Assert.That(config.PastebinSettings.Password, Is.Empty);
            Assert.That(config.PastebinSettings.UserKey, Is.Empty);
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    [Test]
    public void Migrate_ClearsOAuth1Tokens_WithoutStoringThem()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig
            {
                FlickrOAuthInfo = new OAuthInfo("consumer-key", "consumer-secret")
                {
                    AuthSecret = "flickr-auth-secret",
                    AuthVerifier = "flickr-verifier",
                    UserToken = "flickr-user-token",
                    UserSecret = "flickr-user-secret"
                }
            };

            int migrated = UploadersConfigSecretsMigrator.Migrate(config, store);

            Assert.That(migrated, Is.EqualTo(5));
            Assert.That(config.FlickrOAuthInfo!.AuthSecret, Is.Empty);
            Assert.That(config.FlickrOAuthInfo.AuthVerifier, Is.Empty);
            Assert.That(config.FlickrOAuthInfo.UserSecret, Is.Empty);

            // OAuth1 secrets are cleared but never stored under legacy keys.
            Assert.That(store.GetSecret("legacy", "FlickrOAuthInfo.AuthSecret", "value"), Is.Null);
            Assert.That(store.GetSecret("legacy", "FlickrOAuthInfo.AuthVerifier", "value"), Is.Null);
            Assert.That(store.GetSecret("legacy", "FlickrOAuthInfo.UserSecret", "value"), Is.Null);

            if (File.Exists(storePath))
            {
                string json = File.ReadAllText(storePath);
                Assert.That(json, Does.Not.Contain("Flickr"));
                Assert.That(json, Does.Not.Contain("AuthSecret"));
            }
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    [Test]
    public void Migrate_OAuth2OnlyConfig_ReturnsPositiveCount()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig
            {
                DropboxOAuth2Info = new OAuth2Info("client-id", "client-secret")
                {
                    Token = new OAuth2Token
                    {
                        access_token = "dropbox-access-token"
                    }
                }
            };
            config.EnsurePolymorphicSettingsInitialized();

            int migrated = UploadersConfigSecretsMigrator.Migrate(config, store);

            Assert.That(migrated, Is.GreaterThan(0));
            Assert.That(config.DropboxOAuth2Info!.Token, Is.Null);
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    [Test]
    public void Migrate_MigratesZeroWidthShortenerToken()
    {
        string storePath = Path.Combine(Path.GetTempPath(), $"secmig_{Guid.NewGuid():N}.json");

        try
        {
            var store = new SecretStore(storePath);

            var config = new UploadersConfig
            {
                ZeroWidthShortenerToken = "zws-token-value"
            };

            int migrated = UploadersConfigSecretsMigrator.Migrate(config, store);

            Assert.That(migrated, Is.GreaterThanOrEqualTo(1));
            Assert.That(store.GetSecret("legacy", "ZeroWidthShortenerToken", "value"), Is.EqualTo("zws-token-value"));
            Assert.That(config.ZeroWidthShortenerToken, Is.Empty);
        }
        finally
        {
            DeleteStoreFiles(storePath);
        }
    }

    private static void DeleteStoreFiles(string storePath)
    {
        if (File.Exists(storePath))
        {
            File.Delete(storePath);
        }

        string? directory = Path.GetDirectoryName(storePath);
        if (!string.IsNullOrEmpty(directory))
        {
            string keyPath = Path.Combine(directory, "SecretsStore.key");
            if (File.Exists(keyPath))
            {
                File.Delete(keyPath);
            }
        }
    }
}