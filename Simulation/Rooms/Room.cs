using MiniHospital.Config;

namespace MiniHospital.Simulation.Rooms
{
    public class Room : ServicePoint
    {
        public RoomConfig Config { get; }

        public bool IsOpen { get; set; }
        private Mutex roomMutex = new Mutex();
        public Room(RoomConfig config): base(config.Staff, config.Patient)
        {
            Config = config;
            IsOpen = false;
        }
        
        public bool TakeMutex()
        {
            return roomMutex.WaitOne(1);
        }

        public void ReleaseMutex()
        {
            roomMutex.ReleaseMutex();
            return;
        }
    }
}
