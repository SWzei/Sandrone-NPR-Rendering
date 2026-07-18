# Development Log

## 2026-07-16 — Independent M0–M9 verification and bounded repairs

- Audited the active nested Unity project, root/source duplicates, technical documents, all M0–M9 code/shaders/configs/scenes/reports, PMX/FBX/Blend invariants, texture import semantics, URP assets, Quality/Graphics/Build settings and historical logs. Blender 5.1.2 opened the main, optional eye-gear and sword `.blend` files read-only; the main and eye-gear files still report two `KeyB02_M` dependency-cycle warnings.
- Fixed four bounded defects: M3 now refreshes slot MPBs when values in the same ShadowProfile object change; M8 masks and sword model disable unnecessary CPU Read/Write; M9 selects PC Quality/`PC_RPAsset` before Player build and records provenance plus independent median/P95 sample statistics; the render-only M6 ground no longer leaves a MeshCollider in a Physics-stripped build.
- Fresh D3D12 full rebuild succeeded. M0–M4 combined is `30/30`; M4/M5/M6/M7/M8/M9 are `94/94`, `94/94`, `154/154`, `131/131`, `110/110`, `91/91`; Shader messages 0. Windows x64 Player build is `0 errors / 0 warnings`, 454,999,822 bytes. It identifies itself as `Sandrone Toon M9`, D3D12, PC, `PC_RPAsset`, render scale 1, 4 cascades and soft shadows.
- Final Player window (120 warmup + 240 sampled frames): frame mean/median/P95 `1.773/1.618/2.593 ms`; CPU `1.772/1.619/2.606 ms` with 240 samples; GPU `0.702/0.694/0.741 ms` with 201 valid samples. An earlier same-configuration run recorded frame mean/P95 `2.552/5.408 ms`, so CPU-window variance remains material. Variant audit is `45 input / 1 removed / 44 retained`; overdraw remains `7.797/19/38` mean/P95/max layers. These results are machine- and scene-specific.
- Fresh visible Game View/Frame Debugger passed on D3D11 and D3D12 with 134/122 events. Target Pass counts were identical and all failures were zero; four paired screenshots differ by only `0.000003–0.000005/255` MAE. Unity service-token TLS errors in visible-Editor logs are unrelated to project rendering and did not abort the audits.
- A direct M8 Validator invocation from the final M9 state intentionally retained its failure: exit 1 because the phase-local gate requires Build Settings to contain M8 only, while the final project correctly contains M9. The subsequent M9 dependency-chain rebuild established the M8 phase state, passed M8 `110/110`, restored M9 Build Settings and completed with exit 0; no gate was weakened.
- Major issue left unimplemented: M3/M4/M5/M6/M8 character Forward shaders interpolate vertex shadow coordinates in the PC cascade variant, while URP 17.5 selects cascades per fragment. Existing near-camera evidence does not validate cascade transitions. This crosses historical M3–M8 acceptance and awaits approval for a coordinated shader fix and D3D11/D3D12 near/mid/far regression.
- Evidence weakness left unimplemented: validators accept artifact presence/JSON substrings without timestamp, input-hash or producer provenance; the Python comparison scripts report metrics but enforce no pass thresholds. A clean compile also reports 114 unique CS0618 warnings across 25 Editor files. Physical Android/mobile hardware, Android build, external-memory bandwidth and animation stress remain unverified.

## 2026-07-16 — M9 final composition, build variants and performance closeout (historical evidence; superseded above)

