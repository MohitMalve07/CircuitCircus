# Circuit Circus

A **4-player online multiplayer arcade kart racer** built in **Unity 6** (`6000.6.5f1`), designed to run in browsers (**WebGL**) using **Unity Netcode for GameObjects**, **Unity Relay (`wss`)**, and **Unity Authentication**.

---

## Key Features

* **Mandatory Player Identity & Lobby Sync**
  * Forces username validation before hosting or joining a match and displays the active player's name on the Main Menu (`PlayerName`).
  * Synchronizes a live pre-race roster (player slots, host label, and UTF-8-clamped `FixedString32Bytes` usernames) across all connected clients via a server-authoritative `NetworkList<LobbyPlayerInfo>` and RPCs (`LobbyNetwork` + `LobbyUI`).
* **NAT-Traversed WebGL Multiplayer (Unity Relay)**
  * Uses anonymous Unity Authentication (`AuthenticationService`) and encrypted WebSockets (`wss`) on `UnityTransport` so hosts and clients can play across different networks or browser tabs using a Relay join code.
* **Stabilized Arcade Kart Physics & Camera**
  * Owner-driven arcade acceleration, speed-scaled steering, wheel turn/spin animation, extra downward gravity, and torque + tilt-clamped anti-rollover stabilization (`PlayerMovement`).
  * Delayed post-spawn Cinemachine target initialization (`CameraMovement`) to avoid stale reference frames over Relay connections, paired with dynamic speed-based FOV scaling (`CurrentSpeed`) and engine pitch feedback (`EngineSound`).
* **Server-Authoritative Race Progression & Edge-Case Handling**
  * Sequential server-validated checkpoint and 3-lap tracking (`RaceProgress` + `CheckpointRegistry`).
  * Dynamic 4-slot spawn assignment with disconnect slot reuse (`SpawnManager`).
  * Physics-safe checkpoint respawn (`R` key) with automatic initial-spawn fallback (`RespawnSystem`).
  * Mid-race disconnect tracking (`RaceManager.activeRacers`) so player dropouts never soft-lock race completion, followed by leaderboard broadcasting (`ResultsUI`) and full `NetworkManager` cleanup when returning to Main Menu.
* **Additive Settings Menu & Persistence**
  * Non-destructive additive `Settings` scene overlay (`MenuManager` + `SettingsManager` + `GameSettings`) persisting Master Volume, Music Volume, Steering Sensitivity (`0.5x`–`1.5x` steering multiplier), and MiniMap toggle state (`OnnToggle` / `OffToggle` sprites + camera/UI visibility) via `PlayerPrefs`.

---

## Tech Stack

| Category | Technology / Version |
| :--- | :--- |
| **Engine** | Unity `6000.6.5f1` (Built-in Render Pipeline) |
| **Target Platform** | WebGL (`IL2CPP`, `.NET Standard 2.0`) |
| **Networking** | Unity Netcode for GameObjects (`2.13.3`) |
| **Relay & Auth** | Unity Services Multiplayer / Relay (`2.3.3`, `wss`), Authentication (`3.8.0`) |
| **Camera & World** | Cinemachine (`6.6.0`), Terrain Tools (`5.3.3`), Adaptive Performance (`6.0.0`) |
| **UI** | Unity uGUI (`com.unity.ugui` `2.6.0`) |

---

## Controls

| Action | Input |
| :--- | :--- |
| **Accelerate / Reverse** | `W` / `S` or `Up` / `Down` Arrow |
| **Steer** | `A` / `D` or `Left` / `Right` Arrow |
| **Respawn at Checkpoint** | `R` |

---

## Architecture Overview

### Scene Flow
* [`Assets/Scenes/Main Menu.unity`](Assets/Scenes/Main%20Menu.unity) — Username confirmation modal, Unity Services initialization, Relay Host/Join UI, and live `LobbyNetwork` player list.
* [`Assets/Scenes/Game.unity`](Assets/Scenes/Game.unity) — Networked race track, `SpawnManager`, `CheckpointRegistry`, `RaceManager`, HUD (`Countdown`, `Lap`, `Speedometer`, `MiniMap`, `LapTimer`), and `ResultsUI`.
* [`Assets/Scenes/Settings.unity`](Assets/Scenes/Settings.unity) — Additive overlay loaded from Main Menu (`PlayerPrefs`-backed audio, sensitivity, and minimap configuration).

### Network Authority Split

| System | Authority | Mechanism |
| :--- | :--- | :--- |
| **Kart Movement & Steering** | Owner Client (`PlayerMovement`) | Local physics + `ClientNetworkTransform`; non-owners set `Rigidbody.isKinematic = true` |
| **Lobby Roster & Usernames** | Server (`LobbyNetwork`) | Client sends `SubmitUsernameRpc` → Server replicates `NetworkList<LobbyPlayerInfo>` |
| **Spawn Grid (Up to 4 Players)** | Server (`SpawnManager`) | Assigns slots on connect, despawns cleanly and frees slots on disconnect |
| **Checkpoints & Lap Validation** | Server (`RaceProgress`) | Owner notifies trigger hit via `ReportCheckpointServerRpc` → Server validates order & writes `NetworkVariable`s |
| **Race Finish & Leaderboard** | Server (`RaceManager`) | Tracks active racers + finish order, ignores dropped clients, broadcasts final podium via `ShowResultsClientRpc` |

---

## Project Structure

```text
Assets/
├── Prefabs/                     # Networked Player kart prefab & checkpoint prefabs
├── Scenes/
│   ├── Main Menu.unity          # Username gate, Relay host/join, Lobby
│   ├── Game.unity               # Track, HUD, RaceManager, Results
│   └── Settings.unity           # Additive Settings UI
└── Scripts/
    ├── NetworkUI.cs             # Username validation, Relay host/join & auth flow
    ├── CameraMovement/          # Cinemachine follow/FOV & minimap camera follow
    ├── LapCounter/              # Race & per-lap timing UI
    ├── Player/                  # Kart physics, NetworkPlayer username, RaceProgress, HUD gauges
    ├── RespawnReset/            # SpawnManager, Checkpoint, CheckpointRegistry, RespawnSystem
    └── UIManager/               # LobbyNetwork, LobbyUI, GameManager, RaceManager, ResultsUI, GameSettings, SettingsManager, MenuManager
```

---

## Running Locally in Unity

1. Open the project in **Unity `6000.6.5f1`**.
2. Link a Unity Gaming Services Project ID in **Edit → Project Settings → Services** and enable **Relay** in the Unity Dashboard.
3. On `NetworkManager`'s `UnityTransport` in `Assets/Scenes/Main Menu.unity`, keep **Use WebSockets** and **Use Encryption** enabled for WebGL Relay builds.
4. Ensure all three scenes (`Main Menu`, `Game`, `Settings`) remain enabled in **File → Build Profiles / Build Settings** (`Assets/Scenes/Main Menu.unity` at index `0`).
5. Press **Play** in `Main Menu`, confirm a username, click **Host** to generate a Relay join code, and share the join code with up to 3 other players clicking **Join**.
