﻿using Remotely_Agent.Client;
using Remotely_Library.Services;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management.Automation;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Remotely_Agent.Services
{
    public class Updater
    {
        internal static async Task<string> GetLatestScreenCastVersion()
        {
            var platform = "";
            if (OSUtils.IsWindows)
            {
                platform = "Windows";
            }
            else if (OSUtils.IsLinux)
            {
                platform = "Linux";
            }
            else
            {
                throw new Exception("Unsupported operating system.");
            }
            var response = await new HttpClient().GetAsync(Utilities.GetConnectionInfo().Host + $"/API/ScreenCastVersion/{platform}");
            return await response.Content.ReadAsStringAsync();
        }


        internal static void CheckForCoreUpdates()
        {
            try
            {
                var platform = "";
                if (OSUtils.IsWindows)
                {
                    platform = "Windows";
                }
                else if (OSUtils.IsLinux)
                {
                    platform = "Linux";
                }
                else
                {
                    throw new Exception("Unsupported operating system.");
                }

                var wc = new WebClient();
                var latestVersion = wc.DownloadString(Utilities.GetConnectionInfo().Host + $"/API/CoreVersion/{platform}");
                var thisVersion = FileVersionInfo.GetVersionInfo("Remotely_Agent.dll").FileVersion.ToString();
                if (thisVersion != latestVersion)
                {
                    Logger.Write($"Service Updater: Downloading update.  Current Version: {thisVersion}.  Latest Version: {latestVersion}.");
                    var fileName = OSUtils.CoreZipFileName;
                    var tempFile = Path.Combine(Path.GetTempPath(), fileName);
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                    wc.DownloadFile(new Uri(Utilities.GetConnectionInfo().Host + $"/Downloads/{fileName}"), tempFile);

                    Logger.Write($"Service Updater: Extracting files.");

                    ZipFile.ExtractToDirectory(tempFile, Path.Combine(Path.GetTempPath(), "Remotely_Update"), true);
                    if (OSUtils.IsLinux)
                    {
                        Process.Start("sudo", $"chmod -R 777 {Path.Combine(Path.GetTempPath(), "Remotely_Update")}").WaitForExit();
                        Process.Start("sudo", $"chmod +x {Path.Combine(Path.GetTempPath(), "Remotely_Update", "Remotely_Agent")}").WaitForExit();
                    }
                    var psi = new ProcessStartInfo()
                    {
                        FileName = Path.Combine(Path.GetTempPath(), "Remotely_Update", OSUtils.ClientExecutableFileName),
                        Arguments = "-update true",
                        Verb = "RunAs"
                    };

                    Logger.Write($"Service Updater: Launching new process.");
                    Process.Start(psi);
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Write(ex);
            }
        }
        internal static void CoreUpdate()
        {
            try
            {
                Logger.Write("Service Updater: Starting update.");
                var ps = PowerShell.Create();
                if (OSUtils.IsWindows)
                {
                    ps.AddScript(@"Get-Service | Where-Object {$_.Name -like ""Remotely_Service""} | Stop-Service -Force");
                    ps.Invoke();
                    ps.Commands.Clear();
                }
                else if (OSUtils.IsLinux)
                {
                    Process.Start("sudo", "systemctl stop remotely_service");
                }

                ps.AddScript(@"
                    Get-Process | Where-Object {
                        $_.Name -like ""Remotely_Agent"" -and 
                        $_.Id -ne [System.Diagnostics.Process]::GetCurrentProcess().Id
                    } | Stop-Process -Force");
                ps.Invoke();
                ps.Commands.Clear();

                Logger.Write("Service Updater: Gathering files.");
                var targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Remotely");
 
                var itemList = Directory.GetFileSystemEntries(Path.Combine(Path.GetTempPath(), "Remotely_Update"));
                Logger.Write("Service Updater: Copying new files.");
                foreach (var item in itemList)
                {
                    try
                    {
                        var targetPath = Path.Combine(targetDir, Path.GetFileName(item));
                        if (File.Exists(targetPath))
                        {
                            File.Delete(targetPath);
                        }
                        else if (Directory.Exists(targetPath))
                        {
                            Directory.Delete(targetPath, true);
                        }
                        Directory.Move(item, targetPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Write(ex);
                    }
                }
                Logger.Write("Service Updater: Update completed.");
            }
            catch (Exception ex)
            {
                Logger.Write(ex);               
            }
            finally
            {
                Logger.Write("Service Updater: Starting service.");
                if (OSUtils.IsWindows)
                {
                    var ps = PowerShell.Create();
                    ps.AddScript("Start-Service -Name \"Remotely_Service\"");
                    ps.Invoke();
                }
                else if (OSUtils.IsLinux)
                {
                    Process.Start("sudo", "systemctl restart remotely-agent").WaitForExit();
                }
                Environment.Exit(0);
            }
        }
    }
}