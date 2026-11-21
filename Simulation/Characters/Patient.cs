using MiniHospital.Config;
using MiniHospital.Rendering;
using MiniHospital.Simulation.Rooms;
using MiniHospital.Utils;

namespace MiniHospital.Simulation.Characters
{
    public class Patient : Character
    {
        public Room? AssignedRoom { get; private set; }
        public Reception? AssignedReception { get; private set; }


        private Hospital hospital;
        private Step step = Step.Arriving;

        private GaugeSprite gaugeSprite;
        private bool wasTreated;
        private float waitingStartedAt;
        private Slot? waitingSeat;
        private float maxPatience;
        private Thread pThread;

        object verrou1 = new();

        public Patient(int spriteIndex, Hospital hospital, float maxPatience) : base(spriteIndex)
        {
            this.hospital = hospital;
            SetRenderScale(CharacterConfig.PatientScale);
            gaugeSprite = new GaugeSprite();
            this.maxPatience = maxPatience;
            pThread = new Thread(Brain);
            pThread.Start();
        }

        private enum Step
        {
            Arriving,
            GoingToReception,
            AtReception,
            GoingToWaiting,
            WaitingForReception,
            WaitingForRoom,
            GoingToRoom,
            InConsultation,
            Leaving,
            Finished
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            lock (verrou1)
            {
                gaugeSprite.Position = position + CharacterConfig.PatienceGaugePosition;
            }

            if (!pThread.IsAlive)
            {
                pThread.Join();
            }
        }

        public override void GetRenderSprites(List<Sprite> list)
        {
            base.GetRenderSprites(list);
            if (step == Step.WaitingForReception || step == Step.WaitingForRoom)
                list.Add(gaugeSprite.Copy());
        }

        private void SetCurrentStep(Step next)
        {
            Chaos.ExecuteIfEnabled();
            step = next;

            if (Hospital.CurrentQuestion != 0)
            {
                //cas spéciaux pour les exercices
                if (Hospital.CurrentQuestion == 1 && next == Step.AtReception ||
                    Hospital.CurrentQuestion == 2 && next == Step.WaitingForReception ||
                    Hospital.CurrentQuestion == 3 && next == Step.WaitingForRoom)
                {
                    StartLeaving(false);
                }
            }
        }

        public void RegistrationFinished(Room? room)
        {
            if (step != Step.AtReception || AssignedReception == null)
                throw new InvalidOperationException("Enregistrement termine hors de l'accueil");
            
            AssignedRoom = room;
            AssignedReception.ReleasePatient(this);
            AssignedReception = null;
            hospital.availableReception.Release();
            if (AssignedRoom == null || hospital.Phase != Hospital.DayPhase.Running)
                StartLeaving(false);
            else
                GoToWaiting();
        }

        public void ConsultationFinished(Room? specialist)
        {
            if (step != Step.InConsultation || AssignedRoom == null)
                throw new InvalidOperationException("Consultation terminee hors consultation");

            AssignedRoom.ReleasePatient(this);
            AssignedRoom = null;

            if (specialist != null && hospital.Phase == Hospital.DayPhase.Running)
            {
                AssignedRoom = specialist;
                GoToWaiting();
            }
            else
            {
                // Une orientation non realisee a la fermeture reste non soignee.
                StartLeaving(specialist == null);
            }
        }

        private bool TryGotoReception()
        {
            Reception? reception = hospital.TryGetReception();
            
            if (reception != null)
            {
                reception.AssignPatient(this);
                AssignedReception = reception;
                ReleaseWaitingSeat();
                MoveTo(reception.PatientSlot);
                SetBubbletype(BubbleType.Question);
                SetCurrentStep(Step.GoingToReception);
                return true;
            }

            return false;
        }

        private void GoToWaiting()
        {
            waitingSeat = hospital.TakeFreeSeat();
            MoveTo(waitingSeat);
            SetBubbletype(BubbleType.Wait);
            SetCurrentStep(Step.GoingToWaiting);
        }

