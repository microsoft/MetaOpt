namespace MetaOptimize {
    using System.IO;
    /// <summary>
    /// Implements a utility function with some .
    /// </summary>
    public static class Utils {
        /// <summary>
        /// appends the given line to the end of file.
        /// </summary>
        public static void AppendToFile(string dirname, string filename, string line) {
            if (!File.Exists(dirname + filename)) {
                throw new System.Exception("file " + dirname + filename + " does not exist!");
            }
            using (StreamWriter file = new (dirname + filename, append: true)) {
                file.WriteLine(line);
            }
        }

        /// <summary>
        /// creates the file in the given directory.
        /// </summary>
        public static void CreateFile(string dirname, string filename, bool removeIfExist) {
            Directory.CreateDirectory(dirname);
            if (removeIfExist) {
                RemoveFile(dirname, filename);
            }
            File.Create(dirname + filename);
        }

        /// <summary>
        /// remove the file if exists.
        /// </summary>
        public static void RemoveFile(string dirname, string filename) {
            if (File.Exists(dirname + filename)) {
                File.Delete(dirname + filename);
            }
        }
    }
}