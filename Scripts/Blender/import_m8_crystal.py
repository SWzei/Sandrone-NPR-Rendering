from __future__ import annotations

import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def arguments() -> list[str]:
    return sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else []


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    args = arguments()
    if len(args) != 4:
        raise SystemExit("Expected: <source.pmx> <output.blend> <output.fbx> <report.json>")

    source, blend_path, fbx_path, report_path = (Path(value).resolve() for value in args)
    for path in (blend_path, fbx_path, report_path):
        path.parent.mkdir(parents=True, exist_ok=True)

    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0

    result = bpy.ops.mmd_tools.import_model(
        filepath=str(source),
        types={"MESH", "ARMATURE"},
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

    roots = [obj for obj in bpy.data.objects if getattr(obj, "mmd_type", "NONE") == "ROOT"]
    armatures = [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
    meshes = [
        obj for obj in bpy.data.objects
        if obj.type == "MESH" and getattr(obj, "mmd_type", "NONE") not in {"RIGID_BODY", "JOINT", "TEMPORARY"}
    ]
    if len(roots) != 1 or len(armatures) != 1 or len(meshes) != 1:
        raise RuntimeError(f"Unexpected structure roots={len(roots)} armatures={len(armatures)} meshes={len(meshes)}")

    root, armature, mesh_object = roots[0], armatures[0], meshes[0]
    materials = [slot.material for slot in mesh_object.material_slots if slot.material]
    expected = ["Claymore_CrystallineSword", "Mat_Cyrstal"]
    actual = [material.mmd_material.name_j or material.name for material in materials]
    if actual != expected:
        raise RuntimeError(f"Unexpected material order: {actual}")

    bounds = [mesh_object.matrix_world @ Vector(corner) for corner in mesh_object.bound_box]
    bounds_min = [min(point[axis] for point in bounds) for axis in range(3)]
    bounds_max = [max(point[axis] for point in bounds) for axis in range(3)]
    source_textures = [source.parent / "tex" / "武器1.png", source.parent / "spa" / "目sp.png"]
    material_records = []
    for index, material in enumerate(materials):
        material_records.append({
            "index": index,
            "name_j": material.mmd_material.name_j,
            "double_sided": bool(material.mmd_material.is_double_sided),
            "triangle_count": sum(len(poly.loop_indices) - 2 for poly in mesh_object.data.polygons if poly.material_index == index),
        })

    report = {
        "phase": "M8",
        "source": str(source),
        "source_sha256": sha256(source),
        "blender_version": bpy.app.version_string,
        "mmd_tools_module": "bl_ext.blender_org.mmd_tools",
        "import_scale": 0.08,
        "object": mesh_object.name,
        "vertex_count": len(mesh_object.data.vertices),
        "triangle_count": sum(len(poly.loop_indices) - 2 for poly in mesh_object.data.polygons),
        "material_count": len(materials),
        "materials": material_records,
        "source_bone_count": sum(pose_bone.mmd_bone.bone_id >= 0 for pose_bone in armature.pose.bones),
        "uv_layers": [layer.name for layer in mesh_object.data.uv_layers],
        "bounds_m": {
            "min": bounds_min,
            "max": bounds_max,
            "size": [bounds_max[i] - bounds_min[i] for i in range(3)],
        },
        "source_textures": [
            {"path": str(path), "sha256": sha256(path), "bytes": path.stat().st_size}
            for path in source_textures
        ],
    }

    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), check_existing=False)

    materials[0].name = "M8_00_SwordBase"
    materials[1].name = "M8_01_Crystal"
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (root, armature, mesh_object):
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
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
    )
    if "FINISHED" not in export_result:
        raise RuntimeError(f"FBX export failed: {export_result}")

    report["blend"] = str(blend_path)
    report["fbx"] = str(fbx_path)
    report["fbx_bytes"] = fbx_path.stat().st_size
    report["fbx_sha256"] = sha256(fbx_path)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("SANDRONE_M8_REPORT=" + json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()
