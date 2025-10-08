using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SportRental.Admin.Services.Media;

public sealed class MediaStorageProcessHostedService : IHostedService, IDisposable
{
    private readonly ILogger<MediaStorageProcessHostedService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private Process? _process;

    public MediaStorageProcessHostedService(
        ILogger<MediaStorageProcessHostedService> logger,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        var section = _configuration.GetSection("MediaStorage");
        var autoStart = section.GetValue<bool?>("AutoStart") ?? false;
        if (!autoStart)
        {
            return Task.CompletedTask;
        }

        try
        {
            var projectPath = ResolveProjectPath(section.GetValue<string>("ProjectPath"));
            var launchProfile = section.GetValue<string>("LaunchProfile") ?? "https";
            if (!Directory.Exists(projectPath))
            {
                _logger.LogWarning("Media storage project path {Path} not found. Skipping auto start.", projectPath);
                return Task.CompletedTask;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectPath}\" --launch-profile {launchProfile}",
                WorkingDirectory = projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    SafeLog(() => _logger.LogDebug("[MediaStorage] {Message}", e.Data));
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    SafeLog(() => _logger.LogError("[MediaStorage] {Message}", e.Data));
                }
            };

            if (process.Start())
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;
                SafeLog(() => _logger.LogInformation("Media storage service started (PID {Pid}).", process.Id));
            }
            else
            {
                SafeLog(() => _logger.LogWarning("Failed to start media storage service process."));
            }
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex, "Failed to start media storage service."));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                _logger.LogInformation("Stopping media storage service (PID {Pid})...", process.Id);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            SafeLog(() => _logger.LogError(ex, "Error while stopping media storage service."));
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
    }

    private static string ResolveProjectPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var full = Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
            if (Directory.Exists(full))
            {
                return full;
            }
        }

        // Fallback: navigate up to solution root and locate project by name
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && current != null; i++)
        {
            var candidate = Path.Combine(current.FullName, "SportRental.MediaStorage");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return configuredPath ?? "SportRental.MediaStorage";
    }

    private void SafeLog(Action logAction)
    {
        try
        {
            logAction();
        }
        catch (ObjectDisposedException)
        {
            // Logging infrastructure has already been torn down (e.g. during test host shutdown).
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("EventLog", StringComparison.OrdinalIgnoreCase))
        {
            // Windows EventLog provider disposed or unavailable.
        }
    }
}
