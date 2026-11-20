using MiniHospital.Config;
using MiniHospital.Rendering;
using MiniHospital.Simulation.Characters;
using MiniHospital.Simulation.Rooms;
using MiniHospital.Utils;

namespace MiniHospital.Simulation
{
    public class Hospital
    {
        //configuration du TP: valeur de 0 = mode classique, autrmement le numéro de la question permet de mettre les comportements dans le mode adapté à la question
        public static int CurrentQuestion { get; internal set; } = 2;

        //public pour l'affichage uniquement
        public List<Room> Rooms { get; private set; } = new List<Room>();

        public DayPhase Phase { get; private set; }

        public HospitalStats DayStats { get; private set; } = new HospitalStats();
        public HospitalStats TotalStats { get; } = new HospitalStats();

        public int DayNumber { get; private set; }
        public int EmployeeCount { get; private set; }

        public float SpeedMultiplier { get; set; } = 1f;

        public TimeSpan ClockTime
        {
            get
            {
                if (dayConfig == null)
                    return TimeSpan.FromHours(8);

                float progress = Math.Clamp(
                    elapsedTimeForToday / Math.Max(1f, dayConfig.DayDuration),
                    0f, 1f
                );

                double minutes =
                    dayConfig.OpeningHour * 60.0
                    + progress * (dayConfig.ClosingHour - dayConfig.OpeningHour) * 60.0;

                return TimeSpan.FromMinutes(minutes);
            }
        }

        public float ElapsedTimeSinceStart { get; private set; }
        public static bool CallBrainOnUpdate { get; set; }

        private List<Sprite> FurnituresSprites = new List<Sprite>();

        private List<Character> characters = new List<Character>();
        private List<Slot> freeSeats = new();

        private List<Reception> receptions = new List<Reception>();
        private List<Employee> employeesInBus = new List<Employee>();
        
        //pour l'affichage
        public int EmployeesInBusCount => employeesInBus.Count;


        public enum DayPhase
        {
            Running,
            Closing,
            BusLeaving,
            Finished
        }

        public Hospital()
        {
            Initialize();

            StartDay(new DayConfig());

        }

        public void Initialize()
        {
            Rooms = new List<Room>();
            foreach (RoomConfig config in HospitalLayout.Rooms)
            {
                Rooms.Add(new Room(config));
            }

            receptions = new List<Reception>();
            for (int i = 0; i < HospitalLayout.ReceptionStaff.Length; i++)
            {
                receptions.Add(new Reception(
                    HospitalLayout.ReceptionStaff[i],
                    HospitalLayout.ReceptionPatients[i]
                ));
            }

            freeSeats.Clear();
            freeSeats.AddRange(HospitalLayout.WaitingSeats);

            InitializeFurniture();
            Navigation.InitFullGraph();

            characters.Clear();
            ElapsedTimeSinceStart = 0;
            CallBrainOnUpdate = Hospital.CurrentQuestion == 0? true : false;
        }

        public void Update(float deltaTime)
        {
            deltaTime *= SpeedMultiplier;
            ElapsedTimeSinceStart += deltaTime;
            HandleDay(deltaTime);

            // copie pour autoriser la suppression du personnage courant dans Brain() sans causer d'exception
            foreach (var character in characters.ToArray())
            {
                character.Update(deltaTime);
            }
        }

