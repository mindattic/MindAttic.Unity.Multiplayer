using System;
using System.IO;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The join-code hand-off between a server and the bots started beside it (UM-T57, <c>-joincodefile</c>): the server
    /// writes each code it publishes, the bots poll for it. Written beside the file and moved into place, so a reader
    /// never sees half a code.
    /// </summary>
    public static class JoinCodeFile
    {
        /// <summary>Writes <paramref name="code"/> to <paramref name="path"/> (creating its folder), replacing what was there.</summary>
        /// <exception cref="ArgumentException"><paramref name="path"/> or <paramref name="code"/> is empty.</exception>
        /// <exception cref="IOException">The file could not be written.</exception>
        public static void Write(string path, string code)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A join-code file needs a path.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("There is no code to write.", nameof(code));
            }

            string full = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string temp = full + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            File.WriteAllText(temp, code.Trim() + Environment.NewLine);
            if (File.Exists(full))
            {
                File.Delete(full);
            }

            File.Move(temp, full);
        }

        /// <summary>
        /// The code in <paramref name="path"/>, once it exists and holds one; false (and null) before that, or while the
        /// file is being replaced.
        /// </summary>
        public static bool TryRead(string path, out string code)
        {
            code = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                string text = File.ReadAllText(path).Trim();
                if (text.Length == 0)
                {
                    return false;
                }

                code = text;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
