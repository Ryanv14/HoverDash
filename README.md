# Hover Dash (Unity + Online Leaderboard)

Fast hover-runner with stars, hazards, a finish gate, and an online leaderboard.

> **Important:** The game **does not use `localhost`** for the API.  
> Deploy the server to a public host (e.g. **Render**) and point the Unity client at that URL.

---

## Features

- Hover vehicle with jump, banking, and multi-point hover suspension  
- Procedural track generation (ground, walls, weighted obstacles, stars, finish line)  
- Start/finish flow with **client-side snapshot score**  
- WebGL-friendly leaderboard (Express + MongoDB)  
- Simple UI (start prompt, game over, level complete, leaderboard)

---

## Requirements

- **Unity** 2021+  
- **Node.js** 18+ (for the API)  
- **MongoDB** (Atlas or other managed instance)  
- A public host for the API (e.g. **Render**)

---

## Project Layout

    Assets/Scripts/        // gameplay, UI, leaderboard client
    Leaderboard-API/server.js              // Express + MongoDB API
    Leaderboard-API/package.json           // server dependencies (ESM)

---

## Deploy the API (Render)

1. Create a **Web Service** on https://render.com/ and point it at your repo/fork.  
2. **Environment variables**
   - `MONGODB_URI` — your MongoDB connection string
   - `PORT` — optional (Render provides one automatically)
3. **Build command:** `npm install`  
   **Start command:** `node server.js`
4. Deploy and note your base URL, e.g. `https://your-api.onrender.com`

CORS in `server.js` already allows Render and itch.io domains. If you host the WebGL build elsewhere, add that origin in `server.js`.

---

## Point Unity to your API

In the scene that has **`LeaderboardClient`**:

- Set **BackendBaseUrl** to your public API URL, e.g.  
  `https://your-api.onrender.com` *(no trailing slash)*

**Scenes**

- `MainMenu`  
- `Level1`  
- `Level2` → leaderboard scene (`leaderboardLevelId = "level-2"`)  
- `ZenLevel` → jumps are free (`GameRules.JumpsAreFreeThisScene = true` for this scene)

**Tagging**

- Ensure the player GameObject is tagged **`Player`** (used by pickups, hazards, finish line).

---

## Generate a Track (Editor)

The **TrackGenerator** builds ground, walls, obstacles, stars, and a finish line.

1. Add an empty GameObject → attach **`TrackGenerator`**.  
2. Configure:
   - **Track:** `trackLength`, `lanes`, `halfTrackWidth`
   - **Ground/Walls:** toggle and assign materials
   - **Obstacles:** add entries under *WeightedObstacle Prefabs* (`prefab` + `weight`)
   - **Stars:** assign `starPrefab` (optional)
   - **Finish Line:** assign `finishLinePrefab` (optional)
3. In the component’s context menu (⋯) choose **`Generate (Editor)`**.  
   Use **`Clear Generated`** to rebuild from scratch.
4. Tweak and regenerate as needed, then save the scene.

Notes:
- Generation is deterministic for a given **seed**.
- At runtime you can call `GetComponent<TrackGenerator>().Generate();`.

---

## Build (WebGL)

1. **File → Build Settings**
   - Add `MainMenu`, `Level1`, `Level2`, `ZenLevel` to **Scenes In Build**.
   - Select **WebGL** → **Switch Platform**.
2. **Build** → upload to itch.io (or any static host).  
   With the API on Render and CORS set, the leaderboard will work out of the box.

---

## License

    MIT License

    Copyright (c) 2025 Hover Dash contributors

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the “Software”), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
