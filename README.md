# Morpheus HypnoLight

A low-cost Audio-Visual Stimulation (AVS) / Flicker Light Stimulation (FLS) device built around an ESP32-S3.

The device uses rhythmic light variation to influence brain activity and induce altered states of consciousness. It is controlled remotely via Bluetooth Low Energy or Wi-Fi, and can drive multiple LED groups with independent frequencies, waveforms, brightness, and phase modulation.

## Repository Structure

```text
MorpheusHypnoLight/
├── doc/                        # Project documentation
│   ├── MHLSpecification.md     # Product specification and features
│   ├── HardwarePrototype.md    # Prototype hardware, pinout, wiring, power budget
│   ├── SoftwareArchitecture.md # Firmware architecture and component design
│   ├── SoftwareSetup.md        # ESP-IDF + VS Code installation guide
│   └── images/                 # Diagrams, schematics, photos
├── firmware/                   # ESP-IDF firmware project
├── sim/                        # Simulation and PC-side tools
└── .venv/                      # Python virtual environment (for PC-side tools)
```

## Documentation

|Document|What you will find|
|--------|------------------|
|[doc/MHLSpecification.md](doc/MHLSpecification.md)|Project objectives, features, LED architecture, oscillator design, sequence engine, real-time mode|
|[doc/HardwarePrototype.md](doc/HardwarePrototype.md)|Bill of materials, power supply, power budget, thermal design, ESP32-S3 pinout, wiring, main/control electronics, enclosure|
|[doc/SoftwareArchitecture.md](doc/SoftwareArchitecture.md)|Firmware layout, component responsibilities, data flow, build/flash instructions|
|[doc/SoftwareSetup.md](doc/SoftwareSetup.md)|Step-by-step installation of ESP-IDF, VS Code extension, and Hello World flash|

## Quick Start

1. Set up the development environment: follow [doc/SoftwareSetup.md](doc/SoftwareSetup.md).
2. Review the hardware prototype: [doc/HardwarePrototype.md](doc/HardwarePrototype.md).
3. Build and flash the firmware from the `firmware/` directory:

   ```bash
   cd firmware
   idf.py set-target esp32s3
   idf.py build
   idf.py -p PORT flash monitor
   ```

   In VS Code, use the ESP-IDF extension commands: **Build your Project**, **Flash your Project**, **Monitor your Device**.

## Hardware at a Glance

- **MCU**: ESP32-S3-DevKitC-1 (N16R8 module, 8MB PSRAM, 16MB flash)
- **LED drivers**: 3× SparkFun PicoBuck (AL8805, 9 channels total) for the prototype; final PCB will use AL8860 LED drivers
- **LEDs**: 32× 1W cold white (PB1..PB4 peripheral banks) + 4× 3W warm white (CG central group)
- **Power**: 24V / 5A supply, 5V buck converter for the ESP32
- **Cooling**: PWM-controlled fan driven by a TMP36 temperature sensor
- **Control interface**: 4× Adafruit NeoRotary 4 (I2C rotary encoders) + 1× I2C OLED display

## Status

This project is currently in the **hardware prototype and firmware architecture** phase. The specification and hardware documentation are in place; the firmware is under development. Once the prototype is validated, a dedicated `HardwarePCB.md` document will cover the final PCB design and production files.

## License

- Firmware and documentation: [MIT License](LICENSE)
- Hardware designs and production files: [CERN Open Hardware Licence v2 — Permissive](LICENSE-HARDWARE)
