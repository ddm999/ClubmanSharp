using ClubmanSharp.TrackData;
using Microsoft.Win32;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using NuGet.Versioning;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Threading;

namespace ClubmanSharp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isStarted = false;
        private string ip = "192.168.1.30";
        private readonly Bot bot;
        private readonly Settings settings = Settings.Default;
        private DateTime nextUpdate = DateTime.UtcNow;
        private string specialDebugTxt = "";
        private bool autoRetry = false;
        private uint autoRetryCount = 0;

        public SemanticVersion currentVersion = new(1, 2, 0);

        public MainWindow()
        {
            DebugLog.Log($"Starting MainWindow initialization");
            InitializeComponent();

            CheckDebugLog.IsChecked = settings.debugLog > 0;
            DebugLog.Log("Loaded MainWindow");

            TxtHeader.Text = $"ClubmanSharp by ddm [v{currentVersion}]";
            DebugLog.Log($"Version is {currentVersion}");

            ip = settings.ip;
            TxtIP.Text = ip;
            DebugLog.Log($"Loaded IP from settings as {settings.ip}");

            bot = new Bot();
            DebugLog.Log($"Initialized Bot");

            CustomDelayShort.Text = $"{settings.customShortDelay}";
            DebugLog.Log($"Loaded customShortDelay from settings as {settings.customShortDelay}");
            CustomDelayLong.Text = $"{settings.customLongDelay}";
            DebugLog.Log($"Loaded customLongDelay from settings as {settings.customLongDelay}");

            SliderThrottle.Value = Convert.ToDouble(settings.maxThrottle);
            DebugLog.Log($"Loaded maxThrottle from settings as {settings.maxThrottle}");

            switch (settings.delaySetting)
            {
                case 0:
                    RadioDelayPS4.IsChecked = true;
                    break;
                case 1:
                    RadioDelayPS5.IsChecked = true;
                    break;
                case 2:
                    RadioDelayCustom.IsChecked = true;
                    break;
            }
            DebugLog.Log($"Loaded delaySetting from settings as {settings.delaySetting}");

            switch (settings.confirmButton)
            {
                case 0:
                    RadioConfirmCross.IsChecked = true;
                    autoRetry = false;
                    break;
                case 1:
                    RadioConfirmCircle.IsChecked = true;
                    autoRetry = true;
                    break;
            }
            DebugLog.Log($"Loaded confirmButton from settings as {settings.confirmButton}");

            switch (settings.autoRetry)
            {
                case 0:
                    RadioAutoRetryOff.IsChecked = true;
                    break;
                case 1:
                    RadioAutoRetryOn.IsChecked = true;
                    break;
            }
            DebugLog.Log($"Loaded autoRetry from settings as {settings.autoRetry}");

            // WRX data wasn't actually required after all
            RadioCarGTO.IsChecked = true;
            DebugLog.Log($"Set car to GTO (unused)");

            CompositionTarget.Rendering += VisualLoop;
            DebugLog.Log($"Added VisualLoop");

            TxtDetails.Text = "WARNING: The latest version of PS Remote Play does not currently work with virtual controllers!\n" +
                              "If you do not already use a patched version of PS Remote Play, *CLOSE REMOTE PLAY* and then click the button below " +
                              "to get a version which will work with the bot.\n";

            TxtShortHelp.Text = "Turn on a password requirement for PlayStation purchases before using any script.\n" +
                                "It is recommended to use HidHide to prevent the bot interacting with your desktop.\n\n" +
                                "You must start Remote Play with no controller connected to your PC.\n\n" +
                                "Enter your PS4/PS5's local IP address and hit Start while on the Tokyo Clubman+ pre-race menu.";

            TxtLicensing.Text = "Developed with tunes and tips provided by Photon-Phoenix\n" +
                                "Using the GT7 SimInterface found & documented by Nenkai\n" +
                                "Special thanks to the PSNProfiles GT7 and GT Modding Community discord servers\n\n" +
                                "This project is licensed under the European Union Public License 1.2 (EUPL-1.2).\n" +
                                "This is a copyleft free/open-source software license. (This is not legal advice.)\n" +
                                "Full terms can be found at:\n https://github.com/ddm999/ClubmanSharp/blob/main/LICENSE\n\n" +
                                "This project uses https://github.com/Nenkai/PDTools, licensed under the MIT license.\n" +
                                "Full terms can be found at:\n https://github.com/Nenkai/PDTools/blob/master/LICENSE\n\n" +
                                "This project uses https://github.com/ViGEm/ViGEm.NET, licensed under the MIT license.\n" +
                                "Full terms can be found at:\n https://github.com/ViGEm/ViGEm.NET/blob/master/LICENSE\n\n" +
                                "All developers of this project are not affiliated with Polyphony Digital or Sony Interactive Entertainment.";

            UpdateCheck();
            if (DebugLog.isActive)
            {
                MessageBox.Show("You have the debug.log setting enabled!\n" +
                                "This option WILL make a huge log file if you leave it on all the time & don't delete the log every now and again.\n" +
                                "Turn it off unless you really need someone to verify what's happening with the bot!",
                                "ClubmanSharp Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                DebugLog.Log("Warning user of debug log setting");
            }

            DebugLog.Log($"Finished MainWindow initialization");
        }

        public void TooMuchStuckDetectionCheck()
        {
            DebugLog.Log($"Starting TooMuchStuckDetectionCheck");
            if (bot is null)
                return;

            if (bot.completedRaces >= 2 && (bot.stuckDetectionRuns >= (bot.completedRaces*0.05)))
            {
                string msg = "That last run had a lot of stuck detection attempts, which usually means your delays are too short.\n\n";
                if (RadioDelayCustom.IsChecked is true)
                    msg += "Try switching back to the defaults, or increasing your custom delays.";
                else if (RadioDelayPS5.IsChecked is true)
                    msg += "Try using the PS4 delays.";
                else
                    msg += "Try using large custom delays, or increasing your network stability if possible.";
                MessageBox.Show(msg, "ClubmanSharp Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                DebugLog.Log($"Warning: {msg}");
            }
            DebugLog.Log($"Finished TooMuchStuckDetectionCheck");
        }

        public async void UpdateCheck()
        {
            DebugLog.Log($"Started UpdateCheck");
            try
            {
                using var client = new HttpClient();
                string serverResult = await client.GetStringAsync("http://gt-mod.site/ClubmanSharpVersion.txt");

                SemanticVersionConverter converter = new();
                SemanticVersion serverVersion = (SemanticVersion)converter.ConvertFromString(serverResult);
                if (serverVersion > currentVersion)
                {
                    TxtSrcLink.Visibility = Visibility.Hidden;
                    TxtUpdateLink.Visibility = Visibility.Visible;
                    MessageBox.Show("A new version of ClubmanSharp is available to download.\n" +
                                    "You may have issues using this out-of-date version.\n" +
                                    "Download the latest version at https://github.com/ddm999/ClubmanSharp/releases\n" +
                                    "(A link is available at the bottom of the Startup menu.)", "ClubmanSharp Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    DebugLog.Log($"Warning: serverVersion > currentVersion");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to check for updates.\nException details below:\n\n{ex.Message}", "ClubmanSharp Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DebugLog.Log($"Error: UpdateCheck failed.\n{ex.Message}");
            }
            DebugLog.Log($"Finished UpdateCheck");
        }

        public void VisualLoop(object? sender, EventArgs? e)
        {
            if (bot.error is true)
            {
                DebugLog.Log($"Error: bot.error\n{bot.errorMsg}");
                bool retriableError = bot.errorMsg.Contains("packet") || bot.errorMsg.Contains("Unexpected error");
                if (!(autoRetry && retriableError))
                {
                    MessageBox.Show(bot.errorMsg, "ClubmanSharp Error (in bot)", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                bot.error = false;
                bot.Stop();
                isStarted = false;
                TooMuchStuckDetectionCheck();
                BtnStartStop.Content = "Start";
                TxtIP.IsEnabled = true;
                BtnStartStop.IsEnabled = true;

                if (autoRetry && retriableError)
                {
                    DebugLog.Log($"AutoRetry: Error is retriable");
                    Thread.Sleep(3000);
                    autoRetryCount++;
                    StartStop_Click(null, null);
                    DebugLog.Log($"AutoRetry: Clicked the Start button");
                }
            }

            // don't update text constantly because it actually uses an unnecessary chunk of GPU lol
            if (nextUpdate > DateTime.UtcNow)
            {
                return;
            }
            nextUpdate = DateTime.UtcNow + new TimeSpan(1000000);

            TxtState.Text = "Current State: ";
            if (bot.currentMenuState == Bot.MenuState.Unknown)
                TxtState.Text += "Unknown";
            else if (bot.currentMenuState == Bot.MenuState.Race)
                TxtState.Text += "Race";
            else if (bot.currentMenuState == Bot.MenuState.RacePaused)
                TxtState.Text += "Race Paused";
            else if (bot.currentMenuState == Bot.MenuState.RaceStart)
                TxtState.Text += "Race Start";
            else if (bot.currentMenuState == Bot.MenuState.PreRace)
                TxtState.Text += "Pre Race";
            else if (bot.currentMenuState == Bot.MenuState.RaceResult)
                TxtState.Text += "Race Result";
            else if (bot.currentMenuState == Bot.MenuState.Replay)
                TxtState.Text += "Replay";
            else if (bot.currentMenuState == Bot.MenuState.PostRace)
                TxtState.Text += "Post Race";
            else if (bot.currentMenuState == Bot.MenuState.Stuck_PreOrPostRace)
                TxtState.Text += "Stk Unknown";
            else if (bot.currentMenuState == Bot.MenuState.Stuck_Replay)
                TxtState.Text += "Stk Replay";
            else if (bot.currentMenuState == Bot.MenuState.Stuck_PostRace)
                TxtState.Text += "Stk Post Race";
            else if (bot.currentMenuState == Bot.MenuState.Stuck_PreRace)
                TxtState.Text += "Stk Pre Race";

            TxtLap.Text = $"Fastest Lap: {bot.fastestLap.Minutes:d1}:{bot.fastestLap.Seconds:d2}.{bot.fastestLap.Milliseconds:d3}";
            TxtRaces.Text = $"Completed Races: {bot.completedRaces}";
            TxtCredits.Text = $"Estimated Credits: {bot.completedRaces * 105000 * 0.98:n0}";

            if (tabControl.SelectedItem == debugTabItem)
            {
                TxtDebug.Text = "Bot information:\n";
                TxtDebug.Text += $"error: {bot.error}    ";
                TxtDebug.Text += $"connected: {bot.connected}    ";
                TxtDebug.Text += $"autoRetries: {autoRetryCount}    ";
                TxtDebug.Text += $"stuckDetectRuns: {bot.stuckDetectionRuns}\n";
                TxtDebug.Text += $"Controller: {bot.buttonString}\n";
                if (bot.currentPacket != null)
                {
                    TxtDebug.Text += "\nPacket information:\n";
                    TxtDebug.Text += $"DateReceived: {bot.currentPacket.DateReceived}\n";
                    TxtDebug.Text += $"CarCode: {bot.currentPacket.CarCode}\n";
                    TxtDebug.Text += $"NumCarsAtPreRace: {bot.currentPacket.NumCarsAtPreRace}\n";
                    TxtDebug.Text += $"CurrentGear: {bot.currentPacket.CurrentGear}    ";
                    TxtDebug.Text += $"EngineRPM: {bot.currentPacket.EngineRPM}    ";
                    TxtDebug.Text += $"MetersPerSecond: {bot.currentPacket.MetersPerSecond}\n";
                    TxtDebug.Text += $"LapCount: {bot.currentPacket.LapCount}    ";
                    TxtDebug.Text += $"LapsInRace: {bot.currentPacket.LapsInRace}\n";
                    TxtDebug.Text += $"BestLapTime: {bot.currentPacket.BestLapTime}    ";
                    TxtDebug.Text += $"LastLapTime: {bot.currentPacket.LastLapTime}\n";
                    TxtDebug.Text += $"RelativeOrientationToNorth: {bot.currentPacket.RelativeOrientationToNorth}\n";
                    TxtDebug.Text += $"Position: {bot.currentPacket.Position}\n";
                    TxtDebug.Text += $"Rotation: {bot.currentPacket.Rotation}\n";
                    TxtDebug.Text += $"Velocity: {bot.currentPacket.Velocity}\n";
                }
                if (bot.currentTrackData != null)
                {
                    TxtDebug.Text += "\nTrack information:\n";
                    TxtDebug.Text += $"useInitialSegments: {bot.currentTrackData.useInitialSegments}    ";
                    TxtDebug.Text += $"segmentNum: {bot.currentTrackData.segmentNum}    ";
                    TxtDebug.Text += $"pitboxCounter: {bot.currentTrackData.pitboxCounter}\n";
                }
                TxtDebug.Text += specialDebugTxt;
            }
        }

        private void StartStop_Click(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started StartStop_Click");
            TxtIP.IsEnabled = false;
            BtnStartStop.IsEnabled = false;

            ip = TxtIP.Text;
            isStarted = !isStarted;

            if (isStarted)
            {
                BtnStartStop.Content = "Starting...";
                bot.Start(ip);
                if (bot.error is true)
                {
                    bot.Stop();
                    MessageBox.Show(bot.errorMsg, "ClubmanSharp Error (starting bot)", MessageBoxButton.OK, MessageBoxImage.Error);
                    DebugLog.Log($"Error: bot.error\n{bot.errorMsg}");
                    bot.error = false;
                    BtnStartStop.Content = "Start";
                    TxtIP.IsEnabled = true;
                    BtnStartStop.IsEnabled = true;
                    return;
                }
                BtnStartStop.Content = "Stop";
                BtnStartStop.IsEnabled = true;
            }
            else
            {
                bot.Stop();
                TooMuchStuckDetectionCheck();
                BtnStartStop.Content = "Start";
                TxtIP.IsEnabled = true;
                BtnStartStop.IsEnabled = true;
            }
            DebugLog.Log($"Finished StartStop_Click");
        }

        private void Hyperlink_Click(object sender, RequestNavigateEventArgs e)
        {
            DebugLog.Log($"Started Hyperlink_Click");
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
            DebugLog.Log($"Finished Hyperlink_Click");
        }

        private void RadioDelayPS4_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioDelayPS4_Checked");
            if (bot is null)
                return;
            CustomDelayShort.Text = "250";
            CustomDelayLong.Text = "3000";
            bot.ShortDelay = 250;
            bot.LongDelay = 3000;
            CustomDelayShort.IsEnabled = false;
            CustomDelayLong.IsEnabled = false;
            RadioDelayPS5.IsChecked = false;
            RadioDelayCustom.IsChecked = false;
            settings.delaySetting = 0;
            settings.Save();
            DebugLog.Log($"Finished RadioDelayPS4_Checked");
        }

        private void RadioDelayPS5_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioDelayPS5_Checked");
            CustomDelayShort.Text = "250";
            CustomDelayLong.Text = "1000";
            bot.ShortDelay = 250;
            bot.LongDelay = 1000;
            CustomDelayShort.IsEnabled = false;
            CustomDelayLong.IsEnabled = false;
            RadioDelayPS4.IsChecked = false;
            RadioDelayCustom.IsChecked = false;
            settings.delaySetting = 1;
            settings.Save();
            DebugLog.Log($"Finished RadioDelayPS5_Checked");
        }

        private void RadioDelayCustom_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioDelayCustom_Checked");
            bot.ShortDelay = int.Parse(CustomDelayShort.Text);
            bot.LongDelay = int.Parse(CustomDelayLong.Text);
            CustomDelayShort.IsEnabled = true;
            CustomDelayLong.IsEnabled = true;
            RadioDelayPS4.IsChecked = false;
            RadioDelayPS5.IsChecked = false;
            settings.delaySetting = 2;
            settings.Save();
            DebugLog.Log($"Finished RadioDelayCustom_Checked");
        }

        private void TxtIP_TextChanged(object sender, TextChangedEventArgs e)
        {
            DebugLog.Log($"Started TxtIP_TextChanged");
            DebugLog.Log($"TxtIP is {TxtIP.Text}");
            if (TxtIP.Text == "x")
                return;

            ip = TxtIP.Text;
            settings.ip = ip;
            settings.Save();
            DebugLog.Log($"Finished TxtIP_TextChanged");
        }

        private void CustomDelayShort_TextChanged(object sender, TextChangedEventArgs e)
        {
            DebugLog.Log($"Started CustomDelayShort_TextChanged");
            DebugLog.Log($"CustomDelayShort is {CustomDelayShort.Text}");
            if (bot is null)
                return;
            bool success = int.TryParse(CustomDelayShort.Text, out int number);
            if (success)
            {
                bot.ShortDelay = number;
                settings.customShortDelay = number;
                settings.Save();
            }
            else
            {
                DebugLog.Log($"Error: CustomDelayShort invalid");
                MessageBox.Show($"Invalid delay of {CustomDelayShort.Text}", "ClubmanSharp Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CustomDelayShort.Text = $"{bot.ShortDelay}";
            }
            DebugLog.Log($"Finished CustomDelayShort_TextChanged");
        }

        private void CustomDelayLong_TextChanged(object sender, TextChangedEventArgs e)
        {
            DebugLog.Log($"Started CustomDelayLong_TextChanged");
            DebugLog.Log($"CustomDelayLong is {CustomDelayLong.Text}");
            if (bot is null)
                return;
            bool success = int.TryParse(CustomDelayLong.Text, out int number);
            if (success)
            {
                bot.LongDelay = number;
                settings.customLongDelay = number;
                settings.Save();
            }
            else
            {
                DebugLog.Log($"Error: CustomDelayShort invalid");
                MessageBox.Show($"Invalid delay of {CustomDelayLong.Text}", "ClubmanSharp Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CustomDelayLong.Text = $"{bot.LongDelay}";
            }
            DebugLog.Log($"Finished CustomDelayLong_TextChanged");
        }

        private void RadioCarGTO_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioCarGTO_Checked");
            if (bot is null)
                return;
            bot.currentTrackData = new GTOTrackData();

            RadioCarWRX.IsChecked = false;
            settings.carSetting = 0;
            settings.Save();
            DebugLog.Log($"Finished RadioCarGTO_Checked");
        }

        private void RadioCarWRX_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioCarWRX_Checked");
            /*if (bot is null)
                return;
            bot.currentTrackData = new WRXTrackData();

            RadioCarGTO.IsChecked = false;
            settings.carSetting = 1;
            settings.Save();*/
            DebugLog.Log($"Finished RadioCarWRX_Checked");
        }

        private void SliderThrottle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            DebugLog.Log($"Started SliderThrottle_ValueChanged");
            DebugLog.Log($"SliderThrottle is {SliderThrottle.Value}");
            byte byteVal = Convert.ToByte(SliderThrottle.Value);
            SliderThrottle.ToolTip = $"{byteVal}/255";
            TxtThrottleValue.Text = $"{byteVal}";

            if (bot is null)
                return;

            bot.LateRaceMaxThrottle = byteVal;
            settings.maxThrottle = byteVal;
            settings.Save();
            DebugLog.Log($"Finished SliderThrottle_ValueChanged");
        }

        private void RadioConfirmCross_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioConfirmCross_Checked");
            if (bot is null)
                return;
            bot.confirmButton = DualShock4Button.Cross;
            bot.cancelButton = DualShock4Button.Circle;

            RadioConfirmCircle.IsChecked = false;
            settings.confirmButton = 0;
            settings.Save();
            DebugLog.Log($"Finished RadioConfirmCross_Checked");
        }

        private void RadioConfirmCircle_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioConfirmCircle_Checked");
            if (bot is null)
                return;
            bot.confirmButton = DualShock4Button.Circle;
            bot.cancelButton = DualShock4Button.Cross;

            RadioConfirmCross.IsChecked = false;
            settings.confirmButton = 1;
            settings.Save();
            DebugLog.Log($"Finished RadioConfirmCircle_Checked");
        }

        private void RadioAutoRetryOff_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioAutoRetryOff_Checked");
            RadioAutoRetryOn.IsChecked = false;
            autoRetry = false;
            settings.autoRetry = 0;
            settings.Save();
            DebugLog.Log($"Finished RadioAutoRetryOff_Checked");
        }

        private void RadioAutoRetryOn_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started RadioAutoRetryOn_Checked");
            RadioAutoRetryOff.IsChecked = false;
            autoRetry = true;
            settings.autoRetry = 1;
            settings.Save();
            DebugLog.Log($"Finished RadioAutoRetryOn_Checked");
        }

        private void CheckDebugLog_Checked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started CheckDebugLog_Checked");
            DebugLog.SetActive(true);
            settings.debugLog = 1;
            settings.Save();
            DebugLog.Log($"Finished CheckDebugLog_Checked");
        }

        private void CheckDebugLog_Unchecked(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started CheckDebugLog_Unchecked");
            DebugLog.SetActive(false);
            settings.debugLog = 0;
            settings.Save();
            DebugLog.Log($"Finished CheckDebugLog_Unchecked");
        }

        /*
         * The following function is from https://github.com/xeropresence/remoteplay-version-patcher
        */
        private static string? FindRemotePlay()
        {
            DebugLog.Log($"Started FindRemotePlay (nofin)");
            DebugLog.Log($"Environment.Is64BitOperatingSystem {Environment.Is64BitOperatingSystem}");
            var baseKey = Environment.Is64BitOperatingSystem ?
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" :
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";

            using (var keys = Registry.LocalMachine.OpenSubKey(baseKey))
            {
                var remotePlayKey = keys?.GetSubKeyNames()
                    .Select(name => keys.OpenSubKey(name))
                    .FirstOrDefault(key => key.GetValue("DisplayName", "").ToString().Contains("PS Remote Play") &&
                                           key.GetValue("Publisher", "").ToString().Contains("Sony"));
                DebugLog.Log($"remotePlayKey {remotePlayKey}");
                var path = remotePlayKey?.GetValue("InstallLocation", null)?.ToString() ?? string.Empty;
                DebugLog.Log($"path {path}");
                return Directory.Exists(path) ? path : null;
            }
        }

        public async Task DownloadPatchedRemotePlay(string path)
        {
            DebugLog.Log($"Started DownloadPatchedRemotePlay");
            using var client = new HttpClient();
            using var stream = await client.GetStreamAsync("http://gt-mod.site/PS_Remote_Play_v550_patch.zip");
            using var fileStream = new FileStream(path, FileMode.Create);
            stream.CopyTo(fileStream);
            fileStream.Close();
            DebugLog.Log($"Finished DownloadPatchedRemotePlay");
        }

        private async void BtnPatchedRemotePlay_Click(object sender, RoutedEventArgs e)
        {
            DebugLog.Log($"Started BtnPatchedRemotePlay_Click");
            string? outpath = FindRemotePlay();

            DebugLog.Log($"Directory.GetCurrentDirectory {Directory.GetCurrentDirectory()}");
            string dlpath = Path.Combine(Directory.GetCurrentDirectory(), "PS_Remote_Play_v550_patch.zip");
            string extractpath = Path.Combine(Directory.GetCurrentDirectory(), "PS_Remote_Play_v550_patch");

            // delete if exists, fine if this fails
            DebugLog.Log($"Deleting existing download files");
            try { File.Delete(dlpath); } catch (Exception) { DebugLog.Log($"Failed to delete download path (OK!)"); }
            try { Directory.Delete(extractpath, true); } catch (Exception) { DebugLog.Log($"Failed to delete extract path (OK!)"); }

            await DownloadPatchedRemotePlay(dlpath);
            DebugLog.Log($"Extracting downloaded zip");
            ZipFile.ExtractToDirectory(dlpath, extractpath);

            if (
                outpath != null &&
                File.Exists(Path.Combine(extractpath, "RemotePlay.exe")) &&
                File.Exists(Path.Combine(outpath, "RemotePlay.exe")) &&
                File.Exists(Path.Combine(extractpath, "RpCtrlWrapper.dll")) &&
                File.Exists(Path.Combine(outpath, "RpCtrlWrapper.dll"))
            )
            {
                DebugLog.Log($"Info: Patch install");
                MessageBox.Show("ClubmanSharp will now install the patch for Remote Play.\n"+
                                "You may need to accept a 'Windows Command Processor' UAC prompt that will appear after this box.",
                                "ClubmanSharp Information", MessageBoxButton.OK, MessageBoxImage.Information);

                DebugLog.Log($"Initializing RemotePlay.exe install process");
                Process process = new();
                ProcessStartInfo startInfo = new()
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "cmd.exe",
                    Arguments = $"/C move \"{Path.Combine(extractpath, "RemotePlay.exe")}\" \"{outpath}\"",
                    Verb = "runas",
                    UseShellExecute = true
                };
                process.StartInfo = startInfo;
                DebugLog.Log($"Starting RemotePlay.exe install process");
                process.Start();

                DebugLog.Log($"Initializing RpCtrlWrapper.dll install process");
                Process process2 = new();
                ProcessStartInfo startInfo2 = new()
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "cmd.exe",
                    Arguments = $"/C move \"{Path.Combine(extractpath, "RpCtrlWrapper.dll")}\" \"{outpath}\"",
                    Verb = "runas",
                    UseShellExecute = true
                };
                process2.StartInfo = startInfo2;
                DebugLog.Log($"Starting RpCtrlWrapper.dll install process");
                process2.Start();

                DebugLog.Log($"Deleting download & extract files");
                try
                {
                    File.Delete(dlpath);
                    Directory.Delete(extractpath);
                }
                // ignore delete fails, it probably failed for a reason and might help the user if the files are left
                catch (Exception)
                {
                    DebugLog.Log($"Failed to delete download & extract files (OK!)");
                }

                MessageBox.Show("This should have successfully patched Remote Play.\n"+
                                "Launch Remote Play as normal and see if it works.\n\n"+
                                "If it doesn't, try this process again, making sure that Remote Play is fully closed.\n"+
                                "If you can't get it working, you can request help in the GitHub issues.",
                                "ClubmanSharp Information", MessageBoxButton.OK, MessageBoxImage.Information);
                DebugLog.Log($"Info: Patch probably succeeded");
            }
            else
            {
                MessageBox.Show("Failed to find current Remote Play installation:\n"+
                                "you can manually patch using the downloaded files left next to ClubmanSharp.",
                                "ClubmanSharp Information", MessageBoxButton.OK, MessageBoxImage.Information);
                DebugLog.Log($"Info: Locating Remote Play install failed");
            }
            DebugLog.Log($"Finished BtnPatchedRemotePlay_Click");
        }
    }
}
