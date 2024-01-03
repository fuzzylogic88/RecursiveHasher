global using static RecursiveHasher.Globals;

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using static RecursiveHasher.NativeMethods;

namespace RecursiveHasher
{
    internal class ConsoleHelpers
    {
        public static void ConsoleSetup()
        {
            Console.OutputEncoding = Encoding.Unicode;
            Console.CursorVisible = false;
            Console.SetWindowSize(100, 40);

            IntPtr handle = GetConsoleWindow();

            // Set font
            CONSOLE_FONT_INFO_EX consoleFont = new()
            {
                cbSize = Marshal.SizeOf<CONSOLE_FONT_INFO_EX>(),
                nFont = 0,
                dwFontSize = new COORD { X = 12, Y = 24 },
                FontFamily = 0,
                FontWeight = 400,
                FaceName = "Consolas"
            };

            IntPtr consoleHandle = GetStdHandle((int)StdHandle.STD_OUTPUT_HANDLE);
            int result = SetCurrentConsoleFontEx(consoleHandle, false, ref consoleFont);

            Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);

            Console.Title = "MD5 Hasher";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.BackgroundColor = ConsoleColor.Black;

            Task.Factory.StartNew(() => ConsoleResizeWatcher());
            Task.Factory.StartNew(() => ExceptionOut());

            // Disable user selection of console text to prevent accidental process pauses
            QuickEditMode(false);
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
                try
                {
                    // get number of filled & empty boxes to display
                    FilledBlockCount = (int)Math.Ceiling((decimal)CompletedFileCount / TotalFileCount * PBarBlockCapacity);
                    EmptyBlockCount = PBarBlockCapacity - FilledBlockCount;

                    WriteLineEx("\rCalculating file hashes, please wait...", false, ConsoleColor.Cyan, 0, 0, true, true);

                    string curFile = "Current File: " + StringExtensions.Truncate(Path.GetFileName(currentfilename.ToString()), Console.WindowWidth - 20);
                    WriteLineEx(curFile, false, ConsoleColor.Cyan, 0, 1, true, true);

                    if (RedrawRequired) { WriteLineEx(string.Empty, false, ConsoleColor.Cyan, 0, 2, true, true); }

                    // Draw progressbar to console window...
                    string topRow = BoxBendA + new string(BoxHoriz, EmptyBlockCount + FilledBlockCount) + BoxBendB;
                    string pPercentStr = Math.Round((decimal)CompletedFileCount / TotalFileCount * 100m, 2).ToString() + '%';
                    string firstHalf = BoxVert + new string(' ', topRow.Length / 2 - pPercentStr.Length) + pPercentStr;
                    string secondHalf = new string(' ', topRow.Length - firstHalf.Length - 1) + BoxVert;

                    WriteLineEx(topRow, true, ConsoleColor.White, 0, 3, RedrawRequired, true);
                    WriteLineEx(firstHalf + secondHalf, true, ConsoleColor.White, 0, 4, RedrawRequired, true);
                    WriteLineEx(BoxVert + new string(BoxFill, FilledBlockCount) + new string(BoxEmpty, EmptyBlockCount) + BoxVert, true, ConsoleColor.White, 0, 5, RedrawRequired, true);
                    WriteLineEx(BoxBendD + new string(BoxHoriz, EmptyBlockCount + FilledBlockCount) + BoxBendC, true, ConsoleColor.White, 0, 6, RedrawRequired, true);

                    if (RedrawRequired) { WriteLineEx(string.Empty, false, ConsoleColor.Cyan, 0, 7, true, true); }

                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.StackTrace);
                }
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
        public static void WriteLineEx(string message, bool isCentered, ConsoleColor? cForeground, int left, int top, bool clrRow, bool termRow)
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
                    try
                    {
                        if (isCentered)
                        {
                            int screenWidth = Console.WindowWidth;
                            int stringWidth = message.Length;
                            padCnt = (screenWidth / 2) + (stringWidth / 2);
                        }

                        // otherwise, set position normally
                        Console.SetCursorPosition(left, top);
                    }
                    catch
                    {
                        // Console window is likely zero-size
                        return;
                    }
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

        /// <summary>
        /// Monitors the exception bucket for new errors, and prints them to console window as needed.
        /// </summary>
        public static void ExceptionOut()
        {
            // starting row for exceptions to be printed on
            int consolePos = StartingExceptionRow;
            int lastConsolePos;
            string LastException;

            StringBuilder exStrb = new();

            while (true)
            {
                try
                {
                    // check for pending exceptions to post to UI
                    while (eBucket.TryDequeue(out ExceptionData r))
                    {
                        lastConsolePos = consolePos;
                        LastException = exStrb.ToString();

                        // Add new exception to stringbuilder...
                        exStrb.Clear();
                        exStrb.Append(RightPoint + r.Message);
                        exStrb.Append(r.Exception.GetType().ToString());
                        exStrb.Append(" - " + StringExtensions.Truncate(Path.GetFileName(r.FilePath), Console.WindowWidth - exStrb.Length - 20));

                        // increment row by one for each failed file, eventually wrapping back to the initial row
                        consolePos = (consolePos + 1) % (Console.WindowHeight - 1);
                        if (consolePos == 0) { consolePos = StartingExceptionRow; }

                        WriteLineEx(exStrb.ToString(), false, ConsoleColor.Red, 0, consolePos, true, true);

                        // remove the pointing string from the prior row if applicable
                        if (!string.IsNullOrEmpty(LastException))
                        {
                            WriteLineEx(LastException.Replace(RightPoint, new string(' ', RightPoint.Length)), false, ConsoleColor.Red, 0, lastConsolePos, true, true);
                        }
                    }

                    // If flag was raised to clear exceptions, write empty chars to all lines holding exception info
                    if (ClearVisibleExceptions.IsSet)
                    {
                        consolePos = StartingExceptionRow;
                        for (int i = consolePos; i < Console.WindowHeight - 1; i++)
                        {
                            WriteLineEx(string.Empty, false, null, 0, i, true, false);
                        }
                        ClearVisibleExceptions.Reset();
                    }
                    Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Exception painter: " + ex.StackTrace);
                }
            }
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

        public static bool RedrawRequired = false;
        /// <summary>
        /// Task monitoring console window for size changes, raising redraw flag when necessary
        /// </summary>
        public static void ConsoleResizeWatcher()
        {
            while (true)
            {
                // Compare old/new window sizes with delay
                CWindowSz oldCWindowSz = new() { Width = Console.WindowWidth, Height = Console.WindowHeight };
                Thread.Sleep(100);
                CWindowSz newCWindowSz = new() { Width = Console.WindowWidth, Height = Console.WindowHeight };

                // Window size changed? 
                if (oldCWindowSz.Width != newCWindowSz.Width || oldCWindowSz.Height != newCWindowSz.Height)
                {
                    // Allow redraw to occur on other drawing operations for an amount of time.
                    RedrawRequired = true;
                    Thread.Sleep(500);
                    RedrawRequired = false;
                }
            }
        }
    }
}
