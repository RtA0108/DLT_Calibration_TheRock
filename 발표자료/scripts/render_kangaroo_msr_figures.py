from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


REPO = Path(__file__).resolve().parents[2]
RESEARCH = Path(r"C:\Users\minsu\agentic_audio_projection_mapping")
RUN = RESEARCH / "outputs" / "pipeline_runs" / "kangaroo_msr_intrinsic_v45"
FIGURES = REPO / "발표자료" / "figures"

MESH = RUN / "inputs" / "working" / "Kangaroo_v1_L3.obj"
FIELDS = RUN / "artifacts" / "msr" / "semantic_shadow" / "msrg_refined_fields.npz"
FIELDS_MANIFEST = RUN / "artifacts" / "msr" / "semantic_shadow" / "msrg_refined_fields_manifest.json"
FACE_ID = RUN / "artifacts" / "neutral_views" / "view_a045_ez00" / "view_a045_ez00_face_id.png"
NEUTRAL = RUN / "artifacts" / "neutral_views" / "view_a045_ez00" / "view_a045_ez00_neutral_rgb.png"
ROLE_MANIFEST = RESEARCH / "outputs" / "evaluation" / "automatic_role_assignment_v57" / "kangaroo" / "manifest.json"

OUT_CONCEPT = FIGURES / "20_kangaroo_msr_concept.png"
OUT_MSRG = FIGURES / "21_kangaroo_msrg_roles.png"

EXPECTED_SHA256 = {
    MESH: "A121EA93D25500EB5D9233708D5AC4E1C4D2B17E288244D394F1017AF379274B",
    FIELDS: "C7F49EEDE290DF20EEB113412222A2195D6579C7717EE1D6832ECB026AE20EA0",
}

# Existing pipeline palette: scripts/pipeline_run_v1/stages.py::REGION_COLORS.
REGION_COLORS = np.asarray(
    [
        (57, 189, 248), (255, 112, 108), (116, 230, 142), (255, 196, 76),
        (184, 112, 255), (70, 220, 207), (255, 133, 192), (145, 160, 255),
        (238, 111, 76), (118, 203, 93), (243, 153, 61), (121, 114, 234),
        (91, 205, 214), (234, 103, 131), (174, 216, 84), (247, 176, 118),
    ],
    dtype=np.float32,
)

