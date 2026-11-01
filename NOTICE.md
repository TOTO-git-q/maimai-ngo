# NOTICE

Private collaboration repository. Do not publish or redistribute it, its releases or its contents.
No public licence is granted.

## Third-party rights in this repository

- **maimai / maimai DX** is a trademark of SEGA. The game and its assets are (c) SEGA.
  This repository contains **no game files**, but `patch\` and `art\` contain material derived from SEGA's
  textures (recolours, pixelations, composites, re-cut sprites) and the field values we changed in the game's own
  data files. It is only meant for people who run their own copy of the game. All rights in the original assets
  remain with SEGA.
- **NEEDY GIRL OVERDOSE** (characters Ame / KAngel, Windose, JINE and the NGO logo) is (c) WSS playground /
  NEEDY GIRL PROJECT. The NGO-themed art here is unofficial fan work made for this skin; the title logo is the
  original NGO logo. All rights in NGO remain with their owners.
- `src\Framework\` is a C# port of the **Live2D Cubism Native Framework** and stays under the
  Live2D Open Software License. **Live2D Cubism Core** (`Live2DCubismCore.dll`) and Live2D sample models are not
  included (proprietary licence); get the Core from the Cubism SDK for Native yourself.
- The pixel font written into the game's font asset is **PixelMplus12** (itouhiro), under the M+ FONT LICENSE:
  "These fonts are free software. Unlimited permission is granted to use, copy, and distribute them, with or
  without modification, either commercially or noncommercially. THESE FONTS ARE PROVIDED "AS IS" WITHOUT WARRANTY."
- The release patcher (`ngo_patcher.exe`) bundles Python 3.12 (PSF licence) and open-source libraries under their
  own licences: UnityPy (MIT), TypeTreeGeneratorAPI (MIT) with capstone (BSD), Pillow (MIT-CMU), texture2ddecoder
  (MIT), etcpak (BSD-3-Clause), astc-encoder-py (Apache-2.0 / MIT), lz4 (BSD-3-Clause), brotli (MIT), attrs (MIT),
  fsspec (BSD-3-Clause). Their licence files are in `ngo_patcher\_internal\*.dist-info`.
- Not included and never to be committed: MelonLoader, AquaMai, MuMod, segatools / hinata binaries, the game's
  DLLs, decompiled game code.

## Our own code

The mod, tools, MCP server and patcher were written for this project by its members. Use inside the group only.
