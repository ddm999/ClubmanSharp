using System;
using System.Threading.Tasks;
using PDTools.SimulatorInterface;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using System.Diagnostics;
using System.Threading;
using ClubmanSharp.TrackData;

namespace ClubmanSharp
{
    public class Bot
    {
        private IDualShock4Controller? _ds4 = null;
        private SimulatorInterfaceClient? _simInterface = null;
        private CancellationTokenSource? _simInterfaceCTS = null;
        private int _preRaceStuckCount = 0;
        private int _raceResultStuckCount = 0;

        public SimulatorPacket? currentPacket = null;
        public TrackDataBase? currentTrackData = null;

        public const int TimeOut = 10;
        public TimeSpan TimeOutSpan = new(0, 0, TimeOut);

        public int ShortDelay = 250;
        public int LongDelay = 3000;
        public byte LateRaceMaxThrottle = 255;

        public DualShock4Button confirmButton = DualShock4Button.Cross;
        public DualShock4Button cancelButton = DualShock4Button.Circle;

        public bool connected = false;
        public bool error = false;
        public string errorMsg = "";

        public int completedRaces = 0;
        public int stuckDetectionRuns = 0;

        public string buttonString = "";

        public TimeSpan fastestLap = new(0, 59, 59);

        public MenuState currentMenuState = MenuState.Unknown;

        public bool writeLocationData = false;

        public void Start(string ip)
        {
            SimulatorInterfaceGameType type = SimulatorInterfaceGameType.GT7;
            try
            {
                _simInterface = new SimulatorInterfaceClient(ip, type);
                _simInterface.OnReceive += SimInterface_OnReceive;
            }
            catch (Exception ex)
            {
                error = true;
                errorMsg = $"Failed to start Simulator Interface client.\nException details below:\n\n{ex.Message}";
                return;
            }

            try
            {
                var client = new ViGEmClient();
                _ds4 = client.CreateDualShock4Controller();
                _ds4.Connect();
            }
            catch (Exception ex)
            {
                error = true;
                errorMsg = $"Failed to start ViGEm client and emulated DualShock 4 controller.\nException details below:\n\n{ex.Source}\n{ex.Message}\n{ex.StackTrace}";
                return;
            }


            connected = true;
            Task.Run(() => SimInterfaceClientRunner());
            Task.Run(() => PacketUpdateLoop());
            Task.Run(() => DriverLoop());
            Task.Run(() => MenuUserLoop());

            return;
        }

        public void Stop()
        {
            connected = false;

            if (_simInterfaceCTS != null)
                _simInterfaceCTS.Cancel();

            if (_simInterface != null && _simInterface.Started)
                _simInterface.Dispose();
        }

        private void SimInterface_OnReceive(SimulatorPacket packet)
        {
            currentPacket = packet;
        }

        private void PacketUpdateLoop()
        {
            var ok = true;
            while (ok)
            {
                Thread.Sleep(1000);
                if (!connected || currentPacket is null)
                {
                    continue;
                }

                if (currentPacket.BestLapTime.Milliseconds != -1 && fastestLap > currentPacket.BestLapTime)
                    fastestLap = currentPacket.BestLapTime;

                if (DateTimeOffset.Now - currentPacket.DateReceived > TimeOutSpan)
                {
                    DisconnectController();
                    connected = false;
                    error = true;
                    errorMsg = $"Connection timed out:\nno data packet recieved for {TimeOut} seconds.";
                    return;
                }
            }
        }

