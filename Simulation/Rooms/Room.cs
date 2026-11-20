using MiniHospital.Config;

namespace MiniHospital.Simulation.Rooms
{
    public class Room : ServicePoint
    {
        public RoomConfig Config { get; }

        public bool IsOpen { get; set; }

        public Room(RoomConfig config): base(config.Staff, config.Patient)
        {
            Config = config;
            IsOpen = false;
        }
    }
}
