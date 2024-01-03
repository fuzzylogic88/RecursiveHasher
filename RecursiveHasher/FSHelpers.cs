/* 
 * RecursiveHasher
 * Daniel Green, 2022
 * 
 * FSHelpers.cs - Filesystem related methods
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RecursiveHasher
{
    internal partial class FSHelpers
    {
        public static void AddFiles(string path, IList<string> files)
        {
            try
            {
                Directory.GetFiles(path)
                    .ToList()
                    .ForEach(s => files.Add(s));

                Directory.GetDirectories(path)
                    .ToList()
                    .ForEach(s => AddFiles(s, files));
            }
            catch (Exception ex)
            {
                eBucket.Enqueue(new ExceptionData { Message = "Enum failed: ", Exception = ex, FilePath = GetFilePathFromException(ex.Message) });
            }
        }

        [GeneratedRegex(@"[\\/:*?""<>|]")]
        private static partial Regex DisallowedPathCharacters();

        static string GetFilePathFromException(string exceptionMessage)
        {
            // Matches file path between single quotes
            string pattern = @"'([^']+)'";
            Match match = Regex.Match(exceptionMessage, pattern);

            if (match.Success && match.Groups.Count > 1) { return match.Groups[1].Value; }

            return exceptionMessage;
        }

        public static string ScrubStringForFilename(string inputString)
        {
            // Remove invalid characters
            return DisallowedPathCharacters().Replace(inputString, "");
        }

        public static string FilenameGenerator(string folder, string fileName, int maxAttempts = 1024)
        {
            var fileBase = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);

            // Build hash set of filenames for performance
            var files = new HashSet<string>(Directory.GetFiles(folder));

            for (var index = 0; index < maxAttempts; index++)
            {
                // First try with the original filename, else try incrementally adding an index
                var name = (index == 0)
                    ? fileName
                    : string.Format("{0} ({1}){2}", fileBase, index, ext);

                // Check if exists
                var fullPath = Path.Combine(folder, name);
                if (files.Contains(fullPath))
                    continue;

                // Try to create the file
                try
                {
                    return fullPath;
                }
                catch (DirectoryNotFoundException) { throw; }
                catch (DriveNotFoundException) { throw; }
                catch (IOException)
                {
                    // Will occur if another thread created a file with this name since we created the HashSet.
                    // Ignore this and just try with the next filename.
                }
            }
            throw new Exception("Could not create unique filename in " + maxAttempts + " attempts");
        }
    }
}