        private async void SimInterfaceClientRunner()
        {
            if (_simInterface is null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for Sim Interface Client Runner not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            _simInterfaceCTS = new CancellationTokenSource();

            var task = _simInterface.Start(_simInterfaceCTS.Token);

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            { }
            catch (Exception ex)
            {
                error = true;
                errorMsg = $"Error during Simulator Interface client update.\nException details below:\n\n{ex.Message}";
            }
        }

        /*private void LocateCar()
        {
            var posString = $"{currentPacket.Position.X} x,{currentPacket.Position.Z} z,{(1-currentPacket.RelativeOrientationToNorth)*180} rotn,{currentPacket.MetersPerSecond * 2.23694} mph";
            Trace.WriteLine(posString);

            if (writeLocationData is true)
                File.AppendAllTextAsync("locationData.txt", $"{posString}\n");
        }*/

        private void DriverLoop()
        {
            if (_ds4 is null || currentTrackData is null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for Driver not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            // block for 3 seconds so it doesn't interfere with restarting a race
            Thread.Sleep(3000);

            bool ok = true;
            bool _buttonmashToggler = false;
            while (ok)
            {
                if (!connected)
                {
                    DisconnectController();
                    return;
                }

                if (currentPacket is null || currentMenuState != MenuState.Race)
                {
                    Thread.Sleep(1000);
                    continue;
                }
                Thread.Sleep(100);

                try
                {
                    // handle accel & brake for target speed
                    var mph = currentPacket.MetersPerSecond * 2.23694;
                    var rotn = (1 - currentPacket.RelativeOrientationToNorth) * 180;

                    var targets = currentTrackData.GetTargets(currentPacket.Position.X, currentPacket.Position.Z, currentPacket.LapCount);
                    var targetMph = targets.Item1;
                    var targetOrientation = targets.Item2;

                    buttonString = "";

                    // default all buttons off
                    // NOS
                    _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                    // brake
                    _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                    _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                    // accel
                    _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                    _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);

                    _ds4.SetButtonState(confirmButton, false);
                    if (targetMph == -1 && targetOrientation == -1)
                    {
                        // we're in the pitbox. smash button.
                        _buttonmashToggler = !_buttonmashToggler;
                        if (_buttonmashToggler is true)
                        {
                            _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                            _ds4.SetButtonState(confirmButton, false);
                            buttonString += "L";
                        }
                        else
                        {
                            _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                            _ds4.SetButtonState(confirmButton, true);
                            buttonString += "X";
                        }
                        _ds4.SubmitReport();
                        continue;
                    }

                    targetMph += 2; // needed bc of how the acceleration decrease scales

                    // FULL BRAKE
                    if (mph > targetMph * 1.2)
                    {
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        buttonString += "B";
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 255);
                    }
                    // PARTIAL BRAKE
                    else if (mph > targetMph)
                    {
                        // brake
                        var diff = mph - targetMph;
                        var input = Convert.ToByte(255 - (255 / (targetMph * 0.2) * diff));
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        buttonString += "B";
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, input);
                    }
                    // PARTIAL ACCEL
                    else if (mph > targetMph * 0.9)
                    {
                        // accel
                        var diff = targetMph - mph;
                        var input = Convert.ToByte(255 / (targetMph * 0.1) * diff);
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        buttonString += "A";
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, input);
                    }
                    // FULL ACCEL
                    else
                    {
                        // NOS is only used below 150mph
                        if (mph < 150)
                        {
                            // use NOS
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, true);
                            buttonString += "N";
                        }
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        buttonString += "A";

                        if (currentPacket.LapCount <= 2) // first 2 laps, full throttle
                            _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 255);
                        else // later laps, slow for traffic based on option
                            _ds4.SetSliderValue(DualShock4Slider.RightTrigger, LateRaceMaxThrottle);
                    }

                    // turn towards target line
                    if (targetOrientation == 360.0)
                    {
                        // not in a block, just maintain course
                        _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                    }
                    else if (targetOrientation < 0.0)
                    {
                        // heading west
                        // FULL RIGHT
                        if (-rotn < targetOrientation - 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 255);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL RIGHT
                        else if (-rotn < targetOrientation)
                        {
                            var diff = rotn - (-targetOrientation);
                            var input = Convert.ToByte(128 + (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // FULL LEFT
                        else if (-rotn > targetOrientation + 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 0);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL LEFT
                        else if (-rotn > targetOrientation)
                        {
                            var diff = (-targetOrientation) - rotn;
                            var input = Convert.ToByte(128 - (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 64);
                        }
                        // CENTERED
                        else
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                        }
                    }
                    else
                    {
                        // heading east
                        // FULL RIGHT
                        if (rotn < targetOrientation - 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 255);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL RIGHT
                        else if (rotn < targetOrientation)
                        {
                            var diff = rotn - targetOrientation;
                            var input = Convert.ToByte(128 - (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // FULL LEFT
                        else if (rotn > targetOrientation + 5.0)
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 0);
                            // override NOS to not be used
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            buttonString += "!N";
                        }
                        // PARTIAL LEFT
                        else if (rotn > targetOrientation)
                        {
                            var diff = targetOrientation - rotn;
                            var input = Convert.ToByte(128 + (127 / 5.0 * diff));
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, input);
                        }
                        // CENTERED
                        else
                        {
                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                        }
                    }

                    _ds4.SubmitReport();
                }
                catch (Exception ex)
                {
                    if (_ds4 is null)
                    {
                        return;
                    }

                    DisconnectController();
                    connected = false;
                    error = true;
                    errorMsg = $"Unexpected error sending driver inputs to ViGEm.\nException details below:\n\n{ex.Message}";
                    return;
                }
            }
        }

