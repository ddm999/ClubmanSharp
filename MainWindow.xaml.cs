using PDTools.SimulatorInterface;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;

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

            TxtHelp.Text = "Specific tunes that this script is designed to work with can be found at the link at the bottom of the screen.\n" +
                           "It may work with other vehicles and tunes but performance cannot be guaranteed.\n" +
                           "You must set Pedal Controls to R2/L2, Steering to Left Stick, and Nitrous to R3 button.\n" +
                           "Automatic gearing assist is required. Turning off other assists (but keep ABS default) may improve performance. Sensitivity 10 may also help.\n\n" +
                           "This script does not use pixel checks, so the script cannot inform you when it doesn't achieve a Clean Race Bonus.\n" +
                           "Latency or other issues with your Remote Play setup may cause issues with the script.\n\n" +
                           "Turn on a password requirement for PlayStation purchases before using any script.\n" +
                           "You must start Remote Play with no controller connected to your PC. It cannot be minimised, but you can move other programs above it.\n" +
                           "Enter your PS4/PS5's local IP address and hit Start while on the Tokyo Clubman+ pre-race menu.";

            TxtShortHelp.Text = "Turn on a password requirement for PlayStation purchases before using any script.\n\n" +
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

            TxtLap.Text = $"Fastest Lap: {bot.fastestLap.Minutes:d1}:{bot.fastestLap.Seconds:d2}.{bot.fastestLap.Milliseconds:d3}";
            TxtRaces.Text = $"Completed Races: {bot.completedRaces}";
            TxtCredits.Text = $"Estimated Credits: {bot.completedRaces * 105000 * 0.98:n0}";
            
             if (bot.currentMenuState == Bot.MenuState.Race)
             {
                MapCanvasViewbox.Visibility = Visibility.Visible;
                TxtShortHelp.Visibility = Visibility.Collapsed;
             } 
             else
             {
                MapCanvasViewbox.Visibility = Visibility.Collapsed;
                TxtShortHelp.Visibility = Visibility.Visible;
             }

            MapCanvas.Children.Clear();

            int transformX = 1000;
            int transformY = 800;

            foreach (Segment segment in TrackData.segments)
            { 
                Rectangle rectangle = new Rectangle();
                rectangle.Stroke = new SolidColorBrush(Colors.Black);
                
                rectangle.Width = segment.maxX - segment.minX;
                rectangle.Height = segment.maxZ - segment.minZ;

                MapCanvas.Children.Add(rectangle);
                Canvas.SetLeft(rectangle, segment.minX + transformX); 
                Canvas.SetTop(rectangle, segment.minZ + transformY); 
            }

             if (bot.currentPacket is SimulatorPacket)
             {

                Ellipse avatar = new Ellipse();
                avatar.Fill = new SolidColorBrush(Colors.Red);
                avatar.Width = 24;
                avatar.Height = 24;
                avatar.HorizontalAlignment = HorizontalAlignment.Center;
                avatar.VerticalAlignment = VerticalAlignment.Center;

                MapCanvas.Children.Add(avatar);
                Canvas.SetLeft(avatar, bot.currentPacket.Position.X + transformX); 
                Canvas.SetTop(avatar, bot.currentPacket.Position.Z + transformY); 
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
                settings.ip = ip;
                settings.Save();
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
            CustomDelayLong.Text = "2000";
            bot.ShortDelay = 250;
            bot.LongDelay = 2000;
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
