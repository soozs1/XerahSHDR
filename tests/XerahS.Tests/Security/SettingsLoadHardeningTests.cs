using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using XerahS.Common;

namespace XerahS.Tests.Security;

[TestFixture]
public class SettingsLoadHardeningTests
{
    private sealed class HardenedSettings : SettingsBase<HardenedSettings>
    {
        public object? Data { get; set; }

        public List<object> Items { get; set; } = new();
    }

    [Test]
    public void Load_ConfigFileLargerThanLimit_ReturnsFallbackDefault()
    {
        string filePath = CreateTempFilePath();
        try
        {
            // Valid JSON that would parse fine without the size cap: the giant string sits in an
            // unknown property (ignored by the deserializer), and ApplicationVersion would be
            // populated if the file were actually read.
            string oversizedJson =
                "{\"ApplicationVersion\":\"9.9.9\",\"Padding\":\"" +
                new string('x', (int)SettingsBase<HardenedSettings>.MaxConfigFileSize + 1024) +
                "\"}";
            File.WriteAllText(filePath, oversizedJson);

            HardenedSettings settings = HardenedSettings.Load(filePath, fallbackSupport: false);

            Assert.That(settings.ApplicationVersion, Is.Null,
                "An oversized settings file must be rejected (Load falls back to a fresh default), not parsed.");
        }
        finally
        {
            DeleteFile(filePath);
        }
    }

    [Test]
    public void Load_DeeplyNestedJson_ReturnsFallbackDefault()
    {
        string filePath = CreateTempFilePath();
        try
        {
            const int depth = 200; // well above MaxConfigDepth (64)
            StringBuilder json = new StringBuilder();
            json.Append("{\"Data\":");
            for (int i = 0; i < depth; i++)
            {
                json.Append("{\"x\":");
            }
            json.Append("null");
            for (int i = 0; i < depth; i++)
            {
                json.Append('}');
            }
            json.Append('}');
            File.WriteAllText(filePath, json.ToString());

            HardenedSettings settings = HardenedSettings.Load(filePath, fallbackSupport: false);

            Assert.That(settings.Data, Is.Null,
                "A settings file nested deeper than MaxConfigDepth must be rejected (Load falls back to a fresh default), not parsed.");
        }
        finally
        {
            DeleteFile(filePath);
        }
    }

    [Test]
    public void Load_RootTypeFromUnresolvedAssembly_FallsBackToDefault()
    {
        string filePath = CreateTempFilePath();
        try
        {
            File.WriteAllText(filePath, """{"$type":"System.Diagnostics.Process, System.Diagnostics.Process"}""");

            HardenedSettings settings = HardenedSettings.Load(filePath, fallbackSupport: false);

            Assert.Multiple(() =>
            {
                Assert.That(settings, Is.Not.Null);
                Assert.That(settings.ApplicationVersion, Is.Null,
                    "A config whose root $type cannot be resolved must not load silently; Load must fall back to a fresh default.");
            });
        }
        finally
        {
            DeleteFile(filePath);
        }
    }

    [Test]
    public void DeserializeFromStream_RootTypeFromUnresolvedAssembly_IsNotSilentlySwallowed()
    {
        const string json = """{"$type":"System.Diagnostics.Process, System.Diagnostics.Process"}""";

        MethodInfo method = typeof(SettingsBase<HardenedSettings>).GetMethod(
            "DeserializeFromStream",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        TargetInvocationException? invocation = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, new object[] { stream, nameof(HardenedSettings) }));

        Assert.That(invocation, Is.Not.Null);
        Assert.That(invocation!.InnerException, Is.TypeOf<JsonSerializationException>(),
            "The binder failure must propagate as a JsonSerializationException instead of being swallowed into a default/null result.");
        Assert.That(invocation.InnerException!.Message, Does.Contain("Error resolving type specified in JSON"));
        Assert.That(invocation.InnerException.InnerException, Is.Not.Null);
        Assert.That(invocation.InnerException.InnerException!.Message, Does.Contain("Blocked or unknown settings type"));
    }

    [Test]
    public void Load_NestedTypeFromUnresolvedAssembly_RejectsWholeConfig()
    {
        string filePath = CreateTempFilePath();
        try
        {
            // A poisoned polymorphic item must fail the whole load (fail loud) instead of being
            // silently dropped while the rest of the config is kept.
            File.WriteAllText(filePath, """
                {
                  "Items": [
                    { "$type": "System.Diagnostics.Process, System.Diagnostics.Process" },
                    { "SomeValue": "legit" }
                  ]
                }
                """);

            HardenedSettings settings = HardenedSettings.Load(filePath, fallbackSupport: false);

            Assert.That(settings.Items, Is.Empty,
                "A config containing an unresolvable $type must be rejected wholesale (Load falls back to a fresh default), not partially loaded.");
        }
        finally
        {
            DeleteFile(filePath);
        }
    }

    private static string CreateTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), "XerahS-Sec12-" + Guid.NewGuid().ToString("N") + ".json");
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; %TEMP% is reclaimed by the OS eventually.
        }
    }
}