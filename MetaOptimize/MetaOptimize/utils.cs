namespace MetaOptimize {
    using System;
    using System.IO;
    /// <summary>
    /// Implements a utility function with some .
    /// </summary>
    public static class Utils {
        /// <summary>
        /// appends the given line to the end of file.
        /// </summary>
        public static void AppendToFile(string dirname, string filename, string line) {
            AppendToFile(Path.Combine(dirname, filename), line);
        }

        /// <summary>
        /// appends the given line to the end of file.
        /// </summary>
        public static void AppendToFile(string path, string line) {
            if (!File.Exists(path)) {
                throw new System.Exception("file " + path + " does not exist!");
            }
            using (StreamWriter file = new (path, append: true)) {
                file.WriteLine(line);
            }
        }

        /// <summary>
        /// creates the file in the given directory.
        /// </summary>
        public static string CreateFile(string dirname, string filename, bool removeIfExist) {
            string path = Path.Combine(dirname, filename);
            Directory.CreateDirectory(dirname);
            if (removeIfExist) {
                RemoveFile(dirname, filename);
            }
            using (File.Create(path)) {
            }
            return path;
        }

        /// <summary>
        /// creates the file in the given directory.
        /// </summary>
        public static string CreateFile(string path, bool removeIfExist) {
            var filename = Path.GetFileName(path);
            var dirname = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dirname);
            if (removeIfExist) {
                RemoveFile(dirname, filename);
            }
            using (File.Create(path)) {
            }
            return path;
        }

        /// <summary>
        /// creates the file in the given directory.
        /// </summary>
        public static string CreateFile(string path, bool removeIfExist, bool addFid) {
            var filename = Path.GetFileName(path);
            var extension = Path.GetExtension(filename);
            var filenameWoE = Path.GetFileNameWithoutExtension(filename);
            filename = filenameWoE + "_" + GetFID() + extension;
            var dirname = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dirname);
            if (removeIfExist) {
                RemoveFile(dirname, filename);
            }
            path = Path.Combine(dirname, filename);
            using (File.Create(path)) {
            }
            return path;
        }

        /// <summary>
        /// remove the file if exists.
        /// </summary>
        public static void RemoveFile(string dirname, string filename) {
            string path = Path.Combine(dirname, filename);
            RemoveFile(path);
        }

        /// <summary>
        /// remove the file if exists.
        /// </summary>
        public static void RemoveFile(string path) {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }

        /// <summary>
        /// return some fid based on the date of today.
        /// </summary>
        public static string GetFID() {
            string fid = DateTime.Now.Year + "_" + DateTime.Now.Month + "_" + DateTime.Now.Day + "_" + DateTime.Now.Hour + "_" + DateTime.Now.Minute + "_" +
                DateTime.Now.Second + "_" + DateTime.Now.Millisecond;
            return fid;
        }

        /// <summary>
        /// write line to consule if verbose = true.
        /// </summary>
        public static void WriteToConsole(string line, bool verbose) {
            if (verbose) {
                Console.WriteLine(line);
            }
        }

        /// <summary>
        /// log state.
        /// </summary>
        public enum LogState
        {
            /// <summary>
            /// info.
            /// </summary>
            INFO,
            /// <summary>
            /// warning.
            /// </summary>
            WARNING,
            /// <summary>
            /// error.
            /// </summary>
            ERROR,
        }
        /// <summary>
        /// logger for storing output.
        /// </summary>
        public static void logger(string line, bool verbose, LogState state = LogState.INFO)
        {
            string output = "";
            switch (state)
            {
                case LogState.INFO:
                    output += "[INFO]";
                    break;
                case LogState.WARNING:
                    output += "[WARNING]";
                    break;
                case LogState.ERROR:
                    output += "[ERROR]";
                    break;
                default:
                    throw new Exception("state value is not valid");
            }
            output += " " + line;
            WriteToConsole(output, verbose);
        }

        /// <summary>
        /// store progress if store progress is true.
        /// </summary>
        public static void StoreProgress(string path, string line, bool storeProgress) {
            if (storeProgress) {
                Utils.AppendToFile(path, line);
            }
        }
    }
}