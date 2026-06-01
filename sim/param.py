from __future__ import annotations
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import asyncio, logging
# import dme, osc, dmutil


class Param(tk.Frame):
    """
    The Param class is a frame that contains everything related to sequence parameters:
    - the osc_frame : all parameters about the 4 oscillators (led, freq, bright, duty)
    - the step_frame: selection of a specific step in a sequence + index + duration
    - the seq_frame: sequence name
    - the dm_frame: connection to the Dream machine with bluetooth
    """

    def __init__(self, app: dme.App) -> None:
        super().__init__(app)
        self.app = app
        # self.img = app.img

        self.step_var = tk.StringVar()
        # self.index_var = tk.StringVar()
        self.seq_name_var = tk.StringVar()
        self.dm_bright_var = tk.IntVar(value=50)

        # self.osc_frame = osc.Oscillators(self)
        # self.osc_frame.grid(row=0, column=0, sticky="nw")
        # self.step_frame = tk.LabelFrame(self, text="Step")
        # self.step_frame.grid(row=1, column=0, padx=2, pady=2, sticky="we")
        # self.seq_frame = tk.LabelFrame(self, text="Sequence")
        # self.seq_frame.grid(row=2, column=0, padx=2, pady=2, sticky="news")
        # self.dm_frame = tk.LabelFrame(self, text="Dream Machine Bluetooth Connection")
        # self.dm_frame.grid(row=3, column=0, padx=2, pady=3, sticky="news")
        # self.dm_frame.columnconfigure(3, weight=1)
        # self.columnconfigure(0, weight=1)

    #     def step_ret_event(event) -> None:
    #         value = self.step_var.get()
    #         step_event("-1", value)

    #     def step_event(action: str, value: str) -> bool:
    #         """validate the entry in step field"""
    #         if action == "-1" or not value:
    #             return True  # ignore empty or forced
    #         try:  # validate user entry
    #             val = int(value)  # generate exception if not valid
    #             if not (0 < val < len(self.app.sequence.steps) + 1):
    #                 raise ValueError
    #             self.app.activate_step(val - 1)
    #             return True
    #         except ValueError:
    #             messagebox.showerror(
    #                 "Invalid entry",
    #                 f"The step must be an integer in range 1-{len(self.app.sequence.steps)}",
    #             )
    #             return False

    #     #
    #     # step elements
    #     #
    #     tk.Button(
    #         self.step_frame, image=self.img.step_img_first, command=self.first_step
    #     ).grid(row=0, column=0)
    #     tk.Button(
    #         self.step_frame, image=self.img.step_img_previous, command=self.prev_step
    #     ).grid(row=0, column=1)
    #     step_entry = tk.Entry(
    #         self.step_frame,
    #         textvariable=self.step_var,
    #         width=3,
    #         justify="center",
    #         validatecommand=(self.register(step_event), "%d", "%P"),
    #         validate="focusout",
    #     )
    #     step_entry.grid(row=0, column=2, padx=2)
    #     step_entry.bind("<Return>", step_ret_event)

    #     tk.Button(
    #         self.step_frame, image=self.img.step_img_next, command=self.next_step
    #     ).grid(row=0, column=3, padx=2)
    #     tk.Button(
    #         self.step_frame, image=self.img.step_img_last, command=self.last_step
    #     ).grid(
    #         row=0,
    #         column=4,
    #     )

    #     tk.Label(self.step_frame, text="Index").grid(row=0, column=5, padx=5)
    #     self.index_lbl = tk.Label(self.step_frame, bg="#eee")
    #     self.index_lbl.grid(row=0, column=6, padx=2, pady=5)

    #     tk.Label(self.step_frame, text="Duration").grid(row=0, column=7, padx=2)
    #     self.duration_entry = tk.Entry(self.step_frame, width=5)
    #     self.duration_entry.grid(row=0, column=8, padx=2, pady=2, sticky="ew")
    #     self.step_frame.columnconfigure(8, weight=1)

    #     tk.Label(self.step_frame, text="Time").grid(row=0, column=9, padx=2)
    #     self.t_start_lbl = tk.Label(self.step_frame, width=5)
    #     self.t_start_lbl.grid(row=0, column=10, padx=2)
    #     self.t_end_entry = tk.Entry(self.step_frame, width=5)
    #     self.t_end_entry.grid(row=0, column=11, padx=2)

    #     def t_end_event(event) -> bool:
    #         """t_end value changed in input field"""
    #         value = self.t_end_entry.get()
    #         # print(f"{event=} {value=}")
    #         try:
    #             t_end = int(value)  # generate exception if not valid
    #             if t_end <= self.step_t_start:
    #                 raise ValueError
    #             duration = self.step_t_end - self.step_t_start
    #             self.app.sequence.steps[self.cur_step_display].t_end = t_end
    #             self.step_duration = duration
    #             self.app.seq_modified = True
    #             self.app.sequence.fix_following_steps(self.cur_step_display)
    #             self.seq_duration = self.app.sequence.steps[-1].t_end
    #             self.app.curve.set_player_duration(self.app.sequence.steps[-1].t_end)
    #             self.app.curve.plot_sequence()
    #             return True
    #         except ValueError:
    #             messagebox.showerror(
    #                 "Invalid entry",
    #                 f"The Values {value} must be an integer",
    #             )
    #             return False

    #     def duration_event(event) -> bool:
    #         """duration value changed in input field"""
    #         value = self.duration_entry.get()
    #         # print(f"{event=} {value=}")
    #         try:
    #             duration = int(value)  # generate exception if not valid
    #             t_end = (
    #                 self.app.sequence.steps[self.cur_step_display].t_start + duration
    #             )
    #             self.app.sequence.steps[self.cur_step_display].t_end = t_end
    #             self.step_t_end = t_end
    #             self.app.seq_modified = True
    #             self.app.sequence.fix_following_steps(self.cur_step_display)
    #             self.seq_duration = self.app.sequence.steps[-1].t_end
    #             self.app.curve.set_player_duration(self.app.sequence.steps[-1].t_end)
    #             self.app.curve.plot_sequence()
    #             return True
    #         except ValueError:
    #             messagebox.showerror(
    #                 "Invalid entry",
    #                 f"The Values {value} must be an integer",
    #             )
    #             return False

    #     self.duration_entry.bind("<Return>", duration_event)
    #     self.duration_entry.bind("<FocusOut>", duration_event)
    #     self.t_end_entry.bind("<Return>", t_end_event)
    #     self.t_end_entry.bind("<FocusOut>", t_end_event)
    #     dmutil.enable_widget(self.step_frame, False)

    #     # seq_frame content
    #     def seq_name_event(action: str, value: str) -> bool:
    #         if action == "-1" or not value:  # forced do nothing
    #             return True
    #         self.app.sequence.name = value
    #         self.app.seq_modified = True
    #         self.app.selected_seq = None
    #         self.app.seq_dir_name = ""
    #         return True

    #     tk.Entry(
    #         self.seq_frame,
    #         textvariable=self.seq_name_var,
    #         validatecommand=(self.register(seq_name_event), "%d", "%P"),
    #         validate="key",
    #     ).grid(row=0, column=0, padx=5, pady=5, sticky="ew")
    #     self.seq_frame.columnconfigure(0, weight=1)

    #     tk.Label(self.seq_frame, text="Duration").grid(row=0, column=1, padx=2)
    #     self.seq_dur_lbl = tk.Label(self.seq_frame, bg="#eee")
    #     self.seq_dur_lbl.grid(row=0, column=2, padx=2)
    #     tk.Label(self.seq_frame, text="Steps").grid(row=0, column=3, padx=2)
    #     self.num_step_lbl = tk.Label(self.seq_frame, bg="#eee")
    #     self.num_step_lbl.grid(row=0, column=4, padx=5)
    #     dmutil.enable_widget(self.seq_frame, False)

    #     #
    #     # dream machine connection
    #     #
    #     def on_brightness_change(value):
    #         if self.app.ble.is_connected:
    #             asyncio.run_coroutine_threadsafe(
    #                 self.app.ble.aio_send_brightness(self.dm_bright_var.get()),
    #                 self.app.ble.ble_loop,
    #             )

    #     self.dm_connect_btn = tk.Button(
    #         self.dm_frame,
    #         command=self.connect_to_dm,
    #         image=self.img.ble_connect_img,
    #         width=40,
    #         bg="#bbf",
    #     )
    #     self.dm_connect_btn.grid(row=0, column=0, sticky="w", padx=6, pady=4)

    #     self.dm_lbl = tk.Label(
    #         self.dm_frame,
    #         text="Disconnected",
    #         bg="coral2",
    #         justify="left",
    #         width=30,
    #         anchor="w",
    #     )
    #     self.dm_lbl.grid(row=0, column=1, pady=5, sticky="w")

    #     self.dm_status_lbl = tk.Label(
    #         self.dm_frame, image=self.img.ble_disconnected_img, bg="coral2", height=19
    #     )
    #     self.dm_status_lbl.grid(row=0, column=2, sticky="w", pady=4)
    #     self.bright_slider = ttk.Scale(
    #         self.dm_frame,
    #         from_=0,
    #         to=100,
    #         variable=self.dm_bright_var,
    #         command=on_brightness_change,
    #     )
    #     self.bright_slider.grid(row=0, column=3, padx=10, pady=5, sticky="we")

    # def first_step(self) -> None:
    #     self.cur_step_display = 0
    #     self.app.activate_step(self.cur_step_display)

    # def last_step(self) -> None:
    #     self.cur_step_display = len(self.app.sequence.steps) - 1
    #     self.app.activate_step(self.cur_step_display)

    # def prev_step(self) -> None:
    #     """decrement step if not first"""
    #     if self.cur_step_display == 0:
    #         return
    #     else:
    #         self.cur_step_display -= 1
    #         self.app.activate_step(self.cur_step_display)

    # def next_step(self) -> None:
    #     """increment step if not last"""
    #     if self.cur_step_display == len(self.app.sequence.steps) - 1:
    #         return
    #     else:
    #         self.cur_step_display += 1
    #         self.app.activate_step(self.cur_step_display)

    # @property
    # def step_t_start(self) -> int:
    #     return int(self.t_start_lbl["text"])

    # @step_t_start.setter
    # def step_t_start(self, value: int) -> None:
    #     self.t_start_lbl.config(text=f"{value}")

    # @property
    # def step_t_end(self) -> int:
    #     return int(self.t_end_entry.get())

    # @step_t_end.setter
    # def step_t_end(self, value: int) -> None:
    #     self.t_end_entry.delete(0, tk.END)
    #     self.t_end_entry.insert(0, f"{value}")

    # @property
    # def step_duration(self) -> int:
    #     return int(self.duration_entry.get())

    # @step_duration.setter
    # def step_duration(self, value: int) -> None:
    #     self.duration_entry.delete(0, tk.END)
    #     self.duration_entry.insert(0, f"{value}")

    # @property
    # def cur_step_display(self) -> int:
    #     return int(self.step_var.get()) - 1

    # @cur_step_display.setter
    # def cur_step_display(self, value: int) -> None:
    #     self.step_var.set(str(value + 1))
    #     self.app.activated_step = value

    # @property
    # def index_display(self) -> int:
    #     return int(self.index_lbl["text"])

    # @index_display.setter
    # def index_display(self, value: int) -> None:
    #     self.index_lbl.config(text=str(value))

    # @property
    # def seq_name(self) -> str:
    #     return self.seq_name_var.get()

    # @seq_name.setter
    # def seq_name(self, value: str):
    #     self.seq_name_var.set(value)

    # @property
    # def seq_duration(self) -> int:  # TODO bad wr a string cant read an int
    #     """Read sequence duration label"""
    #     return self.seq_dur_lbl["text"]

    # @seq_duration.setter
    # def seq_duration(self, value: int):
    #     """Update sequence duration label"""
    #     self.seq_dur_lbl["text"] = dmutil.fmt_time(value)
    #     self.app.curve.set_player_duration(value)

    # @property
    # def seq_steps_display(self) -> int:
    #     return int(self.num_step_lbl["text"])

    # @seq_steps_display.setter
    # def seq_steps_display(self, value: int):
    #     self.num_step_lbl["text"] = str(value)

    # def update_seq_param(self) -> None:
    #     """Update the name/steps/duration fields in the sequence display frame"""
    #     self.seq_name = self.app.sequence.name
    #     self.seq_steps_display = len(self.app.sequence.steps)
    #     self.seq_duration = self.app.sequence.duration = self.app.sequence.steps[
    #         -1
    #     ].t_end

    # def connect_to_dm(self):
    #     # logging.info("Calling asyncio_ble in a thread")
    #     asyncio.run_coroutine_threadsafe(self.aio_ble_connect(), self.app.ble.ble_loop)

    # async def aio_ble_connect(self) -> bool:
    #     logging.info("Scan/Connect/Disconnect thread started")
    #     self.app.ble
    #     if not (self.app.ble.found_dm and self.app.ble.is_connected):
    #         self.dm_lbl.configure(text=f"Searching for a DM ...", bg="yellow")
    #         self.dm_status_lbl.configure(image=self.img.ble_searching, bg="yellow")
    #         found = await self.app.ble.aio_scan_for_dm()
    #         if not found:
    #             logging.warning("no dm found")
    #             self.dm_lbl.configure(text=f"Did not found any DM", bg="coral2")
    #             self.dm_status_lbl.configure(
    #                 image=self.img.ble_disconnected_img, bg="coral2"
    #             )
    #             return False

    #     if self.app.ble.is_connected:
    #         self.dm_lbl.configure(
    #             text=f"Disconnecting from {self.app.ble.name} ...",
    #         )
    #         await self.app.ble.aio_disconnect()
    #         self.dm_status_lbl.configure(
    #             image=self.img.ble_disconnected_img, bg="coral2"
    #         )
    #         self.dm_lbl.configure(text=f"Disconnected", bg="coral2")
    #         self.dm_connect_btn.configure(image=self.img.ble_connect_img)
    #     else:
    #         self.dm_lbl.configure(
    #             text=f"Connecting to {self.app.ble.name} ...",
    #         )
    #         await self.app.ble.aio_connect()
    #         self.dm_status_lbl.configure(
    #             image=self.img.ble_connected_img, bg="aquamarine3"
    #         )
    #         self.dm_lbl.configure(
    #             text=f"Connected to {self.app.ble.name}", bg="aquamarine3"
    #         )
    #         self.dm_connect_btn.configure(
    #             text=f"Disconnect", image=self.img.ble_dis_img
    #         )
    #         if self.app.ble.is_connected:
    #             return True
    #     return False

    # def update_params(self, step_num):
    #     """Update :
    #     - all oscillators params
    #     - step: step_num, index, duration, t_start, t_end
    #     - update sequence params
    #     """
    #     self.osc_frame.set_all_osc(step_num)
    #     step = self.app.sequence.steps[step_num]
    #     self.cur_step_display = step_num
    #     self.index_display = step.index
    #     self.step_duration = step.t_end - step.t_start
    #     self.step_t_start = step.t_start
    #     self.step_t_end = step.t_end
    #     self.update_seq_param()