        private void HandleDay(float deltaTime)
        {
            switch (Phase)
            {
                case DayPhase.Running:
                    //lock de l'horloge pour les questions 1 à 4
                    if (CurrentQuestion == 0 || Hospital.CurrentQuestion >= 5)
                        elapsedTimeForToday += deltaTime;

                    if (elapsedTimeForToday >= dayConfig!.DayDuration)
                    {
                        elapsedTimeForToday = dayConfig.DayDuration;
                        Phase = DayPhase.Closing;
                    }
                    else
                    {

                        if (dayConfig != null)
                        {
                            if (ElapsedTimeSinceStart >= nextArrivalTime)
                            {
                                SpawnPatient();
                                nextArrivalTime = ElapsedTimeSinceStart + dayConfig.PatientInterval.Sample();
                            }
                        }

                    }
                    break;

                case DayPhase.Closing:
                    if (employeesInBus.Count == EmployeeCount && DayStats.Active == 0)
                    {
                        Phase = DayPhase.BusLeaving;
                        transitionTimer = 0f;
                    }
                    break;

                case DayPhase.BusLeaving:
                    transitionTimer += deltaTime;
                    if (transitionTimer >= dayConfig!.DayTransitionDelay)
                    {
                        foreach (Employee employee in employeesInBus)
                        {
                            employee.OnBusDeparted();
                        }
                        employeesInBus.Clear();
                        Phase = DayPhase.Finished;
                    }
                    break;

                case DayPhase.Finished:
                    DayConfig newday = new DayConfig();
                    newday.RandomizeOpenServices();
                    StartDay(newday);
                    break;
			}
        }

        public List<Sprite> GetRenderSprites()
        {

            var list = new List<Sprite>(FurnituresSprites);
            foreach (var character in characters)
            {
                character.GetRenderSprites(list);
            }
            return list;
        }

        private void InitializeFurniture()
        {
            FurnituresSprites = new List<Sprite>();
            foreach (SpriteConfig config in HospitalLayout.Furniture)
            {
                Sprite sprite = new Sprite(
                    TextureManager.Get(config.Texture),
                    config.Source,
                    config.Position,
                    config.Anchor
                );

                sprite.Layer = config.Layer;
                sprite.Scale = config.Scale;

                FurnituresSprites.Add(sprite);
            }
        }

        private DayConfig? dayConfig;
        private float nextArrivalTime;
        private float elapsedTimeForToday;
        private float transitionTimer;

        public Room? ChooseValidRoom(bool wantsSpecialist)
        {
            Room? assigned = null;
            Room[] specialists = Rooms.Where(r =>
                r.IsOpen && r.Staff != null && (r.Config.Type != RoomType.Generalist) == wantsSpecialist
            ).ToArray();

            if (specialists.Length > 0)
                assigned = specialists[Random.Shared.Next(specialists.Length)];
            return assigned;
        }

        public void StartDay(DayConfig config)
        {
            if (characters.Count != 0)
                throw new Exception("Des personnages sont encore presents");
            if (DayNumber > 0 && freeSeats.Count != HospitalLayout.WaitingSeats.Length)
                throw new Exception("Certains sièges n'ont pas été libérés la veille");
            if(employeesInBus.Count > 0)
                throw new Exception("Le bus n'est pas vide en début de journée");
            if (DayNumber > 0 && DayStats.Arrived != DayStats.Treated + DayStats.Untreated)
                throw new Exception("Statistiques de la veille incohérentes");

            dayConfig = config;

            DayNumber++;
            DayStats = new HospitalStats();

            elapsedTimeForToday = 0f;
            transitionTimer = 0f;
            nextArrivalTime = ElapsedTimeSinceStart + config.PatientInterval.Sample();
            EmployeeCount = 0;


            freeSeats.Clear();
            freeSeats.AddRange(HospitalLayout.WaitingSeats);

            // Déterminer les salles ouvertes.
            int generalistIndex = 0;
            foreach (Room room in Rooms)
            {
                if(room.Staff != null || room.Occupant != null)
                {
                    throw new Exception("Certaines salles n'ont pas été libérées la veille");
                }
                room.Staff = null;
                room.ReleasePatient(null);

                switch (room.Config.Type)
                {
                    case RoomType.Generalist:
                        room.IsOpen = generalistIndex < config.GeneralistCount;
                        generalistIndex++;
                        break;

                    case RoomType.Radiology:
                        room.IsOpen = config.RadiologyOpen;
                        break;

                    case RoomType.Psychiatry:
                        room.IsOpen = config.PsychiatryOpen;
                        break;
                }
            }

            // Créer les réceptionistes.
            for (int i = 0; i < config.ReceptionistCount; i++)
            {

                Reception reception = receptions[i];
                if (reception.Staff != null || reception.Occupant != null)
                {
                    throw new Exception("Certains accueils n'ont pas été libérées la veille");
                }
                reception.Staff = null;
                reception.ReleasePatient(null);

                Employee employee = new(
                    CharacterConfig.ReceptionitsSpriteIds[Random.Shared.Next(CharacterConfig.ReceptionitsSpriteIds.Length)],
                    reception,
                    this,
                    dayConfig.ReceptionDuration
                );

                reception.Staff = employee;
                employee.PlaceAt(reception.StaffSlot);
                characters.Add(employee);
                ++EmployeeCount;
            }

            // Créer un médecin par salle ouverte.
            foreach (Room room in Rooms)
            {
                if (!room.IsOpen)
                    continue;
                TimeRange duration = dayConfig.GeneralistDuration;
                switch(room.Config.Type)
                {
                    case RoomType.Generalist:
                        duration = dayConfig.GeneralistDuration;
                        break;
                    case RoomType.Psychiatry:
                        duration = dayConfig.SpecialistDuration;
                        break;
                    case RoomType.Radiology:
                        duration = dayConfig.SpecialistDuration;
                        break;
                }

                Employee employee = new(
                    CharacterConfig.DoctorSpriteIds[Random.Shared.Next(CharacterConfig.DoctorSpriteIds.Length)],
                    room,
                    this,
                    duration
                );

                room.Staff = employee;
                characters.Add(employee);
                ++EmployeeCount;
            }

            Phase = DayPhase.Running;
        }

