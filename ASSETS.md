# Asset Provenance

This repository ships a small set of original Gulu artwork, redistributed with the author's authorization under the repository's MIT License:

- `App.ico`, `AppIcon.png` — the Gulu cat application icon.
- `Interactions/*.png` — feeding, watering, travel, petting, and wardrobe icons.
- `Docking/*.png` — side-dock frames (rest/hover, eyes open/closed).
- `UI/paw-scroll.png` — scroll marker.
- `Diary/diary-paper-footer.png` — diary page footer illustration.
- `Memories/UI/memory-loading-companion-v1.png` — memory loading illustration.
- `Animations/idle_breathe/` — the single authorized Gulu animation clip (24 fps, 320x320, 121 frames), used as the packaged `normal`-state fallback behavior.

No other private Gulu artwork (memories, postcards, accessories, additional animations) is included; those feature catalogs load optional packs that forks may supply.

`tools/generate_sample_assets.py` is a placeholder generator for forks. It never overwrites existing files; it only recreates simple geometric stand-ins for missing icons and UI art so a fork can rebuild after replacing or deleting artwork.

Test fixtures under `tests/GuluPet.Tests/TestMedia` are tiny solid-color MP4 clips (~1.5 KB each) generated locally by `tools/generate_test_media.ps1`. They exist solely to exercise the playback-window code paths and contain no private media.

When adding replacement artwork, document its author, source, license, and any redistribution restrictions here before publishing it.
