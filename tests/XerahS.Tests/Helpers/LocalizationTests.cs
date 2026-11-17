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

namespace XerahS.Tests.Helpers;

/// <summary>
/// The language selector must be data-driven: any culture that ships a satellite resource file is
/// discovered at runtime, so adding a language never requires editing a list in code.
/// </summary>
public class LocalizationTests
{
    [Test]
    public void GetAvailableCultures_IncludesEnglishAndShippedTranslations()
    {
        var codes = Localization.GetAvailableCultures().Select(culture => culture.Name).ToList();

        Assert.That(codes, Does.Contain("en"), "neutral resources must always be offered");
        Assert.That(codes, Does.Contain("ru"), "Resources.ru.resx ships with the app");
    }

    [Test]
    public void GetAvailableCultures_DoesNotReportCulturesWithoutResources()
    {
        var codes = Localization.GetAvailableCultures().Select(culture => culture.Name).ToList();

        // A culture with no Resources.<code>.resx must not appear; otherwise every one of the
        // ~800 known cultures would show up in the selector.
        Assert.That(codes.Where(code => code != "en").All(code => code is "ru" or "de" or "es" or "zh-CN" or "zh-TW" or "fr" or "it" or "pt" or "pt-BR" or "tr" or "pl" or "uk" or "ko" or "ja" or "ar" or "nl" or "he" or "hu" or "id" or "es-MX" or "fa" or "ro" or "vi"),
            Is.True, $"unexpected cultures: {string.Join(", ", codes)}");
    }

    [Test]
    public void GetDisplayName_ReturnsNonEmptyName()
    {
        foreach (var culture in Localization.GetAvailableCultures())
        {
            Assert.That(Localization.GetDisplayName(culture), Is.Not.Empty, culture.Name);
        }
    }

    [Test]
    public void ApplyCulture_EmptyCode_KeepsCurrentCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentUICulture;
        Localization.ApplyCulture(string.Empty);
        Assert.That(System.Globalization.CultureInfo.CurrentUICulture, Is.EqualTo(before));
    }
}
