
using CsvHelper;
using System;
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

        [STAThread]
        static void Main(string[] args)
        {
            Task.Factory.StartNew(() => LoadSpinTask());
            Console.Title = "Recursive MD5 Hasher";

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

            HashFinder(files);

            GoSpin = false;

            Console.WriteLine("");
            Console.WriteLine("Finished in " + stopwatch.Elapsed.ToString() + ".");
            Console.ReadKey();
        }

        static List<string> FileList()
        {
            Console.WriteLine("Select a folder to read...");

            string dir = string.Empty;
            List<string> files = new List<string>();

            while (dir == string.Empty)
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
                    dir = hf.SelectedPath;
                    Console.WriteLine("Selected path: '" + dir.ToString() + "'");
                    Console.WriteLine("Reading directory info");
                    GoSpin = true;
                    AddFiles(hf.SelectedPath, files);
                }
                else
                {
                    Console.WriteLine("No directory selected. Please select a folder.");
                    Console.ReadKey();
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

        static void HashFinder(List<string> files)
        {
            Console.WriteLine("\rCalculating file hashes, please wait");
            string LogPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\hashout.csv";

            decimal currentfile = 1;

            ConcurrentBag<FileData> resultdata = new ConcurrentBag<FileData>();

            FileData objHeader = new FileData()
            {
                FilePath = "Directory",
                FileHash = "MD5 Hash",
                DateOfAnalysis = "Date analyzed"
            };
            resultdata.Add(objHeader);

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
                                Console.WriteLine("\x000DCurrent File: " + f.ToString());
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
        }
    }
    public class FileData
    {
        public string FilePath { get; set; }
        public string FileHash { get; set; }
        public string DateOfAnalysis { get; set; }
    }
}
