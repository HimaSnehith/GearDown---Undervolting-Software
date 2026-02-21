# Gear Down

Gear Down is a lightweight, native Windows hardware governor designed to manage CPU and GPU thermals on modern gaming laptops and desktop systems. 

## The Architecture & Anti-Cheat Safety
Most aggressive thermal throttling and undervolting utilities rely on kernel-level drivers (such as `WinRing0.sys`) to write directly to motherboard SMU registers. While effective, these drivers operate in Ring 0 and are frequently flagged or blocked by modern kernel-level anti-cheat engines (e.g., Riot Vanguard, Easy Anti-Cheat, BattlEye). 

Gear Down was engineered to achieve significant temperature reduction entirely in User Mode (Ring 3). By utilizing native Windows APIs and official driver interfaces, it remains 100% safe for competitive multiplayer environments without risking account bans.

## Core Features
* **Native CPU Power Governance:** Modifies Windows Power Plan states via `powercfg` to cap maximum processor power draw. This limits thermal output natively without injecting code or running resource-heavy background loops.
* **Dynamic GPU Frequency Range:** Utilizes the Nvidia System Management Interface (`nvidia-smi`) to establish a strict ceiling on GPU clock speeds, while still allowing the hardware to naturally downclock to idle states (e.g., 210 MHz) to conserve power during load screens or desktop use.
* **Zero Overhead:** The application relies on event-driven UI updates rather than aggressive polling loops. The background process consumes 0.00% CPU.
* **State Persistence:** Automatically serializes user limits to a local JSON configuration, reapplying target hardware states silently upon application boot.
* **Fail-Safe Restoration:** Integrates cleanly with the Windows System Tray. Instantly relinquishes control and restores all hardware to factory default settings the moment the process is terminated.

## ScreenShots
<img width="506" height="830" alt="Screenshot 2026-02-21 222641" src="https://github.com/user-attachments/assets/0ca53857-ee52-467e-86a4-33af8ec336fa" />
<img width="504" height="834" alt="Screenshot 2026-02-21 222627" src="https://github.com/user-attachments/assets/5f388786-2efe-4ea7-b9bb-dba0dcfab679" />


## Installation
1. Navigate to the **Releases** tab on this repository.
2. Download the latest `Setup.exe` (Highly compressed, framework-dependent build).
3. Run the installer. 
*Note: The application requires Administrator privileges to successfully modify system-level power plans and interface with the Nvidia driver.*

## System Requirements
* Windows 10 / Windows 11 (64-bit)
* .NET 8.0 Desktop Runtime
* Nvidia Dedicated GPU (GTX 10-Series or newer)

## Development
Built with C# and WPF (Windows Presentation Foundation). The project is compiled as a framework-dependent executable to minimize deployment payload, handling dependencies via the host operating system.
