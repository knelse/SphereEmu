meta:
  id: pickup_item_request
  file-extension: pickup_item_request
  endian: be
  bit-endian: le
seq:
  - id: header
    type: client_header
  - id: skip_13_bit
    type: b13
  - id: item_id
    type: b16
  - id: zeroes
    type: b19
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
