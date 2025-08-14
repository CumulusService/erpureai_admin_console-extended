using System.Runtime.InteropServices;

namespace AdminConsole.Services
{
    /// <summary>
    /// Service to handle explicit loading of SAP HANA native libraries in Azure environment
    /// </summary>
    public static class HanaNativeLibraryLoader
    {
        private static bool _librariesLoaded = false;
        private static readonly object _lockObject = new object();

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        /// <summary>
        /// Explicitly load SAP HANA native libraries before any HANA operations
        /// </summary>
        public static void EnsureLibrariesLoaded()
        {
            if (_librariesLoaded) return;

            lock (_lockObject)
            {
                if (_librariesLoaded) return;

                try
                {
                    var basePath = AppContext.BaseDirectory;
                    var possiblePaths = new[]
                    {
                        basePath,
                        Path.Combine(basePath, "runtimes", "win-x64", "native"),
                        Path.Combine(basePath, "runtimes", "win", "lib", "net8.0"),
                        Path.Combine(basePath, "bin"),
                        Environment.CurrentDirectory,
                        @"D:\home\site\wwwroot", // Azure App Service path
                        @"D:\home\site\wwwroot\runtimes\win-x64\native" // Azure runtime path
                    };

                    var librariesToLoad = new[] { "libSQLDBCHDB.dll", "libadonetHDB.dll" };

                    foreach (var library in librariesToLoad)
                    {
                        var loaded = false;
                        foreach (var path in possiblePaths)
                        {
                            var libraryPath = Path.Combine(path, library);
                            if (File.Exists(libraryPath))
                            {
                                var handle = LoadLibrary(libraryPath);
                                if (handle != IntPtr.Zero)
                                {
                                    Console.WriteLine($"✅ Successfully loaded {library} from {libraryPath}");
                                    loaded = true;
                                    break;
                                }
                            }
                        }
                        
                        if (!loaded)
                        {
                            Console.WriteLine($"⚠️ Could not load {library} from any path");
                            // Log all searched paths for debugging
                            Console.WriteLine($"   Searched paths:");
                            foreach (var path in possiblePaths)
                            {
                                var libraryPath = Path.Combine(path, library);
                                var exists = File.Exists(libraryPath) ? "EXISTS" : "NOT FOUND";
                                Console.WriteLine($"     - {libraryPath} ({exists})");
                            }
                        }
                    }

                    _librariesLoaded = true;
                    Console.WriteLine("🔍 SAP HANA native library loading completed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error loading SAP HANA libraries: {ex.Message}");
                    // Don't throw - let the connection attempt proceed and fail with better error info
                }
            }
        }

        /// <summary>
        /// Get diagnostic information about library availability
        /// </summary>
        public static string GetLibraryDiagnostics()
        {
            var diagnostics = new List<string>();
            var basePath = AppContext.BaseDirectory;
            
            diagnostics.Add($"Base Directory: {basePath}");
            diagnostics.Add($"Current Directory: {Environment.CurrentDirectory}");
            diagnostics.Add($"Runtime Identifier: {RuntimeInformation.RuntimeIdentifier}");
            diagnostics.Add($"OS Description: {RuntimeInformation.OSDescription}");
            diagnostics.Add($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
            
            // Check for library files
            var possiblePaths = new[]
            {
                basePath,
                Path.Combine(basePath, "runtimes", "win-x64", "native"),
                @"D:\home\site\wwwroot",
                @"D:\home\site\wwwroot\runtimes\win-x64\native"
            };

            diagnostics.Add("\nLibrary Search Results:");
            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.dll")
                        .Where(f => Path.GetFileName(f).Contains("HANA", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileName(f).Contains("adonet", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileName(f).Contains("SQLDB", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    
                    diagnostics.Add($"  {path}: {files.Length} HANA-related DLLs");
                    foreach (var file in files)
                    {
                        var info = new FileInfo(file);
                        diagnostics.Add($"    - {Path.GetFileName(file)} ({info.Length:N0} bytes)");
                    }
                }
                else
                {
                    diagnostics.Add($"  {path}: Directory not found");
                }
            }

            return string.Join("\n", diagnostics);
        }
    }
}