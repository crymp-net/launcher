using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace launcher
{
    class Util
    {
        public Util()
        {
        }

        [DllImport("kernel32.dll")]
        private static extern bool IsWow64Process(
            IntPtr hProcess,
            out bool wow64Process
        );

        public bool InternalCheckIsWow64()
        {
            if ((Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1) ||
                Environment.OSVersion.Version.Major >= 6)
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    bool retVal;
                    if (!IsWow64Process(p.Handle, out retVal))
                    {
                        return false;
                    }
                    return retVal;
                }
            }
            else
            {
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, ShowWindowEnum flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetForegroundWindow(IntPtr hwnd);

        private enum ShowWindowEnum
        {
            Hide = 0,
            ShowNormal = 1, ShowMinimized = 2, ShowMaximized = 3,
            Maximize = 3, ShowNormalNoActivate = 4, Show = 5,
            Minimize = 6, ShowMinNoActivate = 7, ShowNoActivate = 8,
            Restore = 9, ShowDefault = 10, ForceMinimized = 11
        };

        public Process ElevateProcess(Process source, string finalPath, bool createShortcut)
        {
            Process target = new Process();
            target.StartInfo = source.StartInfo;
            target.StartInfo.FileName = source.MainModule.FileName;
            target.StartInfo.Arguments = "install \"" + (finalPath + "\\\\").Replace('"', '_') + "\" " + (createShortcut ? "1" : "0");
            target.StartInfo.WorkingDirectory = Path.GetDirectoryName(source.MainModule.FileName);

            target.StartInfo.UseShellExecute = true;
            target.StartInfo.Verb = "runas";

            try
            {
                if (!target.Start())
                    return null;
                else
                {
                    if (target.MainWindowHandle == IntPtr.Zero)
                    {
                        ShowWindow(target.Handle, ShowWindowEnum.Restore);
                    }
                    SetForegroundWindow(target.MainWindowHandle);
                    Process.GetCurrentProcess().Kill();
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("Failed to elevate process rights in order to write files.\nPlease, run this installer under administrator permissions.\n\nError message: " + e.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Process.GetCurrentProcess().Kill();
            }
            return target;
        }
        public bool TestWriteability(string finalPath)
        {
            bool ok = true;

            try
            {
                File.WriteAllText(finalPath + "install.txt", "OK");
                File.WriteAllText(finalPath + "Game\\install.txt", "OK");
                File.WriteAllText(finalPath + "Game\\Levels\\install.txt", "OK");
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to test file writeability inside folder, please, run this installer with administrator rights, message: \n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Process.GetCurrentProcess().Kill();
                return true;
            }
            finally
            {
                if (File.Exists(finalPath + "install.txt"))
                {
                    if (File.ReadAllText(finalPath + "install.txt") != "OK") ok = false;
                    File.Delete(finalPath + "install.txt");
                }
                else ok = false;
                if (File.Exists(finalPath + "Game\\install.txt"))
                {
                    if (File.ReadAllText(finalPath + "Game\\install.txt") != "OK") ok = false;
                    File.Delete(finalPath + "Game\\install.txt");
                }
                else ok = false;
                if (File.Exists(finalPath + "Game\\Levels\\install.txt"))
                {
                    if (File.ReadAllText(finalPath + "Game\\Levels\\install.txt") != "OK") ok = false;
                    File.Delete(finalPath + "Game\\Levels\\install.txt");
                }
                else ok = false;
            }
            return ok;
        }
        public bool StripRights(string folder)
        {
            Process p = new Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = "icacls.exe";
            p.StartInfo.Arguments = "\"" + folder + "\\\\\" /grant Users:(OI)(CI)F /T";
            p.StartInfo.UseShellExecute = false;
            p.EnableRaisingEvents = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = true;
            return p.Start();
        }

        /// <summary>
        /// HTTP GET raw content
        /// </summary>
        /// <param name="url">endpoint URL</param>
        /// <returns>response</returns>
        /// <exception cref="Exception">If download fails</exception>
        public Task<byte[]> RawGET(string url, Func<DownloadProgressChangedEventArgs, bool> callback = null) 
        {
            TaskCompletionSource<byte[]> promise = new TaskCompletionSource<byte[]>();
            WebClient wc = new WebClient();
            wc.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            wc.DownloadDataCompleted += (object sender, DownloadDataCompletedEventArgs e) =>
            {
                if (e.Error != null)
                {
                    promise.SetException(e.Error);
                }
                else
                {
                    promise.SetResult(e.Result);
                }
            };
            if (callback != null)
            {
                wc.DownloadProgressChanged += (object sender, DownloadProgressChangedEventArgs e) =>
                {
                    callback(e);
                };
            }
            wc.DownloadDataAsync(new Uri(url));
            return promise.Task;
        }

        /// <summary>
        /// HTTP GET content
        /// </summary>
        /// <param name="url">endpoint URL</param>
        /// <returns>response</returns>
        /// <exception cref="Exception">If download fails</exception>
        public async Task<string> GET(string url, Func<DownloadProgressChangedEventArgs, bool> callback = null)
        {
            var result = await RawGET(url, callback);
            return Encoding.UTF8.GetString(result);
        }
        public string BytesToString(byte[] bytes)
        {
            string result = "";
            foreach (byte b in bytes) result += b.ToString("x2");
            return result;
        }

        public string FileSHA256(string path)
        {
            SHA256 sha256 = SHA256.Create();
            using(FileStream stream = File.OpenRead(path))
            {
                byte[] result = sha256.ComputeHash(stream);
                return BytesToString(result).ToLower();
            }
        }
    }
}