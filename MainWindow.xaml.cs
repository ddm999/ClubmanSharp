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

        public SemanticVersion currentVersion = new(1, 1, 1);

        public MainWindow()
        {
            InitializeComponent();

            TxtHeader.Text = $"ClubmanSharp by ddm [v{currentVersion}]";
            ip = settings.ip;
            TxtIP.Text = ip;

            bot = new Bot();

            CustomDelayShort.Text = $"{settings.customShortDelay}";
            CustomDelayLong.Text = $"{settings.customLongDelay}";

            SliderThrottle.Value = Convert.ToDouble(settings.maxThrottle);

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

            switch (settings.confirmButton)
            {
                case 0:
                    RadioConfirmCross.IsChecked = true;
                    break;
                case 1:
                    RadioConfirmCircle.IsChecked = true;
                    break;
            }

            // WRX data wasn't actually required after all
            RadioCarGTO.IsChecked = true;

            CompositionTarget.Rendering += VisualLoop;

            TxtDetails.Text = "WARNING: The latest version of PS Remote Play does not currently work with virtual controllers!\n" +
                              "If you do not already use a patched version of PS Remote Play, *CLOSE REMOTE PLAY* and then click the button below " +
                              "to download & install a patched older version which will work with the bot.\n";

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
        }

        public void TooMuchStuckDetectionCheck()
        {
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
                MessageBox.Show(msg, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public async void UpdateCheck()
        {
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
                                    "(A link is available at the bottom of the Startup menu.)", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to check for updates.\nException details below:\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void VisualLoop(object? sender, EventArgs? e)
        {
            if (bot.error is true)
            {
                MessageBox.Show(bot.errorMsg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                bot.error = false;
                bot.Stop();
                isStarted = false;
                TooMuchStuckDetectionCheck();
                BtnStartStop.Content = "Start";
                TxtIP.IsEnabled = true;
                BtnStartStop.IsEnabled = true;
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
                    MessageBox.Show(bot.errorMsg, "Error on starting bot", MessageBoxButton.OK, MessageBoxImage.Error);
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
        }

        private void Hyperlink_Click(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void RadioDelayPS4_Checked(object sender, RoutedEventArgs e)
        {
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
        }

        private void RadioDelayPS5_Checked(object sender, RoutedEventArgs e)
        {
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
        }

        private void RadioDelayCustom_Checked(object sender, RoutedEventArgs e)
        {
            bot.ShortDelay = int.Parse(CustomDelayShort.Text);
            bot.LongDelay = int.Parse(CustomDelayLong.Text);
            CustomDelayShort.IsEnabled = true;
            CustomDelayLong.IsEnabled = true;
            RadioDelayPS4.IsChecked = false;
            RadioDelayPS5.IsChecked = false;
            settings.delaySetting = 2;
            settings.Save();
        }

        private void TxtIP_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtIP.Text == "x")
                return;

            ip = TxtIP.Text;
            settings.ip = ip;
            settings.Save();
        }

        private void CustomDelayShort_TextChanged(object sender, TextChangedEventArgs e)
        {
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
                MessageBox.Show($"Invalid delay of {CustomDelayShort.Text}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CustomDelayShort.Text = $"{bot.ShortDelay}";
            }
        }

        private void CustomDelayLong_TextChanged(object sender, TextChangedEventArgs e)
        {
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
                MessageBox.Show($"Invalid delay of {CustomDelayLong.Text}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CustomDelayLong.Text = $"{bot.LongDelay}";
            }
        }

        private void RadioCarGTO_Checked(object sender, RoutedEventArgs e)
        {
            if (bot is null)
                return;
            bot.currentTrackData = new GTOTrackData();

            RadioCarWRX.IsChecked = false;
            settings.carSetting = 0;
            settings.Save();
        }

        private void RadioCarWRX_Checked(object sender, RoutedEventArgs e)
        {
            /*if (bot is null)
                return;
            bot.currentTrackData = new WRXTrackData();

            RadioCarGTO.IsChecked = false;
            settings.carSetting = 1;
            settings.Save();*/
        }

        private void SliderThrottle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            byte byteVal = Convert.ToByte(SliderThrottle.Value);
            SliderThrottle.ToolTip = $"{byteVal}/255";
            TxtThrottleValue.Text = $"{byteVal}";

            if (bot is null)
                return;

            bot.LateRaceMaxThrottle = byteVal;
            settings.maxThrottle = byteVal;
            settings.Save();
        }

        private void RadioConfirmCross_Checked(object sender, RoutedEventArgs e)
        {
            if (bot is null)
                return;
            bot.confirmButton = DualShock4Button.Cross;
            bot.cancelButton = DualShock4Button.Circle;

            RadioConfirmCircle.IsChecked = false;
            settings.confirmButton = 0;
            settings.Save();
        }

        private void RadioConfirmCircle_Checked(object sender, RoutedEventArgs e)
        {
            if (bot is null)
                return;
            bot.confirmButton = DualShock4Button.Circle;
            bot.cancelButton = DualShock4Button.Cross;

            RadioConfirmCross.IsChecked = false;
            settings.confirmButton = 1;
            settings.Save();
        }

        /*
         * The following function is from https://github.com/xeropresence/remoteplay-version-patcher
        */
        private static string? FindRemotePlay()
        {
            var baseKey = Environment.Is64BitOperatingSystem ?
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" :
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";

            using (var keys = Registry.LocalMachine.OpenSubKey(baseKey))
            {
                var remotePlayKey = keys?.GetSubKeyNames()
                    .Select(name => keys.OpenSubKey(name))
                    .FirstOrDefault(key => key.GetValue("DisplayName", "").ToString().Contains("PS Remote Play") &&
                                           key.GetValue("Publisher", "").ToString().Contains("Sony"));
                var path = Path.Combine(remotePlayKey?.GetValue("InstallLocation", null)?.ToString() ?? string.Empty, "RemotePlay.exe");
                return File.Exists(path) ? path : null;
            }
        }

        public async Task DownloadPatchedRemotePlay(string path)
        {
            using var client = new HttpClient();
            using var stream = await client.GetStreamAsync("http://gt-mod.site/RemotePlay-550-patched.exe");
            using var fileStream = new FileStream(path, FileMode.Create);
            stream.CopyTo(fileStream);
        }

        private async void BtnPatchedRemotePlay_Click(object sender, RoutedEventArgs e)
        {
            string? outpath = FindRemotePlay();
            string dlpath = Path.Combine(Directory.GetCurrentDirectory(), "RemotePlay.exe");

            await DownloadPatchedRemotePlay(dlpath);

            if (File.Exists(dlpath) && File.Exists(outpath))
            {
                MessageBox.Show($"ClubmanSharp will now install the downloaded Remote Play version.\nTo do this you need to accept the 'Windows Command Processor' UAC prompt that will appear after this box.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);

                Process process = new();
                ProcessStartInfo startInfo = new();
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/C move \"{dlpath}\" \"{outpath}\"";
                startInfo.Verb = "runas";
                startInfo.UseShellExecute = true;
                process.StartInfo = startInfo;
                process.Start();

                MessageBox.Show($"This should have successfully installed patched Remote Play.\nLaunch Remote Play as normal and see if it works.\n\nIf it doesn't, try this process again, making sure that Remote Play is fully closed.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            MessageBox.Show("Failed to find current Remote Play installation:\nthe patched RemotePlay.exe has been left next to ClubmanSharp.exe for manual installation.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
    }
}
