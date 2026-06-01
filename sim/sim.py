from __future__ import annotations
import threading
import tkinter as tk
from tkinter import filedialog, messagebox
import logging
from pathlib import Path
import time
import random

import param, config, curve, dmutil

# import curve, param, config, dmutil, advance, audio, ble, player, ioseq
# from seq import Oscillator, Step, Sequence

MPSIM_BASE_DIR = Path(__file__).parent.resolve()
MPSIM_HOME_DIR = Path.home() / "mpsim"
RESOURCES_DIR = MPSIM_BASE_DIR / "resources"


class App(tk.Tk):
    """The Morpheus Light Simulator.
    This is the main class of the application. It contains the main window and all the components of the application.
    """

    def __init__(self) -> None:
        """The MPSim App contains:
        - param frame (all parameters):
        - curve frame (display curves)
        """
        tk.Tk.__init__(self)
        

        def save_config() -> None:
            """Save the current configuration"""
            # We retrieve the last window position before saving
            self.dmconfig.window = self.winfo_geometry()
            config.DMConfig.write(self.dmconfig, MPSIM_HOME_DIR / "simconfig.json")

        def read_config() -> config.DMConfig:
            """return the configuration if it exists otherwise create one"""
            if not MPSIM_HOME_DIR.exists():
                MPSIM_HOME_DIR.mkdir()  # the mpsim home dir does not exist create one
            if (MPSIM_HOME_DIR / "simconfig.json").exists():
                return config.DMConfig.read(MPSIM_HOME_DIR / "simconfig.json")
            else:
                return config.DMConfig()  # return a default config

        def _quit() -> None:
            """Executed before exciting the program"""
            # self.save_if_modified()
            # self.withdraw()
            save_config()
            # del self.ble  # ?
            # self.after_cancel(self.player.player_loop)
            app.quit()  # stops mainloop
            app.destroy()  # Windows Fatal Python Error
            # https://stackoverflow.com/questions/26168967/invalid-command-name-while-executing-after-script

        VERSION = " (Version 0.0.0)"
        self.dmconfig = read_config()
        save_config()  # we save the config at the beginning to be sure to have the window position for next time
        self.title("Morpheus Light Simulator" + VERSION)
        # self.tk.call("source", RESOURCES_DIR / "azure.tcl")
        # self.tk.call("set_theme", "light")
        # self.iconbitmap(RESOURCES_DIR / "DreamMachine.ico")
        self.protocol("WM_DELETE_WINDOW", _quit)

        # self.dmconfig = read_config()
        logging.basicConfig(
            level=self.dmconfig.debug.upper(),
            format="{asctime} {module}:{lineno} L{levelno} {threadName}-> {message}",
            style="{",
        )
        logging.info(f"configuration file:\n{self.dmconfig}")
        self.toto = MPSIM_HOME_DIR / "toto.txt"
        logging.info(f"test {self.toto}")

        # self.ble = ble.Ble(self)
        # self.audio = audio.Audio(self)
        # self.player = player.Player(self)
        self.img = dmutil.DMImage(self)
        # self.sequence: Sequence
        # self.has_sound = False
        # self.new_snd_file = ""
        # self.audio_ready = False
        # self.initialized = False
        # self.seq_modified = False
        # self.activated_step: int = 0
        # self.cursor_pos = 0
        # self.selected_seq: ioseq.SeqEntry | None = None
        # self.seq_dir_name = ""

        # curve needs param to be created before to link player button :)
        self.param = param.Param(self)
        self.param.grid(row=0, column=0, pady=3, padx=5, sticky="n")
        self.curve = curve.Curve(self)
        self.curve.grid(row=0, column=1, sticky="news")
        self.rowconfigure(0, weight=1)
        self.columnconfigure(1, weight=1)

        # def insert_step() -> None:
        #     """insert a new step before current one (menu event)"""
        #     last_step_t_start_was = self.sequence.steps[self.activated_step].t_start
        #     step = App.create_default_step(
        #         self.activated_step, t_start=last_step_t_start_was
        #     )
        #     self.sequence.steps.insert(self.activated_step, step)
        #     self.sequence.fix_following_steps(self.activated_step)
        #     self.seq_modified = True
        #     self.curve.plot_sequence()
        #     self.activate_step(self.activated_step)

        # def append_step() -> None:
        #     """add a new step after the current one (menu event)"""
        #     last_step_t_end_was = self.sequence.steps[self.activated_step].t_end
        #     self.activated_step += 1
        #     step = App.create_default_step(self.activated_step, last_step_t_end_was)
        #     self.sequence.steps.insert(self.activated_step, step)
        #     self.sequence.fix_following_steps(self.activated_step)
        #     self.seq_modified = True
        #     self.curve.plot_sequence()
        #     self.activate_step(self.activated_step)

        # def delete_step() -> None:
        #     """delete current state unless it is the last (menu event)"""
        #     if len(self.sequence.steps) < 2:
        #         messagebox.showerror(
        #             "You can't delete the last step",
        #             f"A sequence must have at least one step",
        #         )
        #         return
        #     cur_time_start = self.sequence.steps[self.activated_step].t_start
        #     del self.sequence.steps[self.activated_step]
        #     if self.activated_step < len(self.sequence.steps):
        #         dur = (
        #             self.sequence.steps[self.activated_step].t_end
        #             - self.sequence.steps[self.activated_step].t_start
        #         )
        #         self.sequence.steps[self.activated_step].t_start = cur_time_start
        #         self.sequence.steps[self.activated_step].t_end = cur_time_start + dur
        #         self.sequence.steps[self.activated_step].index = self.activated_step + 1
        #         self.sequence.fix_following_steps(self.activated_step)
        #     else:  # we have deleted the last step decrease cur_step
        #         self.activated_step -= 1
        #     self.seq_modified = True
        #     self.curve.plot_sequence()
        #     self.activate_step(self.activated_step)

        # menu_bar = tk.Menu(self)

        # # create category "Sequence" for menu
        # self.seq_menu = tk.Menu(menu_bar, tearoff=0)
        # self.seq_menu.add_command(
        #     label="New", accelerator="CTRL+N", command=self.new_seq
        # )
        # self.seq_menu.add_command(
        #     label="Open", accelerator="CTRL+O", command=self.open_seq
        # )
        # self.seq_menu.add_command(
        #     label="Save", accelerator="CTRL+S", command=self.save_seq, state="disabled"
        # )
        # self.seq_menu.add_command(
        #     label="Check Sequence", command=self.check_seq, state="disabled"
        # )
        # self.seq_menu.add_command(
        #     label="Fix Sequence", command=self.fix_seq, state="disabled"
        # )
        # self.seq_menu.add_separator()
        # self.seq_menu.add_command(label="Exit", command=_quit)
        # menu_bar.add_cascade(label="Sequence", menu=self.seq_menu)
        # # https://koor.fr/Python/Tutoriel_Tkinter/tkinter_menu.wp
        # self.bind_all("<Control-n>", lambda s=self: self.new_seq())  # type: ignore
        # self.bind_all("<Control-o>", lambda s=self: self.open_seq())  # type: ignore
        # self.bind_all("<Control-s>", lambda s=self: self.save_seq())  # type: ignore

        # # create category "Step" for menu
        # self.step_menu = tk.Menu(menu_bar, tearoff=0)
        # self.step_menu.add_command(
        #     label="Insert", accelerator="CTRL+I", command=insert_step, state="disabled"
        # )
        # self.step_menu.add_command(
        #     label="Append", accelerator="CTRL+A", command=append_step, state="disabled"
        # )
        # self.step_menu.add_command(
        #     label="Delete", accelerator="CTRL+X", command=delete_step, state="disabled"
        # )
        # menu_bar.add_cascade(label="Step", menu=self.step_menu)
        # self.bind_all("<Control-i>", lambda s=self: insert_step())  # type: ignore
        # self.bind_all("<Control-a>", lambda s=self: append_step())  # type: ignore
        # self.bind_all("<Control-x>", lambda s=self: delete_step())  # type: ignore

        # # create category "Advance" for menu
        # advance_menu = tk.Menu(menu_bar, tearoff=0)
        # advance_menu.add_command(label="Shepard Effect", underline=0, command=lambda s=self: advance.Shepard(s))  # type: ignore
        # menu_bar.add_cascade(label="Advance", menu=advance_menu)

        # # create category "Sound" for menu
        # self.audio_menu = tk.Menu(menu_bar, tearoff=0)
        # self.audio_menu.add_command(
        #     label="Add audio", command=self.add_sound, state="disabled"
        # )
        # # sound_menu.add_command(label="Adjust sound", command=self.audio.adjust_sound)
        # # sound_menu.add_command(label="Adjust sequence", underline=0, command=lambda s=self: audio.adjust_sequence(s))  # type: ignore
        # menu_bar.add_cascade(label="Audio", menu=self.audio_menu)

        # self.config(menu=menu_bar)

    # def new_seq(self) -> None:
    #     """Creates a new sequence with one step"""
    #     if not self.save_if_modified():
    #         return
    #     self.curve.seq_dur_lbl.config(text="---")
    #     self.curve.seq_audio_lbl.config(text="---")
    #     self.sequence = App.create_default_seq()
    #     self.initialized = True
    #     self.activate_control()
    #     self.has_sound = False
    #     self.new_snd_file = ""
    #     self.seq_modified = True
    #     self.selected_seq = None
    #     # self.curve.mode_var.set("Seq")
    #     self.seq_dir_name = ""

    #     self.curve.create_canvas()
    #     self.activated_step = 0
    #     self.curve.plot_sequence()
    #     self.activate_step(0)

    # def open_seq(self) -> None:
    #     """Open an existing sequence
    #     - We first open a new selection window and based on return value
    #         - we read the selected sequence file
    #         - we read the audio file for this sequence if it exist"""
    #     if not self.save_if_modified():
    #         return

    #     self.seq_dir_name = ""
    #     # ask user to select a sequence
    #     selection_win = ioseq.ReadWriteSeq(self, mode=ioseq.Mode.READ)
    #     self.wait_window(selection_win)  # wait until window destroyed
    #     if not self.selected_seq:
    #         return  # nothing selected ignore
    #     logging.info(f"Selected sequence: '{self.seq_dir_name}'")
    #     self.curve.seq_dur_lbl.config(text="---")

    #     # read the sequence
    #     seq_file = self.selected_seq["path"] / "sequence.json"
    #     logging.info(f"Reading {seq_file}")
    #     start_time = time.time()
    #     self.sequence = Sequence.read(seq_file)
    #     elapsed_time = round(time.time() - start_time, 2)
    #     dur = f"{self.sequence.duration} ({dmutil.fmt_time(self.sequence.duration)})"
    #     st = len(self.sequence.steps)
    #     logging.info(
    #         f"Sequence file processed in {elapsed_time}: sequence has {st} steps duration={dur}"
    #     )
    #     self.initialized = True
    #     self.activate_control()

    #     # read audio if present
    #     audio_file = self.selected_seq["path"] / "son.mp3"
    #     if audio_file.exists():
    #         logging.info(f"Reading {audio_file}")
    #         self.audio.read_audio(audio_file)
    #         self.has_sound = True
    #     else:
    #         self.has_sound = False
    #     self.new_snd_file = ""

    #     self.curve.create_canvas()
    #     self.curve.plot_sequence()
    #     self.activate_step(0)
    #     self.curve.set_player_duration(self.sequence.duration)
    #     self.seq_modified = False

    # def add_sound(self) -> None:
    #     """Add sound to a sequence
    #     - We first open a new selection window and based on return value we read the selected audio file
    #     """

    #     if self.has_sound:
    #         ask = messagebox.askyesnocancel(
    #             "This sequence already has audio",
    #             f"Are you sure you want to replace the existing audio file?",
    #         )
    #         if ask == False:
    #             logging.warning("Add sound aborted by user")
    #             return

    #     # ask user to select a audio file
    #     audio_dir = self.dmconfig.directories.audio
    #     if not audio_dir.exists():
    #         audio_dir = Path(".")
    #     file_path = filedialog.askopenfilename(
    #         initialdir=audio_dir,
    #         title="Please select the sound file",
    #         filetypes=(
    #             ("mp3 file", "*.mp3"),
    #             ("All files", "*.*"),
    #         ),
    #     )
    #     if not file_path:
    #         logging.warning("Oops you did not provide a name!")
    #         return
    #     else:
    #         path = Path(file_path)
    #     self.dmconfig.directories.audio = path.parent.resolve()

    #     self.has_sound = True
    #     self.curve.create_canvas()
    #     self.curve.plot_sequence()

    #     logging.info(f"Reading selected sound file: {file_path}")
    #     self.audio.read_audio(Path(file_path))
    #     self.new_snd_file = file_path

    # @staticmethod
    # def create_default_osc(leds: list[str]) -> Oscillator:
    #     """Create a new default osc"""
    #     # leds = random.choice()
    #     return Oscillator(
    #         leds,
    #         random.randint(*RND_F_START),
    #         random.randint(*RND_F_END),
    #         random.randint(*RND_B_START),
    #         random.randint(*RND_B_END),
    #         random.randint(*RND_D_START),
    #         random.randint(*RND_D_END),
    #     )

    # @staticmethod
    # def create_default_step(step_idx: int, t_start: int) -> Step:
    #     """Create a new default step"""
    #     osc = App.create_default_osc(["A1", "A2"])
    #     # osc = Oscillator(["A1", "B3", "B4"], 10, 12, 30, 80, 40, 60)
    #     return Step(
    #         step_idx + 1, t_start, t_start + random.randint(*RND_STEP_DUR), [osc]
    #     )

    # @staticmethod
    # def create_default_seq() -> Sequence:
    #     """Create a new default sequence"""
    #     step = App.create_default_step(0, 0)
    #     return Sequence("New sequence", None, step.t_end, [step])

    # def check_seq(self) -> None:
    #     self.sequence.check_sequence(fix=False)

    # def fix_seq(self) -> None:
    #     has_been_fixed = self.sequence.check_sequence(fix=True)
    #     if has_been_fixed:
    #         self.param.osc_frame.set_all_osc(self.activated_step)
    #         self.seq_modified = True

    # def save_seq(self) -> None:
    #     """Save the current sequence:
    #     - if this is a selected (with existing path) sequence we:
    #         - ask if user want to overwrite the sequence
    #         - if sound exist and modified ask if user wants to overwrite it
    #     - if this is a new sequence or a renamed (= new) sequence
    #         - we compute a new path and create the directory
    #         - we save the sequence to this new path
    #         - we save the sound if it exist
    #     """
    #     # ask user to select a directory
    #     selection_win = ioseq.ReadWriteSeq(self, mode=ioseq.Mode.WRITE)
    #     self.wait_window(selection_win)  # wait until window destroyed
    #     # print(f"{self.selected_seq=}")
    #     if not self.selected_seq:
    #         return

    #     if self.seq_dir_name:
    #         dir_name = self.seq_dir_name
    #     else:
    #         dir_name = dmutil.remove_special_char(self.sequence.name)
    #     # print(f"{dir_name=}")
    #     file_path = self.selected_seq["path"] / Path(dir_name) / "sequence.json"
    #     # create missing directory if necessary
    #     file_path.parent.mkdir(parents=True, exist_ok=True)

    #     if file_path.exists():
    #         ask = messagebox.askyesnocancel(
    #             "This sequence already exist",
    #             f"Are you sure you want to overwrite the existing sequence?",
    #         )
    #         if ask == False:
    #             logging.warning("Save sequence aborted")
    #             return
    #     self.sequence.write(file_path)

    #     if self.has_sound and self.new_snd_file:
    #         audio_path = self.selected_seq["path"] / Path(dir_name) / "son.mp3"
    #         if audio_path.exists():
    #             ask = messagebox.askyesnocancel(
    #                 "Audio file already exist",
    #                 f"Are you sure you want to overwrite the existing audio file?",
    #             )
    #             if ask == False:
    #                 logging.info("Save audio aborted")
    #                 return
    #         # # as the conversion to audio segment takes a long time we start a separate thread
    #         # threading.Thread(
    #         #     target=lambda af=audio_path: self.audio.save_audio(audio_path),
    #         #     daemon=True,
    #         # ).start()
    #         # copy from source to destination
    #         audio_path.write_bytes(Path(self.new_snd_file).read_bytes())
    #         logging.info("Done copying audio")

    #     self.seq_modified = False
    #     self.new_snd_file = ""  # no need anymore

    # def save_if_modified(self) -> bool:
    #     """we check if current seq is modified:
    #     - not modified: do nothing return True
    #     - modified ask user:
    #         - answer yes: save return True
    #         - answer no : do not save return True
    #         - answer abort: do not save return False"""
    #     if not self.seq_modified:
    #         return True
    #     status = messagebox.askyesnocancel(
    #         "Sequence modified but not saved",
    #         f"The current sequence has been modified but not saved.\n Do you want to save it before continuing?",
    #     )
    #     if status == True:
    #         logging.info("Saving current sequence")
    #         self.save_seq()
    #     elif status == None:
    #         logging.info("Aborting command")
    #         return False
    #     return True

    # def activate_step(self, step_num: int) -> None:
    #     """With the step_num provided we
    #     - update the parameters:
    #         - update display of oscillators param (including activation)
    #         - update display of step: step, index, t_start, t_end, duration
    #         - update display of sequence
    #         - update the steps information in curve
    #     - if we are in seq mode: we update the active regions,
    #     - else (step mode): we redraw all the curves
    #     """
    #     logging.info(f"Activating step {step_num}")
    #     self.param.update_params(step_num)
    #     self.curve.set_cursor_pos(self.sequence.steps[step_num].t_start)
    #     self.curve.cur_step_lbl.config(text=f"{step_num+1}/{len(self.sequence.steps)}")
    #     self.curve.update_active_region()

    # def activate_control(self):
    #     self.seq_menu.entryconfig(2, state="active")
    #     self.seq_menu.entryconfig(3, state="active")
    #     self.seq_menu.entryconfig(4, state="active")
    #     self.step_menu.entryconfig(0, state="active")
    #     self.step_menu.entryconfig(1, state="active")
    #     self.step_menu.entryconfig(2, state="active")
    #     self.audio_menu.entryconfig(0, state="active")
    #     dmutil.enable_widget(self.param.step_frame, True)
    #     dmutil.enable_widget(self.param.seq_frame, True)
    #     dmutil.enable_widget(self.curve, True)
    #     self.curve.range_slider.grid(row=0, column=2, sticky="ew")

    # def split_step(self, position):
    #     """split the step at a given position
    #     - shorten current step till position
    #     - add new step from position till end of original
    #     - the param are set to the values at the position"""
    #     prev_step = self.sequence.steps[self.activated_step]
    #     (step, _, values) = dmutil.osc_values_at_pos(position, self.sequence)
    #     oscs = []
    #     for idx, osc in enumerate(self.sequence.steps[step].oscillators):
    #         val = values[idx]
    #         oscs.append(
    #             Oscillator(
    #                 osc.leds,
    #                 val[0],
    #                 prev_step.oscillators[idx].f_end,
    #                 val[1],
    #                 prev_step.oscillators[idx].b_end,
    #                 val[2],
    #                 prev_step.oscillators[idx].d_end,
    #             )
    #         )
    #         prev_step.oscillators[idx].f_end = val[0]
    #         prev_step.oscillators[idx].b_end = val[1]
    #         prev_step.oscillators[idx].d_end = val[2]
    #     new_step = Step(prev_step.index + 1, int(position), prev_step.t_end, oscs)
    #     prev_step.t_end = int(position)
    #     self.activated_step += 1
    #     self.sequence.steps.insert(self.activated_step, new_step)
    #     self.sequence.fix_following_steps(self.activated_step)
    #     self.seq_modified = True
    #     self.curve.plot_sequence()
    #     self.activate_step(self.activated_step)


