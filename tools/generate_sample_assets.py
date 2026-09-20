"""Placeholder artwork generator for forks.

The repository ships the authorized Gulu sample assets (icon, interaction
icons, docking frames, diary footer, loading illustration, and the
idle_breathe animation). This script never overwrites existing files; it
only fills in missing ones so a fork that deleted its artwork can still
build. Replace the placeholders with your own character art as described
in README.md.
"""

from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1] / "src" / "GuluPet" / "Assets"


def save_if_missing(image: Image.Image, path: Path, **kwargs) -> None:
    if path.exists():
        return
    image.save(path, **kwargs)


def icon_image(size: int = 256) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    margin = size // 8
    draw.rounded_rectangle(
        (margin, margin, size - margin, size - margin),
        radius=size // 5,
        fill=(111, 167, 154, 255),
        outline=(45, 76, 70, 255),
        width=max(2, size // 32),
    )
    draw.ellipse(
        (size * 0.32, size * 0.36, size * 0.42, size * 0.46),
        fill=(45, 76, 70, 255),
    )
    draw.ellipse(
        (size * 0.58, size * 0.36, size * 0.68, size * 0.46),
        fill=(45, 76, 70, 255),
    )
    draw.arc(
        (size * 0.38, size * 0.42, size * 0.62, size * 0.64),
        start=15,
        end=165,
        fill=(45, 76, 70, 255),
        width=max(2, size // 40),
    )
    return image


def dock_frame(side: str, hovered: bool, eyes_closed: bool) -> Image.Image:
    """Half-companion peeking from a screen edge, used for side docking.

    The pet silhouette is clipped to the left or right half of the frame so
    it reads as tucked against the screen edge. Hover raises the body slightly
    and brightens the fill; blink swaps open eyes for closed arcs.
    """
    size = 512
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    fill = (139, 195, 181, 255) if hovered else (111, 167, 154, 255)
    outline = (45, 76, 70, 255)
    lift = 14 if hovered else 0

    # Body: a large ellipse clipped to the docking side.
    body_top = 170 - lift
    if side == "left":
        body = (-140, body_top, 330, body_top + 300)
    else:
        body = (182, body_top, 652, body_top + 300)
    draw.ellipse(body, fill=fill, outline=outline, width=10)

    # Ears.
    ear_y = body_top + 30
    if side == "left":
        draw.polygon(
            [(150, ear_y), (185, ear_y - 95), (245, ear_y - 5)],
            fill=fill,
            outline=outline,
        )
        draw.polygon(
            [(255, ear_y - 5), (300, ear_y - 80), (330, ear_y + 15)],
            fill=fill,
            outline=outline,
        )
        eye_y = body_top + 105
        left_eye = (185, eye_y, 225, eye_y + 40)
        right_eye = (270, eye_y, 310, eye_y + 40)
    else:
        draw.polygon(
            [(185, ear_y + 15), (215, ear_y - 80), (260, ear_y - 5)],
            fill=fill,
            outline=outline,
        )
        draw.polygon(
            [(270, ear_y - 5), (330, ear_y - 95), (365, ear_y)],
            fill=fill,
            outline=outline,
        )
        eye_y = body_top + 105
        left_eye = (205, eye_y, 245, eye_y + 40)
        right_eye = (290, eye_y, 330, eye_y + 40)

    if eyes_closed:
        for eye in (left_eye, right_eye):
            draw.arc(eye, start=15, end=165, fill=outline, width=7)
    else:
        draw.ellipse(left_eye, fill=outline)
        draw.ellipse(right_eye, fill=outline)

    # Clip everything outside the docking half.
    mask = Image.new("L", (size, size), 0)
    mask_draw = ImageDraw.Draw(mask)
    if side == "left":
        mask_draw.rectangle((0, 0, size // 2, size), fill=255)
    else:
        mask_draw.rectangle((size // 2, 0, size, size), fill=255)
    image.putalpha(mask)
    return image


def main() -> None:
    interaction_dir = ROOT / "Interactions"
    ui_dir = ROOT / "UI"
    memory_ui_dir = ROOT / "Memories" / "UI"
    docking_dir = ROOT / "Docking"
    for directory in (
        interaction_dir,
        ui_dir,
        memory_ui_dir,
        docking_dir,
    ):
        directory.mkdir(parents=True, exist_ok=True)

    icon = icon_image()
    save_if_missing(icon, ROOT / "AppIcon.png", format="PNG")
    save_if_missing(
        icon,
        ROOT / "App.ico",
        format="ICO",
        sizes=[(16, 16), (32, 32), (48, 48), (256, 256)],
    )
    save_if_missing(
        icon.resize((96, 96), Image.Resampling.LANCZOS),
        ui_dir / "paw-scroll.png",
        format="PNG",
    )

    for name in ("feed", "water", "travel", "pet", "wardrobe"):
        save_if_missing(
            icon, interaction_dir / f"{name}.png", format="PNG")

    loading = Image.new("RGBA", (960, 640), (245, 248, 246, 255))
    loading.alpha_composite(icon.resize((256, 256)), (352, 192))
    save_if_missing(
        loading,
        memory_ui_dir / "memory-loading-companion-v1.png",
        format="PNG",
    )

    for side in ("left", "right"):
        for posture, hovered in (("rest", False), ("hover", True)):
            for eyes, closed in (("open", False), ("closed", True)):
                save_if_missing(
                    dock_frame(side, hovered, closed),
                    docking_dir / f"{side}_{posture}_{eyes}.png",
                    format="PNG",
                )


if __name__ == "__main__":
    main()