        public enum MenuState
        {
            Race = 0,
            RacePaused,
            RaceStart,
            TokyoEvents,
            PreRace,
            RaceResult,
            Replay,
            PostRace,
            Stuck_PreOrPostRace,
            Stuck_PostRace,
            Stuck_PreRace,
            Stuck_Replay,
            NoPacket,

            Unknown = -1,
        }

        public MenuState FindBaseMenuState(bool allowReplay=false)
        {
            if (currentPacket is null)
                return MenuState.NoPacket;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                return MenuState.RacePaused;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                return MenuState.Race;
            if (currentPacket.NumCarsAtPreRace > 1)
                return MenuState.PreRace;
            if (allowReplay && currentPacket.LapsInRace > 0)
                return MenuState.Replay;
            return MenuState.Unknown;
        }

        public void PreRaceStuckDetection()
        {
            _preRaceStuckCount += 1;
            if (_preRaceStuckCount >= 5)
            {
                currentMenuState = MenuState.Stuck_PreOrPostRace;
                _preRaceStuckCount = 0;
                stuckDetectionRuns += 1;
            }
        }

        public void RaceResultStuckDetection()
        {
            _raceResultStuckCount += 1;
            if (_raceResultStuckCount >= 100)
            {
                DisconnectController();
                connected = false;
                error = true;
                errorMsg = "Stuck in Race Result. Unable to figure out how to get out of this state.";
            }
        }

