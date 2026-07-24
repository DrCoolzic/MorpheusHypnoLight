#!/usr/bin/env python3
"""
Morpheus HypnoLight BLE transfer and control script.

Requires `bleak` (pip install bleak).
"""

import argparse
import asyncio
import struct
import sys
from pathlib import Path

from bleak import BleakClient, BleakScanner

# 128-bit UUIDs used by the firmware (must match doc/ble_protocol.md).
CMD_UUID = "D4C38BC0-4F25-AF02-8F15-A1B5C2A60001"
STATUS_UUID = "D4C38BC0-4F25-AF02-8F15-A1B5C2A60002"


class BleProtocolError(Exception):
    """Raised when the device reports a non-zero status result."""

    def __init__(self, opcode: int, result: int) -> None:
        self.opcode = opcode
        self.result = result
        super().__init__(f"Command 0x{opcode:02x} failed with result 0x{result:02x}")


async def find_device(name: str, timeout: float = 10.0) -> str:
    """Scan for a BLE peripheral with the given advertised name."""
    print(f"Scanning for '{name}'...")
    devices = await BleakScanner.discover(timeout=timeout)
    for device in devices:
        if device.name == name:
            return str(device.address)
    raise RuntimeError(f"Device '{name}' not found")


async def send_command(
    client: BleakClient, opcode: int, payload: bytes = b"", timeout: float = 5.0
) -> int:
    """Write a command and wait for the matching status notification.

    Returns the result byte (0x00 for success).
    """
    future: asyncio.Future[bytes] = asyncio.get_running_loop().create_future()

    def notification_handler(_sender, data: bytearray) -> None:
        if len(data) >= 2 and data[0] == opcode:
            if not future.done():
                future.set_result(bytes(data[:2]))

    await client.start_notify(STATUS_UUID, notification_handler)
    try:
        await client.write_gatt_char(
            CMD_UUID, bytes([opcode]) + payload, response=False
        )
        try:
            await asyncio.wait_for(future, timeout=timeout)
        except asyncio.TimeoutError as exc:
            raise RuntimeError(f"No status for command 0x{opcode:02x}") from exc
    finally:
        await client.stop_notify(STATUS_UUID)

    result = future.result()[1]
    if result != 0x00:
        raise BleProtocolError(opcode, result)
    return result


async def upload_sequence(client: BleakClient, compact: bytes, chunk_size: int) -> None:
    """Transfer a compact sequence using LOAD_START/LOAD_CHUNK/LOAD_COMMIT."""
    total = len(compact)
    print(f"Uploading {total} bytes in chunks of {chunk_size} bytes")

    await send_command(client, 0x10, struct.pack("<I", total))

    offset = 0
    while offset < total:
        block = compact[offset : offset + chunk_size]
        payload = struct.pack("<H", offset) + block
        await send_command(client, 0x11, payload)
        offset += len(block)
        print(f"  -> offset {offset}/{total}")

    print("Committing sequence")
    await send_command(client, 0x12)
    print("Sequence loaded")


async def main() -> int:
    parser = argparse.ArgumentParser(
        description="Transfer a compact sequence to a HypnoLight device."
    )
    parser.add_argument(
        "--name",
        default="HypnoLight",
        help="Advertised BLE device name (default: HypnoLight)",
    )
    parser.add_argument(
        "--address",
        help="BLE address (optional, otherwise scans by name)",
    )
    parser.add_argument(
        "--binary",
        default="build/demo_compact.bin",
        help="Compact binary sequence file",
    )
    parser.add_argument(
        "--chunk",
        type=int,
        default=17,
        help=(
            "Sequence bytes per LOAD_CHUNK. "
            "Safe default 17 fits a 23-byte ATT MTU (1 opcode + 2 offset + 17 data)."
        ),
    )
    parser.add_argument(
        "--play",
        action="store_true",
        help="Send PLAY after the transfer",
    )
    parser.add_argument(
        "--pause",
        action="store_true",
        help="Send PAUSE instead of PLAY",
    )
    parser.add_argument(
        "--stop",
        action="store_true",
        help="Send STOP after the transfer",
    )
    parser.add_argument(
        "--brightness",
        type=int,
        default=None,
        help="Set global brightness (0-100)",
    )
    args = parser.parse_args()

    binary_path = Path(args.binary)
    if not binary_path.is_file():
        print(f"Error: binary file not found: {binary_path}", file=sys.stderr)
        return 1

    address = args.address
    if address is None:
        address = await find_device(args.name)

    print(f"Connecting to {address}...")
    async with BleakClient(address) as client:
        compact = binary_path.read_bytes()
        await upload_sequence(client, compact, args.chunk)

        if args.brightness is not None:
            if not 0 <= args.brightness <= 100:
                print("Error: brightness must be 0-100", file=sys.stderr)
                return 1
            print(f"Setting brightness to {args.brightness}")
            await send_command(client, 0x05, bytes([args.brightness]))

        if args.pause:
            print("Sending PAUSE")
            await send_command(client, 0x02)
        elif args.stop:
            print("Sending STOP")
            await send_command(client, 0x03)
        elif args.play:
            print("Sending PLAY")
            await send_command(client, 0x01)

    print("Done")
    return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
