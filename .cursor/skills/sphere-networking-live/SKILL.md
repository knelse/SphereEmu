---
name: sphere-networking-live
description: Keep a deep understanding of packet structures when working on Sphere networking and packet classification for the emulator and PacketLogViewer. Always consult MBC decompile and client recompile as ground truth before hypothesizing wire layouts. Always prefer inferred semantic names over MBC routine names for display. Use when classifying packets, decoding MBC, matching client receive behavior, editing PacketLogViewer overlays, or recreating the network protocol 1-to-1.
---

# sphere-networking-live

When working on networking and packet classification for both emulator itself and PacketLogViewer, you need to keep a deep understanding of packet structures.

## Ground truth before hypotheses (hard rule)

If you ever need to analyze a behavior for the network protocol, **first** look into MBC decompile and client recompile before making hypotheses.

1. Open the real `.mbc` / `decompile_result/<module>.txt` and the matching client receive/send path (`semantic_classes.cpp` `skipRegion` / `receiveRegion` / `readField` / `send`).
2. Treat region `formats`, handler bytecode, and client bit widths as authoritative.
3. Only after that, use captures / PacketLogViewer / inferred jsonl to confirm wire samples.
4. Do **not** invent adaptive probes, dual-width guessers, or "maybe u8 on live wire" logic when the module formats and client already define the layout.
5. If a capture seems to disagree with ground truth, debug the decode framing (wrong region, wrong start bit, missing origin, wrong module tag). Do not "fix" the catalog by guessing.

## Inferred names over MBC routines (hard rule)

**Always** use inferred semantic names for PLV list / banners / `EventName` / `RecoveredName`. Never use MBC routine or receive-site names as the primary label.

| Prefer (inferred) | Never primary (routine / site) |
|-------------------|--------------------------------|
| `SetStat`, `ApplyDamageOrHp`, `WorldSnapshot`, `PositionStream` | `CheckPing`, `ContMan`, `TradeMan`, `Owner`, `Manager`, `NManager`, `CycleSend`, `Resstop`, `RcvInfo`, `Image`, `regionN` |

- Inferred names come from `Sphere.NetworkProtocol/mbc_commands.jsonl` (`n`), recovered catalog overrides, and field-analysis renames.
- Routine names are **aliases / evidence / lookup keys** only (`handler`, `receive_callers`, `orig`).
- If jsonl has no `n` yet, name from wire meaning while you dig; do not fall back to the coroutine that drains the region.

Use:

