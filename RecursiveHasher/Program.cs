
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RecursiveHasher
{
    internal class Program
    {
        public static bool GoSpin = false;
        public static string RootDirectory = string.Empty;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Task.Factory.StartNew(() => LoadSpinTask());

                Console.Title = "Recursive MD5 Hasher";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.BackgroundColor = ConsoleColor.Black;
                Console.Clear();

                Console.WriteLine("Press D to select a directory.");
                Console.WriteLine("Press C to open files for comparison.");

                ConsoleKeyInfo op = Console.ReadKey(true);
                switch (op.KeyChar.ToString().ToUpper())
                {
                    case "D":
                        DirectoryAnalysis();
                        break;
                    case "C":
                        ResultComparison();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadKey(true);
            }
        }

        static void DirectoryAnalysis()
        {
            List<string> files = new List<string>();
            while (!files.Any())
            {
                files = FileList();
                if (!files.Any())
                {
                    Console.WriteLine("Directory contains no files. Please choose another directory.");
                }
            }
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (HashFinder(files) != string.Empty)
            {
                Console.WriteLine("");
                Console.WriteLine("Finished in " + stopwatch.Elapsed.ToString() + ".");
                Console.ReadKey(true);
            }
            else
            {
                Console.WriteLine("");
                Console.WriteLine("Process failed.");
                Console.ReadKey(true);
            }

        }

        static List<string> FileList()
        {
            Console.Clear();
            Console.WriteLine("Select a directory to read...");

            RootDirectory = string.Empty;
            List<string> files = new List<string>();

            while (RootDirectory == string.Empty)
            {
                FolderBrowserDialog hf = new FolderBrowserDialog()
                {
                    RootFolder = Environment.SpecialFolder.Desktop,
                    Description = "Select a folder.",
                    ShowNewFolderButton = true,
                };
                hf.ShowDialog();

                if (hf.SelectedPath != string.Empty)
                {
                    RootDirectory = hf.SelectedPath;
                    Console.WriteLine("Selected path: '" + RootDirectory + "'");
                    Console.WriteLine("Reading directory info");
                    GoSpin = true;
                    AddFiles(hf.SelectedPath, files);
                }
                else
                {
                    Console.WriteLine("No directory selected. Please select a folder.");
                    Console.ReadKey(true);
                }
            }
            return files;
        }
        private static void AddFiles(string path, IList<string> files)
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
            catch (UnauthorizedAccessException)
            {
                // ok, so we are not allowed to dig into that directory. Move on.
            }
        }

        static void LoadSpinTask()
        {
            while (true)
            {
                while (GoSpin)
                {
                    Console.Write(".");
                    Thread.Sleep(100);
                }
                Thread.Sleep(100);
            }
        }

        static string HashFinder(List<string> files)
        {
            try
            {
                Console.WriteLine("\rCalculating file hashes, please wait");

                // Create a unique filename for our output file
                string FolderName = new DirectoryInfo(RootDirectory).Name;
                string LogPath = FilenameGenerator(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FileHashes_" + FolderName + ".csv", 1024);

                decimal currentfile = 1;

                ConcurrentBag<FileData> resultdata = new ConcurrentBag<FileData>();

                // Compute MD5 hashes for each file in selected directory
                Parallel.ForEach(files, f =>
                    {
                        FileData fd = new FileData()
                        {
                            FilePath = f,
                            DateOfAnalysis = DateTime.Now.ToString()
                        };

                        using (MD5 MD5hsh = MD5.Create())
                        {
                            try
                            {
                                using (var stream = File.OpenRead(f))
                                {
                                    Console.WriteLine("\rCurrent File: " + f.ToString());
                                    string MD5 = BitConverter.ToString(MD5hsh.ComputeHash(stream)).Replace("-", string.Empty);

                                    fd.FileHash = MD5;
                                    resultdata.Add(fd);

                                    // Post progress to console titlebar
                                    decimal p = Math.Round(currentfile / files.Count() * 100m, 2);
                                    Console.Title = p.ToString() + "% complete.";
                                    currentfile++;
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                fd.FileHash = "Read access denied.";
                                resultdata.Add(fd);
                            }

                            catch (IOException)
                            {
                                fd.FileHash = "File in use.";
                                resultdata.Add(fd);
                            }
                        }
                    });

                // Write computed hashes to .csv on desktop
                Console.WriteLine("\rWriting results to disk");
                using (var sw = new StreamWriter(LogPath))
                {
                    using (CsvWriter csv = new CsvWriter(sw, CultureInfo.CurrentCulture))
                    {
                        csv.WriteRecords(resultdata);
                    }
                }
                GoSpin = false;
                return LogPath;
            }
            catch (Exception ex)
            {
                GoSpin = false;
                MessageBox.Show("Hash calculation task failed with exception.\r\n" + ex.StackTrace,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error,MessageBoxDefaultButton.Button1);
                return string.Empty;
            }
        }

        static void ResultComparison()
        {
            int FilesAdded = 0;

            List<string> ComparisonFiles = new List<string>();
            List<List<FileData>> FileDataBlob = new List<List<FileData>>();

            OpenFileDialog FileSelect = new OpenFileDialog();

            // Get two files for comparison
            Console.Clear();
            while (FilesAdded < 2)
            {
                if (FilesAdded == 0) { Console.WriteLine("Select first file for comparison."); }
                if (FilesAdded == 1) { Console.WriteLine("Select second file for comparison."); }
                else if (FilesAdded > 1) { Console.WriteLine("Select a file."); }

                FileSelect = new OpenFileDialog();
                FileSelect.ShowDialog();

                if (FileSelect.FileName != string.Empty)
                {
                    ComparisonFiles.Add(FileSelect.FileName);
                    FilesAdded++;
                }
                else
                {
                    Console.Clear();
                    Console.WriteLine("No file was selected.");
                }
            }


            // Read all selected files into memory
            foreach (string ComparisonFile in ComparisonFiles)
            {
                using (var reader = new StreamReader(ComparisonFile))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    FileDataBlob.Add(csv.GetRecords<FileData>().ToList());
                }
            }

            // Generate a list of file hash differences between all files read.
            List<string> FilesToCopy = SymmetricDifferenceGetter(FileDataBlob);
            Console.WriteLine(FilesToCopy.Count().ToString() + " file differences found.");
        }

        /// <summary>
        /// Gets diffences between a set of FileData object lists.
        /// 
        /// </summary>
        /// <param name="FileDataBlob"></param>
        /// <returns></returns>
        static List<string> SymmetricDifferenceGetter(List<List<FileData>> FileDataBlob)
        {
            List<string> FileDifferences = new List<string>();

            for (int f = 0; f < FileDataBlob.Count(); f++)
            {
                var differences = FileDataBlob[f]
                    .Except(FileDataBlob[f + 1])
                    .Union(FileDataBlob[f + 1].Except(FileDataBlob[f]));
            }
            return FileDifferences;
        }

        static string FilenameGenerator(string folder, string fileName, int maxAttempts = 1024)
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

    public class FileData
    {
        public string FilePath { get; set; }
        public string FileHash { get; set; }
        public string DateOfAnalysis { get; set; }
    }
}
