using UnityEngine;

namespace WuWa
{
    /// Single owner of the hardware cursor. Screens and the director ask for a
    /// mode; the actual lock/visibility also depends on the active device.
    public static class CursorService
    {
        public enum Mode { Gameplay, Free, Menu }
        public static Mode Current { get; private set; } = Mode.Gameplay;

        public static void Apply(Mode m)
        {
            Current = m;
            Refresh();
        }

        public static void Refresh()
        {
            switch (Current)
            {
                case Mode.Gameplay:
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    break;
                case Mode.Free:
                    Cursor.lockState = CursorLockMode.Confined;
                    Cursor.visible = true;
                    break;
                default:
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = !InputService.GamepadActive;     // pad drives focus, hide the pointer
                    break;
            }
        }
    }
}