- Used M8 `110/110` as a hard gate and kept all M0–M8 character shaders, 31 material slots, model, UVs, 692 bones and textures unchanged. M9 adds only an independent final Volume/controller, audit tooling and build-time measurement.
- Selected Neutral tonemapping with bounded exposure/saturation (`-0.08/-18`, contrast/hue 0), desktop SMAA High and mobile FXAA. ACES was rejected by a same-camera `24.002/255` MAE and visible black/red compression; TAA remains deferred without motion-vector/outline stability evidence.
- The M9 Volume contains exactly Tonemapping + ColorAdjustments and remains separate from M8 Bloom. M8→M9 saturation moved from `0.2917` to `0.2526` toward the unregistered reference `0.1782`; luminance changed `0.4837→0.4820`, reference `0.4598`. Final red skirt is preserved and magenta pixels remain zero.
- Built and ran a Windows x64 Release Player on D3D12/RTX 4060 Laptop at 768×1280. After 120 warmup and 240 samples: CPU mean/max `1.329/6.540 ms`, GPU mean/max `0.308/0.529 ms`, p95 frame duration `2.040 ms`; mean Standard Draw/SetPass/Triangles/Vertices are `68/81/181,364/203,029`.
- Conservative Sandrone-only variant audit saw 33 input variants, removed one unused punctual-light ShadowCaster variant and retained 32. The additive fixed-view overdraw audit reports foreground mean/p95/max `7.797/19/38` layers; it is not a hardware early-Z/ROP counter.
- Real visible Game View/Frame Debugger on D3D12 recorded 122 events: M5/M6/M4 `2/10/18`, M8 Eye/VFX/Sword `1/1/1`, Outline `14`, ShadowCaster `46`, Receiver `1`, Bloom `16`, SMAA `3`, Final Post `1`; failures 0 and state restoration passed.
- Final automated report is M9 `91/91`, warnings 3, Shader compiler messages 0, with all M0–M8 gates clean. External-memory bandwidth and physical mobile-device performance remain explicitly unmeasured; material merging and bone stripping were rejected for lack of safe state/animation evidence.

## 2026-07-16 — M8 EyeLight / crystalline sword HDR emission and isolated Bloom

- Kept M8 to two of three candidates: EyeLight and `大剑.pmx/Mat_Cyrstal`; display, particles, dissolve and all M9 work remain deferred.
- Replaced only character slot 10. The other 30 M7 material objects and the separate 14-draw outline renderer remain exact; the eye shader preserves the full M6/M4 CBUFFER prefix and stencil contract.
- Imported the sword with Blender 5.1.2/mmd_tools as a separate 7,473-vertex, 6,492-triangle, two-material FBX. The 1,576-triangle crystal submesh is the primary VFX boundary.
- Generated an exact EyeLight-alpha mask and a documented cyan-separation crystal seed. Both are Linear/mipped/uncompressed; original PMX, FBX, UV, rig and textures were not overwritten.
- Added HDR emission after bounded LDR base and a dedicated Bloom-only Volume (`1.1/0.35/0.55/8`, Gaussian Half, six iterations). No tonemapping, grading or AA was added.
- Fixed two evidence defects: Bloom overrides must be persisted with `AddObjectToAsset`, and isolated EyeLight capture must retain slot 6's stencil writer. Same-HDR M7 control versus M8-all-off is now exact MAE 0; the legacy M7-LDR comparison remains recorded as configuration delta 3.792.
- Final automated gate is 108/108 before the added MPB checks (110/110 after final revalidation), with all M0–M7 gates clean and Shader messages 0. HDR peak is 4.478; eye/crystal extraction contains 141/8,700 pixels; final red-skirt count is 99,902.
- Visible D3D12/RTX 4060 Play Mode Game View/Frame Debugger passed with 119 events: 1 M8 Eye, 1 M8 crystal, 1 M0 sword base, 16 Bloom, 14 M7 outline, 2 M5, 10 M6, 18 M4, 46 ShadowCaster and one receiver. MPB Role/Weight/Threshold were 1/1/1.1 and 2/1/1.1; failures 0.
- GPU time, bandwidth, SRP Batcher, built-player variants, physical mobile-device profiling and artist-authored masks remain unmeasured. M9 was not implemented.

## 2026-07-16 — M7 pixel-space coloured inverted-hull outline