if __name__ == "__main__":

    # def run_asyncio_loop(loop):
    #     """Run an asyncio loop forever"""
    #     asyncio.set_event_loop(loop)
    #     loop.run_forever()

    # # Create and start the thread for the asynchronous loop
    # loop = asyncio.new_event_loop()
    # asyncio_thread = threading.Thread(
    #     target=run_asyncio_loop, args=(loop,), daemon=True
    # )
    # asyncio_thread.start()

    app = App()
    # app.after(0, app.player.player_update_loop)  # start player event_loop

    # if app.dmconfig.window:
    #     # print(root.geometry(root.dmconfig.window))
    #     app.minsize(1700, 768)  # for now seems the right value
    #     app.geometry(app.dmconfig.window)
    app.mainloop()

    # # Set a minsize for the window, and place it in the middle
    # root.update()
    # root.minsize(root.winfo_width(), root.winfo_height())
    # print(f"{root.winfo_width()=} {root.winfo_height()=}")
    # x_coordinate = int((root.winfo_screenwidth() / 2) - (root.winfo_width() / 2))
    # y_coordinate = int((root.winfo_screenheight() / 2) - (root.winfo_height() / 2))
    # root.geometry("+{}+{}".format(x_coordinate, y_coordinate - 20))

# pip install --upgrade PyInstaller pyinstaller-hooks-contrib
# pyinstaller --hidden-import=winrt.windows.foundation.collections --icon=src\Resources\DreamMachine.ico --add-data="src\Resources;." src\dme.py
