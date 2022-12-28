// This is a generated file! Please edit source .ksy file and use kaitai-struct-compiler to rebuild



namespace Kaitai
{
    public partial class MainhandReequipPowderPowder : KaitaiStruct
    {
        public static MainhandReequipPowderPowder FromFile(string fileName)
        {
            return new MainhandReequipPowderPowder(new KaitaiStream(fileName));
        }


        public enum SlotState
        {
            Full = 0,
            Fists = 22,
            Empty = 255,
        }
        public MainhandReequipPowderPowder(KaitaiStream p__io, KaitaiStruct p__parent = null, MainhandReequipPowderPowder p__root = null) : base(p__io)
        {
            m_parent = p__parent;
            m_root = p__root ?? this;
            _read();
        }
        private void _read()
        {
            _header = new ClientHeader(m_io, this, m_root);
            _skip1 = m_io.ReadBitsIntLe(32);
            _skip2 = m_io.ReadBitsIntLe(28);
            _equipItemId = m_io.ReadBitsIntLe(16);
            _mainhandState = ((SlotState) m_io.ReadBitsIntLe(12));
        }
        public partial class PacketLength : KaitaiStruct
        {
            public static PacketLength FromFile(string fileName)
            {
                return new PacketLength(new KaitaiStream(fileName));
            }

            public PacketLength(KaitaiStream p__io, MainhandReequipPowderPowder.ClientHeader p__parent = null, MainhandReequipPowderPowder p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _length = m_io.ReadU2le();
            }
            private ushort _length;
            private MainhandReequipPowderPowder m_root;
            private MainhandReequipPowderPowder.ClientHeader m_parent;
            public ushort Length { get { return _length; } }
            public MainhandReequipPowderPowder M_Root { get { return m_root; } }
            public MainhandReequipPowderPowder.ClientHeader M_Parent { get { return m_parent; } }
        }
        public partial class ClientHeader : KaitaiStruct
        {
            public static ClientHeader FromFile(string fileName)
            {
                return new ClientHeader(new KaitaiStream(fileName));
            }

            public ClientHeader(KaitaiStream p__io, MainhandReequipPowderPowder p__parent = null, MainhandReequipPowderPowder p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _length = new PacketLength(m_io, this, m_root);
                _sync1 = m_io.ReadU4le();
                _okMarker = new OkMarker(m_io, this, m_root);
                _sync2 = m_io.ReadBitsIntLe(24);
                m_io.AlignToByte();
                _clientId = m_io.ReadU2le();
                _packetType = m_io.ReadBitsIntLe(24);
            }
            private PacketLength _length;
            private uint _sync1;
            private OkMarker _okMarker;
            private ulong _sync2;
            private ushort _clientId;
            private ulong _packetType;
            private MainhandReequipPowderPowder m_root;
            private MainhandReequipPowderPowder m_parent;
            public PacketLength Length { get { return _length; } }
            public uint Sync1 { get { return _sync1; } }
            public OkMarker OkMarker { get { return _okMarker; } }
            public ulong Sync2 { get { return _sync2; } }
            public ushort ClientId { get { return _clientId; } }
            public ulong PacketType { get { return _packetType; } }
            public MainhandReequipPowderPowder M_Root { get { return m_root; } }
            public MainhandReequipPowderPowder M_Parent { get { return m_parent; } }
        }
        public partial class OkMarker : KaitaiStruct
        {
            public static OkMarker FromFile(string fileName)
            {
                return new OkMarker(new KaitaiStream(fileName));
            }

            public OkMarker(KaitaiStream p__io, MainhandReequipPowderPowder.ClientHeader p__parent = null, MainhandReequipPowderPowder p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _ok2c = m_io.ReadU1();
                _ok01 = m_io.ReadU1();
            }
            private byte _ok2c;
            private byte _ok01;
            private MainhandReequipPowderPowder m_root;
            private MainhandReequipPowderPowder.ClientHeader m_parent;
            public byte Ok2c { get { return _ok2c; } }
            public byte Ok01 { get { return _ok01; } }
            public MainhandReequipPowderPowder M_Root { get { return m_root; } }
            public MainhandReequipPowderPowder.ClientHeader M_Parent { get { return m_parent; } }
        }
        private ClientHeader _header;
        private ulong _skip1;
        private ulong _skip2;
        private ulong _equipItemId;
        private SlotState _mainhandState;
        private MainhandReequipPowderPowder m_root;
        private KaitaiStruct m_parent;
        public ClientHeader Header { get { return _header; } }
        public ulong Skip1 { get { return _skip1; } }
        public ulong Skip2 { get { return _skip2; } }
        public ulong EquipItemId { get { return _equipItemId; } }
        public SlotState MainhandState { get { return _mainhandState; } }
        public MainhandReequipPowderPowder M_Root { get { return m_root; } }
        public KaitaiStruct M_Parent { get { return m_parent; } }
    }
}