# Existing automatic-role palette: build_automatic_role_review_v57.py.
ROLE_COLORS = {
    "source": (48, 225, 255),
    "path": (66, 145, 255),
    "barrier": (255, 151, 62),
    "reservoir": (220, 78, 255),
    "modifier": (142, 255, 112),
    "sink": (111, 91, 235),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(4 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def validate_inputs() -> None:
    for path in (MESH, FIELDS, FIELDS_MANIFEST, FACE_ID, NEUTRAL, ROLE_MANIFEST):
        if not path.is_file():
            raise FileNotFoundError(path)
    for path, expected in EXPECTED_SHA256.items():
        actual = sha256(path)
        if actual != expected:
            raise RuntimeError(f"Input hash mismatch: {path}\nexpected={expected}\nactual={actual}")


def parse_obj(path: Path) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    vertices: list[list[float]] = []
    colors: list[list[float]] = []
    faces: list[list[int]] = []
    with path.open("r", encoding="utf-8", errors="ignore") as handle:
        for raw in handle:
            if raw.startswith("v "):
                values = [float(value) for value in raw.split()[1:]]
                vertices.append(values[:3])
                colors.append(values[3:6] if len(values) >= 6 else [0.72, 0.72, 0.72])
            elif raw.startswith("f "):
                indices = [int(token.split("/")[0]) - 1 for token in raw.split()[1:]]
                for index in range(1, len(indices) - 1):
                    faces.append([indices[0], indices[index], indices[index + 1]])
    return (
        np.asarray(vertices, dtype=np.float32),
        np.asarray(faces, dtype=np.int32),
        np.asarray(colors, dtype=np.float32),
    )


def decode_face_id(path: Path) -> np.ndarray:
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.int64)
    return rgb[..., 0] + (rgb[..., 1] << 8) + (rgb[..., 2] << 16) - 1


def crop_box(face_ids: np.ndarray, padding: int = 14) -> tuple[int, int, int, int]:
    yy, xx = np.nonzero(face_ids >= 0)
    return (
        max(0, int(xx.min()) - padding),
        max(0, int(yy.min()) - padding),
        min(face_ids.shape[1], int(xx.max()) + padding + 1),
        min(face_ids.shape[0], int(yy.max()) + padding + 1),
    )


def shade_face_colors(face_colors: np.ndarray, face_ids: np.ndarray, neutral: np.ndarray) -> Image.Image:
    valid = (face_ids >= 0) & (face_ids < len(face_colors))
    canvas = np.full((*face_ids.shape, 3), 255.0, dtype=np.float32)
    luma = neutral.astype(np.float32).mean(axis=2) / 240.0
    light = np.clip(0.69 + 0.31 * luma, 0.76, 1.02)
    canvas[valid] = face_colors[face_ids[valid]] * light[valid, None]
    return Image.fromarray(np.clip(canvas, 0, 255).astype(np.uint8), mode="RGB")


def fit_image(image: Image.Image, box: tuple[int, int, int, int]) -> tuple[Image.Image, float, int, int]:
    x0, y0, x1, y1 = box
    scale = min((x1 - x0) / image.width, (y1 - y0) / image.height)
    size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    resized = image.resize(size, Image.Resampling.LANCZOS)
    px = x0 + ((x1 - x0) - size[0]) // 2
    py = y0 + ((y1 - y0) - size[1]) // 2
    return resized, scale, px, py


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    name = "malgunbd.ttf" if bold else "malgun.ttf"
    return ImageFont.truetype(str(Path(r"C:\Windows\Fonts") / name), size=size)


def centered_text(draw: ImageDraw.ImageDraw, y: int, text: str, center_x: int, text_font: ImageFont.FreeTypeFont) -> None:
    bbox = draw.textbbox((0, 0), text, font=text_font)
    draw.text((center_x - (bbox[2] - bbox[0]) / 2, y), text, font=text_font, fill=(25, 28, 33))


def render_concept(
    face_ids: np.ndarray,
    neutral: np.ndarray,
    faces: np.ndarray,
    vertex_colors: np.ndarray,
    fields: np.lib.npyio.NpzFile,
) -> None:
    if len(faces) != len(fields["region_labels"]):
        raise RuntimeError("Mesh face count and MSR field face count differ")

    base = np.clip(vertex_colors[faces].mean(axis=1) * 255.0, 0, 255)
    hard_labels = fields["region_labels"].astype(np.int64)
    hard = REGION_COLORS[hard_labels % len(REGION_COLORS)]

    memberships = fields["visual_semantic_parent_membership"].astype(np.float32)
    if memberships.ndim != 2 or memberships.shape[0] != len(faces):
        raise RuntimeError("Unexpected visual_semantic_parent_membership shape")
    parent_palette = REGION_COLORS[: memberships.shape[1]]
    soft_mix = memberships @ parent_palette
    own = memberships.max(axis=1, keepdims=True)
    # Presentation-only continuous opacity: no thresholded core/band/halo masks are invented.
    # Strong membership remains saturated; weaker transition/halo support becomes paler.
    opacity = np.clip(0.12 + 0.88 * np.power(own, 1.7), 0.0, 1.0)
    soft = 248.0 * (1.0 - opacity) + soft_mix * opacity

    crop = crop_box(face_ids)
    panels = [
        (base, "원본"),
        (hard, "기존 부위 분할 (hard label)"),
        (soft, "MSR (core · band · halo)"),
    ]
    canvas = Image.new("RGB", (1680, 912), "white")
    draw = ImageDraw.Draw(canvas)
    label_font = font(29, bold=True)
    panel_width = 540
    lefts = [20, 570, 1120]
    for index, ((face_colors, label), left) in enumerate(zip(panels, lefts)):
        rendered = shade_face_colors(face_colors, face_ids, neutral).crop(crop)
        fitted, _, px, py = fit_image(rendered, (left + 12, 18, left + panel_width - 12, 824))
        canvas.paste(fitted, (px, py))
        centered_text(draw, 846, label, left + panel_width // 2, label_font)
        if index < 2:
            draw.line((left + panel_width + 5, 36, left + panel_width + 5, 812), fill=(226, 229, 234), width=2)
    canvas.save(OUT_CONCEPT, optimize=True)


def nearest_mask_point(mask: np.ndarray) -> tuple[float, float]:
    yy, xx = np.nonzero(mask)
    if len(xx) == 0:
        raise ValueError("No visible pixels for region")
    cx, cy = float(np.mean(xx)), float(np.mean(yy))
    closest = np.argmin((xx - cx) ** 2 + (yy - cy) ** 2)
    return float(xx[closest]), float(yy[closest])


def render_msrg(
    face_ids: np.ndarray,
    neutral: np.ndarray,
    faces: np.ndarray,
    vertex_colors: np.ndarray,
    fields: np.lib.npyio.NpzFile,
    role_manifest: dict,
) -> None:
    parent_names = [str(value) for value in fields["semantic_parent_names"].tolist()]
    face_parent = fields["face_semantic_parent_index"].astype(np.int64)
    assignments = role_manifest["assignment"]["parent_assignments"]
    graph = role_manifest["assignment"]["region_graph"]
    if set(assignments) != set(parent_names):
        raise RuntimeError("Automatic role assignments do not match semantic parent names")

    base = np.clip(vertex_colors[faces].mean(axis=1) * 255.0, 0, 255)
    # Keep the mesh readable while leaving role nodes/edges visually dominant.
    muted = 0.55 * base + 0.45 * 242.0
    full = shade_face_colors(muted, face_ids, neutral)
    crop = crop_box(face_ids, padding=18)
    crop_image = full.crop(crop)

    canvas = Image.new("RGB", (1800, 750), "white")
    draw = ImageDraw.Draw(canvas)
    fitted, scale, px, py = fit_image(crop_image, (305, 22, 1495, 726))
    canvas.paste(fitted, (px, py))

    valid = (face_ids >= 0) & (face_ids < len(face_parent))
    projected_parent = np.full(face_ids.shape, -1, dtype=np.int16)
    projected_parent[valid] = face_parent[face_ids[valid]]
    x0, y0, _, _ = crop
    nodes: dict[str, tuple[float, float]] = {}
    for parent_index, name in enumerate(parent_names):
        sx, sy = nearest_mask_point(projected_parent == parent_index)
        nodes[name] = (px + (sx - x0) * scale, py + (sy - y0) * scale)

    # Draw the stored undirected region_graph exactly once; no directed transfer edge is invented.
    seen: set[tuple[str, str]] = set()
    for left, neighbors in graph.items():
        for right in neighbors:
            edge = tuple(sorted((left, right)))
            if edge in seen:
                continue
            seen.add(edge)
            draw.line((*nodes[left], *nodes[right]), fill=(73, 81, 94), width=5)

    role_font = font(25, bold=True)
    offsets = {
        "foot": (-142, 34),
        "head": (-145, -62),
        "leg": (-122, 42),
        "paired_appendage": (-172, -22),
        "small_detail": (42, -70),
        "tail": (40, 30),
        "torso": (44, -20),
    }
    role_display = {
        "source": "Source",
        "path": "Path",
        "reservoir": "Reservoir",
        "modifier": "Modifier",
        "sink": "Sink",
    }
    for name in parent_names:
        role = assignments[name]["role"]
        color = ROLE_COLORS[role]
        x, y = nodes[name]
        radius = 12
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color, outline=(22, 26, 33), width=3)
        label = role_display[role]
        ox, oy = offsets[name]
        lx, ly = x + ox, y + oy
        bbox = draw.textbbox((lx, ly), label, font=role_font)
        pad_x, pad_y = 10, 6
        draw.rounded_rectangle(
            (bbox[0] - pad_x, bbox[1] - pad_y, bbox[2] + pad_x, bbox[3] + pad_y),
            radius=8,
            fill=(255, 255, 255),
            outline=color,
            width=3,
        )
        draw.text((lx, ly), label, font=role_font, fill=(22, 26, 33))

    legend_font = font(22)
    draw.line((50, 680, 116, 680), fill=(73, 81, 94), width=5)
    draw.text((132, 664), "저장된 인접 관계", font=legend_font, fill=(45, 51, 61))
    canvas.save(OUT_MSRG, optimize=True)


def main() -> None:
    validate_inputs()
    _, faces, vertex_colors = parse_obj(MESH)
    face_ids = decode_face_id(FACE_ID)
    neutral = np.asarray(Image.open(NEUTRAL).convert("RGB"), dtype=np.uint8)
    if face_ids.shape != neutral.shape[:2]:
        raise RuntimeError("Face-ID and neutral-view dimensions differ")
    role_manifest = json.loads(ROLE_MANIFEST.read_text(encoding="utf-8"))
    with np.load(FIELDS, allow_pickle=False) as fields:
        render_concept(face_ids, neutral, faces, vertex_colors, fields)
        render_msrg(face_ids, neutral, faces, vertex_colors, fields, role_manifest)
    print(f"wrote {OUT_CONCEPT}")
    print(f"wrote {OUT_MSRG}")


if __name__ == "__main__":
    main()