        private void ReleaseWaitingSeat()
        {
            if (waitingSeat == null)
                return;
            hospital.ReleaseSeat(waitingSeat);
            waitingSeat = null;
        }

        private bool WaitOrGiveUp()
        {
            float remaining = 1f -
                              (hospital.ElapsedTimeSinceStart - waitingStartedAt) / maxPatience;
            gaugeSprite.Value = Math.Clamp(remaining, 0f, 1f);
            SetBubbletype(remaining < 0.25f ? BubbleType.Angry : BubbleType.Wait);

            if (remaining > 0f)
                return false;

            StartLeaving(false);
            return true;
        }

        private void StartLeaving(bool treated)
        {
            wasTreated = treated;
            ReleaseWaitingSeat();

            if (AssignedReception?.Occupant == this)
                AssignedReception.ReleasePatient(this);
            AssignedReception = null;

            if (AssignedRoom?.Occupant == this)
                AssignedRoom.ReleasePatient(this);
            AssignedRoom = null;

            SetBubbletype(treated ? BubbleType.Done : BubbleType.Angry);
            MoveTo(NavNode.Outside);
            SetCurrentStep(Step.Leaving);
        }

        protected override void Brain()
        {
            while (step != Step.Finished)
            {
                if (hospital.Phase != Hospital.DayPhase.Running &&
                    step != Step.InConsultation && step != Step.AtReception &&
                    step != Step.Leaving && step != Step.Finished)
                {
                    StartLeaving(false);
                    return;
                }

                lock (verrou1)
                {
                    switch (step)
                    {
                        case Step.Arriving:
                            //cas spéciaux pour les exercices
                            if (Hospital.CurrentQuestion == 1)
                            {
                                MoveTo(HospitalLayout.ReceptionPatients[0]);
                                SetCurrentStep(Step.GoingToReception);
                            }
                            else if (Hospital.CurrentQuestion == 2)
                            {
                                GoToWaiting();
                            }
                            else if (!TryGotoReception())
                            {
                                GoToWaiting();
                            }

                            break;

                        case Step.GoingToReception:
                            if (!IsMoving)
                            {
                                SetCurrentStep(Step.AtReception);
                                if (step == Step.AtReception)
                                    AssignedReception!.Staff!.HandlePatient(this);
                            }

                            break;

                        case Step.AtReception:
                            break;

                        case Step.GoingToWaiting:
                            if (!IsMoving)
                            {
                                waitingStartedAt = hospital.ElapsedTimeSinceStart;
                                gaugeSprite.Value = 1f;
                                SetCurrentStep(AssignedRoom == null ? Step.WaitingForReception : Step.WaitingForRoom);
                            }

                            break;

                        case Step.WaitingForReception:
                            if (!WaitOrGiveUp())
                                TryGotoReception();
                            break;

                        case Step.WaitingForRoom:
                            if (WaitOrGiveUp())
                                break;

                            if (AssignedRoom != null && AssignedRoom.IsOpen &&
                                AssignedRoom.Occupant == null &&
                                AssignedRoom.Staff?.IsAvailable == true)
                            {
                                AssignedRoom.AssignPatient(this);
                                ReleaseWaitingSeat();
                                MoveTo(AssignedRoom);
                                SetBubbletype(BubbleType.Health);
                                SetCurrentStep(Step.GoingToRoom);
                            }

                            break;

                        case Step.GoingToRoom:
                            if (!IsMoving)
                            {
                                AssignedRoom!.Staff!.HandlePatient(this);
                                SetCurrentStep(Step.InConsultation);
                            }

                            break;

                        case Step.InConsultation:
                            break;

                        case Step.Leaving:
                            if (!IsMoving)
                            {
                                SetCurrentStep(Step.Finished);
                                hospital.RegisterPatientDeparture(this, wasTreated);
                            }

                            break;
                    }
                }

                Thread.Sleep(100);
            }
        }
    }
}