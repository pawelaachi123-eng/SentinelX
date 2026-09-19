namespace SentinelX
{
    public class VoiceModeService
    {
        public bool IsEnabled { get; private set; }

        public void TurnOn()
        {
            IsEnabled = true;
        }

        public void TurnOff()
        {
            IsEnabled = false;
        }
    }
}