- Re-read the technical document and M0–M6 project/evidence, then kept M7 strictly to outline. M6 `154/154` was a hard entry gate; M8 HDR/Bloom and M9 post/performance work were not added.
- The standard FBX has no vertex colour or dedicated outline-normal channel. Of 17,767 coincident-position + bone-index groups, 12,919 had discontinuous source normals (maximum about 90 degrees).
- Added a separate outline SkinnedMeshRenderer and derived mesh. Hemisphere-aligned averaged normals reduce the discontinuous groups to zero while vertex, BlendShape and bindpose parity remain exact; the original FBX and shading normals are untouched.
- Limited submission to slots `0,1,12–18,20,22–24,26`. Transparent overlays and known overlapping inner-skirt slots are excluded. Widths are 0.72–1.30 px with material-family colours.
- The pass uses clip/NDC pixel correction, `Cull Front / ZWrite Off / ZTest Less / Blend One,Zero`, no shadow, Stencil, Emission or Bloom. Near/mid/far measured thickness is `0.984/0.930/0.984 px`; outline-off versus M6 MAE is zero.
- The first implementation retained 31 submeshes with empty indices. Real Frame Debugger still submitted 31 outline draws. The generated mesh was compressed to 14 effective submeshes/materials; the retained first audit documents the failure.
- Final visible D3D11/RTX 4060 Play Mode audit at 768×1680 recorded 108 events: 14 M7 Outline, 2 M5, 11 M6, 18 M4 Forward, 46 ShadowCaster and 1 receiver. Event states and lifecycle restoration passed.
- Same-camera M6→M7 RGB MAE is `0.4814/255`, changed coverage `1.403%`; changes are silhouette/inter-part lines. Final results: M7 `131/131`, M6 `154/154`, M5 `94/94`, M0–M4 `30/30`, Shader messages 0. GPU/SRP Batcher/built-player/mobile profiling and artist-authored outline data remain unmeasured.

## 2026-07-16 — M6 hair / eyes and optional movable eye gear

- Re-read the full technical document, project code/assets/configuration/reports and both standard/optional PMX baselines. Locked Unity 6000.5.3f1, URP 17.5.0, Linear; M5 entry gate was 94/94.
- Added M6 only to slots 2,3,6–13,29. Face slots 0/1 remain exact M5 assets; all other non-target slots remain exact M4 objects. The skirt cannot consume M6 constants or textures.
- Added bounded LDR eye fill, animatable Eye AL and Control-R-gated low-frequency tangent hair highlights. A real-render failure rejected the initial eye-white template: it clipped 327 valid blue iris pixels and left decorative-layer A/B at zero. The final opaque iris writes Stencil and slots 7–11 read it; M5/M6 blue coverage is identical at 539 pixels. Slot 29 receives no procedural lobe.
- Preserved the exact M4 UnityPerMaterial prefix and reused its ShadowCaster. No Outline, HDR Emission, Bloom or post-processing feature was added.
- The first validator pass exposed three test defects rather than visual defects: a source-comment false positive, a malformed regex compile error, and a whole-frame Eye AL metric diluted below threshold. Each failing log is retained; the final metric uses its 664 changed pixels and reports target MAE 8.268.
- Visible Play Mode Game View/Frame Debugger audit on D3D11/RTX 4060 passed with 93 frame events: 2 M5 face, 11 M6 target, 18 M4 baseline Forward, 46 ShadowCaster and one receiver. Unused audit constants were stripped by Unity, so event mapping now uses effective material signatures and candidate multisets.
- Final visible metrics: M5→M6 MAE 0.198, HairSpec toggle MAE 0.197, PC/Mobile MAE 1.293, red-skirt pixels 93,480; scene reopen and controller restoration passed.
- Imported the optional movable-eye-gear PMX through Blender 5.1.2/mmd_tools as a separate 33-slot FBX. Original gear textures are byte-identical copies with Mip/Preserve Coverage/AlphaClip. Desktop 0.5/2/5/10m coverage is 9074/436/68/14 px; recommend standard fallback from 5m. The optional scene is excluded from Build Settings.
- Final automated results: M6 154/154, M5 94/94, M0–M4 combined 30/30, Shader compiler messages 0. GPU time, SRP Batcher, built-player variants and physical mobile-device profiling remain unmeasured. M7–M9 were not implemented.

## 2026-07-15 — M5 face-only skirt-color finalization

