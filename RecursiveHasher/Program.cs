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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using static RecursiveHasher.NativeMethods;

namespace RecursiveHasher
{
    internal class Program
    {
        public static bool GoSpin = false;
        public static string RootDirectory = string.Empty;

        // box
        public static char BoxFill = '\u2588';
        public static char BoxEmpty = '\u2593';

        public static char BoxBendA = '\u2554';
        public static char BoxBendB = '\u2557';
        public static char BoxBendC = '\u255D';
        public static char BoxBendD = '\u255A';
        public static char BoxHoriz = '\u2550';
        public static char BoxVert = '\u2551';

        public static readonly int PBarBlockCapacity = 64;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.Unicode;
                Console.CursorVisible = false;

                Console.SetWindowSize(100, 20);

                //DEBUG
                //ProcessFinished = false;
                //progresspercent = 0;
                //EmptyBlockCount = PBarBlockCapacity;
                //FilledBlockCount = 0;
                //UpdateProcessProgress();
                //DEBUG

                IntPtr handle = GetConsoleWindow();
                IntPtr sysMenu = GetSystemMenu(handle, false);

                if (handle != IntPtr.Zero)
                {
                    //DeleteMenu(sysMenu, SC_CLOSE, MF_BYCOMMAND);
                    //DeleteMenu(sysMenu, SC_MINIMIZE, MF_BYCOMMAND);
                   DeleteMenu(sysMenu, SC_MAXIMIZE, MF_BYCOMMAND);
                   DeleteMenu(sysMenu, SC_SIZE, MF_BYCOMMAND);//resize
                }

                // Set font
                CONSOLE_FONT_INFO_EX consoleFont = new CONSOLE_FONT_INFO_EX
                {
                    cbSize = Marshal.SizeOf<CONSOLE_FONT_INFO_EX>(),
                    nFont = 0,
                    dwFontSize = new COORD { X = 14, Y = 28 }, // Set your desired font size
                    FontFamily = 0,
                    FontWeight = 400,
                    FaceName = "Courier New" // Set your desired font face
                };

                IntPtr consoleHandle = GetStdHandle((int)StdHandle.STD_OUTPUT_HANDLE);
                int result = SetCurrentConsoleFontEx(consoleHandle, false, ref consoleFont);
                
                Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);

                Console.Title = "MD5 Hasher";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.BackgroundColor = ConsoleColor.Black;

                Console.Clear();

                Console.WriteLine("Press D to select a directory.");
                Console.WriteLine("Press C to open files for comparison.");

                Task.Factory.StartNew(() => SpinTask());

                QuickEditMode(false);

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

        /// <summary>
        /// Enables/disables console features to prevent user accidentally stopping process by selecting window text
        /// </summary>
        /// <param name="Enable"></param>
        public static void QuickEditMode(bool Enable)
        {
            IntPtr consoleHandle = GetStdHandle((int)StdHandle.STD_INPUT_HANDLE);
            UInt32 consoleMode;

            GetConsoleMode(consoleHandle, out consoleMode);
            if (Enable)
            {
                consoleMode |= ((uint)ConsoleMode.ENABLE_QUICK_EDIT_MODE);
            }
            else
            {
                consoleMode &= ~((uint)ConsoleMode.ENABLE_QUICK_EDIT_MODE);
            }
            consoleMode |= ((uint)ConsoleMode.ENABLE_EXTENDED_FLAGS);
            SetConsoleMode(consoleHandle, consoleMode);
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

            Console.SetCursorPosition(0, 9);
            if (HashFinder(files) != string.Empty)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
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
                    Console.Write("\rReading directory info");

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
            catch (UnauthorizedAccessException) { /* ok, so we are not allowed to dig into that directory. Move on. */ }
            catch (DirectoryNotFoundException) { /* odd, but we'll look past it. */ }
        }

        static void SpinTask()
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

        public static string currentfilename = string.Empty;
        public static decimal progresspercent = 0m;
        public static int FilledBlockCount = 0;
        public static int EmptyBlockCount = 0;
        public static bool ProcessFinished = false;

