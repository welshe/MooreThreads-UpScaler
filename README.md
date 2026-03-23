# Moore Threads UpScaler

A lightweight frame generation tool that integrates OptiScaler to boost FPS in any DirectX 11/12 or Vulkan game.

## Features

- **Frame Generation** — Generate intermediate frames to double (X2), triple (X3), or quadruple (X4) your FPS
- **Multiple Upscalers** — Choose between FSR 2.2, FSR 3.0, XeSS, or DLSS
- **Game Profiles** — Save settings per game for quick access
- **Automatic Injection** — One-click DLL injection into running games

## How It Works

Moore Threads UpScaler leverages [OptiScaler](https://github.com/cdozdil/OptiScaler), an open-source middleware that intercepts rendering calls and applies frame generation. When you inject into a game:

1. The `version.dll` is copied to the game's directory
2. An `OptiScaler.ini` config file is created with your settings
3. On next launch, the game loads the DLL and applies frame generation

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- DirectX 11, DirectX 12, or Vulkan compatible GPU
- Administrator privileges (for some game directories)

## Usage

1. **Launch your game** — The game must be running to detect it
2. **Select the game** — Choose from the target dropdown
3. **Configure settings** — Enable frame generation, select multiplier and upscaler
4. **Click INJECT** — DLLs are copied to the game directory
5. **Restart the game** — Frame generation is now active

Press **Insert** in-game to toggle the OptiScaler overlay.

## Notes

- Frame generation works best with a base FPS of 30-60
- Not recommended for online multiplayer games (may trigger anti-cheat)
- GPU must support the selected upscaler (DLSS requires NVIDIA, XeSS works best on Intel ARC)

<img width="735" height="530" alt="image" src="https://github.com/user-attachments/assets/74051a8f-b4b2-4740-b1c1-e2ded73e81dd" />
