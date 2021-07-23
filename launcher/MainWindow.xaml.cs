using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Xml.Serialization;
using System.Web;
using System.Diagnostics;
using System.IO;
//using IWshRuntimeLibrary;

namespace launcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public class UpdateFile
        {
            public string url;
            public string hash;
            public string path;
            public string arch;
            public string type;
            public bool miss = true;
        }

        public class MirrorResponse
        {
            public UpdateFile[] files;
        }

        public class AuthResponse
        {
            public string id;
            public string nickname;
            public string token;
        }

        public class Option
        {
            public string Value { get; set; }
            public string Name { get; set; }
        }

        public class Settings
        {
            public string handle = null;
            public string token = null;
            public string renderer = "dx10";
            public string architecture = "64";
        }

        public class AccountInfo
        {
            public int kills = 0;
            public int deaths = 0;
            public int playedTime = 0;
            public string profileId = "";
            public string master = "crymp.net";
            public string display = "unknown";
            public string handle = null;
            public string token = null;
        }

        public class OnlineStatus
        {
            public int online = 0;
        }

        public T DeserializeXml<T>(byte[] data) where T: class
        {
            XmlSerializer ser = new XmlSerializer(typeof(T));
            using (MemoryStream fs = new MemoryStream(data))
            {
                return (T)ser.Deserialize(fs);
            }
        }

        public T DeserializeXml<T>(string data) where T : class
        {
            return DeserializeXml<T>(Encoding.UTF8.GetBytes(data));
        }

        AccountInfo accountInfo = new AccountInfo();
        OnlineStatus onlineStatus = new OnlineStatus();

        Util util = new Util();
        List<UpdateFile> updateFiles = new List<UpdateFile>();
        bool needsUpdate = true;
        bool installed = true;
        bool validInstallPath = false;
        string installPath = ".\\";
        string knownMaster = null;
        bool isWow64 = false;
        DispatcherTimer globalTimer = new DispatcherTimer();

        Settings settings = new Settings();

        List<Option> archOptions = new List<Option>
        {
                new Option{ Name = "32-bit", Value = "32" },
                new Option{ Name = "64-bit", Value = "64" },
        };

        Option[] rendererOptions = new Option[]
        {
                new Option{ Name = "DirectX 9", Value = "dx9" },
                new Option{ Name = "DirectX 10", Value = "dx10" },
        };

        public MainWindow()
        {
            LoadSettings();
            InitializeComponent();

            loadingGrid.Visibility = Visibility.Visible;
            loadedGrid.Visibility = Visibility.Hidden;

            architecture.ItemsSource = archOptions;
            architecture.DisplayMemberPath = "Name";
            architecture.SelectedValuePath = "Value";
            renderer.ItemsSource = rendererOptions;
            renderer.DisplayMemberPath = "Name";
            renderer.SelectedValuePath = "Value";

            architecture.SelectedValue = settings.architecture;
            renderer.SelectedValue = settings.renderer;

            architecture.SelectionChanged += (s, e) =>
            {
                settings.architecture = (e.AddedItems[0] as Option).Value;
                SaveSettings();
            };

            renderer.SelectionChanged += (s, e) =>
            {
                settings.renderer = (e.AddedItems[0] as Option).Value;
                SaveSettings();
            };

            string[] possibleFolders =
            {
                Directory.GetCurrentDirectory(),
                @"C:\Program Files\Electronic Arts\Crytek\Crysis",
                @"C:\Program Files (x86)\Electronic Arts\Crytek\Crysis",
                @"C:\Program Files (x86)\Steam\steamapps\common\Crysis",
                @"C:\Program Files (x86)\Origin Games\Crysis",
                @"C:\GOG Games\Crysis",
                @"C:\Program Files\Steam\steamapps\common\Crysis",
                @"D:\SteamLibrary\steamapps\common\Crysis",
                @"D:\Electronic Arts\Crytek\Crysis",
                @"D:\Games\Electronic Arts\Crytek\Crysis",
                @"D:\Games\Crysis",
                @"E:\Games\Crysis",
            };

            foreach (string folder in possibleFolders)
            {
                if (Directory.Exists(folder) && VerifyFolder(folder))
                {
                    validInstallPath = true;
                    installPath = folder;
                    instPath.Text = folder;
                    break;
                }
            }

            if (!validInstallPath)
            {
                if (isWow64)
                {
                    instPath.Text = "C:\\Program Files (x86)\\Electronic Arts\\Crytek\\Crysis";
                } else
                {
                    instPath.Text = "C:\\Program Files\\Electronic Arts\\Crytek\\Crysis";
                }
            }
            
            validInstallPath = VerifyFolder(instPath?.Text);

            instProgress.Visibility = Visibility.Hidden;
            instPath.Visibility = Visibility.Hidden;
            instHelper3.Visibility = Visibility.Hidden;
            instHelper2.Visibility = Visibility.Hidden;
            instHelper1.Visibility = Visibility.Hidden;
            isWow64 = util.InternalCheckIsWow64();

            string[] args = Environment.GetCommandLineArgs();

            globalTimer.Tick += new EventHandler(async (s, e) => {
                await UpdateOnlineMessage();
            });

            globalTimer.Interval = new TimeSpan(0, 0, 30);
            globalTimer.Start();

            Initialize(args);
        }

        async void Initialize(string[] args)
        {
            updateFiles.Clear();
            string result = null;
            byte[] changelog = null;

            bool forceInstall = false;

            if (args != null && args.Length >= 4)
            {
                if (args[1] == "install")
                {
                    string path = args[2];
                    string shortcut = args[3];
                    createShortcutCheckbox.IsChecked = shortcut == "1";
                    if (path.EndsWith("\\")) path = path.Substring(0, path.Length - 1);
                    if (VerifyFolder(path))
                    {
                        installPath = args[2];
                        instPath.Text = args[2];
                        forceInstall = true;
                    }
                }
            }

            string master = await GetMaster();

            if(master == null)
            {
                MessageBox.Show("Couldn't find any suitable master server, please consider contacting community", "Internet error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            try
            {
                var t_changelog = util.RawGET("https://"+ master + "/client/changelog.rtf");
                var t_loggedIn = FetchProfile();
                var t_onlineStatus = UpdateOnlineMessage();
                await Task.WhenAll(new Task[] { t_changelog, t_loggedIn, t_onlineStatus });
                changelog = t_changelog.Result;
            }

            catch (Exception)
            {
                MessageBox.Show("Couldn't fetch latest information from server, please try again later", "Internet error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            changeLog.Selection.Load(new MemoryStream(changelog), DataFormats.Rtf);
            
            needsUpdate = CheckFiles(".\\");
            installed = File.Exists(updateFiles[0].path);
            if (installed)
            {
                installPath = Directory.GetCurrentDirectory();
                instPath.Text = installPath;
            }
            mainButton.IsEnabled = true;
            instProgress.Visibility = Visibility.Visible;
            instPath.Visibility = Visibility.Hidden;

            instPath.PreviewMouseLeftButtonUp += OnPromptFolder;

            instPath.TextChanged += (s, e) =>
            {
                validInstallPath = VerifyFolder(instPath.Text);
                installPath = instPath.Text;
                UpdateUI();
            };

            mainButton.Click += (s, e) =>
            {
                OnMainButton();
            };

            UpdateUI();
            loadingGrid.Visibility = Visibility.Hidden;
            loadedGrid.Visibility = Visibility.Visible;

            if (forceInstall)
            {
                OnMainButton();
            }
        }

        async Task<MirrorResponse> NoThrowGetFileList(string master)
        {
            try
            {
                string response = await util.GET(ToEndpoint(master) + "/mirror?xml&latest");
                return DeserializeXml<MirrorResponse>(response);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        async Task<string> GetMaster(List<string> candidates)
        {
            List<Task<MirrorResponse>> responses = new List<Task<MirrorResponse>>();
            foreach(string candidate in candidates)
            {
                responses.Add(NoThrowGetFileList(candidate));
            }
            await Task.WhenAll(responses.ToArray());
            int i = 0;
            foreach(var task in responses)
            {
                if(task.Result != null)
                {
                    updateFiles = task.Result.files.ToList();
                    return candidates[i];
                }
                i++;
            }
            return null;
        }

        async Task<string> GetMaster()
        {
            //if (knownMaster != null) return knownMaster;
            List<string> candidates = new List<string>();
            candidates.Add("crymp.net");
            if (File.Exists("masters.txt"))
            {
                candidates = File.ReadAllLines("masters.txt")
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();
            } else {
                try
                {
                    string fallback = await util.GET("https://raw.githubusercontent.com/ccomrade/crymp-client/master/masters.txt");
                    candidates = fallback.Split('\n')
                        .Select(x => x.Trim())
                        .Where(x => x.Length > 0)
                        .ToList();
                }
                catch (Exception)
                {
                }
            }
            string master = await GetMaster(candidates);
            if(master != null)
            {
                knownMaster = master;
            }
            return master;
        }

        string ToEndpoint(string master)
        {
            return "https://" + master;
        }

        async void OnMainButton()
        {
            if (needsUpdate)
            {
                bool createShortcut = createShortcutCheckbox.IsChecked != null && createShortcutCheckbox.IsChecked == true;
                mainButton.IsEnabled = false;
                mainButton.Content = installed ? "Updating" : "Installing";
                instProgress.Visibility = Visibility.Visible;
                instPath.Visibility = Visibility.Hidden;
                instHelper1.Visibility = Visibility.Hidden;
                instHelper2.Visibility = Visibility.Hidden;
                instHelper3.Content = installed ? "Updating, please wait" : "Installing, please wait";
                instHelper3.Visibility = Visibility.Visible;

                createShortcutCheckbox.IsEnabled = false;
                createShortcutCheckbox.Visibility = Visibility.Hidden;
                var writable = util.TestWriteability(installPath);
                if (!writable)
                {
                    util.ElevateProcess(Process.GetCurrentProcess(), installPath, createShortcut);
                }
                string gameFolder = Path.Combine(installPath, "Game");
                if (Directory.Exists(gameFolder))
                {
                    if (!util.StripRights(gameFolder))
                    {
                        MessageBox.Show("Failed to strip rights for Game folder", "Installer error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                var updateResult = await UpdateFiles(installPath, (double progress) =>
                {
                    instProgress.Value = (int)(100.0 * progress);
                    return true;
                }, !installed && createShortcut);
                if (updateResult)
                {
                    needsUpdate = false;
                    installed = true;
                }
                mainButton.IsEnabled = true;
                createShortcutCheckbox.IsEnabled = true;
                UpdateUI();
            }
            else
            {
                Play();
            }
        }

        async Task<bool> UpdateOnlineMessage()
        {
            try
            {
                var online = await util.GET(ToEndpoint(knownMaster) + "/api/online?xml");
                onlineStatus = DeserializeXml<OnlineStatus>(online);
                var n = onlineStatus.online;
                if (n == 0) activeStatus.Content = "No players active right now";
                else if (n == 1) activeStatus.Content = "1 player active right now";
                else activeStatus.Content = n + " players active right now";
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        void LoadSettings()
        {
            string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sfwcl");
            string path = Path.Combine(parent, "launcher.xml");
            if (!Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }
            if (File.Exists(path))
            {
                try
                {
                    XmlSerializer ser = new XmlSerializer(settings.GetType());
                    using (FileStream fs = File.OpenRead(path))
                    {
                        settings = (Settings)ser.Deserialize(fs);
                    }
                } catch(Exception)
                {
                    SaveSettings();
                }
            } else
            {
                SaveSettings();
            }
        }

        void SaveSettings()
        {
            string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sfwcl");
            string path = Path.Combine(parent, "launcher.xml");
            if (!Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }
            XmlSerializer ser = new XmlSerializer(settings.GetType());
            using(FileStream fs = File.Create(path))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    ser.Serialize(sw, settings);
                }
            }
        }

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        void OnPromptFolder(object sender, RoutedEventArgs e)
        {
            PromptFolder();
        }

        async void OnLoginButton(object snder, RoutedEventArgs e)
        {
            bool result = await DoLogin();
        }

        async Task<bool> DoLogin()
        {
            try
            {
                bool result = await Login();
                if (!result)
                {
                    MessageBox.Show("Entered information is incorrect", "Log-in", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                } else
                {
                    return await FetchProfile();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Failed to connect server in order to verify log-in, try again later", "Internet error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        async Task<bool> FetchProfile()
        {
            if(settings.handle == null || settings.token == null)
            {
                loginScreen.Visibility = Visibility.Visible;
                accountScreen.Visibility = Visibility.Hidden;
                return false;
            }
            try
            {
                string master = "crymp.net";
                string nickname = settings.handle;
                if(nickname.IndexOf("@") != -1)
                {
                    string[] parts = nickname.Split('@');
                    if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 1)
                    {
                        master = parts[1];
                        nickname = parts[0];
                    }
                }
                string info = await util.GET(ToEndpoint(master) + "/api/profile?xml&a=" + HttpUtility.UrlEncode(nickname) + "&b=" + HttpUtility.UrlEncode(settings.token));
                if(info == "FAIL")
                {
                    loginScreen.Visibility = Visibility.Visible;
                    accountScreen.Visibility = Visibility.Hidden;
                    return false;
                }
                XmlSerializer ser = new XmlSerializer(accountInfo.GetType());
                accountInfo = (AccountInfo)ser.Deserialize(new MemoryStream(Encoding.UTF8.GetBytes(info)));
                accountKills.Content = accountInfo.kills.ToString();
                accountDeaths.Content = accountInfo.deaths.ToString();
                accountTime.Content = (accountInfo.playedTime / 3600) + " hours, " + (accountInfo.playedTime / 60 % 60) + " minutes";
                accountName.Content = accountInfo.display;
                loginScreen.Visibility = Visibility.Hidden;
                accountScreen.Visibility = Visibility.Visible;
                return true;
            } catch(Exception)
            {
                loginScreen.Visibility = Visibility.Visible;
                accountScreen.Visibility = Visibility.Hidden;
                return false;
            }
        }

        void OnLogoutButton(object sender, RoutedEventArgs args)
        {
            Logout();
        }

        void Logout()
        {
            settings.handle = null;
            settings.token = null;
            SaveSettings();
            loginScreen.Visibility = Visibility.Visible;
            accountScreen.Visibility = Visibility.Hidden;
        }

        void UpdateUI()
        {
            if (needsUpdate)
            {
                rightPanelInstall.Visibility = Visibility.Visible;
                rightPanelPlay.Visibility = Visibility.Hidden;
                mainButton.Content = installed ? "Update" : "Install";
                mainButton.IsEnabled = validInstallPath;
                if (installed)
                {
                    instProgress.Visibility = Visibility.Visible;
                    createShortcutCheckbox.Visibility = Visibility.Hidden;
                    instPath.Visibility = Visibility.Hidden;
                    instHelper1.Visibility = Visibility.Hidden;
                    instHelper2.Visibility = Visibility.Hidden;
                    instHelper3.Visibility = Visibility.Visible;
                    instHelper3.Content = "Your client is out of date, an update is required";
                } else
                {
                    createShortcutCheckbox.Visibility = Visibility.Visible;
                    instProgress.Visibility = Visibility.Hidden;
                    instPath.Visibility = Visibility.Visible;
                    mainButton.IsEnabled = validInstallPath;
                    instHelper1.Visibility = Visibility.Visible;
                    instHelper2.Visibility = Visibility.Visible;
                    instHelper3.Visibility = Visibility.Hidden;
                }
            }
            else
            {
                bool canPlay32 = Directory.Exists(Path.Combine(installPath, "Bin32"));
                bool canPlay64 = Directory.Exists(Path.Combine(installPath, "Bin64"));

                if (canPlay32 && !canPlay64)
                {
                    settings.architecture = "32";
                } else if(canPlay64 && !canPlay32)
                {
                    settings.architecture = "64";
                }


                string target = settings.architecture;
                if (!canPlay64) archOptions.Remove(archOptions[1]);
                if (!canPlay32) archOptions.Remove(archOptions[0]);

                architecture.SelectedValue = target;
                SaveSettings();

                mainButton.Content = "Play";
                rightPanelInstall.Visibility = Visibility.Hidden;
                rightPanelPlay.Visibility = Visibility.Visible;
            }
        }

        bool VerifyFolder(string folder)
        {
            try
            {
                if (File.Exists(folder + "\\Bin32\\Crysis.exe") || File.Exists(folder + "\\Bin64\\Crysis.exe"))
                {
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
        string PromptFolder()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                if (dialog.SelectedPath.Length == 0) return instPath.Text;
                instPath.Text = dialog.SelectedPath;
                string selPath = dialog.SelectedPath;
                validInstallPath = VerifyFolder(selPath);
                installPath = selPath;
                UpdateUI();
                return selPath;
            }
        }

        void Play()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            bool canPlay32 = Directory.Exists(Path.Combine(installPath, "Bin32"));
            bool canPlay64 = Directory.Exists(Path.Combine(installPath, "Bin64"));
            if (!canPlay32 && !canPlay64)
            {
                MessageBox.Show("Cannot find neither Bin32 nor Bin64 directories!", "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            bool play64 = settings.architecture == "64";
            if (play64 && !canPlay64)
            {
                MessageBox.Show("Cannot find 64-bit executables!", "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!play64 && !canPlay32)
            {
                MessageBox.Show("Cannot find 32-bit executables!", "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string targetArch = play64 ? "64" : "32";

            bool found = false;
            foreach(var file in updateFiles)
            {
                if(file.type == "exe" && file.arch == targetArch)
                {
                    startInfo.FileName = file.path;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                MessageBox.Show("Couldn't find client executable", "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string args = settings.renderer == "dx9" ? "-dx9" : "-dx10";
            if(settings.handle != null && settings.token != null)
            {
                args += " +secu_login " + settings.handle + " " + settings.token;
            }

            startInfo.Arguments = args;
            startInfo.WorkingDirectory = installPath;
            Process.Start(startInfo);
            Close();
        }

        bool CheckFiles(string path)
        {
            bool needsUpdate = false;
            for (int i = 0; i < updateFiles.Count; i++)
            {
                var file = updateFiles[i];
                var jointPath = Path.Combine(path, file.path.Replace('/', '\\'));
                if (File.Exists(jointPath))
                    file.miss = file.hash != util.FileSHA256(jointPath);
                else
                    file.miss = true;
                if (file.miss) needsUpdate = true;
            }
            return needsUpdate;
        }

        async Task<bool> UpdateFiles(string path, Func<double, bool> progress = null, bool createShortcut = false)
        {
            try
            {
                for (var i = 0; i < updateFiles.Count; i++)
                {
                    var file = updateFiles[i];
                    var jointPath = Path.Combine(path, file.path.Replace('/', '\\'));
                    if (!file.miss) continue;
                    var raw = await util.RawGET(file.url, (System.Net.DownloadProgressChangedEventArgs e) =>
                    {
                        if(progress != null)
                        {
                            double part = 1.0 / (double)updateFiles.Count;
                            double partialProgress = (double)e.BytesReceived / (double)e.TotalBytesToReceive;
                            double totalProgress = (double)i / (double)updateFiles.Count + part * partialProgress;
                            progress(totalProgress);
                        }
                        return true;
                    });
                    using (BinaryWriter binWriter = new BinaryWriter(File.Open(jointPath, FileMode.Create)))
                    {
                        binWriter.Write(raw);
                    }
                }
                var itself = Process.GetCurrentProcess().MainModule.FileName;
                var that = Path.Combine(path, "CryMP-Launcher.exe");
                if (itself != that)
                {
                    File.Copy(itself, Path.Combine(path, "CryMP-Launcher.exe"), true);
                }
                if (createShortcut)
                {
                    if (File.Exists(that))
                    {
                        try
                        {
                            IWshRuntimeLibrary.WshShell shell = new IWshRuntimeLibrary.WshShell();
                            object shDesktop = (object)"Desktop";
                            string shortcutAddress = (string)shell.SpecialFolders.Item(ref shDesktop) + @"\Crysis Multiplayer.lnk";
                            IWshRuntimeLibrary.IWshShortcut shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(shortcutAddress);
                            shortcut.Description = "Crysis multiplayer launcher";
                            shortcut.TargetPath = that;
                            shortcut.WorkingDirectory = Path.GetDirectoryName(that);
                            shortcut.Save();
                        } catch(Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    }
                }
                return true;
            } catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
        }

        async Task<bool> Login()
        {
            string master = "crymp.net";
            string nickname = loginEmail.Text;
            if(loginEmail.Text.IndexOf("@") != -1)
            {
                string[] parts = loginEmail.Text.Split('@');
                if(parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 1)
                {
                    master = parts[1];
                    nickname = parts[0];
                }
            }
            string response = await util.GET(ToEndpoint(master) + "/api/login?export=xml&hash=1&a=" + System.Web.HttpUtility.UrlEncode(nickname) + "&b=" + System.Web.HttpUtility.UrlEncode(loginPassword.Password));
            try
            {
                var login = DeserializeXml<AuthResponse>(response);
                settings.handle = nickname;
                if (master != "crymp.net") settings.handle += "@" + master;
                settings.token = login.token;
                SaveSettings();
                return settings.handle != null;
            } catch(Exception)
            {
                settings.handle = null;
                settings.token = null;
                SaveSettings();
                return false;
            }
        }
    }
}
