/* 
 * RecursiveHasher
 * Daniel Green, 2022
 * 
 * Globals.cs - Global-scope variables 
 */

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace RecursiveHasher
{
    internal class Globals
    {
        public static string RootDirectory = string.Empty;

        // Semaphore metering access to console position/color/writes 
        public static readonly SemaphoreSlim conSph = new(1, 1);
        public static bool RedrawRequired = false;

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
