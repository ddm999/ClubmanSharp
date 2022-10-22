using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

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

        public MainWindow()
        {
            InitializeComponent();

            ip = settings.ip;
            TxtIP.Text = ip;

            bot = new Bot();

            CustomDelayShort.Text = $"{settings.customShortDelay}";
            CustomDelayLong.Text = $"{settings.customLongDelay}";

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

            CompositionTarget.Rendering += VisualLoop;

            TxtDetails.Text = "Developed with tunes and tips provided by Photon-Phoenix\n\n" +
                              "Using the GT7 SimInterface found & documented by Nenkai\n\n" +
                              "Based on Clubman+ by igoticecream\n\n" +
                              "Special thanks to the PSNProfiles GT7 and GT Modding Community discord servers\n\n";

            TxtShortHelp.Text = "Turn on a password requirement for PlayStation purchases before using any script.\n" +
                                "It is recommended to use HidHide to prevent the bot interacting with your desktop.\n\n" +
                                "You must start Remote Play with no controller connected to your PC. It cannot be minimised, but you can move other programs above it.\n\n" +
                                "Enter your PS4/PS5's local IP address and hit Start while on the Tokyo Clubman+ pre-race menu.";

            TxtLicensing.Text = "This project is licensed under the European Union Public License 1.2 (EUPL-1.2).\n" +
                                "This is a copyleft free/open-source software license. (This is not legal advice.)\n" +
                                "Full terms can be found at:\n https://github.com/ddm999/ClubmanSharp/blob/main/LICENSE\n\n" +
                                "This project uses https://github.com/ViGEm/ViGEm.NET, licensed under the MIT license.\n" +
                                "Full terms can be found at:\n https://github.com/ViGEm/ViGEm.NET/blob/master/LICENSE\n\n" +
                                "All developers of this project are not affiliated with Polyphony Digital or Sony Interactive Entertainment.";
        }

        public void VisualLoop(object? sender, EventArgs? e)
        {
            if (bot.error is true)
            {
                MessageBox.Show(bot.errorMsg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                bot.error = false;
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
            else if (bot.currentMenuState == Bot.MenuState.Stuck_PostRace)
                TxtState.Text += "Stk Post Race";
            else if (bot.currentMenuState == Bot.MenuState.Stuck_PreRace)
                TxtState.Text += "Stk Pre Race";

            TxtLap.Text = $"Fastest Lap: {bot.fastestLap.Minutes:d1}:{bot.fastestLap.Seconds:d2}.{bot.fastestLap.Milliseconds:d3}";
            TxtRaces.Text = $"Completed Races: {bot.completedRaces}";
            TxtCredits.Text = $"Estimated Credits: {bot.completedRaces * 105000 * 0.98:n0}";
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
            CustomDelayLong.Text = "750";
            bot.ShortDelay = 250;
            bot.LongDelay = 750;
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
    }
}