1. MBC decompiler toolkit at `d:\Download\MBCdecompiler-main\`
2. Client recompile at `d:\Download\SferaClientRecompiled-main\`
3. Inferred commands for all mbc modules with descriptions:
   - `Sphere.NetworkProtocol/mbc_commands.jsonl` (one command per line)
   - `Sphere.NetworkProtocol/mbc_commands_index.txt` (tag, module, jsonl line, count)
   - `Sphere.NetworkProtocol/mbc_commands_by_module.html` (human browse)

We want to build a comprehensive picture of what happens, what changes and what it will affect on the client. We will use that to build a 1-to-1 recreation of the network protocol, so every nuance is important.

## Paths missing

Before networking work, confirm the decompiler and recompile directories exist.

If those paths are not available and the user is asking you to work on networking, ask them if they want to ignore the skill or provide new paths. Do not invent substitute trees.

## Inferred command lookup

- Index: `Sphere.NetworkProtocol/mbc_commands_index.txt`
- Then Read the jsonl at that line with that count
- Grep `"t":210,` or `"n":"TransformUpdate"` in `Sphere.NetworkProtocol/mbc_commands.jsonl`

## Field analysis standard (required)

This is the base level of analysis detail we want. For every field, you should do a similar inference and adjust their name / contents processing (like hp delta is -28 instead of 29972).

Do not stop at wire type + offset. For each named field in a command under work:

1. Trace MBC receive path (`decompile_result/<module>.txt` ContMan/TradeMan/etc.) and client recompile if native code touches it.
2. Infer what the value *means* on the client (storage slot, clamp, UI, popup, spawn sync), not only how many bits it occupies.
3. Rename the field to that meaning (`current_hp`, `max_hp`, `hp_delta` signed damage, `killer_id`, …). Prefer names that match MBC stores when clear (`g_rec_0468.i0` / `.i1`).
4. Apply contents processing so the viewer/emulator show semantic values: `SignedMinus30000` → `raw - 30000` (−28, not 29972); MBC varint (`g`) → signed magnitude after 1-bit sign + 2-bit width (362, not the bit-span integer); enums; object-id hex; etc. **Part list and event banner must show the same parsed value.**
5. Note length variants and ignored trailing bytes (`rest`) when the handler only reads a prefix.
6. Report (and then encode in layouts/decoders) length-discriminated shapes in one place, e.g. `ApplyDamageOrHp`: len5 = killer + delta; len6 = absolute `current_hp` + `max_hp`.

Example bar (monster.ApplyDamageOrHp): relative hit queues ShowKill from `hp_delta`; absolute `current_hp`/`max_hp` write `g_rec_0468.i0`/`.i1` and drive the HP bar; native recompile has no separate ContMan HP logic (MBC VM owns it). Known renames: `hp`→`current_hp`, `hp2`→`max_hp`.

## PacketLogViewer MBC field display

- Event comment banners on each section's `process_id` (header + each 0x3F switch), not one joined banner for the whole packet. Full `DisplayName` including summary (`monster.ApplyDamageOrHp killer_id=0x36EB hp_delta=-28`; `server.entity.position B111 -1706.61 1500.791 100.565 187`). Not on every field.
- `process_id` display/summary is always hex `X4` (e.g. `B111`), never decimal.
- Named commands: `ApplyDamageOrHp = 13 (0xD)` (parsed/name first, then wire).
- `SignedMinus30000` / `Hp16` fields (`hp_delta`, clan coords, marks, etc.): wire is `raw`, meaning is `raw - 30000`. Show `parsed = 0xRAW (raw)`, e.g. `-142 = 0x74A2 (29858)`. Kind on decoded fields is `signed_m30000`.
- **Part preview = banner value.** Whenever decode has a semantic `IntValue` / `DoubleValue` (varint sign+tier, `signed_m30000`, world coords, floats), the part list primary must show that value. Never show the raw bit-span integer as primary when it differs (e.g. SetStat agility `362`, not wire `2900`). Raw only as secondary.
- Prefer MBC decode for msg300 before classic `08 C0` / identity fallbacks. `_player` region 10 wire is **SetStat** (`u6` index + varint → `g_rec_0C44[i]`). Drained inside `CheckPing` (alias only).
- `_player` region 11 **PositionStream**: schema `array4<u32>`; CycleSend sends count=4 of IEEE floats from `g_rec_0004` (`x,y,z,angle`/yaw). Absolute world floats (not `encodeCoordinate`). Receive only resets connection-loss timer.
- Do **not** block MBC on `LooksLikeClassicServerItemSpawn`. Guild/map SpawnData bits at offset 56 often look like `Special_Guild` + `FULL_SPAWN` (0x7C). Require real MBC Events/Lifecycle; put the classic-item gate on identity fallback only.
- ContMan region formats are **per module** from the current client `.mbc` (`d:\Games\Sfera_std\mbc\`). Examples: `_player` ContMan is `[4, 102, 8]` (u4); `monster` ContMan is `[8, 102, 8]` (u8). Do not globalize one width. Keep `sfera_protocol_schema.json` in sync with those `.mbc` region defs. Do not probe widths.
- `guild` / `st_map` ContMan cmd9: len6 = name; len5 = `blk_type`+`blk_id` (MBC `getBLkType`/`getBLkID`); else u32. Not `_player` killer/hp.
- Item-like region 61 (EInit SpawnData): `x,y,z` IEEE floats (inventory often `x=1e6`), `angle`, `contain_state` (`g_01A8`: 2=in container), `current_durability`/`max_durability` (MBC `SetHealth`/`GetHealth` on `g_rec_0464`; SphGameObject durability, no separate MBC durability path), `game_id` (`g_0440` / SetItemGroup, resolve name from objectLocalization), `suffix_id` (`g_0444`, &lt;0 = none). ContMan cmd0 = `container_id` hex X4 (PutHere parent process).
- Wire 7-bit markers: `0` TERM, `0x3F` NewProcessOrModule, else region = wire-1 (`0x3E` = SpawnData, `0x05` = NextCommand). Show region type, not the raw wire.
- When the module tag has no recovered row, still prefer an inferred shared command name (e.g. `ApplyDamageOrHp`) over the handler string `ContMan`.
- Field names and kinds must reflect the Field analysis standard above; do not leave raw-only labels when meaning is known.
- ContMan/TradeMan wire after command: `array8`/`array4` length prefix is `payload_len_bytes` (byte count). Do not leave it as an unnamed blank when unpacking the array body.
- Msg300 `has_position` block (`origin_x/y/z`): wire = `trunc(world) + bias` with biases **32768 / 1200 / 32768** (not `32768 - z`). Decode = wire − bias. Client: `semantic_classes.cpp` receiveEvents / encode origin.
- Region 1 TransformUpdate (`relX12,relY12,relZ12,angle8`): 12-bit nonlinear delta from that origin (`encodeCoordinate` / `decodeCoordinate`), range &lt; 120 units; bit15 = negative. Absolute world = origin + delta. Keep `angle` as raw u8 (do not show degrees). Banner per mob section: `server.entity.position {process_id:X4} newX newY newZ newAngle`. Show world primary / raw secondary for x/y/z.