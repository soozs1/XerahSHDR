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
using System.Text.RegularExpressions;
using XerahS.Uploaders;
using XerahS.Uploaders.CustomUploader;

namespace XerahS.Tests.Security;

[TestFixture]
public class CustomUploaderHardeningTests
{
    [Test]
    public void ValidateItem_RejectsNonHttpScheme()
    {
        // Arrange - file:// scheme
        var fileItem = CustomUploaderItem.Init();
        fileItem.RequestURL = "file:///etc/passwd";

        // Act
        var fileError = CustomUploaderRepository.ValidateItem(fileItem);

        // Assert
        Assert.That(fileError, Is.Not.Null);

        // Arrange - ftp:// scheme
        var ftpItem = CustomUploaderItem.Init();
        ftpItem.RequestURL = "ftp://example.com/file.txt";

        // Act
        var ftpError = CustomUploaderRepository.ValidateItem(ftpItem);

        // Assert
        Assert.That(ftpError, Is.Not.Null);
    }

    [Test]
    public void ValidateItem_WithPlaceholder_DefersSchemeCheck()
    {
        // Arrange - URL contains a syntax placeholder, so static validation is deferred
        var item = CustomUploaderItem.Init();
        item.RequestURL = "file://{input}";

        // Act
        var error = CustomUploaderRepository.ValidateItem(item);

        // Assert - scheme check is deferred when a placeholder is present
        Assert.That(error, Is.Null, "URL with placeholder should pass static validation");

        // Act - after substitution the resolved URL must still be http/https
        var input = new CustomUploaderInput("test.png", "payload");
        Assert.Throws<InvalidDataException>(() => item.GetRequestURL(input));
    }

    [Test]
    public void RegexTimeout_Applies()
    {
        // Arrange - catastrophic backtracking pattern from an untrusted .sxcu file
        var function = new CustomUploaderFunctionRegex();
        var parser = new ShareXCustomUploaderSyntaxParser();
        parser.ResponseInfo = new ResponseInfo { ResponseText = new string('a', 30000) + "!" };

        // Act & Assert - must not hang; the 2s timeout should be enforced
        Assert.Throws<RegexMatchTimeoutException>(() => function.Call(parser, new[] { "(a+)+$" }));
    }

    [Test]
    public void CreateInstance_ValidatesItem()
    {
        // Arrange
        var item = CustomUploaderItem.Init();
        item.RequestURL = "https://example.com/upload";
        var provider = new CustomUploaderProvider(item, "test.sxcu");
        string settingsJson = JsonConvert.SerializeObject(new { RequestURL = "file:///etc/passwd" });

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => provider.CreateInstance(settingsJson));
    }
}