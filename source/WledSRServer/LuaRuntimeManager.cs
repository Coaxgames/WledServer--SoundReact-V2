using System.Diagnostics;

namespace WledSRServer
{
    internal sealed class LuaRuntimeManager : IDisposable
    {
        private readonly object _sync = new();
        private Process? _themeProcess;

        public void StartDefaultTheme()
        {
            var themeRoot = Path.Combine(AppContext.BaseDirectory, "Themes");
            if (!Directory.Exists(themeRoot))
                return;

            var themePath = Directory.EnumerateFiles(themeRoot, "theme.lua", SearchOption.AllDirectories)
                                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                                     .FirstOrDefault();
            if (themePath == null)
                return;

            StartTheme(themePath);
        }

        public void StartTheme(string themePath)
        {
            if (!File.Exists(themePath))
                throw new FileNotFoundException("Lua theme was not found.", themePath);

            lock (_sync)
            {
                if (_themeProcess is { HasExited: false })
                    throw new InvalidOperationException("A Lua theme is already running.");

                var runtimeRoot = Path.Combine(AppContext.BaseDirectory, "LuaRT");
                var runtimeExecutable = Path.Combine(runtimeRoot, "bin", "luart.exe");
                if (!File.Exists(runtimeExecutable))
                    throw new FileNotFoundException("The bundled LuaRT interpreter was not found.", runtimeExecutable);

                var moduleRoot = Path.Combine(runtimeRoot, "modules");
                var libraryRoot = Path.Combine(runtimeRoot, "lualibs");
                var themeDirectory = Path.GetDirectoryName(themePath)!;

                var startInfo = new ProcessStartInfo
                {
                    FileName = runtimeExecutable,
                    WorkingDirectory = themeDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add(themePath);
                startInfo.Environment["LUA_PATH"] = string.Join(';',
                    Path.Combine(themeDirectory, "?.lua"),
                    Path.Combine(themeDirectory, "?", "init.lua"),
                    Path.Combine(libraryRoot, "?.lua"),
                    Path.Combine(libraryRoot, "?", "init.lua"));
                startInfo.Environment["LUA_CPATH"] = string.Join(';',
                    Path.Combine(moduleRoot, "?", "?.dll"),
                    Path.Combine(moduleRoot, "?.dll"));
                startInfo.Environment["PATH"] = string.Join(';',
                    Path.Combine(runtimeRoot, "bin"),
                    Environment.GetEnvironmentVariable("PATH") ?? string.Empty);

                var process = Process.Start(startInfo) ?? throw new InvalidOperationException("LuaRT could not be started.");
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                        Debug.WriteLine($"Lua theme: {args.Data}");
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data != null)
                        Debug.WriteLine($"Lua theme error: {args.Data}");
                };
                process.Exited += (_, _) =>
                {
                    lock (_sync)
                    {
                        if (ReferenceEquals(_themeProcess, process))
                            _themeProcess = null;
                    }
                };

                _themeProcess = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (_themeProcess == null)
                    return;

                try
                {
                    if (!_themeProcess.HasExited)
                        _themeProcess.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    _themeProcess.Dispose();
                    _themeProcess = null;
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