        public static void UpdateProcessProgress()
        {
            progresspercent = 0;
            EmptyBlockCount = PBarBlockCapacity;
            FilledBlockCount = 0;

            while (!ProcessFinished)
            {
                Console.SetCursorPosition(0, 0);
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
                Console.WriteLine("\rCalculating file hashes, please wait.");

                Console.SetCursorPosition(0, 1);
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
                Console.WriteLine("Current File: " + StringExtensions.Truncate(Path.GetFileName(currentfilename.ToString()), Console.WindowWidth - 10));

                Console.SetCursorPosition(0, 2);
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");

                // Draw progressbar to console window...
                Console.ForegroundColor = ConsoleColor.White;

                Console.SetCursorPosition(0, 3);
                string toprow = BoxBendA + new string(BoxHoriz, EmptyBlockCount + FilledBlockCount) + BoxBendB;
                Console.WriteLine(toprow);

                string ppercent = progresspercent.ToString() + '%';                                            // current prog value
                string firsthalf = BoxVert + new string(' ', toprow.Length / 2 - ppercent.Length) + ppercent;  // first portion including percent
                string secondhalf = new string(' ', toprow.Length - firsthalf.Length - 1) + BoxVert;           // second portion including final box char
                Console.SetCursorPosition(0, 4);
                Console.WriteLine(firsthalf + secondhalf);

                Console.SetCursorPosition(0, 5);
                Console.WriteLine(BoxVert + new string(BoxFill, FilledBlockCount) + new string(BoxEmpty, EmptyBlockCount) + BoxVert);

                Console.SetCursorPosition(0, 6);
                Console.WriteLine(BoxBendD + new string(BoxHoriz, EmptyBlockCount + FilledBlockCount) + BoxBendC);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Thread.Sleep(60);
            }
            Console.SetCursorPosition(0, 2);
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
        }

        static string HashFinder(List<string> files)
        {
            decimal CompletedFileCount = 1;
            try
            {
                ProcessFinished = false;
                Task.Factory.StartNew(() => UpdateProcessProgress());

                Console.Clear();

                // Create a unique filename for our output file
                string FolderName = new DirectoryInfo(RootDirectory).Name;
                string LogPath = FilenameGenerator(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "FileHashes_" + FolderName + ".csv", 1024);

                decimal PBarChunk = Math.Ceiling(files.Count / (decimal)PBarBlockCapacity);

                ConcurrentBag<FileData> resultdata = new ConcurrentBag<FileData>();

                // Compute MD5 hashes for each file in selected directory
                // Using foreach vs parallel foreach because we want more sequential reads of HDD.
                Parallel.ForEach(files, s =>
                {
                    currentfilename = s;
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

                            // get number of filled & empty boxes to display
                            FilledBlockCount = (int)Math.Ceiling(CompletedFileCount / PBarChunk);
                            EmptyBlockCount = PBarBlockCapacity - FilledBlockCount;
                            progresspercent = Math.Round(CompletedFileCount / files.Count() * 100m,2);

                            CompletedFileCount++;
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

                progresspercent = 100;
                FilledBlockCount = PBarBlockCapacity;

                // Write computed hashes to .csv on desktop
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.SetCursorPosition(0, 8);
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
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
            finally
            {
                ProcessFinished = true;
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

            try
            {
                while (FilesAdded < 2)
                {
                    if (FilesAdded == 0) { Console.WriteLine("Select first file for comparison."); }
                    if (FilesAdded == 1) { Console.WriteLine("Select second file for comparison."); }

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

                Console.Write("\r\rWorking");
                GoSpin = true;

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
                List<FileData> FileDifferences = null;
                Thread queryThread = new Thread(() =>
                {
                    FileDifferences = FileDataB
                        .AsParallel()
                        .Where(x => !FileDataA.Any(y => y.FileHash == x.FileHash))
                        .ToList();
                });

                queryThread.Start();

                if (!queryThread.Join(TimeSpan.FromSeconds(4800)))
                {
                    queryThread.Abort(); // Terminate the thread
                    Console.WriteLine("\rQuery timed out.");
                }
                else
                {
                    Console.WriteLine("\rQuery completed successfully.");
                }

                if (FileDifferences.Count == 0)
                {
                    GoSpin = false;
                    Thread.Sleep(250);

                    Console.WriteLine("\rNo differences found, press a key to compare in reverse direction.");
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
                                fname = FilenameGenerator(dfolder, OriginalFileName, 1024);
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
            catch (Exception ex)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ReadKey();
            }
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
    internal static class StringExtensions
    {
        public static string Truncate(this string value, int maxLength, string truncationSuffix = "…")
        {
            return value?.Length > maxLength
                ? value.Substring(0, maxLength) + truncationSuffix
                : value;
        }
    }
    public class FileData
    {
        public string FilePath { get; set; }
        public string FileHash { get; set; }
        public string DateOfAnalysis { get; set; }
    }
}
