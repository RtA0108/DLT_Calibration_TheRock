"""Render the existing E237 result as a synchronized three-view presentation video.

This script performs no generative inference. It only rasterizes the existing
E237 final UV video on the unchanged TheRock geometry, draws the recorded E237
event schedule, and stream-copies the E237 AAC audio into the final MP4.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


WIDTH = 1920
HEIGHT = 1080
FPS = 24
FRAME_COUNT = 720
DURATION = 30.0
POSTER_FRAME = 383  # 15.958 s: first frame immediately after the 15.917 s trigger.
MAP_WIDTH = 496
MAP_HEIGHT = 584
PANEL_WIDTH = 620
PANEL_HEIGHT = 730
PANEL_TOP = 10
TIMELINE_TOP = 800

EXPECTED_VIDEO_SHA256 = "072bb35faec28d02568fc0c5a9319e02164f3ddf56b219b8c59d5845f3d11482"
EXPECTED_GEOMETRY_SHA256 = "279c68f319d8e988931e049e4e39003b728a07a4232b6cc42062ade82118da27"
EXPECTED_SURFACE_SHA256 = "737c877e7667eb4c2a5069c2c980d92b8ef5d648b7996a7eee758820193bf227"

EVENT_ORDER = ("front", "nose", "eye", "ear", "mouth", "rear")
EVENT_LABELS = {
    "front": "전면",
    "nose": "코",
    "eye": "눈",
    "ear": "귀",
    "mouth": "입",
    "rear": "후면·정수리",
}
EVENT_COLORS = {
    "front": (244, 166, 70),
    "nose": (239, 103, 97),
    "eye": (255, 209, 102),
    "ear": (91, 192, 235),
    "mouth": (190, 126, 235),
    "rear": (105, 211, 154),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(4 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run_json(command: list[str]) -> dict:
    proc = subprocess.run(command, check=True, capture_output=True, text=True, encoding="utf-8")
    return json.loads(proc.stdout)


def read_exact(handle, byte_count: int) -> bytes:
    chunks: list[bytes] = []
    remaining = byte_count
    while remaining:
        chunk = handle.read(remaining)
        if not chunk:
            break
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def font(path: Path, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(path), size=size)


def camera_axes(yaw_degrees: float, elevation_degrees: float) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    yaw = math.radians(yaw_degrees)
    elevation = math.radians(elevation_degrees)
    direction = np.asarray(
        [math.sin(yaw) * math.cos(elevation), math.sin(elevation), math.cos(yaw) * math.cos(elevation)],
        dtype=np.float32,
    )
    world_up = np.asarray([0.0, 1.0, 0.0], dtype=np.float32)
    right = np.cross(world_up, direction)
    right /= np.linalg.norm(right)
    up = np.cross(direction, right)
    up /= np.linalg.norm(up)
    return direction, right, up


@dataclass
class RasterMap:
    yaw_degrees: float
    x: np.ndarray
    y: np.ndarray
    texture_x: np.ndarray
    texture_y: np.ndarray
    visible_mask: np.ndarray

    def sample(self, texture: np.ndarray) -> np.ndarray:
        image = np.zeros((MAP_HEIGHT, MAP_WIDTH, 3), dtype=np.uint8)
        image[self.y, self.x] = texture[self.texture_y, self.texture_x]
        return image

    def colors(self, texture: np.ndarray) -> np.ndarray:
        return texture[self.texture_y, self.texture_x]


def common_scale(vertices: np.ndarray, center: np.ndarray, yaws: tuple[int, ...], elevation: float) -> float:
    relative = vertices - center
    max_width = 0.0
    max_height = 0.0
    for yaw in yaws:
        _, right, up = camera_axes(yaw, elevation)
        projected_x = relative @ right
        projected_y = relative @ up
        max_width = max(max_width, float(np.ptp(projected_x)))
        max_height = max(max_height, float(np.ptp(projected_y)))
    return min((MAP_WIDTH - 32) / max_width, (MAP_HEIGHT - 38) / max_height)


def build_raster_map(
    vertices: np.ndarray,
    faces: np.ndarray,
    face_uv: np.ndarray,
    center: np.ndarray,
    scale: float,
    yaw_degrees: float,
    elevation_degrees: float,
) -> RasterMap:
    direction, right, up = camera_axes(yaw_degrees, elevation_degrees)
    relative = vertices - center
    screen = np.column_stack(
        (
            MAP_WIDTH * 0.5 + (relative @ right) * scale,
            MAP_HEIGHT * 0.51 - (relative @ up) * scale,
        )
    )
    depth_values = relative @ direction
    zbuffer = np.full((MAP_HEIGHT, MAP_WIDTH), -np.inf, dtype=np.float32)
    map_u = np.full((MAP_HEIGHT, MAP_WIDTH), -1.0, dtype=np.float32)
    map_v = np.full((MAP_HEIGHT, MAP_WIDTH), -1.0, dtype=np.float32)

    for face_index, triangle in enumerate(faces):
        vertex_triangle = vertices[triangle]
        normal = np.cross(vertex_triangle[1] - vertex_triangle[0], vertex_triangle[2] - vertex_triangle[0])
        normal_length = float(np.linalg.norm(normal))
        if normal_length < 1e-8 or float(normal @ direction) <= 0.0:
            continue
        points = screen[triangle]
        low = np.maximum(np.floor(points.min(axis=0) - 1).astype(np.int32), 0)
        high = np.minimum(
            np.ceil(points.max(axis=0) + 1).astype(np.int32),
            np.asarray([MAP_WIDTH - 1, MAP_HEIGHT - 1], dtype=np.int32),
        )
        if np.any(low > high):
            continue
        grid_x, grid_y = np.meshgrid(
            np.arange(low[0], high[0] + 1, dtype=np.int32),
            np.arange(low[1], high[1] + 1, dtype=np.int32),
        )
        pixel_x = grid_x.astype(np.float32) + 0.5
        pixel_y = grid_y.astype(np.float32) + 0.5
        x = points[:, 0]
        y = points[:, 1]
        denominator = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(float(denominator)) < 1e-8:
            continue
        b0 = ((y[1] - y[2]) * (pixel_x - x[2]) + (x[2] - x[1]) * (pixel_y - y[2])) / denominator
        b1 = ((y[2] - y[0]) * (pixel_x - x[2]) + (x[0] - x[2]) * (pixel_y - y[2])) / denominator
        b2 = 1.0 - b0 - b1
        inside = (b0 >= -1e-5) & (b1 >= -1e-5) & (b2 >= -1e-5)
        if not np.any(inside):
            continue
        barycentric = np.stack((b0, b1, b2), axis=-1)
        triangle_depth = barycentric @ depth_values[triangle]
        local_depth = zbuffer[low[1] : high[1] + 1, low[0] : high[0] + 1]
        update = inside & (triangle_depth > local_depth)
        if not np.any(update):
            continue
        uv = barycentric @ face_uv[face_index]
        local_u = map_u[low[1] : high[1] + 1, low[0] : high[0] + 1]
        local_v = map_v[low[1] : high[1] + 1, low[0] : high[0] + 1]
        local_depth[update] = triangle_depth[update]
        local_u[update] = uv[..., 0][update]
        local_v[update] = uv[..., 1][update]

    visible = map_u >= 0.0
    y_pixels, x_pixels = np.nonzero(visible)
    texture_x = np.clip(np.rint(map_u[visible] * 1023.0).astype(np.int32), 0, 1023)
    texture_y = np.clip(np.rint((1.0 - map_v[visible]) * 1023.0).astype(np.int32), 0, 1023)
    return RasterMap(
        yaw_degrees=yaw_degrees,
        x=x_pixels,
        y=y_pixels,
        texture_x=texture_x,
        texture_y=texture_y,
        visible_mask=visible,
    )


def decode_selected_frames(ffmpeg: Path, source: Path, frame_indices: tuple[int, ...]) -> list[np.ndarray]:
    expression = "+".join(f"eq(n\\,{index})" for index in frame_indices)
    command = [
        str(ffmpeg), "-v", "error", "-i", str(source),
        "-vf", f"select={expression}", "-vsync", "0", "-frames:v", str(len(frame_indices)),
        "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1",
    ]
    proc = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=8 * 1024 * 1024)
    assert proc.stdout is not None
    frame_bytes = 1024 * 1024 * 3
    frames = []
    for _ in frame_indices:
        payload = read_exact(proc.stdout, frame_bytes)
        if len(payload) != frame_bytes:
            stderr = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
            raise RuntimeError(f"Could not decode selected E237 frame: {stderr}")
        frames.append(np.frombuffer(payload, dtype=np.uint8).reshape(1024, 1024, 3).copy())
    return_code = proc.wait()
    if return_code != 0:
        stderr = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
        raise RuntimeError(f"Selected-frame decoder exited {return_code}: {stderr}")
    return frames


def choose_side_view(
    ffmpeg: Path,
    source: Path,
    raster_maps: dict[int, RasterMap],
) -> tuple[int, dict[int, float]]:
    before, active = decode_selected_frames(ffmpeg, source, (381, 454))
    scores: dict[int, float] = {}
    for yaw in (90, 270):
        mapping = raster_maps[yaw]
        a = mapping.colors(before).astype(np.int16)
        b = mapping.colors(active).astype(np.int16)
        scores[yaw] = float(np.mean(np.abs(b - a)))
    selected = max(scores, key=scores.get)
    return selected, scores


def rounded_text_box(
    draw: ImageDraw.ImageDraw,
    xy: tuple[int, int],
    text: str,
    text_font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int],
    text_fill: tuple[int, int, int] = (248, 248, 250),
    padding: tuple[int, int] = (10, 5),
) -> tuple[int, int, int, int]:
    x, y = xy
    box = draw.textbbox((0, 0), text, font=text_font)
    width = box[2] - box[0] + padding[0] * 2
    height = box[3] - box[1] + padding[1] * 2
    rect = (x, y, x + width, y + height)
    draw.rounded_rectangle(rect, radius=7, fill=fill)
    draw.text((x + padding[0], y + padding[1] - box[1]), text, font=text_font, fill=text_fill)
    return rect


def build_static_canvas(
    events: dict[str, dict],
    empty_seconds: float,
    side_yaw: int,
    regular: ImageFont.FreeTypeFont,
    regular_small: ImageFont.FreeTypeFont,
    bold: ImageFont.FreeTypeFont,
) -> tuple[Image.Image, tuple[int, int], list[int]]:
    canvas = Image.new("RGB", (WIDTH, HEIGHT), (0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    for x in (640, 1280):
        draw.line((x, 18, x, 785), fill=(35, 38, 44), width=1)

    labels = (("정면", 0), ("측면", side_yaw), ("후면", 180))
    for index, (label, yaw) in enumerate(labels):
        text = f"{label}  ·  yaw {yaw}°  ·  elev +10°"
        box = draw.textbbox((0, 0), text, font=regular)
        text_width = box[2] - box[0]
        x = index * 640 + (640 - text_width) // 2
        draw.text((x, 754), text, font=regular, fill=(218, 221, 226))

    draw.rectangle((0, TIMELINE_TOP, WIDTH, HEIGHT), fill=(8, 10, 15))
    draw.line((0, TIMELINE_TOP, WIDTH, TIMELINE_TOP), fill=(48, 53, 62), width=2)
    draw.text((25, 809), "E237 · 오디오 사건 타임라인", font=bold, fill=(245, 246, 248))

    timeline_left = 250
    timeline_right = 1880
    row_y = [848 + index * 29 for index in range(len(EVENT_ORDER))]
    for event, y in zip(EVENT_ORDER, row_y):
        label = EVENT_LABELS[event]
        label_box = draw.textbbox((0, 0), label, font=regular)
        draw.text((220 - (label_box[2] - label_box[0]), y - 2), label, font=regular, fill=(218, 221, 226))
        draw.rounded_rectangle((timeline_left, y, timeline_right, y + 18), radius=5, fill=(27, 31, 38))
        item = events[event]
        start = float(item["start_seconds"])
        end = float(item["end_seconds"])
        x0 = int(round(timeline_left + (timeline_right - timeline_left) * start / DURATION))
        x1 = int(round(timeline_left + (timeline_right - timeline_left) * end / DURATION))
        color = EVENT_COLORS[event]
        draw.rounded_rectangle((x0, y, x1, y + 18), radius=5, fill=color)
        range_text = f"{start:.2f}–{end:.2f}"
        if x1 - x0 > 115:
            draw.text((x0 + 7, y + 1), range_text, font=regular_small, fill=(12, 14, 18))

    for tick in range(0, 31, 5):
        x = int(round(timeline_left + (timeline_right - timeline_left) * tick / DURATION))
        draw.line((x, 834, x, 1022), fill=(45, 49, 57), width=1)
        label = str(tick)
        box = draw.textbbox((0, 0), label, font=regular_small)
        draw.text((x - (box[2] - box[0]) // 2, 1027), label, font=regular_small, fill=(143, 149, 158))
    draw.text((1890, 1027), "s", font=regular_small, fill=(143, 149, 158))

    empty_x = int(round(timeline_left + (timeline_right - timeline_left) * empty_seconds / DURATION))
    for y in range(838, 1021, 10):
        draw.line((empty_x, y, empty_x, min(y + 5, 1021)), fill=(140, 146, 156), width=2)
    no_event_text = f"사건 없음 · {empty_seconds:.2f} s"
    no_event_box = draw.textbbox((0, 0), no_event_text, font=regular_small)
    no_event_x = max(timeline_left, min(empty_x - (no_event_box[2] - no_event_box[0]) // 2, timeline_right - (no_event_box[2] - no_event_box[0])))
    draw.text((no_event_x, 813), no_event_text, font=regular_small, fill=(174, 179, 188))
    return canvas, (timeline_left, timeline_right), row_y


def compose_frame(
    texture: np.ndarray,
    mappings: tuple[RasterMap, RasterMap, RasterMap],
    static_canvas: Image.Image,
    timeline_bounds: tuple[int, int],
    frame_index: int,
    time_font: ImageFont.FreeTypeFont,
) -> Image.Image:
    canvas = static_canvas.copy()
    for index, mapping in enumerate(mappings):
        view = Image.fromarray(mapping.sample(texture), mode="RGB")
        view = view.resize((PANEL_WIDTH, PANEL_HEIGHT), Image.Resampling.LANCZOS)
        canvas.paste(view, (index * 640 + 10, PANEL_TOP))

    draw = ImageDraw.Draw(canvas)
    timeline_left, timeline_right = timeline_bounds
    seconds = frame_index / FPS
    head_x = int(round(timeline_left + (timeline_right - timeline_left) * seconds / DURATION))
    draw.line((head_x + 1, 836, head_x + 1, 1022), fill=(0, 0, 0), width=4)
    draw.line((head_x, 836, head_x, 1022), fill=(250, 250, 252), width=2)
    draw.polygon(((head_x - 7, 831), (head_x + 7, 831), (head_x, 840)), fill=(238, 79, 75))
    time_text = f"t = {seconds:.1f} s"
    text_box = draw.textbbox((0, 0), time_text, font=time_font)
    pill_width = text_box[2] - text_box[0] + 18
    pill_x = max(timeline_left, min(head_x - pill_width // 2, timeline_right - pill_width))
    draw.rounded_rectangle((pill_x, 1047, pill_x + pill_width, 1075), radius=7, fill=(238, 79, 75))
    draw.text((pill_x + 9, 1050 - text_box[1]), time_text, font=time_font, fill=(255, 255, 255))
    return canvas


def validate_inputs(ffprobe: Path, source: Path, result_path: Path, geometry: Path, surface: Path) -> tuple[dict, dict[str, dict], float]:
    for path in (source, result_path, geometry, surface):
        if not path.is_file():
            raise FileNotFoundError(path)
    checks = {
        source: EXPECTED_VIDEO_SHA256,
        geometry: EXPECTED_GEOMETRY_SHA256,
        surface: EXPECTED_SURFACE_SHA256,
    }
    for path, expected in checks.items():
        actual = sha256(path)
        if actual.lower() != expected.lower():
            raise RuntimeError(f"Source hash mismatch: {path}\nexpected {expected}\nactual   {actual}")

    probe = run_json([
        str(ffprobe), "-v", "error", "-show_entries",
        "stream=index,codec_name,codec_type,width,height,r_frame_rate,pix_fmt,sample_rate,channels",
        "-show_entries", "format=duration", "-of", "json", str(source),
    ])
    video_stream = next(stream for stream in probe["streams"] if stream["codec_type"] == "video")
    audio_stream = next(stream for stream in probe["streams"] if stream["codec_type"] == "audio")
    if (
        video_stream["width"] != 1024
        or video_stream["height"] != 1024
        or video_stream["r_frame_rate"] != "24/1"
        or audio_stream["codec_name"] != "aac"
        or audio_stream["sample_rate"] != "48000"
        or audio_stream["channels"] != 2
        or abs(float(probe["format"]["duration"]) - DURATION) > 1e-6
    ):
        raise RuntimeError(f"Unexpected E237 media contract: {json.dumps(probe, indent=2)}")

    result = json.loads(result_path.read_text(encoding="utf-8"))
    if result["frames"] != FRAME_COUNT or result["fps"] != FPS or float(result["duration_seconds"]) != DURATION:
        raise RuntimeError("E237 result duration contract mismatch")
    events = {item["event"]: item for item in result["plan"]["events"]}
    if set(events) != set(EVENT_ORDER):
        raise RuntimeError(f"Unexpected E237 event set: {sorted(events)}")
    empty_decisions = [item for item in result["plan"]["trigger_decisions"] if not item["selected"]]
    if len(empty_decisions) != 1:
        raise RuntimeError(f"Expected one no-event trigger, found {len(empty_decisions)}")
    return result, events, float(empty_decisions[0]["seconds"])


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--research-root", type=Path, default=Path(r"C:\Users\minsu\agentic_audio_projection_mapping"))
    parser.add_argument("--repo-root", type=Path, default=Path(r"C:\Users\minsu\Projects\DLT_Calibration_TheRock"))
    parser.add_argument("--ffmpeg", type=Path, default=Path(r"C:\ffmpeg\bin\ffmpeg.exe"))
    parser.add_argument("--ffprobe", type=Path, default=Path(r"C:\ffmpeg\bin\ffprobe.exe"))
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args()

    research_root = args.research_root.resolve()
    repo_root = args.repo_root.resolve()
    source = research_root / "outputs/review_exports/experiment237/therock/audio_parallel_30s_r1/therock_audio_generated_events_30s_uv_1024.mp4"
    result_path = research_root / "outputs/review_exports/experiment237/therock/audio_parallel_30s_r1/result.json"
    geometry_path = research_root / "outputs/review_exports/experiment95/programs/therock/geometry.npz"
    surface_path = research_root / "outputs/review_exports/experiment102/therock/surface_masks.npz"
    figures = repo_root / "발표자료/figures"
    output_video = figures / "18_e237_three_view_timeline_30s.mp4"
    output_poster = figures / "19_e237_three_view_poster.png"
    if not args.ffmpeg.is_file() or not args.ffprobe.is_file():
        raise FileNotFoundError("ffmpeg/ffprobe executable not found")
    for path in (output_video, output_poster):
        if path.exists() and not args.overwrite:
            raise FileExistsError(path)
    figures.mkdir(parents=True, exist_ok=True)

    _, events, empty_seconds = validate_inputs(args.ffprobe, source, result_path, geometry_path, surface_path)
    geometry = np.load(geometry_path)
    vertices = geometry["vertices"].astype(np.float32)
    faces = geometry["faces"].astype(np.int32)
    face_uv = np.load(surface_path)["uv"].astype(np.float32)
    if vertices.shape != (248, 3) or faces.shape != (492, 3) or face_uv.shape != (492, 3, 2):
        raise RuntimeError((vertices.shape, faces.shape, face_uv.shape))
    center = (vertices.min(axis=0) + vertices.max(axis=0)) * 0.5
    elevation = 10.0
    yaws = (0, 90, 180, 270)
    scale = common_scale(vertices, center, yaws, elevation)
    raster_maps = {
        yaw: build_raster_map(vertices, faces, face_uv, center, scale, yaw, elevation)
        for yaw in yaws
    }
    side_yaw, side_scores = choose_side_view(args.ffmpeg, source, raster_maps)
    selected_maps = (raster_maps[0], raster_maps[side_yaw], raster_maps[180])
    print(f"SIDE_SELECTION yaw={side_yaw} scores={json.dumps(side_scores, sort_keys=True)}", flush=True)

    regular_path = Path(r"C:\Windows\Fonts\malgun.ttf")
    bold_path = Path(r"C:\Windows\Fonts\malgunbd.ttf")
    regular = font(regular_path, 22)
    regular_small = font(regular_path, 15)
    bold = font(bold_path, 24)
    time_font = font(bold_path, 16)
    static_canvas, timeline_bounds, _ = build_static_canvas(
        events, empty_seconds, side_yaw, regular, regular_small, bold
    )

    decode_command = [
        str(args.ffmpeg), "-v", "error", "-i", str(source), "-map", "0:v:0",
        "-frames:v", str(FRAME_COUNT), "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1",
    ]
    encode_command = [
        str(args.ffmpeg), "-y", "-v", "warning",
        "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{WIDTH}x{HEIGHT}", "-r", str(FPS), "-i", "pipe:0",
        "-i", str(source), "-map", "0:v:0", "-map", "1:a:0", "-frames:v", str(FRAME_COUNT),
        "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-maxrate", "8M", "-bufsize", "16M",
        "-pix_fmt", "yuv420p", "-profile:v", "high", "-level:v", "4.1",
        "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
        "-c:a", "copy", "-t", "30.000", "-movflags", "+faststart",
        "-metadata", "title=E237 synchronized three-view timeline", str(output_video),
    ]
    decoder = subprocess.Popen(decode_command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=16 * 1024 * 1024)
    encoder = subprocess.Popen(encode_command, stdin=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=16 * 1024 * 1024)
    assert decoder.stdout is not None
    assert encoder.stdin is not None
    texture_bytes = 1024 * 1024 * 3
    try:
        for frame_index in range(FRAME_COUNT):
            payload = read_exact(decoder.stdout, texture_bytes)
            if len(payload) != texture_bytes:
                decoder_error = decoder.stderr.read().decode("utf-8", errors="replace") if decoder.stderr else ""
                raise RuntimeError(f"E237 decoder stopped at frame {frame_index}: {decoder_error}")
            texture = np.frombuffer(payload, dtype=np.uint8).reshape(1024, 1024, 3)
            frame = compose_frame(texture, selected_maps, static_canvas, timeline_bounds, frame_index, time_font)
            if frame_index == POSTER_FRAME:
                frame.save(output_poster, format="PNG", optimize=True)
            encoder.stdin.write(np.asarray(frame, dtype=np.uint8).tobytes())
            if frame_index % 48 == 0 or frame_index == FRAME_COUNT - 1:
                print(f"RENDER {frame_index + 1}/{FRAME_COUNT}", flush=True)
    finally:
        if encoder.stdin:
            encoder.stdin.close()
    decoder_code = decoder.wait()
    encoder_code = encoder.wait()
    decoder_error = decoder.stderr.read().decode("utf-8", errors="replace") if decoder.stderr else ""
    encoder_error = encoder.stderr.read().decode("utf-8", errors="replace") if encoder.stderr else ""
    if decoder_code != 0:
        raise RuntimeError(f"Decoder exited {decoder_code}: {decoder_error}")
    if encoder_code != 0:
        raise RuntimeError(f"Encoder exited {encoder_code}: {encoder_error}")
    if not output_poster.is_file() or not output_video.is_file():
        raise RuntimeError("Expected presentation outputs were not created")

    report = {
        "video": str(output_video),
        "poster": str(output_poster),
        "video_bytes": output_video.stat().st_size,
        "poster_frame": POSTER_FRAME,
        "poster_seconds": POSTER_FRAME / FPS,
        "side_yaw": side_yaw,
        "side_scores": side_scores,
        "camera_elevation_degrees": elevation,
        "orthographic_scale_internal": scale,
        "source_video_sha256": sha256(source),
        "output_video_sha256": sha256(output_video),
        "output_poster_sha256": sha256(output_poster),
    }
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
    main()