- Follow-up asset-binding audit parsed the source PMX texture indices and captured all 31 live Renderer slots plus Frame Debugger effective constants. All material/BaseMap GUIDs match the phase baseline; all imported BaseMap files hash-identically match the PMX source textures; no MPB overrides Base/Control/Ramp/MatCap/ST/Color.
- The source PMX itself maps skirt slots 20/25 to `tex/体.png` and 21–24/26–27 to `tex/裙.png`. The Unity map is therefore not an accidental red/black texture swap. All skirt ST values are identity and BaseColor values are white.
- Unity 6000.5 still reports `m_MeshSubset=0` and does not expose asset GUIDs for these SRP draws. The audit now joins Renderer GUID evidence with a 31-draw Frame Debugger effective-signature multiset; identical signatures are reported as candidate groups instead of fabricated unique slot IDs. Result: zero failures.
- The first repair fixed the incompatible M4/M5 `UnityPerMaterial` prefix, but the scene still routed all 31 slots through the M5 shader. That architecture unnecessarily exposed every non-face material to M5 changes and did not meet the face-only boundary strongly enough.
- M5 scene generation now creates/binds M5 materials only for face slots 0/1. Slots 2–30 reuse the exact corresponding M4 `Material` assets; no BaseMap, ControlMap, material constant, queue or render-state copy can drift. The skirt slots 20–27 therefore use `Sandrone/M4/MaterialResponse` directly.
- `SandroneM5Controller` applies shared M4 debug/feature/light MPBs to either shader and writes Face SDF properties only when the material exposes them. Face-debug evidence temporarily isolates non-face slots and always restores the production array.
- Hardened `SandroneM5Validator`: scene dependencies are reloaded after scene open, material validation cannot silently skip on Unity fake-null objects, all shader-local CBUFFER blocks are checked, slots 0/1 require M5 and slots 2–30 require exact M4 object identity.
- Hardened the visible fault reproducer so it removes only the target ShadowCaster CBUFFER field; it no longer creates an unrelated missing `_M4DebugMode` compile error.
- Final D3D11 evidence: Validator 93/93, shader compiler messages 0; M0–M4 combined regression 30/30; Special Audit failures 0; visible 768×1680 Game View/Frame Debugger failures 0.
- Frame Debugger recorded 116 frame events and 101 detailed draws: 2 `M5FaceSDF`, 29 `M4MaterialResponse`, 69 character ShadowCaster and 1 ground receiver. M4→M5 non-face/skirt ROI MAE is 0/0, with 0 new near-black pixels/components.
- The user screenshots use a different editor framing/state from the locked Game View audit. The final claim is based on a same-condition M4/M5 capture plus exact non-face asset identity, not on relighting or forcing a red override. M6–M9 remain untouched.

## 2026-07-15 — M5 strict regression, skirt/Face SDF/light repair

- Rejected the previous pass conclusion and rebuilt M5 on a real RTX 4060/D3D11 device. The first strict same-input M4/M5 run failed: non-face MAE 1.561 and skirt-slot MAE up to 13.670.
- Located the root cause in the reused M4 ShadowCaster contract: M5 had changed the `UnityPerMaterial` layout. Restored the exact complete M4 prefix and appended M5 fields only; retained `_M4DebugMode` compatibility writes.
- Added `SandroneM5SpecialAudit`: 31 visible-slot masks, transparent contribution isolation, non-face MAE <0.5, near-black connected components, actual-Light-Transform 20-degree sweep, sign/wrap continuity and semantic checks for all 17 debug modes.
- Replaced the hard `dot(Lh,HeadRight)` UV sign flip with dual FaceMap sampling and a 0.10-wide continuous mirror blend. Final results: non-face/max skirt-slot MAE 0, new black pixels/component 0/0, max boundary step 35.833 px, -1/+1 MAE 10.975 and 359/0 MAE 5.515.
- Added a local `_SANDRONE_FACE` variant only on slots 0/1. The current FaceMap remains a deterministic project seed, not final art.
- Added a visible-Editor 768×1680 Game View audit driven by the actual Directional Light Transform. Captured M4, controlled broken-CBUFFER reproduction, fixed M5, 18 light angles, four ground-shadow cardinals and PC/Mobile frames; Play enter/exit, scene reopen, Domain Reload and Controller restoration all passed.
- Fixed Unity 6000.5 Frame Debugger capture timing by selecting each event and waiting for a rendered frame. Recorded 101 detailed events: 31 M5 Forward, 69 ShadowCaster and one formal ground receiver, zero failures.
- Unity 6000.5 reports `m_MeshSubset=0` for these split draws, so a no-op audit identity uses material slot as Stencil Ref with Read/WriteMask=0 and Always/Keep. This maps all 31 events without changing pixels or implementing M6 stencil behavior.
- Final Game View M4→M5: global MAE 0.0022, non-face/skirt ROI MAE 0/0, no new near-black pixels. 20-degree Game View Face ROI max adjacent MAE 3.778 and 340→0 MAE 1.065.
- Rejected an initial visible-audit false positive: the first Mobile 0-degree capture was a stale cyan CBUFFER-reproduction frame. Split pipeline switching from capture, cleared old PNG/JSON evidence at audit start, added cyan/resolution/PC-Mobile-MAE/light-response hard gates, and made audit failures return a non-zero Editor exit code. The fresh original-project run passed with PC/Mobile 0-degree full-frame/foreground MAE 1.220/3.184.
- The original project initially failed slots 23/25/27 at MAE 2.639/2.023/2.639 even though the audit clone passed. Rebuilding M0–M4 regenerated the stale phase assets; the same M5 audit then returned all non-face slots to 0 MAE. The failing log is retained rather than overwritten.
- Final regressions: M0–M4 combined 30/30; M5 Validator 80/80; Special Audit and Game View/Frame Debugger reports have zero failures. GPU time and SRP Batcher profiling remain explicitly unmeasured.

