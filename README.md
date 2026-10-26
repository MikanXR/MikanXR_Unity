# MikanXR Unity Client

A Unity package that mirrors a running [MikanXR](https://github.com/MikanXR/MikanXR) editor into a Unity scene and renders back into it. The editor's scenes, stages, anchors, stencils and cameras appear as GameObjects under a client root; each Mikan camera renders the Unity scene with the editor's calibrated intrinsics and publishes color and depth to the editor through Spout, where the compositor layers them over the video.

The package is managed code only. It talks to the editor over the websocket client protocol with `System.Net.WebSockets` and writes its shared textures through [KlakSpout](https://github.com/keijiro/KlakSpout), so a host application loads one assembly and no native Mikan DLL. The same package is what the [VNyan plugin](https://github.com/MikanXR/MikanXR_VNyan) compiles into its DLL.

## Layout

- `Packages/com.mikanxr.unity/`: the package
	- `Runtime/Bindings/`: the MikanXR C# bindings, copied from a client SDK by `FetchMikan.bat` and never edited here. `Native/` within it is host-optional (see Hosts).
	- `Runtime/Scripts/`: the client, the data store, the scene mirror, the camera, the Spout writer. `FileSystem/` within it is host-optional.
	- `Runtime/Shaders/`: the depth packing shader
	- `Runtime/Resources/MikanXR/`: the KlakSpout resources asset the senders need at runtime
	- `Editor/`: the test scene setup menu
	- `Tests/Runtime/`: play mode tests that run against a live editor
	- `Tests/Editor/`: an edit mode test that keeps the runtime compilable in a sandboxed host
- `Assets/`: the test project around the package (the `MikanTestScene` scene, a sample character)

## Requirements

- Unity 2022.3.62f3, the built-in render pipeline, Direct3D 11 or 12 (KlakSpout supports no other API)
- A MikanXR editor on the same machine, listening on the default client port (8080)
- The Keijiro scoped registry in the project manifest for `jp.keijiro.klak.spout`, and `com.unity.nuget.newtonsoft-json`

## Updating the bindings

The bindings come from a MikanXR client SDK: `dist/ClientSDK/Win64` of a MikanXR build, or an unpacked `Mikan_<version>_Win64_ClientSDK.zip` release asset. From the package folder:

```bat
FetchMikan.bat D:\path\to\ClientSDK\Win64
```

or set `MIKAN_CLIENT_SDK` and run it bare. It replaces `Runtime/Bindings` and records the SDK version beside it.

## Using the client

Add a `MikanClient` component to an empty GameObject (the `MikanXR/Add Client To Open Scene` menu does this). Its transform is where the Mikan world sits in the Unity scene. On play it connects, mirrors the editor, and keeps the mirror current from the editor's property updates.

- Stages carry the editor's tracking spaces. Move a stage object in the Unity scene and call `PinHere` on it (or set a placement in the settings) and the client keeps that placement over the editor's transform for a stage of that name.
- Cameras sit under their stage. Each has a disabled render camera driven by the editor's frame pacing, a frustum helper, and a `MikanSpout camera<N>` object holding its senders.
- World scale: the editor works in meters and its scenes are tabletop sized. `World Scale` on the client sets Unity units per Mikan meter and scales the whole mirrored scene, cameras and clip planes included, under the client root; the root's own transform stays yours.
- Layers: the Mikan cameras render every layer but Unity's UI layer. The frustum helpers and the stencil previews are ordinary scene objects that hide themselves around each Mikan render, so your own cameras see them (`Show Camera Frustums`, `Show Stencils`, `Show Light Cones`) and the published frames never do. A host's spare layers are never assumed to render.
- Lights: a light environment the editor captured becomes the scene's ambient probe and, when its directionality is confident, a directional key light, both under `Apply Light Environment`. The editor's RGB spot lights become Unity spot lights under `Apply Spot Lights`, lit from the live DMX channels the client subscribes to. `Light Intensity Scale` multiplies both.
- Settings persist through an `IMikanSettingsProvider`. The default is none; `Persist Settings To Json` on the client writes a file under the persistent data path, and a host supplies its own store.

## Hosts

A host that loads the client as a plugin rather than building it into a project supplies two things before the client starts:

- a settings store, through `MikanClient.SettingsProvider` (an `IMikanSettingsProvider`)
- the package's shaders and assets, which its build never saw, through `MikanShaderLibrary.SetBundle` for an AssetBundle or `MikanShaderLibrary.SetAssetSource` for any other `IMikanAssetSource`, which is handed the package's Resources-relative paths

Two more hooks are optional:

- `MikanClient.SetRenderCullingMask` names the layers the Mikan cameras render, for a host with its own layer scheme. The default is every layer but UI.
- `MikanSceneAnnouncer` raises `SceneAnnounced` with the editor's current scene id and name, once the scene's values have arrived, for a host that reacts per scene.

A sandboxed host that forbids `System.IO`, `System.Reflection` and P/Invoke, such as a Warudo plugin mod, compiles `Runtime/` from source without two folders:

- `Runtime/Bindings/Native/`, the P/Invoke transport and writer and the native `MikanAPI()` constructor. The client runs on the managed websocket transport either way.
- `Runtime/Scripts/FileSystem/`, the JSON file settings provider and the client's default that creates it. Without it the default is no persistence.

The edit mode test compiles exactly that subset and scans it against the Warudo mod tools' rules, so a change that reaches for a forbidden API fails here rather than at a mod export.

Conventions: Mikan space is right handed with -Z forward and Unity's is left handed with +Z forward, so the package mirrors Z once, in `MikanMath`. Camera poses on the wire are relative to the camera's stage. Depth is published as the editor's `PACK_DEPTH_RGBA` encoding.

## Testing against the editor

Start the editor with a project loaded, then run the play mode tests in the Test Runner, or from a shell:

```bat
"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults TestResults.xml -logFile unity_tests.log
```

The tests connect, check that every component the editor reports has an object under the root, that a property written through the API patches the cached values, that model stencils carry their geometry, and, while the editor's compositor runs a camera, that frames render and publish. Without an editor they are ignored rather than failed. The property test nudges a stage's bounds and puts them back.

The edit mode test needs no editor:

```bat
"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults.xml -logFile unity_tests.log
```

## Stencils, knockout and shadows

`Render Shadows` (on by default) and `Render Depth` (off by default) choose which of the extra buffers the cameras publish beside color. The knockout alone covers occlusion by real geometry, so depth is only for a compositor graph that keys on it; the editor's render target is allocated without it until it is turned on.

With `Render Stencils` on (the default) every stencil renders into the Mikan cameras twice. In the color pass it is a holdout: it writes depth and no color, so it cuts the character out where real geometry stands in front and leaves the alpha clear for the video. In the shadow pass it is a catcher: the scene renders once with shadows and once without into white targets, and the shadow buffer is their ratio, so only the stencils' shadows survive and the host's own materials are never touched. `Shadow Strength` sets how dark a fully shadowed point gets. The editor's `unity_client_shadow_graph.compgraph` composites the three buffers: video, then the shadow buffer multiplied, then the color buffer over it. The editor's per-stencil cull axis and disabled flag are honored per camera. With `Render Stencils` off the Mikan cameras hide the stencils for their renders.

A model stencil's mesh is a GLB the client fetches with `UnityWebRequest` from the editor's HTTP server at the stencil's `geometry_url`, and fetches again whenever that url changes. `MikanGlbReader` decodes it without a glTF package, so a host that ships none (VNyan) needs nothing extra. A stencil whose model carries a texture, a depth proxy's captured frame or an occlusion mesh's atlas, shows that texture on its host-view preview, opaque and unlit so it sits in the depth buffer like scene geometry, instead of the translucent tint. The pass materials ignore it.

The shadows come from the scene's shadow casting lights, which include the Mikan key light while it is on. Color space: the published color buffer is sRGB encoded in either Unity color space (a linear project's render texture encodes on write, a gamma project's shaders write display values) and the editor decodes it, while the depth and shadow textures are created linear so their packed depth and shadow ratio travel as raw numbers. KlakSpout's sender copies its source into a buffer of its own, which a linear project makes sRGB, so there the depth and shadow go out through sRGB staging textures (`MikanXR/SrgbRawBytes` writes the exact sRGB decode of each byte) and arrive byte for byte. The package's shaders live in `Runtime/Resources/MikanXR/Shaders` so a player build keeps them; `MikanXR/Build Shader Bundle` packs them, with the Spout resources, into `mikanxr_shaders.bundle` for a host that loads the client as a plugin.

## Lights

The light environment's coefficients are a radiance environment in the capturing stage's space. A spherical function cannot be mirrored into Unity's frame by rewriting its coefficients, so `MikanAmbientProbe.Project` samples the environment over the sphere, clamped at zero, converts and rotates each sample direction like any other direction (the stage's world rotation included, so a rotated root or stage stays right), and projects the samples onto Unity's ambient probe. The scene's previous ambient settings are restored when the environment stops driving them. The key light follows the Unreal plugin's rule: hidden while `directionality` is below 0.25, since the direction is noise in a near-ambient scene.

A spot light's color is its three DMX channels and its output is the fixture's wattage times the channels' mean fraction times its lumens per watt, as in the Unreal plugin. Unity's built-in intensity has no unit, so the output becomes the fixture's on-axis candela, lumens over the cone's solid angle, and 5 candela map to an intensity of one; a narrow fixture is therefore as bright as it is, and `Light Intensity Scale` tunes the whole set. The spot lights are forced to per-pixel rendering, since the directional lights would otherwise take the forward renderer's few per-pixel slots and leave the cones as vertex lighting. One light environment drives the ambient probe and the key light at a time, the one with the lowest component id. The cone opens along the fixture's +Z, its range scales with the world scale, and `Show Light Cones` draws its outline in the light's color for your own cameras, hidden from the Mikan cameras like the other helpers.

## Status

Milestones 1 to 3 of the Unity client: connection, the mirrored scene, the camera path with color and packed depth, stencil knockout and the shadow buffer, the editor's lights, and the VNyan plugin that consumes this package.
