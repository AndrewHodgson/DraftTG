# Live WGC capture, P1P7

Captured locally through DraftTG's one-shot Windows.Graphics.Capture backend on
2026-10-03 at 22:42:03 UTC (17:42:03 CDT). Original PNG is unchanged, 1325 x 1131,
SHA256 `B744651BB9FFF71B2EC57FC532D6EC3A5D0C3446CE77DCF08254F3756B147EC7`.
The calibrated Arena crop contains game cards/background only; no desktop,
account names, chat, or other applications. No screenshot was uploaded.

Arena HWND was 0x140C8E, client 2560 x 1440, DPI 1.0. Pixel SHA256 from the
opaque BGRA output was `B3E4F53C4AF8F62BBC1BDA83F174093F1A03B0C6DCE23773C44B989F158B0F81`.

Manifest retains the actual P1P7 payload candidate order. ExpectedVisualOrder
is the independently verified screen order, not a reversal or sort of payload
order. Reference thumbnails are the existing exact Scryfall printing IDs from
the P1P7 audit, copied locally without new network requests.

The unchanged registered artwork matcher accepts 8/8 at similarity 0.94 and
ambiguity margin 0.07. This regression exercises a captured image, not a fake
Direct3D compositor. It does not validate future live pack transitions.
