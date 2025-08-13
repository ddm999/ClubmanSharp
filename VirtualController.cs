using System;
using System.Threading;
using System.Threading.Tasks;
using ClubmanSharp.TrackData;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace ClubmanSharp
{
    public class VirtualController
    {
        private IDualShock4Controller? _ds4 = null;
        private ViGEmClient? _client = null; 
        public DualShock4Button crossButton = DualShock4Button.Cross;
        public DualShock4Button circleButton = DualShock4Button.Circle;
        public DualShock4Button optionsButton = DualShock4Button.Options;
        public DualShock4Button psButton = DualShock4SpecialButton.Ps;


        public bool IsConnected => _ds4 != null;

        public void Initialize()
        {
            try
            {
                _client = new ViGEmClient();
                DebugLog.Log($"Create ViGEmClient", LogType.Main);
                _ds4 = _client.CreateDualShock4Controller();
                DebugLog.Log($"ds4 Connect", LogType.Main);
                _ds4.Connect();
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public void PressDPad(DualShock4DPadDirection direction)
        {
            if (_ds4 != null)
            {
                _ds4.SetDPadDirection(direction);
                _ds4.SubmitReport();
                Thread.Sleep(100); // Simulated delay
                _ds4.SetDPadDirection(DualShock4DPadDirection.None);
                _ds4.SubmitReport();
            }
        }

        public void PressButton(DualShock4Button button)
        {
            if (_ds4 != null)
            {
                _ds4.SetButtonState(button, true);
                _ds4.SubmitReport();
                Thread.Sleep(100); // Simulated delay
                _ds4.SetButtonState(button, false);
                _ds4.SubmitReport();
            }
        }

        public void Dispose()
        {
            if (_ds4 != null)
            {
                _ds4.Disconnect();
                _ds4 = null;
                DebugLog.Log($"Dispose ds4", LogType.Main);
            }

            if (_client != null)
            {
                _client.Dispose();
                _client = null;
                DebugLog.Log($"Dispose ViGEmClient", LogType.Main);
            }
        }
    }
}