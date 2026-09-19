using System;

namespace SentinelX
{
    public class EmergencyStopService
    {
        public bool IsActive { get; private set; }

        public DateTime? ActivatedAt { get; private set; }

        public event Action<bool>? StateChanged;

        public void Activate()
        {
            if (IsActive)
                return;

            IsActive =
                true;

            ActivatedAt =
                DateTime.Now;

            StateChanged?.Invoke(
                true);
        }

        public void Reset()
        {
            if (!IsActive)
                return;

            IsActive =
                false;

            ActivatedAt =
                null;

            StateChanged?.Invoke(
                false);
        }
    }
}