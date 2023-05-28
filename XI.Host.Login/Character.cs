namespace XI.Host.Login
{
    internal class Character
    {
        public byte Race { get; private set; }
        public byte MainJob { get; private set; }
        public byte Nation { get; private set; }
        public ushort Size { get; private set; }
        public byte Face { get; private set; }
        public ushort Zone { get; set; } // pos_zone

        public bool IsValid { get; private set; }

        public Character(in byte race, in byte mainJob, in byte nation, in ushort size, in byte face)
        {
            Race = race; // error if not 1-8
            MainJob = mainJob; // error if not 1-6
            Nation = nation; // error if not 0-2
            Size = size; // error if not 0-2
            Face = face; // error if not 0-15

            IsValid = true;

            if (Race < 1 || Race > 8)
            {
                IsValid = false;
            }

            if (MainJob < 1 || MainJob > 6)
            {
                IsValid = false;
            }

            if (Nation < 0 || Nation > 2)
            {
                IsValid = false;
            }

            if (Size < 0 || Size > 2)
            {
                IsValid = false;
            }

            if (Face < 0 || Face > 15)
            {
                IsValid = false;
            }
        }
    }
}