        private void SpawnPatient()
        {
            DayStats.Arrived++;
            TotalStats.Arrived++;
            DayStats.Active++;
            TotalStats.Active++;

            Patient patient = new(
                CharacterConfig.PatientSpriteIds[Random.Shared.Next(CharacterConfig.PatientSpriteIds.Length)],
                this,
                dayConfig!.PatientPatience.Sample());

            patient.PlaceAt(NavNode.Outside);

            characters.Add(patient);

        }

        public Slot TakeFreeSeat()
        {
            Slot seat;
            if (freeSeats.Count == 0)
            {
                //on renvoie une place debout non réservée. Pour le moment les persos vont stack dessus, pas grave c'est un TP pas un vrai jeu...
                seat = HospitalLayout.StandingWaitingSlots[Random.Shared.Next(HospitalLayout.StandingWaitingSlots.Length)];
            }else
            {
                seat = freeSeats[0];
                Chaos.ExecuteIfEnabled();
                freeSeats.RemoveAt(0);
            }


            return seat;
        }

        public void ReleaseSeat(Slot seat)
        {
            //un peu hacky comme check mais ça passe avec la version actuelle (place assisse = réservée)
            if(seat.Pose != CharacterAction.Sitting)
            {
                return;
            }
            if (freeSeats.Contains(seat))
            {
                throw new Exception("Un siège vient d'être libéré une 2e fois");
            }
            Chaos.ExecuteIfEnabled();
            freeSeats.Add(seat);
        }

        public void RegisterEmployeeBoarding(Employee employee)
        {
            if (!characters.Remove(employee))
            {
                throw new Exception("Enregistrement de départ d'un employé non présent dans l'hopital");
            }
            if (employeesInBus.Contains(employee))
            {
                throw new Exception("Employé est déjà dans le bus");
            }
            employeesInBus.Add(employee);
            
        }

        public void RegisterPatientDeparture(Patient patient, bool treated)
        {
            if (!characters.Remove(patient))
            {
                throw new Exception("Enregistrement de départ d'un patient non présent dans l'hopital");
            }

            DayStats.Active--;
            TotalStats.Active--;
            if (treated)
            {
                DayStats.Treated++;
                TotalStats.Treated++;
            }
            else
            {
                DayStats.Untreated++;
                TotalStats.Untreated++;
            }
            DayStats.UpdateScore(treated);
            TotalStats.UpdateScore(treated);
        }

        public Reception? TryGetReception()
        {
            foreach (Reception candidate in receptions)
            {
                if (candidate.IsOpen && candidate.Occupant == null && candidate.Staff != null && candidate.Staff.IsAvailable)
                    return candidate;
            }
            return null;
        }
    }
}