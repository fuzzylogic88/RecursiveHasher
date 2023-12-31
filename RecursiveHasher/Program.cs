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
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using static RecursiveHasher.NativeMethods;
using File = System.IO.File;

namespace RecursiveHasher
{
    internal class Program
    {
        public static bool GoSpin = false;
        public static string RootDirectory = string.Empty;

        // Semaphore metering access to console position/color/writes 
        static readonly SemaphoreSlim conSph = new SemaphoreSlim(1, 1);

        public static ConsoleColor LastConsoleForeground = ConsoleColor.White;

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

        // Container for exception data as files fail to be read
        public static ConcurrentQueue<ExceptionData> eBucket = new ConcurrentQueue<ExceptionData>();
        public static ManualResetEventSlim ClearVisibleExceptions = new ManualResetEventSlim(false);


        public static string currentfilename = string.Empty;
        public static decimal progresspercent = 0m;
        public static int FilledBlockCount = 0;
        public static int EmptyBlockCount = 0;
        public static int CompletedFileCount = 0;
        public static int TotalFileCount = 0;
        public static bool ProcessFinished = false;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.Unicode;
                Console.CursorVisible = false;
                Console.SetWindowSize(100, 40);

                IntPtr handle = GetConsoleWindow();
                IntPtr sysMenu = GetSystemMenu(handle, false);

                if (handle != IntPtr.Zero)
                {
                   DeleteMenu(sysMenu, SC_MAXIMIZE, MF_BYCOMMAND);
                   DeleteMenu(sysMenu, SC_SIZE, MF_BYCOMMAND);
                }

                // Set font
                CONSOLE_FONT_INFO_EX consoleFont = new CONSOLE_FONT_INFO_EX
                {
                    cbSize = Marshal.SizeOf<CONSOLE_FONT_INFO_EX>(),
                    nFont = 0,
                    dwFontSize = new COORD { X = 12, Y = 24 }, 
                    FontFamily = 0,
                    FontWeight = 400,
                    FaceName = "Courier New" 
                };

                IntPtr consoleHandle = GetStdHandle((int)StdHandle.STD_OUTPUT_HANDLE);
                int result = SetCurrentConsoleFontEx(consoleHandle, false, ref consoleFont);
                
                Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);

                Console.Title = "MD5 Hasher";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.BackgroundColor = ConsoleColor.Black;

                Task.Factory.StartNew(() => SpinTask());
                Task.Factory.StartNew(() => ExceptionOut());

                QuickEditMode(false);

