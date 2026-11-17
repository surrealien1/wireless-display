# Wireless Display for Windows

This project is a small Windows-only proof-of-concept for a wireless second monitor experience between two PCs running Windows.

Features:
- Sender mode: captures the primary screen and streams it over TCP.
- Receiver mode: listens for frames and displays them in a borderless fullscreen window.
- Intended for extended display use on a second Windows PC.
- Designed for local Wi‑Fi or LAN networks.

Important:
- This is a prototype, not a full commercial-grade remote display engine.
- It is intentionally limited to Windows-to-Windows communication.
- Performance depends on Wi‑Fi speed, screen resolution, and CPU power.

## Quick start

1. Open the solution in Visual Studio 2022 on Windows.
2. Build the project.
3. Run the app on both computers.
4. On the sender PC:
   - choose Sender mode
   - enter the receiver PC IP address
   - set the port (default 5000)
   - click Start
5. On the receiver PC:
   - choose Receiver mode
   - set the same port
   - click Start
6. Drag the receiver window to the second monitor if needed.

## Notes

- The sender captures the primary display only.
- To act like an extended display, use the receiver window on the second monitor.
- For better image quality, use a LAN or strong Wi‑Fi signal.

## Project structure

- `WirelessDisplay/` — Windows desktop app source code
- `WirelessDisplay.sln` — Visual Studio solution

## Requirements

- Windows 10 or newer
- .NET 8 SDK
- Visual Studio 2022 with desktop development workload

## Legal note

This project is a learning/demo implementation. It is meant for local desktop use and not intended as a replacement for commercial display software.
