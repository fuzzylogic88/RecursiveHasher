
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
            Console.WriteLine("Finished in " + stopwatch.Elapsed.ToString() + ". (parallel foreach)");
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
                    Thread.Sleep(250);
                }
                Thread.Sleep(100);
            }
        }

        static void HashFinder(List<string> files)
        {
            Console.WriteLine("\rCalculating file hashes, please wait.");
            string LogPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\hashout.csv";

            decimal Progress = 1;

            // todo CONVERT TO CSVHELPER OBJ

            ConcurrentBag<string> resultdata = new ConcurrentBag<string>();

            using (StreamWriter writer = new StreamWriter(LogPath))
            {
                writer.WriteLine("Path,MD5 Hash");
                Parallel.ForEach(files, f =>
                {
                    using (MD5 MD5hsh = MD5.Create())
                    {
                        try
                        {
                            using (var stream = File.OpenRead(f))
                            {
                                Console.WriteLine("\x000DCurrent File: " + f.ToString());
                                string MD5 = BitConverter.ToString(MD5hsh.ComputeHash(stream)).Replace("-", string.Empty);
                                resultdata.Add(f.ToString() + "," + MD5);

                                decimal p =  Math.Round(Progress / files.Count() * 100m,2);
                                Console.Title = p.ToString() + "% complete.";
                                Progress++;
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            resultdata.Add(f.ToString() + "," + "Read access denied.");
                        }
                        catch (IOException)
                        {
                            resultdata.Add(f.ToString() + "," + "File in use.");
                        }
                    }
                });

                foreach (string d in resultdata)
                {
                    writer.WriteLine(d);
                }
                writer.Flush();
                writer.Close(); 
            }
        }
    }
}