                if (IsAdministrator())
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
                    Console.Title = "Error!";
                    WriteLineEx("Application not running with administrator privileges! :(", true, ConsoleColor.Red, 0, 0, false, true);
                    WriteLineEx("Press any key to exit...", true, ConsoleColor.White, 0, 2, false, true);
                    Console.ReadKey();
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Console.ReadKey(true);
            }
        }

        public static bool IsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>
        /// Enables/disables console features to prevent user accidentally stopping process by selecting window text
        /// </summary>
        /// <param name="Enable"></param>
        public static void QuickEditMode(bool Enable)
        {
            IntPtr consoleHandle = GetStdHandle((int)StdHandle.STD_INPUT_HANDLE);

            GetConsoleMode(consoleHandle, out uint consoleMode);
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

        static void DirectoryAnalysis(string argDir)
        {
            List<string> files = new List<string>();
            while (!files.Any())
            {
                files = FileList(argDir);
                if (!files.Any())
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
                ClearVisibleExceptions.Set();
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

            List<string> files = new List<string>();

            RootDirectory = string.Empty;

            // if we weren't passed in a folder by the user
            if (string.IsNullOrEmpty(argDir))
            {
                while (RootDirectory == string.Empty)
                {
                    FolderBrowserDialog hf = new FolderBrowserDialog()
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
            WriteLineEx("Reading directory info", false, ConsoleColor.Cyan, 0, 3, false, false);

            AddFiles(RootDirectory, files);
            return files;
        }
        private static void AddFiles(string path, IList<string> files)
        {
            try
            {
                GoSpin = true;
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
            finally
            {
                GoSpin = false;
            }
        }

        static string GetFilePathFromException(string exceptionMessage)
        {
            // Matches file path between single quotes
            string pattern = @"'([^']+)'"; 
            Match match = Regex.Match(exceptionMessage, pattern);

            if (match.Success && match.Groups.Count > 1) { return match.Groups[1].Value; }

            return exceptionMessage; 
        }

        static void SpinTask()
        {
            while (true)
            {
                while (GoSpin)
                {
                    WriteLineEx(".", false, null, -1, -1, false, false);
                    Thread.Sleep(100);
                }
                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Monitors the exception bucket for new errors, and prints them to console window as needed.
        /// </summary>
        public static void ExceptionOut()
        {
            // starting row for exceptions to be printed on
            int consolePos = 12;

            while (true)
            {
                // check for pending exceptions to post to UI
                while (eBucket.TryDequeue(out ExceptionData r))
                {
                    // increment row by one for each failed file, wrapping back to the initial row
                    consolePos = (consolePos + 1) % (Console.WindowHeight - 1);
                    if (consolePos == 0) { consolePos = 12; }

                    StringBuilder exStrb = new StringBuilder();
                    exStrb.Append(r.Message);
                    exStrb.Append(r.Exception.GetType().ToString());
                    exStrb.Append(" - " + StringExtensions.Truncate(Path.GetFileName(r.FilePath), Console.WindowWidth - exStrb.Length - 5));
                    WriteLineEx(exStrb.ToString(), false, ConsoleColor.Red, 0, consolePos, true, true);
                }

                if (ClearVisibleExceptions.IsSet)
                {
                    for (int i = consolePos; i < Console.WindowHeight - 1; i++)
                    {
                        WriteLineEx(string.Empty, false, null, 0, i, true, true);
                    }
                    ClearVisibleExceptions.Reset();
                }

                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Indicates hash progress to user with graphical bar
        /// </summary>
        public static void UpdateProcessProgress()
        {
            progresspercent = 0;
            EmptyBlockCount = PBarBlockCapacity;
            FilledBlockCount = 0;

            while (!ProcessFinished)
            {
                // get number of filled & empty boxes to display
                FilledBlockCount = (int)Math.Ceiling((decimal)CompletedFileCount / TotalFileCount * PBarBlockCapacity);
                EmptyBlockCount = PBarBlockCapacity - FilledBlockCount;

                WriteLineEx("\rCalculating file hashes, please wait.", false, ConsoleColor.Cyan, 0, 0, true, true);

                string _curFile = "Current File: " + StringExtensions.Truncate(Path.GetFileName(currentfilename.ToString()), Console.WindowWidth - 20);
                WriteLineEx(_curFile, false, ConsoleColor.Cyan, 0, 1, true, true);
                WriteLineEx(string.Empty, false, ConsoleColor.Cyan, 0, 2, true, true);

                // Draw progressbar to console window...
                string toprow = BoxBendA + new string(BoxHoriz, EmptyBlockCount + FilledBlockCount) + BoxBendB;         
                string ppercent = Math.Round((decimal)CompletedFileCount / TotalFileCount * 100m, 2).ToString() + '%';  
                string firsthalf = BoxVert + new string(' ', toprow.Length / 2 - ppercent.Length) + ppercent;           
                string secondhalf = new string(' ', toprow.Length - firsthalf.Length - 1) + BoxVert;                    

                WriteLineEx(toprow, true, ConsoleColor.White, 0, 3, false, true);
                WriteLineEx(firsthalf + secondhalf, true, ConsoleColor.White,0 ,4, false, true);
                WriteLineEx(BoxVert + new string(BoxFill, FilledBlockCount) + new string(BoxEmpty, EmptyBlockCount) + BoxVert, true, ConsoleColor.White, 0, 5, false, true);
                WriteLineEx(BoxBendD + new string(BoxHoriz, EmptyBlockCount + FilledBlockCount) + BoxBendC, true, ConsoleColor.White, 0, 6, false, true);
                Thread.Sleep(50);
            }
        }

        /// <summary>
        /// Simplifies console print operations
        /// </summary>
        /// <param name="message">String to be output to console window</param>
        /// <param name="isCentered">TRUE if centered in window, FALSE otherwise</param>
        /// <param name="cForeground">Text (foreground) color</param>
        /// <param name="left">Console cell / padding</param>
        /// <param name="top">Console row</param>
        /// <param name="clrRow">TRUE to clear row before output</param>
        /// <param name="termRow">TRUE to add termination char to output</param>
        static void WriteLineEx(string message, bool isCentered, ConsoleColor? cForeground, int left, int top, bool clrRow, bool termRow)
        {
            try
            {
                conSph.Wait();

                if (cForeground.HasValue)
                {
                    Console.ForegroundColor = (ConsoleColor)cForeground;
                    LastConsoleForeground = (ConsoleColor)cForeground;
                }

                else { cForeground = LastConsoleForeground; }

                int padCnt = 0;

                // if a console position has been defined...
                if (left != -1 && top != -1)
                {
                    // if centered text is selected, calculate left-pad
                    if (isCentered)
                    {
                        int screenWidth = Console.WindowWidth;
                        int stringWidth = message.Length;
                        padCnt = (screenWidth / 2) + (stringWidth / 2);
                    }

                    // otherwise, set position normally
                    Console.SetCursorPosition(left, top);
                }

                // Clear row of existing data
                if (clrRow) { Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r"); }

                if (termRow) { Console.WriteLine(message.PadLeft(padCnt)); }
                else { Console.Write(message.PadLeft(padCnt)); }
            }

            finally
            {
                conSph.Release();
            }
        }

        static string HashFinder(List<string> files)
        {
            try
            {
                ProcessFinished = false;
                TotalFileCount = files.Count();

                Task.Factory.StartNew(() => UpdateProcessProgress());

                Console.Clear();

                // Create a unique filename for our output file
                string FolderName = new DirectoryInfo(RootDirectory).Name;
                string LogPath = FilenameGenerator(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                    "FileHashes_" + ScrubStringForFilename(FolderName) + ".csv", 1024);

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                ConcurrentBag<FileData> resultdata = new ConcurrentBag<FileData>();

                // Compute MD5 hashes for each file in selected directory
                // Using foreach vs parallel foreach because we want more sequential reads of HDD.
                Parallel.ForEach(files, parallelOptions, s =>
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
                    }
                });

                // Write computed hashes to .csv on desktop
                WriteLineEx("Writing results to disk", false, ConsoleColor.Cyan, 0, 8, true, false);
                GoSpin = true;

                using (var sw = new StreamWriter(LogPath))
                {
                    using (CsvWriter csv = new CsvWriter(sw, CultureInfo.CurrentCulture))
                    {
                        csv.WriteRecords(resultdata);
                    }
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
                GoSpin = false;
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
                    if (FilesAdded == 0) 
                    {
                        WriteLineEx("Select first file for comparison.", false, ConsoleColor.White, 0, 0, true, true);
                    }
                    if (FilesAdded == 1) 
                    {
                        WriteLineEx("Select second file for comparison.", false, ConsoleColor.White, 0, 0, true, true);
                    }

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
                        WriteLineEx("No file was selected.", false, ConsoleColor.Red, 0, 0, true, true);
                    }
                }

                WriteLineEx("Working", false, ConsoleColor.Cyan, 0, 0, true, false);
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
                    WriteLineEx("Query timed out.", false, ConsoleColor.Red, 0, 2, true, true);
                }
                else
                {
                    WriteLineEx("Query completed successfully.", false, ConsoleColor.Green, 0, 2, true, true);
                }

                if (FileDifferences.Count == 0)
                {
                    GoSpin = false;
                    Thread.Sleep(250);
                    WriteLineEx("No differences found.", false, ConsoleColor.Yellow, 0, 3, true, true);
                    Console.ReadKey();
                }

                GoSpin = false;
                Thread.Sleep(250);

                Console.Clear();
                if (FileDifferences.Count > 0)
                {
                    WriteLineEx(FileDifferences.Count().ToString() + " file differences found.", false, ConsoleColor.Green, 0, 3, true, true);
                    WriteLineEx("Press C to copy differences to folder on Desktop.", false, ConsoleColor.White, 0, 5, true, true);
                    WriteLineEx("Press any other key to exit.", false, ConsoleColor.White, 0, 6, true, true);

                    ConsoleKeyInfo op = Console.ReadKey(true);
                    if (op.KeyChar.ToString().ToUpper() == "C")
                    {
                        Console.Clear();
                        WriteLineEx("Copying data, please wait", false, ConsoleColor.Cyan, 0, 0, false, false);
                        GoSpin = true;

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
                            try
                            {
                                File.Copy(diff.FilePath, fname, false);
                            }
                            catch (Exception ex)
                            {
                                eBucket.Enqueue(new ExceptionData { Message = "Copy error: ", Exception = ex, FilePath = diff.FilePath });
                            }
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
                        Thread.Sleep(250);
                        Console.Clear();
                        WriteLineEx("Copy operation finished successfully.", false, ConsoleColor.Green, 0, 0, false, true);
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                }
                WriteLineEx("Press any key to exit.", false, ConsoleColor.White, 0, 2, false, true);
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ReadKey();
            }
            finally { GoSpin = false; }
        }

        static string ScrubStringForFilename(string inputString)
        {
            // Remove invalid characters
            return Regex.Replace(inputString, @"[\\/:*?""<>|]", "");
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
    public class ExceptionData
    {
        public string Message {  get; set; }
        public Exception Exception { get; set; }
        public string FilePath { get; set; }
    }
}

