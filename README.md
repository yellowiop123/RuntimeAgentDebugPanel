# Unity Runtime Agent Debug Panel

A single-file runtime debug inspector for Unity.

This tool provides an in-game IMGUI panel for inspecting scene objects, monitoring runtime metrics, viewing logs, and editing selected GameObject transforms during Play Mode.

## Features

- Toggle debug panel with F1
- Runtime scene object index
- Search and select GameObjects
- Inspect object path, tag, layer, active state, and components
- Edit Transform position, rotation, and scale at runtime
- Capture Unity logs with warning and error highlighting
- Monitor FPS, memory usage, scene info, and object count
- Built-in command console
- Quick actions for resetting transform, focusing camera, and toggling active state

## Usage

1. Copy `RuntimeAgentDebugPanel.cs` into your Unity project.
2. Create an empty GameObject in the scene.
3. Attach `RuntimeAgentDebugPanel` to the GameObject.
4. Enter Play Mode.
5. Press `F1` to open or hide the panel.

## Use Cases

- Runtime gameplay debugging
- Level validation
- Prototype tuning
- Scene object inspection
- Reducing repetitive `Debug.Log` usage
- Checking runtime object state without leaving Play Mode

## Screenshots

Add screenshots here after running the tool in Unity.

## License

MIT
