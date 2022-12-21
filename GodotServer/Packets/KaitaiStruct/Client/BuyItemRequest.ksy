meta:
  id: buy_item_request
  endian: le
  bit-endian: le
seq:
  - id: header
    type: client_header
  - id: skip_13_bit
    type: b13
  - id: vendor_id
    type: b16
  - id: zeroes_2
    type: b8
  - id: slot_id
    type: b8
  - id: zeroes_3
    type: b16
  - id: cost_per_one
    type: b32
  - id: quantity
    type: b32
types:
  packet_length:
    seq:
      - id: length
        type: u2
  client_header:
    seq:
      - id: length
        type: packet_length
      - id: sync_1
        type: u4
      - id: ok_marker
        type: ok_marker
      - id: sync_2
        type: b24
      - id: client_id
        type: u2
      - id: packet_type
        type: b24
  ok_marker:
    seq:
      - id: ok_2c
        type: u1
      - id: ok_01
        type: u1