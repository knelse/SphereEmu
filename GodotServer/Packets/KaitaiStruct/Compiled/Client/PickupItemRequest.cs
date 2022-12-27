// This is a generated file! Please edit source .ksy file and use kaitai-struct-compiler to rebuild



namespace Kaitai
{
    public partial class PickupItemRequest : KaitaiStruct
    {
        public static PickupItemRequest FromFile(string fileName)
        {
            return new PickupItemRequest(new KaitaiStream(fileName));
        }

        public PickupItemRequest(KaitaiStream p__io, KaitaiStruct p__parent = null, PickupItemRequest p__root = null) : base(p__io)
        {
            m_parent = p__parent;
            m_root = p__root ?? this;
            _read();
        }
        private void _read()
        {
            _header = new ClientHeader(m_io, this, m_root);
            _skip13Bit = m_io.ReadBitsIntLe(13);
            _itemId = m_io.ReadBitsIntLe(16);
            _zeroes = m_io.ReadBitsIntLe(19);
        }
        public partial class PacketLength : KaitaiStruct
        {
            public static PacketLength FromFile(string fileName)
            {
                return new PacketLength(new KaitaiStream(fileName));
            }

            public PacketLength(KaitaiStream p__io, PickupItemRequest.ClientHeader p__parent = null, PickupItemRequest p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _length = m_io.ReadU2be();
            }
            private ushort _length;
            private PickupItemRequest m_root;
            private PickupItemRequest.ClientHeader m_parent;
            public ushort Length { get { return _length; } }
            public PickupItemRequest M_Root { get { return m_root; } }
            public PickupItemRequest.ClientHeader M_Parent { get { return m_parent; } }
        }
        public partial class ClientHeader : KaitaiStruct
        {
            public static ClientHeader FromFile(string fileName)
            {
                return new ClientHeader(new KaitaiStream(fileName));
            }

            public ClientHeader(KaitaiStream p__io, PickupItemRequest p__parent = null, PickupItemRequest p__root = null) : base(p__io)
            {
                m_parent = p__parent;
                m_root = p__root;
                _read();
            }
            private void _read()
            {
                _length = new PacketLength(m_io, this, m_root);
                _sync1 = m_io.ReadU4be();
                _okMarker = new OkMarker(m_io, this, m_root);
                _sync2 = m_io.ReadBitsIntLe(24);
                m_io.AlignToByte();
                _clientId = m_io.ReadU2be();
                _packetType = m_io.ReadBitsIntLe(24);
            }
            private PacketLength _length;
            private uint _sync1;
            private OkMarker _okMarker;
            private ulong _sync2;
            private ushort _clientId;
            private ulong _packetType;
            private PickupItemRequest m_root;
            private PickupItemRequest m_parent;
            public PacketLength Length { get { return _length; } }
            public uint Sync1 { get { return _sync1; } }
            public OkMarker OkMarker { get { return _okMarker; } }
            public ulong Sync2 { get { return _sync2; } }
            public ushort ClientId { get { return _clientId; } }
            public ulong PacketType { get { return _packetType; } }
            public PickupItemRequest M_Root { get { return m_root; } }
            public PickupItemRequest M_Parent { get { return m_parent; } }
        }
        public partial class OkMarker : KaitaiStruct
        {
            public static OkMarker FromFile(string fileName)
            {
                return new OkMarker(new KaitaiStream(fileName));
            }

            public OkMarker(KaitaiStream p__io, PickupItemRequest.ClientHeader p__parent = null, PickupItemRequest p__root = null) : base(p__io)
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
            private PickupItemRequest m_root;
            private PickupItemRequest.ClientHeader m_parent;
            public byte Ok2c { get { return _ok2c; } }
            public byte Ok01 { get { return _ok01; } }
            public PickupItemRequest M_Root { get { return m_root; } }
            public PickupItemRequest.ClientHeader M_Parent { get { return m_parent; } }
        }
        private ClientHeader _header;
        private ulong _skip13Bit;
        private ulong _itemId;
        private ulong _zeroes;
        private PickupItemRequest m_root;
        private KaitaiStruct m_parent;
        public ClientHeader Header { get { return _header; } }
        public ulong Skip13Bit { get { return _skip13Bit; } }
        public ulong ItemId { get { return _itemId; } }
        public ulong Zeroes { get { return _zeroes; } }
        public PickupItemRequest M_Root { get { return m_root; } }
        public KaitaiStruct M_Parent { get { return m_parent; } }
    }
}