## 2026-07-15 — M5 Face SDF

- Confirmed M5 as the current phase and kept M6 hair/eyes, M7 outline, M8 VFX/Bloom and M9 post-processing out of scope.
- Confirmed that the source asset has no Face SDF and that `T_Face.png` is an sRGB BaseMap, not threshold data.
- Added `SandroneFaceProfile_v1_M5`, a per-slot MPB controller and `Sandrone/M5/FaceSDF`; only face slots 0/1 use the face threshold field. Slots 17/18 remain M4 skin responses for face/neck continuity checks.
- Implemented head-bone horizontal light projection, `q=(1-f)/2`, right-axis UV mirroring, derivative AA and a vertical-light fallback. The Face SDF replaces only form lighting; real cast shadow remains a limiting mask.
- Added a non-destructive 2048² linear/Clamp/uncompressed authoring seed. It is generated only when missing and is explicitly not extracted game data or a true 3D signed distance field.
- Reused the audited M4 ShadowCaster via `UsePass`; all 31 Base/Ramp/Control/MatCap bindings and queue/blend/ZWrite/Cull/AlphaClip/ShadowCull states remain unchanged.
- First strict D3D11 run failed 3/73 because full-frame black-background MAE diluted the small face ROI and near-front light made Lambert/SDF both lit. Kept the thresholds and corrected evidence to same-camera side light plus face-slot masks.
- Final real D3D11 validation is 74/74: Face On/Off target MAE 3.112, non-target 0; left/right light 2.967; 25° head rotation 4.789; synthetic FaceMap R=.2/.8 response 6.713; PC Forward+/Mobile Forward MAE 0.374; shader compiler messages 0.
- Rebuilt and revalidated M0–M4 after M5: 67/67, 61/61, 88/88, 88/88, 94/94 and combined 30/30; post-regression M5 remains 74/74.
- Real Game View/Frame Debugger inspection, SRP Batcher status and GPU timing remain explicit manual acceptance items; automated captures used a real RTX 4060 D3D11 device, not `-nographics`.

## 2026-07-15 — Game View cascade-atlas interpolation fix

- Reproduced the ground rectangles in the actual 768×1680 Play Mode Game View on D3D11; no Scene View or `Camera.Render` substitute was used.
- Frame Debugger event 81 uniquely mapped the artifact to `M4_ShadowGround / M3_ShadowReceiver / M3ShadowReceiver`; stepping from event 80 to 81 introduced every rectangle and the clipped character shadow.
- Root cause: vertex-stage cascade selection interpolated shadow-atlas coordinates across Unity Plane grid cells.
- Moved world-to-shadow cascade selection to the fragment stage while retaining the screen-space shadow branch. Render state, Bias, cascades, ground geometry and ShadowCaster submission were not changed.
- Added an Editor-only reproducible Game View/Frame Debugger capture tool, including PC/Mobile, light rotation and camera near/mid/far evidence.
- Replaced the outdated validator contract with a stricter per-pixel receiver contract. Post-fix full regression: M0 67, M1 61, M2 88, M3 88, M4 94, combined 30; all zero failure.

## 2026-07-15 — M0–M4 regression audit and repair

