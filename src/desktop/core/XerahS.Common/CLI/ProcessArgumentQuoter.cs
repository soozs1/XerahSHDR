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

using System.Linq;
using System.Text;

namespace XerahS.Common
{
    /// <summary>
    /// Quotes a single command-line argument the same way the Windows
    /// CommandLineToArgvW parser expects it (backslashes and quotes escaped).
    /// Inputs that contain no whitespace or quotes are returned unchanged.
    /// </summary>
    public static class ProcessArgumentQuoter
    {
        public static string Quote(string argument)
        {
            if (argument.Length == 0)
            {
                return "\"\"";
            }

            if (!argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');
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
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append('"');
                }
                else
                {
                    builder.Append('\\', backslashCount);
                    builder.Append(c);
                }

                backslashCount = 0;
            }

            builder.Append('\\', backslashCount * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }
}