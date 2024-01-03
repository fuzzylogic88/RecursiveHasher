using System;
using System.Collections.Concurrent;
using System.Threading;

namespace RecursiveHasher
{
    internal class Globals
    {
        // box
        public static readonly char BoxFill = '\u2588';
        public static readonly char BoxEmpty = '\u2593';

        public static readonly char BoxBendA = '\u2554';
        public static readonly char BoxBendB = '\u2557';
        public static readonly char BoxBendC = '\u255D';
        public static readonly char BoxBendD = '\u255A';
        public static readonly char BoxHoriz = '\u2550';
        public static readonly char BoxVert = '\u2551';

        public static readonly int StartingExceptionRow = 8;

        public static readonly string RightPoint = ">> ";

        public static Version minWinVersion = new(6, 1);

        public static string RootDirectory = string.Empty;

        // Semaphore metering access to console position/color/writes 
        public static readonly SemaphoreSlim conSph = new(1, 1);

        public static ConsoleColor LastConsoleForeground = ConsoleColor.White;

        public static readonly int PBarBlockCapacity = 64;
        public static int FilledBlockCount = 0;
        public static int EmptyBlockCount = 0;

        public static ConcurrentQueue<ExceptionData> eBucket = new();
        public static ManualResetEventSlim ClearVisibleExceptions = new(false);

        public static string currentfilename = string.Empty;
        public static decimal progresspercent = 0m;
        public static int CompletedFileCount = 0;
        public static int TotalFileCount = 0;

        public static bool ProcessFinished = false;
    }
}
