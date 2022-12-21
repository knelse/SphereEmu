// This is a generated file! Please edit source .ksy file and use kaitai-struct-compiler to rebuild



namespace Kaitai
{
    public partial class BuyItemRequest : KaitaiStruct
    {
        public static BuyItemRequest FromFile(string fileName)
        {
            return new BuyItemRequest(new KaitaiStream(fileName));
        }

        public BuyItemRequest(KaitaiStream p__io, KaitaiStruct p__parent = null, BuyItemRequest p__root = null) : base(p__io)
        {
            m_parent = p__parent;
            m_root = p__root ?? this;
            _read();
        }
        private void _read()
        {
            _header = new ClientHeader(m_io, this, m_root);
            _skip13Bit = m_io.ReadBitsIntLe(13);
            _vendorId = m_io.ReadBitsIntLe(16);
            _zeroes2 = m_io.ReadBitsIntLe(8);
            _slotId = m_io.ReadBitsIntLe(8);
            _zeroes3 = m_io.ReadBitsIntLe(16);
            _costPerOne = m_io.ReadBitsIntLe(32);
            _quantity = m_io.ReadBitsIntLe(32);
        }
        public partial class PacketLength : KaitaiStruct
        {
            public static PacketLength FromFile(string fileName)
            {
                return new PacketLength(new KaitaiStream(fileName));
            }

            public PacketLength(KaitaiStream p__io, BuyItemRequest.ClientHeader p__parent = null, BuyItemRequest p__root = null) : base(p__io)
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
            private BuyItemRequest m_root;
            private BuyItemRequest.ClientHeader m_parent;
            public ushort Length { get { return _length; } }
            public BuyItemRequest M_Root { get { return m_root; } }
            public BuyItemRequest.ClientHeader M_Parent { get { return m_parent; } }
        }
        public partial class ClientHeader : KaitaiStruct
        {
            public static ClientHeader FromFile(string fileName)
            {
                return new ClientHeader(new KaitaiStream(fileName));
            }

            public ClientHeader(KaitaiStream p__io, BuyItemRequest p__parent = null, BuyItemRequest p__root = null) : base(p__io)
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
            private BuyItemRequest m_root;
            private BuyItemRequest m_parent;
            public PacketLength Length { get { return _length; } }
            public uint Sync1 { get { return _sync1; } }
            public OkMarker OkMarker { get { return _okMarker; } }
            public ulong Sync2 { get { return _sync2; } }
            public ushort ClientId { get { return _clientId; } }
            public ulong PacketType { get { return _packetType; } }
            public BuyItemRequest M_Root { get { return m_root; } }
            public BuyItemRequest M_Parent { get { return m_parent; } }
        }
        public partial class OkMarker : KaitaiStruct
        {
            public static OkMarker FromFile(string fileName)
            {
                return new OkMarker(new KaitaiStream(fileName));
            }

            public OkMarker(KaitaiStream p__io, BuyItemRequest.ClientHeader p__parent = null, BuyItemRequest p__root = null) : base(p__io)
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
            private BuyItemRequest m_root;
            private BuyItemRequest.ClientHeader m_parent;
            public byte Ok2c { get { return _ok2c; } }
            public byte Ok01 { get { return _ok01; } }
            public BuyItemRequest M_Root { get { return m_root; } }
            public BuyItemRequest.ClientHeader M_Parent { get { return m_parent; } }
        }
        private ClientHeader _header;
        private ulong _skip13Bit;
        private ulong _vendorId;
        private ulong _zeroes2;
        private ulong _slotId;
        private ulong _zeroes3;
        private ulong _costPerOne;
        private ulong _quantity;
        private BuyItemRequest m_root;
        private KaitaiStruct m_parent;
        public ClientHeader Header { get { return _header; } }
        public ulong Skip13Bit { get { return _skip13Bit; } }
        public ulong VendorId { get { return _vendorId; } }
        public ulong Zeroes2 { get { return _zeroes2; } }
        public ulong SlotId { get { return _slotId; } }
        public ulong Zeroes3 { get { return _zeroes3; } }
        public ulong CostPerOne { get { return _costPerOne; } }
        public ulong Quantity { get { return _quantity; } }
        public BuyItemRequest M_Root { get { return m_root; } }
        public KaitaiStruct M_Parent { get { return m_parent; } }
    }
}
