using MiniHospital.Config;
using MiniHospital.Simulation.Rooms;
using MiniHospital.Utils;

namespace MiniHospital.Simulation.Characters
{
    public class Employee : Character
    {
        public bool IsAvailable => step == Step.WaitForPatient;

        private readonly Hospital hospital;
        private Patient? patient;
        private float patientHandlingEndTime;
        private TimeRange patientHandlingRequiredTime;
        private ServicePoint workPlace;

        private Thread eThread;

        private enum Step
        {
            Arriving, //Pas utilisé dans la verison TP
            WaitForPatient,
            HandlePatient,
            Leaving,
            WaitingForBusDeparting,
            Finished
        }

        private Step step;

        public Employee(int spriteIndex, ServicePoint workplace, Hospital hospital,
            TimeRange patientHandlingRequiredTime)
            : base(spriteIndex)
        {
            this.workPlace = workplace;
            SetRenderScale(workplace is Reception ? CharacterConfig.RenderScale : CharacterConfig.DoctorScale);
            this.hospital = hospital;
            PlaceAt(workPlace.StaffSlot);
            step = Step.WaitForPatient;
            this.patientHandlingRequiredTime = patientHandlingRequiredTime;
            eThread = new Thread(Brain);
            eThread.IsBackground = true;
            eThread.Start();
        }

        public void LeaveHospital()
        {
            Chaos.ExecuteIfEnabled();
            if (patient != null)
            {
                //problem
                throw new InvalidOperationException(
                    "Un employé reçoit l'ordre de partir alors qu'il n'a pas encore fini avec le patient");
            }

            workPlace.Staff = null;

            MoveTo(NavNode.BusStop);
            step = Step.Leaving;
        }

        public void OnBusDeparted()
        {
            if (step != Step.WaitingForBusDeparting)
                throw new InvalidOperationException("Employé absent du bus");
            step = Step.Finished;
            hospital.eBarrier.RemoveParticipant();
        }

        public void HandlePatient(Patient newPatient)
        {
            if (patient != null)
            {
                //problem
                throw new InvalidOperationException(
                    "Un employé reçoit un patient alors qu'il n'a pas encore fini avec le précédent");
            }

            if (step != Step.WaitForPatient)
            {
                throw new InvalidOperationException(
                    "Un employé reçoit un patient alors qu'il n'est pas en train d'en attendre un");
            }

            patient = newPatient;

            patientHandlingEndTime = hospital.ElapsedTimeSinceStart + patientHandlingRequiredTime.Sample();
            SetBubbletype(BubbleType.Health);
            step = Step.HandlePatient;
        }

        private void FinishHandlingPatient()
        {
            if (patient == null)
            {
                throw new Exception("FinishHandlingPatient appelé alors que patient est null");
            }

            if (workPlace is Reception)
            {
                Room? assigned = hospital.ChooseValidRoom(false);
                patient.RegistrationFinished(assigned);
            }
            else if (workPlace is Room room)
            {
                Room? assigned = null;
                if (room.Config.Type == RoomType.Generalist && hospital.Phase == Hospital.DayPhase.Running &&
                    Random.Shared.NextDouble() < 0.35)
                {
                    assigned = hospital.ChooseValidRoom(true);
                }

                patient.ConsultationFinished(assigned);
            }

            patient = null;
            SetBubbletype(BubbleType.None);
            step = Step.WaitForPatient;
        }

        protected override void Brain()
        {
            while (step != Step.Finished)
            {
                switch (step)
                {
                    case Step.WaitForPatient:
                        //attente, check fin de journée
                        if (hospital.Phase != Hospital.DayPhase.Running)
                        {
                            LeaveHospital();
                        }

                        break;
                    case Step.HandlePatient:
                        if (patient != null)
                        {
                            if (hospital.ElapsedTimeSinceStart >= patientHandlingEndTime)
                            {
                                FinishHandlingPatient();
                            }
                        }
                        else
                        {
                            throw new Exception("Emplyé bloqué en état HandlePatient sans patient à traiter");
                        }

                        break;
                    case Step.Leaving:
                        if (!IsMoving)
                        {
                            step = Step.WaitingForBusDeparting;
                            hospital.RegisterEmployeeBoarding(this);
                        }

                        break;
                    case Step.WaitingForBusDeparting:
                        break;
                }

                Thread.Sleep(100);
            }
        }
    }
}