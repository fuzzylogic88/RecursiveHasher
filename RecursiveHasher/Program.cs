/* 
 * RecursiveHasher
 * Daniel Green, 2022
 * 
 * Computes MD5 hashes for all files in directory/subdirectory,
 * Compares output files to determine if files were missing / deleted.
 */

using CsvHelper;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RecursiveHasher
{
    internal class Program
    {
        public static bool GoSpin = false;
        public static string RootDirectory = string.Empty;
        public static char BoxFill = '\u2588';
        public static char BoxEmpty = '\u2593';

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.Unicode;

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
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Finished in " + stopwatch.Elapsed.ToString() + ".");
                Console.ReadKey(true);
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Process failed.");
                Console.ReadKey(true);
                Console.ForegroundColor = ConsoleColor.Cyan;
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
                    GoSpin = false;
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
                Console.Clear();

                // Create a unique filename for our output file
                string FolderName = new DirectoryInfo(RootDirectory).Name;
                string LogPath = FilenameGenerator(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FileHashes_" + FolderName + ".csv", 1024);

                decimal currentfile = 1;
                decimal PBarChunk = files.Count / (decimal)50;

                ConcurrentBag<FileData> resultdata = new ConcurrentBag<FileData>();

                // Compute MD5 hashes for each file in selected directory
                // Using foreach vs parallel foreach because we want more sequential reads of HDD.
                Parallel.ForEach(files, s =>
                {
                    Console.SetCursorPosition(0, 0);
                    Console.WriteLine("\rCalculating file hashes, please wait.");
                    Console.SetCursorPosition(0, 1);
                    Console.Write("\r" + new string(' ', Console.WindowWidth) + "\r");
                    Console.WriteLine("\rCurrent File: " + s.ToString());

                    FileData fd = new FileData()
                    {
                        FilePath = s,
                        DateOfAnalysis = DateTime.Now.ToString()
                    };

                    using (MD5 MD5hsh = MD5.Create())
                    {
                        try
                        {
                            using (var stream = File.OpenRead(s))
                            {
                                string MD5 = BitConverter.ToString(MD5hsh.ComputeHash(stream)).Replace("-", string.Empty);
                                fd.FileHash = MD5;
                                resultdata.Add(fd);
                            }
                            // get number of filled boxes to display
                            decimal FillCount_d = currentfile / PBarChunk;
                            int FillCount = (int)Math.Round(FillCount_d, 0);
                            int EmptyCount = 50 - FillCount;
                            decimal p = Math.Round(currentfile / files.Count() * 100m, 2);

                            Console.SetCursorPosition(0, 3);
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine(new string(BoxFill, FillCount) + new string(BoxEmpty, EmptyCount) + " " + p.ToString() + "%");
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            currentfile++;
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
                Console.WriteLine("");
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
                MessageBox.Show("Hash calculation task failed with exception.\r\n" + ex.StackTrace, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return string.Empty;
            }
        }

        static void ResultComparison()
        {
            int FilesAdded = 0;

            List<string> ComparisonFiles = new List<string>();

            List<FileData> FileDataA = new List<FileData>();
            List<FileData> FileDataB = new List<FileData>();

            // Get two files for comparison
            Console.Clear();
            while (FilesAdded < 2)
            {
                if (FilesAdded == 0) { Console.WriteLine("Select first file for comparison. (Folder to be verified / with files suspected missing)"); }
                if (FilesAdded == 1) { Console.WriteLine("Select second file for comparison. ('Known-Good' to be verified against / without deleted files)"); }

                OpenFileDialog FileSelect = new OpenFileDialog();
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

            GoSpin = true;
            Console.WriteLine("Working");

            // Read all selected files into memory
            for (int i = 0; i < ComparisonFiles.Count; i++)
            {
                using (var reader = new StreamReader(ComparisonFiles[i]))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    if (i == 0) { FileDataA = csv.GetRecords<FileData>().ToList(); }
                    else if (i == 1) { FileDataB = csv.GetRecords<FileData>().ToList(); }
                }
            }

            // Generate a list of file hash differences between all files read.
            List<FileData> FileDifferences = FileDataB
                .Where(x => !FileDataA.Any(y => y.FileHash == x.FileHash))
                .ToList();

            if (FileDifferences.Count == 0)
            {
                GoSpin = false;
                Thread.Sleep(250);

                Console.WriteLine("No differences found, press a key to compare in reverse direction.");
                Console.ReadKey();
                FileDifferences = FileDataA
                .Where(x => !FileDataB.Any(y => y.FileHash == x.FileHash))
                .ToList();
            }

            GoSpin = false;
            Thread.Sleep(250);

            Console.Clear();
            if (FileDifferences.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(FileDifferences.Count().ToString() + " file differences found.");
                Console.WriteLine("Press C to copy differences to folder on Desktop.");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Press any other key to exit.");

                ConsoleKeyInfo op = Console.ReadKey(true);
                if (op.KeyChar.ToString().ToUpper() == "C")
                {
                    GoSpin = true;
                    Console.WriteLine("Copying data, please wait");

                    string dfolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\FileDifferences\";
                    if (!Directory.Exists(dfolder))
                    {
                        Directory.CreateDirectory(dfolder);
                    }
                    
                    // Copy file differences to folder on desktop
                    foreach (FileData diff in FileDifferences)
                    {
                        string fname = string.Empty;
                        string OriginalFileName = Path.GetFileName(diff.FilePath);

                        // in the event of duplicate photos, check that filename is unique...
                        if (File.Exists(dfolder + OriginalFileName))
                        {
                           fname = FilenameGenerator(dfolder,OriginalFileName,1024);
                        }
                        else { fname = dfolder + OriginalFileName; }

                        // Copy file to directory, NOT overwriting.
                        File.Copy(diff.FilePath, fname, false);
                    }

                    string DiffResultPath = FilenameGenerator(dfolder, "FileDifferences.csv", 1024);

                    using (var sw = new StreamWriter(DiffResultPath))
                    {
                        using (CsvWriter csv = new CsvWriter(sw, CultureInfo.CurrentCulture))
                        {
                            csv.WriteRecords(FileDifferences);
                        }
                    }

                    GoSpin = false;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Clear();
                    Console.WriteLine("Copy operation finished successfully.");
                }
                else
                {
                    Environment.Exit(0);
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No file differences were found!");
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
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
