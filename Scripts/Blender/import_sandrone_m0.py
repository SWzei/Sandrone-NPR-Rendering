from __future__ import annotations

import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def args_after_separator() -> list[str]:
    return sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []


def rel_or_abs(path: str, base: Path) -> str:
    try:
        return str(Path(path).resolve().relative_to(base.resolve()))
    except (ValueError, OSError):
        return str(Path(path))


def main() -> None:
    args = args_after_separator()
    if len(args) != 4:
        raise SystemExit("Expected: <source.pmx> <baseline.blend> <output.fbx> <report.json>")

    source = Path(args[0]).resolve()
    blend_path = Path(args[1]).resolve()
    fbx_path = Path(args[2]).resolve()
    report_path = Path(args[3]).resolve()
    for path in (blend_path, fbx_path, report_path):
        path.parent.mkdir(parents=True, exist_ok=True)

    # Do not call read_factory_settings here: Blender extensions are unloaded by
    # that operation, including MMD Tools. Clear the current scene explicitly.
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0

    result = bpy.ops.mmd_tools.import_model(
        filepath=str(source),
        types={"MESH", "ARMATURE", "PHYSICS", "DISPLAY", "MORPHS"},
        scale=0.08,
        clean_model=True,
        remove_doubles=False,
        import_adduv2_as_vertex_colors=False,
        fix_bone_order=True,
        fix_ik_links=False,
        apply_bone_fixed_axis=False,
        rename_bones=False,
        use_underscore=False,
        use_mipmap=True,
        log_level="INFO",
        save_log=False,
    )
    if "FINISHED" not in result:
        raise RuntimeError(f"MMD import failed: {result}")

    roots = [o for o in bpy.data.objects if getattr(o, "mmd_type", "NONE") == "ROOT"]
    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    character_meshes = [
        o
        for o in bpy.data.objects
        if o.type == "MESH" and getattr(o, "mmd_type", "NONE") not in {"RIGID_BODY", "JOINT", "TEMPORARY"}
    ]
    physics_objects = [
        o for o in bpy.data.objects if getattr(o, "mmd_type", "NONE") in {"RIGID_BODY", "JOINT"}
    ]
    if len(roots) != 1 or len(armatures) != 1 or not character_meshes:
        raise RuntimeError(
            f"Unexpected import structure: roots={len(roots)}, armatures={len(armatures)}, meshes={len(character_meshes)}"
        )

    root = roots[0]
    armature = armatures[0]
    mmd_root = root.mmd_root

    material_records = []
    materials = []
    seen_materials = set()
    for mesh in character_meshes:
        for slot in mesh.material_slots:
            if slot.material and slot.material.as_pointer() not in seen_materials:
                seen_materials.add(slot.material.as_pointer())
                materials.append(slot.material)
    for material_index, material in enumerate(materials):
        material_records.append(
            {
                "id": material_index,
                "name": material.name,
                "name_j": material.mmd_material.name_j,
                "name_e": material.mmd_material.name_e,
                "diffuse": list(material.diffuse_color),
                "is_double_sided": bool(material.mmd_material.is_double_sided),
            }
        )

    mesh_records = []
    bounds_min = Vector((math.inf, math.inf, math.inf))
    bounds_max = Vector((-math.inf, -math.inf, -math.inf))
    for obj in character_meshes:
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            bounds_min.x, bounds_min.y, bounds_min.z = min(bounds_min.x, p.x), min(bounds_min.y, p.y), min(bounds_min.z, p.z)
            bounds_max.x, bounds_max.y, bounds_max.z = max(bounds_max.x, p.x), max(bounds_max.y, p.y), max(bounds_max.z, p.z)
        keys = []
        if obj.data.shape_keys:
            keys = [k.name for k in obj.data.shape_keys.key_blocks if k.name != "Basis"]
        mesh_records.append(
            {
                "object": obj.name,
                "vertices": len(obj.data.vertices),
                "polygons": len(obj.data.polygons),
                "material_slots": [slot.material.name if slot.material else None for slot in obj.material_slots],
                "shape_keys": keys,
                "vertex_color_attributes": [a.name for a in obj.data.color_attributes],
                "uv_layers": [uv.name for uv in obj.data.uv_layers],
            }
        )

    report = {
        "phase": "M0",
        "source": str(source),
        "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "blender_version": bpy.app.version_string,
        "mmd_tools_module": "bl_ext.blender_org.mmd_tools",
        "import_scale": 0.08,
        "root": root.name,
        "armature": armature.name,
        "source_bone_count": sum(pb.mmd_bone.bone_id >= 0 for pb in armature.pose.bones),
        "helper_bone_count": sum(pb.mmd_bone.bone_id < 0 for pb in armature.pose.bones),
        "blender_armature_bone_count": len(armature.data.bones),
        "bones_of_interest": [name for name in ("頭", "首", "両目", "左目", "右目", "KeyB02_M") if name in armature.data.bones],
        "mesh_count": len(character_meshes),
        "meshes": mesh_records,
        "material_count": len(material_records),
        "materials": material_records,
        "morph_counts": {
            "vertex": len(mmd_root.vertex_morphs),
            "bone": len(mmd_root.bone_morphs),
            "material": len(mmd_root.material_morphs),
            "uv": len(mmd_root.uv_morphs),
            "group": len(mmd_root.group_morphs),
        },
        "bone_morphs": [
            {
                "name": morph.name,
                "offsets": [
                    {
                        "bone_id": offset.bone_id,
                        "location": list(offset.location),
                        "rotation_xyzw": list(offset.rotation),
                    }
                    for offset in morph.data
                ],
            }
            for morph in mmd_root.bone_morphs
        ],
        "material_morphs": [
            {"name": morph.name, "offset_count": len(morph.data)} for morph in mmd_root.material_morphs
        ],
        "physics_object_count": len(physics_objects),
        "bounds_m": {"min": list(bounds_min), "max": list(bounds_max), "size": list(bounds_max - bounds_min)},
        "images": [
            {
                "name": image.name,
                "path": rel_or_abs(bpy.path.abspath(image.filepath), source.parent),
                "size": list(image.size),
                "colorspace": image.colorspace_settings.name,
            }
            for image in bpy.data.images
            if image.type == "IMAGE"
        ],
    }

    # Preserve an unmodified, fully imported MMD baseline before applying FBX-only naming.
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), check_existing=False)

    # Prefix source material IDs in the derived FBX so Unity can map all 31 slots deterministically.
    for material_index, material in enumerate(materials):
        material.name = f"M{material_index:02d}_{material.mmd_material.name_j or material.name}"

    bpy.ops.object.select_all(action="DESELECT")
    export_objects = [root, armature, *character_meshes]
    for obj in export_objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.hide_render = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = armature

    export_result = bpy.ops.export_scene.fbx(
        filepath=str(fbx_path),
        check_existing=False,
        use_selection=True,
        object_types={"EMPTY", "ARMATURE", "MESH"},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        use_mesh_modifiers=False,
        mesh_smooth_type="OFF",
        use_subsurf=False,
        use_mesh_edges=False,
        use_tspace=True,
        use_triangles=False,
        use_custom_props=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        # MMD Tools adds 46 non-deforming helper/shadow bones. Every one of the
        # 692 PMX source bones is marked deforming, so this exports the source
        # skeleton while excluding implementation-only helpers.
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
    )
    if "FINISHED" not in export_result:
        raise RuntimeError(f"FBX export failed: {export_result}")

    report["baseline_blend"] = str(blend_path)
    report["fbx"] = str(fbx_path)
    report["fbx_bytes"] = fbx_path.stat().st_size
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("SANDRONE_M0_REPORT=" + json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