- Rebuilt M0–M4 from current sources on Unity 6000.5.3f1 / URP 17.5.0 / real D3D11.
- Fixed double-sided ShadowCaster acne while preserving double-sided forward rendering.
- Removed the one-cascade Alpha Clip test bypass; production four-cascade IoU is 0.9890.
- Fixed M3 per-slot MPB precedence and M4 global-keyword/shared-material pollution.
- Fixed non-serialized M4 ControlMap/MatCap/Profile properties and scene-before-capture lifecycle.
- Added alpha-aware final-shading A/B for slots 24/28/29; target MAE 2.348/1.427/1.217, non-target 0.
- Added PC Forward+ / Mobile Forward real-render regression, Debug Hash audit, diff images and pipeline report.
- Added active-response probes: 25-degree head rotation changes M1 HeadAxis by MAE 3.638; transient RGBA ControlMap replacement produces four unique, channel-correct M4 debug outputs and restores the original materials afterward.
- Re-ran the complete latest-source chain; the combined audit is now 30/30 on D3D11 / RTX 4060 Laptop GPU.
- Disabled transparent ShadowCaster passes and removed M4 debug/toggle variants; deferred actual GPU timing to manual profiling.
- No M5 feature was implemented.

## 2026-07-15 — M5 long-skirt asset audit and Cull fix

- Parsed the standard PMX material texture indices and verified all 31 Renderer Material/BaseMap GUIDs, BaseMap SHA-256, ST, BaseColor, Control/Ramp/MatCap bindings and MPB overrides. All source/import hashes match; no binding-property MPB override exists.
- Isolated skirt slots 20–27 on the real GPU. Slot 21 renders the dark overlapping long-skirt layer and slot 26 renders the red overlapping layer from the same correct `T_Skirt` asset.
- Cull A/B proved slot 21's large dark surface is back-facing while slot 26's red surface is front-facing. Slot 21 `Cull Off` caused opaque depth competition and resolution-dependent red/black results.
- Applied the minimal baseline correction: slot 21 is generated with `Cull Back`; no model, UV, rig, texture, BaseColor, light or screenshot setting changed. M5 still uses Face SDF only on slots 0/1 and exact M4 assets on slots 2–30.
- Final visible D3D11 768×1680 Game View: M4/M5 lower-frame MAE 0 and red-skirt pixels 93,464/93,464. Frame Debugger: 116 events, zero failures; slot 21 is `Cull Back / ZWrite On / ZTest LessEqual / Blend One,Zero`.
- Final regression: M4 94/94, M5 94/94, M0–M4 combined 30/30, M5 Special Audit zero failures, shader compiler messages 0. M6–M9 remain unimplemented.

## 2026-07-16 — PC cascade, evidence provenance and lifecycle repair

- Reproduced the PC cascade contract defect in M3/M4/M5/M6/M8: the validators accepted `GetMainLight(input.shadowCoord)` even though URP 17.5 excludes `_MAIN_LIGHT_SHADOWS_CASCADE` from `REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR`. The pre-fix D3D11 M3 report passed 88 checks, proving the old test encoded the same defect.
- Updated only the five affected Forward passes to mirror URP Lit: conditional vertex shadow coordinates for supported variants and fragment `TransformWorldToShadowCoord(input.positionWS)` for cascades. The M3 validator now checks all five shaders and reports 93/93.
- Added D3D11/D3D12 cascade audits: each runs five shader compiler checks and ten real captures across all three split transitions, two camera views and two light extremes. Both APIs completed with zero failure/message. Pre/post Raw Shadow changed 104/272/450 pixels at near/mid/far distance.
- Added evidence sessions with a UUID/start UTC, structured report gates, source and artifact SHA-256/size/mtime records, API/device identity and final verification. Stale reports can no longer pass M8/M9 by file existence or substring. Negative tests cover artifact tamper, stale timestamp and source-fingerprint drift; `-nographics` black-render failure is retained.
- Added change detection to M4–M8 MPB writers. M7/M8/M9 now snapshot and restore their exclusively owned Renderer/Volume/Camera/Crystal state on disable/destroy; M7/M8 reset only their own MPB properties. Lifecycle, parameter refresh, material identity, scene switching and multi-instance isolation passed 18/18.
- Did not clear M0–M6 shared material-index blocks on component disable: overlapping `_Head*`/`_CastShadow*` ownership cannot be removed key-by-key with `MaterialPropertyBlock`. A safe arbitrary-stage unload requires a shared coordinator and is recorded as an architecture decision, not hidden behind destructive `Clear`.
- Fresh session `8a9a1c554fd7496faf035102cc223457`: M0–M9 and M0–M4 30/30 all zero failure; D3D11/D3D12 visible Frame Debugger 134/122 events and zero failure; Windows Player D3D12/PC/4-cascade/soft-shadow build zero errors/warnings. Formal target is Windows PC; Android/mobile device work is out of scope.
