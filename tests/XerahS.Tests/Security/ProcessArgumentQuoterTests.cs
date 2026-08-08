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

using System.Text;
using NUnit.Framework;
using XerahS.Common;

namespace XerahS.Tests.Security;

[TestFixture]
public class ProcessArgumentQuoterTests
{
    [Test]
    public void Quote_ValueWithoutSpacesOrQuotes_ReturnsUnchanged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessArgumentQuoter.Quote("hello"), Is.EqualTo("hello"));
            Assert.That(ProcessArgumentQuoter.Quote(@"C:\path\file.txt"), Is.EqualTo(@"C:\path\file.txt"));
            Assert.That(ProcessArgumentQuoter.Quote("monitor.0.monitor"), Is.EqualTo("monitor.0.monitor"));
        });
    }

    [Test]
    public void Quote_ValueWithSpaces_WrapsInQuotes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProcessArgumentQuoter.Quote("hello world"), Is.EqualTo("\"hello world\""));
            Assert.That(ProcessArgumentQuoter.Quote("C:\\My Files\\a b.mp4"), Is.EqualTo("\"C:\\My Files\\a b.mp4\""));
        });
    }

    [Test]
    public void Quote_ValueWithEmbeddedQuote_EscapesIt()
    {
        Assert.That(ProcessArgumentQuoter.Quote("a\"b"), Is.EqualTo("\"a\\\"b\""));
    }

    [Test]
    public void Quote_TrailingBackslash_IsDoubledBeforeClosingQuote()
    {
        Assert.That(ProcessArgumentQuoter.Quote("C:\\path with space\\"), Is.EqualTo("\"C:\\path with space\\\\\""));
    }

    [Test]
    public void Quote_EmptyString_ReturnsQuotedEmpty()
    {
        Assert.That(ProcessArgumentQuoter.Quote(""), Is.EqualTo("\"\""));
    }

    [Test]
    public void Quote_OnlyQuoteChar_EscapesIt()
    {
        Assert.That(ProcessArgumentQuoter.Quote("\""), Is.EqualTo("\"\\\"\""));
    }

    private static readonly string[] RoundTripInputs =
    [
        // The six documented cases
        "hello",
        "hello world",
        "a\"b",
        "a\\",
        "",
        "\"",
        // Adversarial combinations of backslashes and quotes
        "a\\\\b",
        "a\\\"b",
        "\\\\",
        "\\\"",
        "\"quoted\"",
        "a b\\c",
        "C:\\Temp\\My Folder\\file name.mp4",
        "  leading and trailing spaces  ",
        "tab\there",
        "mix \"of\" everything \\\\ here"
    ];

    [TestCaseSource(nameof(RoundTripInputs))]
    public void Quote_RoundTripsThroughCrtParsing(string input)
    {
        string quoted = ProcessArgumentQuoter.Quote(input);

        Assert.That(ParseCrtArgument(quoted), Is.EqualTo(input));
    }

    /// <summary>
    /// Minimal parser following the Windows CommandLineToArgvW rules:
    /// 2n backslashes before a quote yield n backslashes and the quote toggles
    /// quoting; 2n+1 backslashes yield n backslashes and an escaped literal
    /// quote; backslashes not followed by a quote are literal.
    /// </summary>
    private static string ParseCrtArgument(string argument)
    {
        var result = new StringBuilder(argument.Length);
        int backslashCount = 0;

        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashCount++;
                continue;
            }

            if (c == '"')
            {
                result.Append('\\', backslashCount / 2);
                if (backslashCount % 2 == 1)
                {
                    // Escaped quote: literal quote character, quoting state unchanged
                    result.Append('"');
                }

                backslashCount = 0;
                continue;
            }

            result.Append('\\', backslashCount);
            backslashCount = 0;
            result.Append(c);
        }

        result.Append('\\', backslashCount);
        return result.ToString();
    }
}