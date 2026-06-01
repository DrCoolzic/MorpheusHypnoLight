from __future__ import annotations
from typing import Any
from pathlib import Path
import json

# https://json2csharp.com/code-converters/json-to-python


class Directories:
    def __init__(self, read=Path(""), write=Path(""), sound=Path("")) -> None:
        self.read: Path = read
        self.write: Path = write
        self.audio: Path = sound

    def __str__(self) -> str:
        mess = f"Directories: read='{self.read}'\n   write='{self.write}'\n   sound='{self.audio}'"
        return mess

    @staticmethod
    def from_dict(obj: Any) -> Directories:
        read = obj.get("read") if "read" in obj else ""
        write = obj.get("write") if "write" in obj else ""
        sound = obj.get("sound") if "sound" in obj else ""
        return Directories(Path(read), Path(write), Path(sound))

    @staticmethod
    def to_dict(obj: Directories) -> dict:
        data: dict = dict()
        data["read"] = str(obj.read)
        data["write"] = str(obj.write)
        data["sound"] = str(obj.audio)
        return data


class RunOpt:
    def __init__(self, set_start_time=False, led_ovr=False) -> None:
        self.set_start_time: bool = set_start_time
        self.led_ovr: bool = led_ovr

    def __str__(self) -> str:
        mess = (
            f"Run options: set_start_time={self.set_start_time}, led_ovl={self.led_ovr}"
        )
        return mess

    @staticmethod
    def from_dict(obj: Any) -> RunOpt:
        time_overlap = obj.get("set_start_time") if "set_start_time" in obj else False
        led_overlap = obj.get("led_overlap") if "led_overlap" in obj else False
        return RunOpt(time_overlap, led_overlap)

    @staticmethod
    def to_dict(obj: RunOpt) -> dict:
        data: dict = dict()
        data["set_start_time"] = obj.set_start_time
        data["led_overlap"] = obj.led_ovr
        return data


class RepairOpt:
    def __init__(self, time_ovr="disabled", led_ovr="disabled") -> None:
        self.time_ovr: str = time_ovr
        self.led_ovr: str = led_ovr

    def __str__(self) -> str:
        mess = (
            f"Repair options: time_overlap={self.time_ovr}, led_overlap={self.led_ovr}"
        )
        return mess

    @staticmethod
    def from_dict(obj: Any) -> RepairOpt:
        time_overlap = obj.get("time_overlap") if "time_overlap" in obj else "disabled"
        led_overlap = obj.get("led_overlap") if "led_overlap" in obj else "disabled"
        return RepairOpt(time_overlap, led_overlap)

    @staticmethod
    def to_dict(obj: RepairOpt) -> dict:
        data: dict = dict()
        data["time_overlap"] = obj.time_ovr
        data["led_overlap"] = obj.led_ovr
        return data


class DMConfig:
    def __init__(
        self,
        window="",
        debug="warning",
        directories=Directories(),
        run_opt=RunOpt(),
        repair_opt=RepairOpt(),
    ) -> None:
        self.window = window
        self.debug = debug
        self.directories: Directories = directories
        self.run = run_opt
        self.repair = repair_opt

    def __str__(self) -> str:
        mess = f"Window: {self.window} \nDebug: {self.debug}\n{str(self.directories)}\n{self.run}\n{self.repair}"
        return mess

    @staticmethod
    def from_dict(obj: Any) -> DMConfig:

        win = obj.get("window") if "window" in obj else ""
        debug = obj.get("debug") if "debug" in obj else "warning"
        dir = (
            Directories.from_dict(obj.get("directories"))
            if "directories" in obj
            else Directories()
        )
        run = (
            RunOpt.from_dict(obj.get("run_options"))
            if "run_options" in obj
            else RunOpt()
        )
        repair = (
            RepairOpt.from_dict(obj.get("repair_options"))
            if "repair_options" in obj
            else RepairOpt()
        )
        return DMConfig(win, debug, dir, run, repair)

    @staticmethod
    def to_dict(obj: DMConfig) -> dict:
        data: dict = dict()
        data["window"] = obj.window
        data["debug"] = obj.debug
        data["directories"] = Directories.to_dict(obj.directories)
        data["run_options"] = RunOpt.to_dict(obj.run)
        data["repair_options"] = RepairOpt.to_dict(obj.repair)
        return data

    @staticmethod
    def read(name: Path) -> DMConfig:
        with open(name) as file:
            data = json.load(file)
            return DMConfig.from_dict(data)

    def write(self, name: Path) -> None:
        with open(name, "w") as file:
            file.write(json.dumps(DMConfig.to_dict(self), indent=4))


def main():
    # data = '{"window":"empty", "directories": {"root": "foo", "read": "bar"}}'
    # data = json.loads(data)
    # config = DMConfig.from_dict(data)
    # config.directories.write = "zoo"

    # # print(DMConfig.to_dict(config))
    config = DMConfig()
    config.write(Path("config.json"))
    c2 = DMConfig.read(Path("config.json"))
    print(c2)


if __name__ == "__main__":
    main()