        public MenuState FindNewMenuState()
        {
            // no packet recieved or no race is loaded
            if (currentPacket is null || currentPacket.LapCount == -1)
                return MenuState.Unknown;

            if (currentMenuState == MenuState.PreRace)
            {
                if (currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                    return MenuState.Race;
                PreRaceStuckDetection();
            }
            else if (currentMenuState == MenuState.Race)
            {
                if (currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                    return MenuState.RacePaused;
                if (currentPacket.LapCount > 5)
                    return MenuState.RaceResult;
            }
            else if (currentMenuState == MenuState.RacePaused)
            {
                // have to just assume Exit is never clicked
                //  because it can bring up a mini-rewards screen with no way to tell
                if (!currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                    return MenuState.Race;
            }
            else if (currentMenuState == MenuState.RaceResult)
            {
                if (!currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                    return MenuState.Replay;
                RaceResultStuckDetection();
            }
            else if (currentMenuState == MenuState.Replay)
            {
                if (currentPacket.NumCarsAtPreRace > 0)
                    return MenuState.PostRace;
            }
            return currentMenuState;
        }

        private void MenuUserLoop()
        {
            if (_ds4 is null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for MenuUser not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            Thread.Sleep(1000);

            currentMenuState = FindBaseMenuState();

            Trace.WriteLine($"{currentMenuState}");

            if (currentMenuState == MenuState.Unknown)
            {
                connected = false;
                error = true;
                errorMsg = "Couldn't determine game state. Open the pre-race menu before starting.";
                return;
            }
            else if (currentMenuState == MenuState.NoPacket)
            {
                connected = false;
                error = true;
                errorMsg = "No packet received.\nCheck your connection, allow ClubmanSharp access through any firewalls, verify the entered IP address for your console.\n\n" +
                           "A restricted network such as a university campus may block the connection:\n" +
                           "you can try using a mobile hotspot (be aware of your data usage) or internet connection sharing through your PC.";
                return;
            }

            try
            {
                if (currentMenuState == MenuState.PreRace)
                {
                    // ensure we're hovered over start race

                    // first, smash the hell out of the circle button
                    for (int i = 0; i < 5; i++)
                    {
                        _ds4.SetButtonState(cancelButton, true);
                        buttonString = "O";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(cancelButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);
                    }
                    // then smash the hell out of left dpad
                    for (int i = 0; i < 5; i++)
                    {
                        _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                        buttonString = "L";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);
                    }
                    // then press down
                    _ds4.SetDPadDirection(DualShock4DPadDirection.South);
                    buttonString = "D";
                    _ds4.SubmitReport();
                    Thread.Sleep(50);

                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    // then press right to move to Start from Weather Radar
                    _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                    buttonString = "R";
                    _ds4.SubmitReport();
                    Thread.Sleep(50);

                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    // and finally, click start race
                    _ds4.SetButtonState(confirmButton, true);
                    buttonString = "X";
                    _ds4.SubmitReport();
                    Thread.Sleep(50);

                    _ds4.SetButtonState(confirmButton, false);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);
                }
                else if (currentMenuState == MenuState.Race)
                {
                    _ds4.SetButtonState(DualShock4Button.Options, true);
                    buttonString = "S";
                    _ds4.SubmitReport();
                    Thread.Sleep(50);

                    _ds4.SetButtonState(DualShock4Button.Options, false);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    currentMenuState = MenuState.RacePaused;
                }

                if (currentMenuState == MenuState.RacePaused)
                {
                    // smash the hell out of left dpad
                    for (int i = 0; i < 5; i++)
                    {
                        _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                        buttonString = "L";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);
                    }
                    // then go right once
                    _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                    buttonString = "R";
                    _ds4.SubmitReport();

                    Thread.Sleep(50);
                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    // and click to restart race
                    _ds4.SetButtonState(confirmButton, true);
                    buttonString = "X";
                    _ds4.SubmitReport();
                    Thread.Sleep(50);

                    _ds4.SetButtonState(confirmButton, false);
                    buttonString = "";
                    _ds4.SubmitReport();
                    Thread.Sleep(ShortDelay);

                    Thread.Sleep(LongDelay);
                }
            }
            catch (Exception ex)
            {
                if (_ds4 is null)
                {
                    return;
                }
                else
                {
                    DisconnectController();
                    connected = false;
                    error = true;
                    errorMsg = $"Unexpected error sending initial menu inputs to ViGEm.\nException details below:\n\n{ex.Message}";
                    return;
                }
            }

            currentTrackData.NewRace();

            bool ok = true;
            bool registeredResult = false;
            while (ok)
            {
                if (!connected)
                {
                    DisconnectController();
                    return;
                }

                if (currentMenuState == MenuState.Unknown)
                {
                    Thread.Sleep(1000);
                    currentMenuState = FindBaseMenuState();
                    continue;
                }

                currentMenuState = FindNewMenuState();

                try
                {
                    if (currentMenuState == MenuState.Unknown)
                    {
                        DisconnectController();
                        connected = false;
                        error = true;
                        errorMsg = "Unexpected menu state change. Unable to determine game state.";
                        return;
                    }
                    else if (currentMenuState == MenuState.RaceResult)
                    {
                        Thread.Sleep(ShortDelay);

                        if (!registeredResult)
                        {
                            completedRaces += 1;

                            _ds4.SetAxisValue(DualShock4Axis.LeftThumbX, 128);
                            _ds4.SetButtonState(DualShock4Button.ThumbRight, false);
                            _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                            _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                            _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                            _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);

                            registeredResult = true;

                            currentTrackData.NewRace();
                        }

                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();
                    }
                    else if (currentMenuState == MenuState.Replay)
                    {
                        registeredResult = false;
                        _raceResultStuckCount = 0;

                        Thread.Sleep(LongDelay);

                        for (int i = 0; i < 2; i++)
                        {
                            _ds4.SetButtonState(confirmButton, true);
                            buttonString = "X";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            _ds4.SetButtonState(confirmButton, false);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(ShortDelay);
                        }

                        Thread.Sleep(LongDelay);
                    }
                    else if (currentMenuState == MenuState.PostRace)
                    {
                        Thread.Sleep(LongDelay);

                        _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                        buttonString = "R";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);

                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        _preRaceStuckCount = 0;
                        currentMenuState = MenuState.PreRace;
                    }
                    else if (currentMenuState == MenuState.PreRace)
                    {
                        Thread.Sleep(LongDelay);

                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        Thread.Sleep(LongDelay);
                    }
                    else if (currentMenuState == MenuState.Stuck_PreOrPostRace)
                    {
                        Thread.Sleep(3000);
                        // ensure we're hovered over start race

                        // first, smash the hell out of the circle button
                        for (int i = 0; i < 5; i++)
                        {
                            _ds4.SetButtonState(cancelButton, true);
                            buttonString = "O";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            _ds4.SetButtonState(cancelButton, false);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(250);
                        }
                        // then smash the hell out of left dpad
                        for (int i = 0; i < 5; i++)
                        {
                            _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                            buttonString = "L";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(250);
                        }
                        // then press down
                        _ds4.SetDPadDirection(DualShock4DPadDirection.South);
                        buttonString = "D";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(250);

                        // and finally, click start race
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "X";
                        _ds4.SubmitReport();

                        Thread.Sleep(5000);

                        // if we're still in pre-race, it's actually post-race
                        currentMenuState = FindBaseMenuState(true);
                        if (currentMenuState == MenuState.PreRace)
                            currentMenuState = MenuState.Stuck_PostRace;
                        if (currentMenuState == MenuState.Replay)
                            currentMenuState = MenuState.Stuck_Replay;
                    }
                    else if (currentMenuState == MenuState.Stuck_Replay)
                    {
                        // get to the exit button
                        for (int i = 0; i < 10; i++)
                        {
                            _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                            buttonString = "R";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(250);
                        }
                        // click exit
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(250);

                        Thread.Sleep(3000);
                        currentMenuState = MenuState.PostRace;
                    }
                    else if (currentMenuState == MenuState.Stuck_PostRace)
                    {
                        // first, smash the hell out of the circle button
                        for (int i = 0; i < 5; i++)
                        {
                            _ds4.SetButtonState(cancelButton, true);
                            buttonString = "O";
                            _ds4.SubmitReport();
                            Thread.Sleep(50);

                            _ds4.SetButtonState(cancelButton, false);
                            buttonString = "";
                            _ds4.SubmitReport();
                            Thread.Sleep(250);
                        }
                        // then press left
                        _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                        buttonString = "L";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        buttonString = "";
                        _ds4.SubmitReport();
                        Thread.Sleep(250);

                        // and finally, click retry
                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        currentMenuState = MenuState.Stuck_PreRace;
                    }
                    else if (currentMenuState == MenuState.Stuck_PreRace)
                    {
                        Thread.Sleep(8000);

                        _ds4.SetButtonState(confirmButton, true);
                        buttonString = "X";
                        _ds4.SubmitReport();
                        Thread.Sleep(50);

                        _ds4.SetButtonState(confirmButton, false);
                        buttonString = "";
                        _ds4.SubmitReport();

                        Thread.Sleep(5000);

                        currentMenuState = MenuState.PreRace;
                        currentMenuState = FindNewMenuState();
                        if (currentMenuState != MenuState.Race)
                        {
                            DisconnectController();
                            connected = false;
                            error = true;
                            errorMsg = "Got stuck attempting to correct a post-race menu failure. Unable to determine game state.";
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_ds4 is null)
                    {
                        return;
                    }
                    else
                    {
                        DisconnectController();
                        connected = false;
                        error = true;
                        errorMsg = $"Unexpected error sending menu inputs to ViGEm.\nException details below:\n\n{ex.Message}";
                        return;
                    }
                }
            }
        }

        private void DisconnectController()
        {
            if (_ds4 != null)
            {
                _ds4.Disconnect();
                _ds4 = null;
            }
        }
    }
}
