// <copyright file="SolverFactory.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    /// <summary>
    /// Factory for creating solver instances with environment variable configuration.
    /// </summary>
    public static class SolverFactory
    {
        /// <summary>
        /// Validates that at least one Gurobi license configuration method is present.
        /// Checks environment variables and default license file locations.
        /// Throws exception with helpful setup instructions if no configuration is found.
        /// </summary>
        public static void ValidateLicenseConfiguration()
        {
            var licenseFile = Environment.GetEnvironmentVariable("GRB_LICENSE_FILE");
            var tokenServer = Environment.GetEnvironmentVariable("GRB_TOKEN_SERVER");
            var wlsAccessId = Environment.GetEnvironmentVariable("GRB_WLSACCESSID");
            var wlsSecret = Environment.GetEnvironmentVariable("GRB_WLSSECRET");
            var licenseId = Environment.GetEnvironmentVariable("GRB_LICENSEID");

            // Check if any valid license configuration exists via environment variables
            bool hasLicenseFile = !string.IsNullOrEmpty(licenseFile);
            bool hasTokenServer = !string.IsNullOrEmpty(tokenServer);
            bool hasWls = !string.IsNullOrEmpty(wlsAccessId) &&
                        !string.IsNullOrEmpty(wlsSecret) &&
                        !string.IsNullOrEmpty(licenseId);

            // Check default license file locations (Gurobi's fallback behavior)
            bool hasDefaultLicense = false;
            string defaultLicensePath = null;

            if (!hasLicenseFile && !hasTokenServer && !hasWls)
            {
                // Check Gurobi's default locations
                var possiblePaths = GetDefaultGurobiLicensePaths();

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        hasDefaultLicense = true;
                        defaultLicensePath = path;
                        break;
                    }
                }
            }

            // If no configuration found anywhere, show error
            if (!hasLicenseFile && !hasTokenServer && !hasWls && !hasDefaultLicense)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Gurobi solver selected but no license configuration found.");
                Console.WriteLine();
                ShowLicenseConfigurationHelp();
                throw new InvalidOperationException("Gurobi license not configured - see setup instructions above");
            }

            // Log which license method is being used
            Console.WriteLine();
            if (hasTokenServer)
            {
                Console.WriteLine($"Using Gurobi token server: {tokenServer}");
            }
            else if (hasLicenseFile)
            {
                Console.WriteLine($"Using Gurobi license file: {licenseFile}");
            }
            else if (hasWls)
            {
                Console.WriteLine($"Using Gurobi Web License Service (Access ID: {wlsAccessId})");
            }
            else if (hasDefaultLicense)
            {
                Console.WriteLine($"Using Gurobi license file from default location: {defaultLicensePath}");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Gets the list of default locations where Gurobi searches for license files.
        /// Follows Gurobi's standard search order.
        /// </summary>
        /// <returns>List of possible license file paths in priority order.</returns>
        private static List<string> GetDefaultGurobiLicensePaths()
        {
            var paths = new List<string>();

            // 1. User's home directory (highest priority for user-specific license)
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homeDir))
            {
                paths.Add(Path.Combine(homeDir, "gurobi.lic"));
            }

            // 2. Current working directory
            paths.Add(Path.Combine(Directory.GetCurrentDirectory(), "gurobi.lic"));

            // 3. Platform-specific system locations
            if (OperatingSystem.IsWindows())
            {
                // Windows: C:\gurobi\ or common program files locations
                paths.Add(@"C:\gurobi\gurobi.lic");
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(programFiles))
                {
                    paths.Add(Path.Combine(programFiles, "gurobi", "gurobi.lic"));
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // Linux/Mac: /opt/gurobi/ or /usr/local/gurobi/
                paths.Add("/opt/gurobi/gurobi.lic");
                paths.Add("/usr/local/gurobi/gurobi.lic");
            }

            return paths;
        }

        /// <summary>
        /// Displays detailed help for configuring Gurobi license via environment variables.
        /// </summary>
        private static void ShowLicenseConfigurationHelp()
        {
            Console.WriteLine("Please set one of the following environment variable configurations:");
            Console.WriteLine();
            Console.WriteLine("  Option 1 - Token Server (recommended for enterprise deployment):");
            Console.WriteLine("    GRB_TOKEN_SERVER=hostname:port");
            Console.WriteLine("    Example: export GRB_TOKEN_SERVER=\"10.137.58.158:41954\"");
            Console.WriteLine();
            Console.WriteLine("  Option 2 - License File:");
            Console.WriteLine("    GRB_LICENSE_FILE=/path/to/gurobi.lic");
            Console.WriteLine("    Example: export GRB_LICENSE_FILE=\"/app/licenses/gurobi.lic\"");
            Console.WriteLine();
            Console.WriteLine("  Option 3 - Web License Service:");
            Console.WriteLine("    GRB_WLSACCESSID=your-access-id");
            Console.WriteLine("    GRB_WLSSECRET=your-secret");
            Console.WriteLine("    GRB_LICENSEID=your-license-id");
            Console.WriteLine();
            Console.WriteLine("For MSRHub deployment:");
            Console.WriteLine("  Set these environment variables on the MSRHub server (requires admin access).");
            Console.WriteLine("  Docker containers will automatically inherit them.");
            Console.WriteLine();
            Console.WriteLine("Alternatively, use the free OR-Tools solver:");
            Console.WriteLine("    --solver Zen");
            Console.WriteLine();
        }
    }
}