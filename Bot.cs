using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using PDTools.Crypto.SimulationInterface;
using PDTools.SimulatorInterface;
using Syroot.BinaryData.Memory;
using System.Numerics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using System.Diagnostics;
using System.Threading;
using System.IO;

namespace ClubmanSharp
{
    public class Bot
    {
        private IDualShock4Controller? _ds4 = null;
        private SimulatorInterfaceClient? _simInterface = null;
        private CancellationTokenSource? _simInterfaceCTS = null;

        public SimulatorPacket? currentPacket = null;

        public const int SendDelaySeconds = 10;
        public const int ReceivePort = 33739;
        public const int BindPort = 33740;

        public const int ShortDelay = 500;
        public const int LongDelay = 3000;

        public bool connected = false;
        public bool error = false;
        public string errorMsg = "";

        public int completedRaces = 0;

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
                errorMsg = $"Failed to start ViGEm client and emulated DualShock 4 controller.\nException details below:\n\n{ex.Message}";
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
                if (!connected || currentPacket == null)
                {
                    continue;
                }

                if (currentPacket.LastLapTime.Milliseconds != -1 && fastestLap > currentPacket.LastLapTime)
                    fastestLap = currentPacket.LastLapTime;
            }
        }

        private async void SimInterfaceClientRunner()
        {
            if (_simInterface == null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for Sim Interface Client Runner not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            if (_simInterfaceCTS == null)
                _simInterfaceCTS = new CancellationTokenSource();
            else
                _simInterfaceCTS.TryReset();

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
            finally
            {
                _simInterface.Dispose();
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
            if (_ds4 is null)
            {
                connected = false;
                error = true;
                errorMsg = "Internal object for Driver not initialized.\nFor developers: call 'Start()' first!";
                return;
            }

            // block for 3 seconds so it doesn't interfere with restarting a race
            Thread.Sleep(3000);

            bool ok = true;
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
                    // always use all the NOS
                    _ds4.SetButtonState(DualShock4Button.ThumbRight, true);

                    // handle accel & brake for target speed
                    var mph = currentPacket.MetersPerSecond * 2.23694;
                    var rotn = (1 - currentPacket.RelativeOrientationToNorth) * 180;

                    var targets = TrackData.GetTargets(currentPacket.Position.X, currentPacket.Position.Z);
                    var targetMph = targets.Item1;
                    var targetOrientation = targets.Item2;

                    targetMph += 2; // needed bc of how the acceleration decrease scales

                    // FULL BRAKE
                    if (mph > targetMph * 1.2)
                    {
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 255);
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);
                    }
                    // PARTIAL BRAKE
                    else if (mph > targetMph)
                    {
                        // brake
                        var diff = mph - targetMph;
                        var input = Convert.ToByte(255 - (255 / (targetMph * 0.2) * diff));
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, true);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, input);
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, false);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 0);
                    }
                    // PARTIAL ACCEL
                    else if (mph > targetMph * 0.9)
                    {
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                        // accel
                        var diff = targetMph - mph;
                        var input = Convert.ToByte(255 / (targetMph * 0.1) * diff);
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, input);
                    }
                    // FULL ACCEL
                    else
                    {
                        // brake
                        _ds4.SetButtonState(DualShock4Button.TriggerLeft, false);
                        _ds4.SetSliderValue(DualShock4Slider.LeftTrigger, 0);
                        // accel
                        _ds4.SetButtonState(DualShock4Button.TriggerRight, true);
                        _ds4.SetSliderValue(DualShock4Slider.RightTrigger, 255);
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

            Unknown = -1,
        }

        public MenuState FindBaseMenuState()
        {
            if (currentPacket == null)
                return MenuState.Unknown;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.Paused))
                return MenuState.RacePaused;
            if (currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                return MenuState.Race;
            if (currentPacket.NumCarsAtPreRace > 1)
                return MenuState.PreRace;
            return MenuState.Unknown;
        }

        public MenuState FindNewMenuState()
        {
            // no packet recieved or no race is loaded
            if (currentPacket == null || currentPacket.LapCount == -1)
                return MenuState.Unknown;

            if (currentMenuState == MenuState.PreRace)
            {
                if (currentPacket.Flags.HasFlag(SimulatorFlags.CarOnTrack))
                    return MenuState.Race;
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

            if (currentMenuState == MenuState.PreRace)
            {
                // ensure we're hovered over start race

                // first, smash the hell out of the circle button
                for (int i = 0; i < 5; i++)
                {
                    _ds4.SetButtonState(DualShock4Button.Circle, true);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetButtonState(DualShock4Button.Circle, false);
                    _ds4.SubmitReport();
                    Thread.Sleep(200);
                }
                // then smash the hell out of left dpad
                for (int i = 0; i < 5; i++)
                {
                    _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    _ds4.SubmitReport();
                    Thread.Sleep(200);
                }
                // then press down
                _ds4.SetDPadDirection(DualShock4DPadDirection.South);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                _ds4.SubmitReport();
                Thread.Sleep(200);
                // and finally, click start race
                _ds4.SetButtonState(DualShock4Button.Cross, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Cross, false);
                _ds4.SubmitReport();
                Thread.Sleep(200);
            }
            else if (currentMenuState == MenuState.Race)
            {
                _ds4.SetButtonState(DualShock4Button.Options, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Options, false);
                _ds4.SubmitReport();
                Thread.Sleep(200);

                currentMenuState = MenuState.RacePaused;
            }

            if (currentMenuState == MenuState.RacePaused)
            {
                // smash the hell out of left dpad
                for (int i = 0; i < 5; i++)
                {
                    _ds4.SetDPadDirection(DualShock4DPadDirection.West);
                    _ds4.SubmitReport();
                    Thread.Sleep(50);
                    _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                    _ds4.SubmitReport();
                    Thread.Sleep(200);
                }
                // then go right once
                _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                _ds4.SubmitReport();
                Thread.Sleep(200);
                // and click to restart race
                _ds4.SetButtonState(DualShock4Button.Cross, true);
                _ds4.SubmitReport();
                Thread.Sleep(50);
                _ds4.SetButtonState(DualShock4Button.Cross, false);
                _ds4.SubmitReport();
                Thread.Sleep(200);

                Thread.Sleep(5000);
            }

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
                        }

                        _ds4.SetButtonState(DualShock4Button.Cross, true);
                        _ds4.SubmitReport();
                        Thread.Sleep(50);
                        _ds4.SetButtonState(DualShock4Button.Cross, false);
                        _ds4.SubmitReport();
                    }
                    else if (currentMenuState == MenuState.Replay)
                    {
                        Thread.Sleep(LongDelay);

                        _ds4.SetButtonState(DualShock4Button.Circle, true);
                        _ds4.SubmitReport();
                        Thread.Sleep(50);
                        _ds4.SetButtonState(DualShock4Button.Circle, false);
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);
                        _ds4.SetButtonState(DualShock4Button.Cross, true);
                        _ds4.SubmitReport();
                        Thread.Sleep(50);
                        _ds4.SetButtonState(DualShock4Button.Cross, false);
                        _ds4.SubmitReport();

                        Thread.Sleep(LongDelay);
                    }
                    else if (currentMenuState == MenuState.PostRace)
                    {
                        Thread.Sleep(LongDelay);

                        _ds4.SetDPadDirection(DualShock4DPadDirection.East);
                        _ds4.SubmitReport();
                        Thread.Sleep(50);
                        _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                        _ds4.SubmitReport();
                        Thread.Sleep(ShortDelay);
                        _ds4.SetButtonState(DualShock4Button.Cross, true);
                        _ds4.SubmitReport();
                        Thread.Sleep(50);
                        _ds4.SetButtonState(DualShock4Button.Cross, false);
                        _ds4.SubmitReport();

                        currentMenuState = MenuState.PreRace;
                    }
                    else if (currentMenuState == MenuState.PreRace)
                    {
                        Thread.Sleep(LongDelay);

                        _ds4.SetButtonState(DualShock4Button.Cross, true);
                        _ds4.SubmitReport();
                        Thread.Sleep(50);
                        _ds4.SetButtonState(DualShock4Button.Cross, false);
                        _ds4.SubmitReport();

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
