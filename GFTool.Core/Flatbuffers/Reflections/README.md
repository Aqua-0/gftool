This folder contains FlatBuffers reflection assets used for flatc-like JSON viewing/editing.

What we ship
- `.bfbs` (reflection schema binaries) stored as base64 text resources under `GFTool.Core/Flatbuffers/Reflections/Bfbs/PokeDocs/*/*.bfbs.b64`.
- At runtime, the app loads the base64 resources and uses the reflection schema to convert FlatBuffer binaries to JSON.

Why no `.fbs` here?
- We intentionally avoid vendoring upstream schema text into the tool; only derived reflection blobs are embedded.

Regenerating `.bfbs.b64` (developer workflow)
- Requires `flatc` on PATH and a local checkout of the PokeDocs schemas.
- Example (SV `trmdl`):
  - `flatc --binary --schema -o /tmp/out /path/to/PokeDocs/SV/Flatbuffers/model/trmdl.fbs`
  - `base64 -w0 /tmp/out/trmdl.bfbs > GFTool.Core/Flatbuffers/Reflections/Bfbs/PokeDocs/SV/model/trmdl.bfbs.b64`
