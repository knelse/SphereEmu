namespace Sphere.Common.Models
{
    public class Doll
    {
        public Slot Slot1 { get; set; } = new();

        public Slot Slot2 { get; set; } = new();

        public Slot Slot3 { get; set; } = new();

        public Slot Slot4 { get; set; } = new();

        public Slot Slot5 { get; set; } = new();

        public Slot Slot6 { get; set; } = new();

        public Slot Slot7 { get; set; } = new();

        public Slot Slot8 { get; set; } = new();

        public Slot Slot9 { get; set; } = new();

        public Slot Slot10 { get; set; } = new();

        public Slot BackPack { get; set; } = new();

        public Slot Key1 { get; set; } = new();

        public Slot Key2 { get; set; } = new();

        public Slot Mission { get; set; } = new();

        public Slot Helmet { get; set; } = new();

        public Slot Chest { get; set; } = new();

        public Slot Gloves { get; set; } = new();

        public Slot Belt { get; set; } = new();

        public Slot Pants { get; set; } = new();

        public Slot Boots { get; set; } = new();

        public Slot RightHand { get; set; } = new();

        public Slot LeftHand { get; set; } = new();

        public Slot Ring1 { get; set; } = new();

        public Slot Ring2 { get; set; } = new();

        public Slot Ring3 { get; set; } = new();

        public Slot Ring4 { get; set; } = new();

        public Slot Amulet { get; set; } = new();

        public Slot BracerLeft { get; set; } = new();

        public Slot BracerRight { get; set; } = new();

        public Slot Inkpot { get; set; } = new();

        public Slot Guild { get; set; } = new();

        public Slot MapBook { get; set; } = new();

        public Slot MantraBook { get; set; } = new();

        public Slot RecipeBook { get; set; } = new();

        public Slot Money { get; set; } = new();

        public Slot Special1 { get; set; } = new();

        public Slot Special2 { get; set; } = new();

        public Slot Special3 { get; set; } = new();

        public Slot Special4 { get; set; } = new();

        public Slot Special5 { get; set; } = new();

        public Slot Special6 { get; set; } = new();

        public Slot Special7 { get; set; } = new();

        public Slot Special8 { get; set; } = new();

        public Slot Special9 { get; set; } = new();

        public Slot Ammo { get; set; } = new();

        public Slot SpeedHack { get; set; } = new();

        public byte[] ToBytes()
        {
            return [
                Helmet.FullOrEmpty,         0x00,
                Amulet.FullOrEmpty,         0x00,
                LeftHand.FullOrEmpty,       0x00,
                Chest.FullOrEmpty,          0x00,
                Gloves.FullOrEmpty,         0x00,
                Belt.FullOrEmpty,           0x00,
                BracerLeft.FullOrEmpty,     0x00,
                BracerRight.FullOrEmpty,    0x00,
                Ring1.FullOrEmpty,          0x00,
                Ring2.FullOrEmpty,          0x00,
                Ring3.FullOrEmpty,          0x00,
                Ring4.FullOrEmpty,          0x00,
                Pants.FullOrEmpty,          0x00,
                Boots.FullOrEmpty,          0x00,
                Guild.FullOrEmpty,          0x00,
                MapBook.FullOrEmpty,        0x00,
                RecipeBook.FullOrEmpty,     0x00,
                MantraBook.FullOrEmpty,     0x00,
                0x00, 0x00, 0x00, 0x00,
                Inkpot.FullOrEmpty,         0x00,
                Money.FullOrEmpty,          0x00,
                BackPack.FullOrEmpty,       0x00,
                Key1.FullOrEmpty,           0x00,
                Key2.FullOrEmpty,           0x00,
                Mission.FullOrEmpty,        0x00,
                Slot1.FullOrEmpty,          0x00,
                Slot2.FullOrEmpty,          0x00,
                Slot3.FullOrEmpty,          0x00,
                Slot4.FullOrEmpty,          0x00,
                Slot5.FullOrEmpty,          0x00,
                Slot6.FullOrEmpty,          0x00,
                Slot7.FullOrEmpty,          0x00,
                Slot8.FullOrEmpty,          0x00,
                Slot9.FullOrEmpty,          0x00,
                Slot10.FullOrEmpty,         0x00,
                0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
                Special1.FullOrEmpty,       0x00,
                Special2.FullOrEmpty,       0x00,
                Special3.FullOrEmpty,       0x00,
                Special4.FullOrEmpty,       0x00,
                Special5.FullOrEmpty,       0x00,
                Special6.FullOrEmpty,       0x00,
                Special7.FullOrEmpty,       0x00,
                Special8.FullOrEmpty,       0x00,
                Special9.FullOrEmpty,       0x00,
                Ammo.FullOrEmpty,           0x00,
                SpeedHack.FullOrEmpty,      0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
            ];
        }
    }
}
