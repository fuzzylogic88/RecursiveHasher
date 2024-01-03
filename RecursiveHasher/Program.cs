/* 
 * RecursiveHasher
 * Daniel Green, 2022
 * 
 * Program.cs - Contains main window loop & data sort methods
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
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RecursiveHasher.ConsoleHelpers;
using static RecursiveHasher.FSHelpers;
using File = System.IO.File;

namespace RecursiveHasher
{
    internal partial class Program
    {
        private static readonly Version minWinVersion = new(6, 1);

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                ConsoleSetup();

                if (IsAdministrator())
                {
                    // Check that windows meets or exceeds minimum required version
                    if (WindowsVersionGreaterThanOrEqual(minWinVersion))
                    {
                        if (args.Length == 0)
                        {
                            while (true)
                            {
                                Console.Clear();
                                WriteLineEx("Press D to select a directory.", false, ConsoleColor.Cyan, 0, 0, false, true);
                                WriteLineEx("Press C to open files for comparison.", false, ConsoleColor.Cyan, 0, 1, false, true);
                                WriteLineEx("Press E to exit.", false, ConsoleColor.White, 0, 3, false, true);

                                ConsoleKeyInfo op = Console.ReadKey(true);
                                switch (op.KeyChar.ToString().ToUpper())
                                {
                                    case "D":
                                        DirectoryAnalysis(string.Empty);
                                        break;
                                    case "C":
                                        ResultComparison();
                                        break;
                                    case "E":
                                        Environment.Exit(0);
                                        break;
                                    default:
                                        Console.Clear();
                                        WriteLineEx("Unexpected input. Try again.", false, ConsoleColor.Red, 0, 0, false, true);
                                        Thread.Sleep(1500);
                                        break;
                                }
                            }
                        }
                        else
                        {
                            if (Directory.Exists(args[0]))
                            {
                                DirectoryAnalysis(args[0]);
                            }
                            else { Environment.Exit(0); }
                        }
                    }
                    else
                    {
                        Console.Title = "Fatal Error - Wrong OS version";
                        WriteLineEx("Application not running under Windows ver " + minWinVersion.ToString() + " or greater.", true, ConsoleColor.Red, 0, 0, false, true);
                    }
                }
                else
                {
                    Console.Title = "Fatal Error - Insufficient permissions";
                    WriteLineEx("Application not running with administrator privileges.", true, ConsoleColor.Red, 0, 0, false, true);
                }
                WriteLineEx("Press any key to exit...", true, ConsoleColor.White, 0, 2, false, true);
                Console.ReadKey();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadKey(true);
            }
        }

        static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static bool WindowsVersionGreaterThanOrEqual(Version ver)
        {
            return Environment.OSVersion.Version >= ver;
        }


        static void DirectoryAnalysis(string argDir)
        {
            List<string> files = [];
            while (files.Count == 0)
            {
                files = FileList(argDir);
                if (files.Count == 0)
                {
                    Console.Clear();
                    WriteLineEx("Directory contains no files. Please choose another directory.", false, ConsoleColor.Red, 0, 0, true, true);
                    WriteLineEx("Press any key to continue...", false, ConsoleColor.White, 0, 2, false, true);
                    Console.ReadKey();
                }
            }

            ClearVisibleExceptions.Set();
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (HashFinder(files) != string.Empty)
            {
                WriteLineEx("Finished in " + stopwatch.Elapsed.ToString() + ".", false, ConsoleColor.Green, 0, 9, true, true);
                Console.ReadKey(true);
            }
            else
            {
                WriteLineEx("Process failed",false,ConsoleColor.Red,0,9, true, true);
                Console.ReadKey(true);
            }
        }

        static List<string> FileList(string argDir)
        {
            Console.Clear();
            WriteLineEx("Select a directory to read.", false, ConsoleColor.White, 0, 0, false, true);

            List<string> files = [];

            RootDirectory = string.Empty;

            // if we weren't passed in a folder by the user
            if (string.IsNullOrEmpty(argDir))
            {
                while (RootDirectory == string.Empty)
                {
                    FolderBrowserDialog hf = new()
                    {
                        RootFolder = Environment.SpecialFolder.Desktop,
                        Description = "Select a folder to read.",
                        ShowNewFolderButton = true,
                    };
                    hf.ShowDialog();

                    if (hf.SelectedPath != string.Empty)
                    {
                        RootDirectory = hf.SelectedPath;
                    }
                    else
                    {
                        WriteLineEx("No directory selected. Please select a different folder.", false, ConsoleColor.Red, 0, 0, true, true);
                        Console.ReadKey(true);
                    }
                }
            }
            else { RootDirectory = argDir; }

            WriteLineEx("Selected path: \"" + RootDirectory + "\"", false, ConsoleColor.Cyan, 0, 2, false, true);
            WriteLineEx("Reading directory info...", false, ConsoleColor.Cyan, 0, 3, false, false);

            AddFiles(RootDirectory, files);
            return files;
        }

        static string HashFinder(List<string> files)
        {
            try
            {
                ProcessFinished = false;
                CompletedFileCount = 0;
                TotalFileCount = files.Count;

                Task.Factory.StartNew(() => UpdateProcessProgress());

                Console.Clear();

                // Create a unique filename for our output file
                string FolderName = new DirectoryInfo(RootDirectory).Name;
                string LogPath = FilenameGenerator(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                    "FileHashes_" + ScrubStringForFilename(FolderName) + ".csv", 1024);

                // Max concurrent threads are limited to logical processor count to prevent out-of-memory errors as files are read.
                // Further, parallelizing file operations on HDDs offers minimal performance benefit.
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                ConcurrentBag<FileData> resultdata = [];

                // Compute MD5 hashes for each file in selected directory
                // Using foreach vs parallel foreach because we want more sequential reads of HDD.
                Parallel.ForEach(files, parallelOptions, s =>
                {
                    currentfilename = s;
                    FileData fd = new()
                    {
                        FilePath = s,
                        DateOfAnalysis = DateTime.Now.ToString()
                    };
             
                    try
                    {
                        using MD5 MD5hsh = MD5.Create();
                        using FileStream stream = new(s, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        fd.FileHash = BitConverter.ToString(MD5hsh.ComputeHash(stream)).Replace("-", string.Empty);
                        resultdata.Add(fd);
                    }
                    catch (Exception ex)
                    {
                        eBucket.Enqueue(new ExceptionData { Message = "Hash error: ", Exception = ex, FilePath = fd.FilePath });
                        if (ex is UnauthorizedAccessException)
                        {
                            fd.FileHash = "Read access denied.";
                        }
                        else if (ex is IOException)
                        {
                            fd.FileHash = "File in use.";
                        }
                        else if (ex is DirectoryNotFoundException)
                        {
                            fd.FileHash = "Directory not found.";
                        }
                        resultdata.Add(fd);
                    }
                    finally
                    {
                        Interlocked.Increment(ref CompletedFileCount);
                    }
                });

                ClearVisibleExceptions.Set();
                while (ClearVisibleExceptions.IsSet)
                {
                    Thread.Sleep(10);
                }

                WriteLineEx(string.Empty, false, ConsoleColor.Cyan, 0, 7, true, false);
                WriteLineEx("Writing results to disk...", false, ConsoleColor.Cyan, 0, 8, true, false);

                // Write computed hashes to .csv on desktop
                using (var sw = new StreamWriter(LogPath))
                {
                    using CsvWriter csv = new(sw, CultureInfo.CurrentCulture);
                    csv.WriteRecords(resultdata);
                }
                return LogPath;
            }
            catch (Exception ex)
            {            
                MessageBox.Show("Hash calculation task failed with exception.\r\n" + ex.StackTrace, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return string.Empty;
            }
            finally
            {      
                Task.Delay(500);
                ProcessFinished = true;
            }
        }

        public static CancellationTokenSource queryCancellationSource = new();

        static void ResultComparison()
        {
            int FilesAdded = 0;
            bool ErrorOnCopy = false;
            List<string> ComparisonFiles = [];
            List<FileData> FileDataA = [];
            List<FileData> FileDataB = [];

            try
            {
                while (FilesAdded < 2)
                {
                    Console.Clear();
                    if (FilesAdded == 0) 
                    {
                        WriteLineEx("Select first file for comparison.", false, ConsoleColor.White, 0, 0, true, true);
                    }
                    if (FilesAdded == 1) 
                    {
                        WriteLineEx("Select second file for comparison.", false, ConsoleColor.White, 0, 0, true, true);
                    }

                    OpenFileDialog FileSelect = new();
                    FileSelect.ShowDialog();

                    if (FileSelect.FileName != string.Empty)
                    {
                        ComparisonFiles.Add(FileSelect.FileName);
                        FilesAdded++;
                    }
                    else
                    {
                        WriteLineEx("No file was selected.", false, ConsoleColor.Red, 0, 0, true, true);
                    }
                }

                WriteLineEx("Comparing datasets, please wait...", false, ConsoleColor.Cyan, 0, 0, true, false);

                // Read all selected files into memory
                for (int i = 0; i < ComparisonFiles.Count; i++)
                {
                    using var reader = new StreamReader(ComparisonFiles[i]);
                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                    if (i == 0) { FileDataA = csv.GetRecords<FileData>().ToList(); }
                    else if (i == 1) { FileDataB = csv.GetRecords<FileData>().ToList(); }
                }

                // Generate a list of file hash differences between all files read.
                List<FileData> FileDifferences = null;
                List<FileData> FilesMissing = null;

                Task HashCompare = Task.Run(() =>
                {
                    WriteLineEx(">> Searching for hash mismatches",false,ConsoleColor.Cyan,0,2,false,true);
                    // Test for hash differences between both datasets
                    FileDifferences =
                    [
                        .. FileDataB.AsParallel().Where(x => !FileDataA.Any(y => y.FileHash == x.FileHash && Path.GetFileName(y.FilePath) == Path.GetFileName(x.FilePath))),
                    ];

                    FileDifferences.ForEach(c => c.Diff = "Hash mismatch");

                    // Test for files missing from dataset 1
                    WriteLineEx(">> Testing for missing files (dataset 1)", false, ConsoleColor.Cyan, 0, 3, false, true);
                    var missingInList1 = FileDataA
                        .AsParallel()
                        .Where(file1 =>
                            !FileDataB.Any(file2 => Path.GetFileName(file2.FilePath) == Path.GetFileName(file1.FilePath)))
                        .ToList();

                    if (missingInList1.Count != 0)
                    {
                        missingInList1.ForEach(c => c.Diff = "Missing from collection " + ComparisonFiles[0].ToString());
                    }

                    // Test for files missing from dataset 2
                    WriteLineEx(">> Testing for missing files (dataset 2)", false, ConsoleColor.Cyan, 0, 4, false, true);
                    var missingInList2 = FileDataB
                        .AsParallel()
                        .Where(
                        file1 => !FileDataA.Any(file2 => Path.GetFileName(file2.FilePath) == Path.GetFileName(file1.FilePath)))
                        .ToList();

                    if (missingInList2.Count != 0)
                    {
                        missingInList2.ForEach(c => c.Diff = "Missing from collection " + ComparisonFiles[1].ToString());
                    }

                    FilesMissing = [.. missingInList1, .. missingInList2];

                }, queryCancellationSource.Token);

                Task.WhenAny(HashCompare, Task.Delay(TimeSpan.FromSeconds(4800))).Wait();

                // If either comparisons time-out...
                if (!HashCompare.IsCompleted)
                {
                    queryCancellationSource.Cancel();
                    Console.Clear();
                    WriteLineEx("Query timed out.", false, ConsoleColor.Red, 0, 0, true, true);
                }

                else
                {
                    Console.Clear();
                    WriteLineEx("Data comparison completed successfully.", false, ConsoleColor.Green, 0, 0, true, true);
                }

                if (FileDifferences.Count == 0 && FilesMissing.Count == 0)
                {
                    Thread.Sleep(250);
                    WriteLineEx("No file content differences or missing files were found.", false, ConsoleColor.Yellow, 0, 2, true, true);
                }

                Thread.Sleep(250);

                if (FileDifferences.Count != 0 || FilesMissing.Count != 0)
                {
                    WriteLineEx(FileDifferences.Count.ToString() + " file content differences found. " + FilesMissing.Count.ToString() + " file presence differences found.", false, ConsoleColor.Green, 0, 2, true, true);
                    WriteLineEx("Press C to copy differences to folder on Desktop.", false, ConsoleColor.White, 0, 4, true, true);
                    WriteLineEx("Press any other key to exit.", false, ConsoleColor.White, 0, 5, true, true);

                    ConsoleKeyInfo op = Console.ReadKey(true);
                    if (op.KeyChar.ToString().Equals("C", StringComparison.CurrentCultureIgnoreCase))
                    {
                        try
                        {
                            WriteLineEx("Copying data, please wait...", false, ConsoleColor.Cyan, 0, 7, false, false);

                            string rootFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\FileDifferences\";
                            string missFolder = Path.Combine(rootFolder, @"missing\");
                            string diffFolder = Path.Combine(rootFolder, @"hash\");
                            string outputFolder = string.Empty;

                            if (!Directory.Exists(rootFolder)) { Directory.CreateDirectory(rootFolder); }
                            if (!Directory.Exists(missFolder)) { Directory.CreateDirectory(missFolder); }
                            if (!Directory.Exists(diffFolder)) {  Directory.CreateDirectory(diffFolder); }

                            // Copy file differences to folder on desktop
                            List<FileData> totalDifferences = [.. FileDifferences, .. FilesMissing];
                            foreach (FileData f in totalDifferences)
                            {
                                string fname = string.Empty;
                                string OriginalFileName = Path.GetFileName(f.FilePath);

                                if (f.Diff.Contains("missing", StringComparison.OrdinalIgnoreCase)) { outputFolder = missFolder; }
                                else if (f.Diff.Contains("mismatch", StringComparison.OrdinalIgnoreCase)) { outputFolder = diffFolder; }
                                else
                                {
                                    outputFolder = rootFolder;
                                    f.Diff = "File disposition unknown (check your code).";
                                }

                                if (!string.IsNullOrEmpty(outputFolder))
                                {
                                    // in the event of duplicate photos, check that filename is unique...
                                    if (File.Exists(outputFolder + OriginalFileName))
                                    {
                                        fname = FilenameGenerator(outputFolder, OriginalFileName, 1024);
                                    }
                                    else { fname = outputFolder + OriginalFileName; }

                                    // Copy file to directory, NOT overwriting.
                                    try
                                    {
                                        File.Copy(f.FilePath, fname, false);
                                    }
                                    catch (Exception ex)
                                    {
                                        ErrorOnCopy = true;
                                        eBucket.Enqueue(new ExceptionData { Message = "Copy error: ", Exception = ex, FilePath = f.FilePath });
                                    }
                                }
                            }

                            string DiffResultPath = FilenameGenerator(rootFolder, "FileDifferences.csv", 1024);

                            using (var sw = new StreamWriter(DiffResultPath))
                            {
                                using CsvWriter csv = new(sw, CultureInfo.CurrentCulture);
                                csv.WriteRecords(totalDifferences);
                            }

                            Thread.Sleep(250);

                            Console.Clear();
                            if (!ErrorOnCopy) { WriteLineEx("Copy operation finished successfully.", false, ConsoleColor.Green, 0, 0, false, true); }
                            else { WriteLineEx("Copy operation finished with errors.", false, ConsoleColor.Yellow, 0, 0, false, true); }
                        }
                        catch (Exception ex)
                        {
                            Console.Clear();
                            WriteLineEx("Copy operation failed. (" + ex.GetType() + ")", false, ConsoleColor.Red, 0, 0, false, true);
                        }
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                }
                WriteLineEx("Press any key to exit.", false, ConsoleColor.White, 0, 3, false, true);
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ReadKey();
            }
        }
    }

    // Custom comparer for FileData based on the FilePath property
    class FileDataComparer : IEqualityComparer<FileData>
    {
        public bool Equals(FileData x, FileData y)
        {
            return x.FilePath == y.FilePath;
        }

        public int GetHashCode(FileData obj)
        {
            // Ensure the hash code is based on the FilePath property
            return obj.FilePath.GetHashCode();
        }
    }

    internal static class StringExtensions
    {
        public static string Truncate(this string value, int maxLength, string truncationSuffix = "…")
        {
            try
            {
                return value?.Length > maxLength
                    ? string.Concat(value.AsSpan(0, maxLength), truncationSuffix)
                    : value;
            }
            catch { return value; }
        }
    }

    public class FileData
    {
        public string FilePath { get; set; }
        public string FileHash { get; set; }
        public string DateOfAnalysis { get; set; }
        public string Diff {  get; set; }
    }

    public class ExceptionData
    {
        public string Message {  get; set; }
        public Exception Exception { get; set; }
        public string FilePath { get; set; }
    }
    struct CWindowSz
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }
}

