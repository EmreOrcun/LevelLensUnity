# LevelLens Unity Package

Project-agnostic in-game level review and QA platform for Unity games.

## Installation

Add this package to your Unity project's `Packages/manifest.json`:

```json
"dependencies": {
  "com.anotherworld.levellens": "https://github.com/EmreOrcun/LevelLensUnity.git#main"
}
```

Or install via Unity Package Manager:
- **Window** > **Package Manager**
- Click **+** > **Add package from git URL...**
- Paste: `https://github.com/EmreOrcun/LevelLensUnity.git`

## Integration

Implement `ILevelLensAdapter` in your game to bridge level lifecycle events and metadata to `LevelLensManager`.
