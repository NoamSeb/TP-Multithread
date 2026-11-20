using MiniHospital.Config;
using MiniHospital.Simulation.Characters;
using MiniHospital.Utils;

namespace MiniHospital.Simulation.Rooms
{
    public abstract class ServicePoint
    {
        public Slot StaffSlot { get; }
        public Slot PatientSlot { get; }

        public Employee? Staff { get; set; }
        public Patient? Occupant { get; private set; }

        protected ServicePoint(Slot staffSlot, Slot patientSlot)
        {
            StaffSlot = staffSlot;
            PatientSlot = patientSlot;
        }

        public void AssignPatient(Patient patient)
        {
            Chaos.ExecuteIfEnabled();
            if (Occupant != null)
                throw new InvalidOperationException("Poste déjà occupé");

            Chaos.ExecuteIfEnabled();

            Occupant = patient;
        }

        public void ReleasePatient(Patient? patient)
        {
            if (Occupant != patient)
                throw new InvalidOperationException("Libération d'un poste par le mauvais patient");

            Occupant = null;

            Chaos.ExecuteIfEnabled();
        }
    }
}
