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
        private static readonly List<string> _loadingLog = new List<string>();

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetEnvironmentVariable(string lpName, string lpValue);

        /// <summary>
        /// Setup Azure App Service environment for SAP HANA client
        /// </summary>
        private static void SetupAzureEnvironment()
        {
            try
            {
                var logMessage = "🔧 Setting up Azure environment for SAP HANA client";
                Console.WriteLine(logMessage);
                _loadingLog.Add(logMessage);

                // Set TEMP directory for Azure App Service
                var tempPath = @"D:\local\Temp";
                if (Directory.Exists(tempPath))
                {
                    SetEnvironmentVariable("TEMP", tempPath);
                    SetEnvironmentVariable("TMP", tempPath);
                    _loadingLog.Add($"  ✅ Set TEMP directory to {tempPath}");
                }

                // Disable certificate validation for Azure environment
                SetEnvironmentVariable("SAP_SSL_DISABLE_HOSTNAME_VERIFICATION", "1");
                SetEnvironmentVariable("SAP_SSL_DISABLE_CERTIFICATE_VALIDATION", "1");
                _loadingLog.Add("  ✅ Disabled SSL certificate validation for Azure");

                // Set current directory as library path
                var currentDir = AppContext.BaseDirectory;
                SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH") + ";" + currentDir);
                _loadingLog.Add($"  ✅ Added {currentDir} to PATH");
            }
            catch (Exception ex)
            {
                var errorMessage = $"  ⚠️ Failed to setup Azure environment: {ex.Message}";
                Console.WriteLine(errorMessage);
                _loadingLog.Add(errorMessage);
            }
        }

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
                    // Set up Azure-specific environment for SAP HANA client
                    SetupAzureEnvironment();
                    
                    var basePath = AppContext.BaseDirectory;
                    var possiblePaths = new[]
                    {
                        basePath,
                        Path.Combine(basePath, "runtimes", "win-x64", "native"),
                        Path.Combine(basePath, "runtimes", "win", "lib", "net8.0"),
                        Path.Combine(basePath, "bin"),
                        Environment.CurrentDirectory,
                        @"C:\home\site\wwwroot", // Azure App Service path (C: drive)
                        @"C:\home\site\wwwroot\runtimes\win-x64\native", // Azure runtime path (C: drive)
                        @"D:\home\site\wwwroot", // Azure App Service path (D: drive)
                        @"D:\home\site\wwwroot\runtimes\win-x64\native" // Azure runtime path (D: drive)
                    };

                    var librariesToLoad = new[] { "libSQLDBCHDB.dll", "libadonetHDB.dll" };

                    var logMessage = $"🔍 Starting explicit library loading for {librariesToLoad.Length} libraries...";
                    Console.WriteLine(logMessage);
                    _loadingLog.Add(logMessage);
                    
                    foreach (var library in librariesToLoad)
                    {
                        var libLogMessage = $"📋 Attempting to load {library}:";
                        Console.WriteLine(libLogMessage);
                        _loadingLog.Add(libLogMessage);
                        var loaded = false;
                        
                        foreach (var path in possiblePaths)
                        {
                            var libraryPath = Path.Combine(path, library);
                            var exists = File.Exists(libraryPath);
                            var checkLogMessage = $"  🔍 Checking {libraryPath}: {(exists ? "EXISTS" : "NOT FOUND")}";
                            Console.WriteLine(checkLogMessage);
                            _loadingLog.Add(checkLogMessage);
                            
                            if (exists)
                            {
                                try
                                {
                                    var handle = LoadLibrary(libraryPath);
                                    if (handle != IntPtr.Zero)
                                    {
                                        var successLogMessage = $"  ✅ Successfully loaded {library} from {libraryPath} (Handle: 0x{handle:X})";
                                        Console.WriteLine(successLogMessage);
                                        _loadingLog.Add(successLogMessage);
                                        loaded = true;
                                        break;
                                    }
                                    else
                                    {
                                        var error = Marshal.GetLastWin32Error();
                                        var errorLogMessage = $"  ❌ LoadLibrary failed for {libraryPath} (Win32 Error: {error})";
                                        Console.WriteLine(errorLogMessage);
                                        _loadingLog.Add(errorLogMessage);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    var exceptionLogMessage = $"  ❌ Exception loading {libraryPath}: {ex.Message}";
                                    Console.WriteLine(exceptionLogMessage);
                                    _loadingLog.Add(exceptionLogMessage);
                                }
                            }
                        }
                        
                        if (!loaded)
                        {
                            var finalLogMessage = $"  ⚠️ Final result: Could not load {library} from any location";
                            Console.WriteLine(finalLogMessage);
                            _loadingLog.Add(finalLogMessage);
                        }
                    }

                    _librariesLoaded = true;
                    var completedLogMessage = "🔍 SAP HANA native library loading completed";
                    Console.WriteLine(completedLogMessage);
                    _loadingLog.Add(completedLogMessage);
                }
                catch (Exception ex)
                {
                    var errorLogMessage = $"❌ Error loading SAP HANA libraries: {ex.Message}";
                    Console.WriteLine(errorLogMessage);
                    _loadingLog.Add(errorLogMessage);
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
            
            // Add loading log if available
            if (_loadingLog.Any())
            {
                diagnostics.Add("\nLibrary Loading Log:");
                diagnostics.AddRange(_loadingLog);
            }
            
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